import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { DatabaseSync } from "node:sqlite";
import { beforeEach, test } from "node:test";
import { processObservationById, processPendingObservations } from "../src/processor.ts";
import { reporterKey } from "../src/reporter.ts";

const migrationsDir = join(import.meta.dirname, "..", "migrations");

type Row = Record<string, unknown>;

function returnsRows(sql: string): boolean {
  return /^\s*(WITH|SELECT)\b/i.test(sql) || /\bRETURNING\b/i.test(sql);
}

class FakeStatement {
  private readonly db: DatabaseSync;
  readonly sql: string;
  readonly params: unknown[];

  constructor(db: DatabaseSync, sql: string, params: unknown[] = []) {
    this.db = db;
    this.sql = sql;
    this.params = params;
  }

  bind(...params: unknown[]): FakeStatement {
    return new FakeStatement(this.db, this.sql, params);
  }

  execute() {
    const statement = this.db.prepare(this.sql);
    const params = this.params as (string | number | null)[];
    if (returnsRows(this.sql)) {
      const results = statement.all(...params) as Row[];
      return { success: true, results, meta: { changes: results.length, last_row_id: undefined } };
    }

    const info = statement.run(...params);
    return {
      success: true,
      results: [] as Row[],
      meta: { changes: Number(info.changes), last_row_id: Number(info.lastInsertRowid) },
    };
  }

  async run() {
    await yieldTurn();
    return this.execute();
  }

  async all() {
    await yieldTurn();
    return this.execute();
  }

  async first() {
    await yieldTurn();
    return this.execute().results[0] ?? null;
  }
}

function yieldTurn(): Promise<void> {
  return new Promise(resolve => setImmediate(resolve));
}

function fakeD1(db: DatabaseSync) {
  return {
    prepare: (sql: string) => new FakeStatement(db, sql),
    async batch(statements: FakeStatement[]) {
      await yieldTurn();
      db.exec("BEGIN");
      try {
        const results = statements.map(statement => statement.execute());
        db.exec("COMMIT");
        return results;
      } catch (error) {
        db.exec("ROLLBACK");
        throw error;
      }
    },
  };
}

let db: DatabaseSync;
let env: { DB: D1Database };

beforeEach(() => {
  db = new DatabaseSync(":memory:");
  for (const file of readdirSync(migrationsDir).filter(name => name.endsWith(".sql")).sort()) {
    db.exec(readFileSync(join(migrationsDir, file), "utf8").replace(/^﻿/, ""));
  }

  env = { DB: fakeD1(db) as unknown as D1Database };
});

function addCoffer(installationHash: string, reporterHash: string | null, x = 100): number {
  const info = db.prepare(`
    INSERT INTO observations (
      territory_id, data_id, world_x, world_y, world_z,
      installation_hash, reporter_hash, plugin_version, observed_at_utc
    ) VALUES (1252, 42, ?, 10, 200, ?, ?, 'test', '2026-11-20T00:00:00.000Z')
  `).run(x, installationHash, reporterHash);
  return Number(info.lastInsertRowid);
}

function cofferCandidates(): Row[] {
  return db.prepare(`
    SELECT id, status, observation_count, distinct_installation_count
    FROM observation_candidates
  `).all() as Row[];
}

function pruneCofferObservations(): void {
  db.exec("DELETE FROM observation_candidate_members; DELETE FROM observations;");
}

test("made-up installation hashes from one reporter do not auto-accept", async () => {
  addCoffer("fake-1", "ip-a");
  addCoffer("fake-2", "ip-a");
  addCoffer("fake-3", "ip-a");

  const result = await processPendingObservations(env, "coffers");

  assert.equal(result.newlyAccepted, 0);
  assert.deepEqual(cofferCandidates().map(c => [c.status, c.observation_count, c.distinct_installation_count]), [
    ["pending", 3, 1],
  ]);
});

test("three different reporters auto-accept", async () => {
  addCoffer("one", "ip-a");
  addCoffer("two", "ip-b");
  addCoffer("three", "ip-c", 100.5);

  const result = await processPendingObservations(env, "coffers");

  assert.equal(result.newlyAccepted, 1);
  assert.equal(cofferCandidates()[0].status, "accepted");
});

test("a reporter seen before the 14-day prune is not counted again", async () => {
  addCoffer("one", "ip-a");
  await processPendingObservations(env, "coffers");
  pruneCofferObservations();

  addCoffer("one", "ip-a");
  addCoffer("two", "ip-b");
  await processPendingObservations(env, "coffers");
  assert.deepEqual(cofferCandidates().map(c => [c.status, c.distinct_installation_count]), [["pending", 2]]);

  addCoffer("three", "ip-c");
  await processPendingObservations(env, "coffers");
  assert.equal(cofferCandidates()[0].status, "accepted");
});

test("simultaneous reports of a new spot share one candidate", async () => {
  const ids = [
    addCoffer("one", "ip-a"),
    addCoffer("two", "ip-b", 100.2),
    addCoffer("three", "ip-c", 99.9),
  ];

  await Promise.all(ids.map(id => processObservationById(env, "coffers", id)));

  assert.deepEqual(cofferCandidates().map(c => [c.status, c.observation_count, c.distinct_installation_count]), [
    ["accepted", 3, 3],
  ]);
});

test("a report linked but never counted is counted once on the next run", async () => {
  addCoffer("one", "ip-a");
  addCoffer("two", "ip-b");
  await processPendingObservations(env, "coffers");
  const stuck = addCoffer("three", "ip-c");
  db.prepare("INSERT INTO observation_candidate_members (candidate_id, observation_id, installation_hash) VALUES (?, ?, 'three')")
    .run(cofferCandidates()[0].id as number, stuck);

  await processPendingObservations(env, "coffers");
  await processPendingObservations(env, "coffers");

  assert.deepEqual(cofferCandidates().map(c => [c.status, c.observation_count, c.distinct_installation_count]), [
    ["accepted", 3, 3],
  ]);
});

test("a stuck report whose reporter the migration already recorded still counts", async () => {
  addCoffer("one", "ip-a");
  addCoffer("two", "ip-b");
  await processPendingObservations(env, "coffers");
  const candidateId = cofferCandidates()[0].id as number;
  const stuck = addCoffer("three", "ip-c");
  db.prepare("INSERT INTO observation_candidate_members (candidate_id, observation_id, installation_hash) VALUES (?, ?, 'three')")
    .run(candidateId, stuck);
  db.prepare("INSERT INTO candidate_reporters (kind, candidate_id, reporter_hash) VALUES ('coffers', ?, 'ip-c')")
    .run(candidateId);

  await processPendingObservations(env, "coffers");

  assert.deepEqual(cofferCandidates().map(c => [c.status, c.observation_count, c.distinct_installation_count]), [
    ["accepted", 3, 3],
  ]);
});

test("the same report processed twice at once is counted once", async () => {
  const id = addCoffer("one", "ip-a");

  await Promise.all([processObservationById(env, "coffers", id), processPendingObservations(env, "coffers")]);

  assert.deepEqual(cofferCandidates().map(c => [c.observation_count, c.distinct_installation_count]), [[1, 1]]);
});

test("rows without a reporter hash fall back to the installation hash", async () => {
  for (const hash of ["one", "two", "three"]) {
    db.prepare(`
      INSERT INTO carrot_observations (
        territory_id, world_x, world_y, world_z,
        installation_hash, plugin_version, observed_at_utc
      ) VALUES (1346, 5, 6, 7, ?, 'test', '2026-11-20T00:00:00.000Z')
    `).run(hash);
  }

  const result = await processPendingObservations(env, "carrots");

  assert.equal(result.newlyAccepted, 1);
});

test("pot chests cluster per FATE and reroll flag", async () => {
  for (const [hash, reroll] of [["one", 0], ["two", 1]] as const) {
    db.prepare(`
      INSERT INTO pot_chest_observations (
        territory_id, pot_fate_id, is_reroll, world_x, world_y, world_z,
        installation_hash, reporter_hash, plugin_version, observed_at_utc
      ) VALUES (1252, 1976, ?, 5, 6, 7, ?, ?, 'test', '2026-11-20T00:00:00.000Z')
    `).run(reroll, hash, `ip-${hash}`);
  }

  await processPendingObservations(env, "pot-chests");

  const rows = db.prepare("SELECT is_reroll, observation_count FROM pot_chest_candidates ORDER BY is_reroll").all();
  assert.deepEqual(rows.map(r => [r.is_reroll, r.observation_count]), [[0, 1], [1, 1]]);
});

test("a pot chest report on an accepted field coffer is dropped", async () => {
  db.prepare(`
    INSERT INTO observation_candidates (
      territory_id, data_id, centroid_x, centroid_y, centroid_z,
      first_observed_at_utc, last_observed_at_utc, status
    ) VALUES (1346, 42, 658.72, 60.5, -552.33, '2026-11-20T00:00:00.000Z', '2026-11-20T00:00:00.000Z', 'accepted')
  `).run();
  for (const [hash, x] of [["one", 658.72], ["two", 700]] as const) {
    db.prepare(`
      INSERT INTO pot_chest_observations (
        territory_id, pot_fate_id, is_reroll, world_x, world_y, world_z,
        installation_hash, reporter_hash, plugin_version, observed_at_utc
      ) VALUES (1346, 2072, 1, ?, 60.5, -552.33, ?, ?, 'test', '2026-11-20T00:00:00.000Z')
    `).run(x, hash, `ip-${hash}`);
  }

  await processPendingObservations(env, "pot-chests");

  const candidates = db.prepare("SELECT centroid_x FROM pot_chest_candidates").all();
  assert.deepEqual(candidates.map(c => c.centroid_x), [700]);
  const pending = db.prepare("SELECT count(*) AS n FROM pot_chest_observations WHERE processed = 0").get() as Row;
  assert.equal(pending.n, 0);
});

test("IPv6 reporters are grouped by /64", () => {
  assert.equal(reporterKey("2001:db8:1:2::5"), reporterKey("2001:0db8:0001:0002:ffff:0:0:9"));
  assert.notEqual(reporterKey("2001:db8:1:2::5"), reporterKey("2001:db8:1:3::5"));
  assert.equal(reporterKey("203.0.113.7"), "203.0.113.7");
});
