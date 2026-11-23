import assert from "node:assert/strict";
import { test } from "node:test";
import {
  validateCarrotLocation,
  validateObservation,
  validatePotChestLocation,
  validatePotCycle,
} from "../src/validation.ts";

function report(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    territoryId: 1252,
    worldX: 100,
    worldY: 10,
    worldZ: -200,
    installationHash: "abc",
    pluginVersion: "4.3.0.0",
    observedAtUtc: new Date().toISOString(),
    ...extra,
  };
}

function potCycle(extra: Record<string, unknown> = {}): Record<string, unknown> {
  return {
    instanceKey: "A".repeat(64),
    territoryId: 1346,
    datacenterId: 4,
    potFateId: 2072,
    spawnAtUnix: Math.floor(Date.now() / 1000) - 120,
    installationHash: "abc",
    pluginVersion: "4.3.0.0",
    observedAtUtc: new Date().toISOString(),
    ...extra,
  };
}

test("valid reports of every kind pass", () => {
  assert.equal(validateObservation(report({ dataId: 2010140 })), null);
  assert.equal(validateCarrotLocation(report({ objectBaseId: 2010139 })), null);
  assert.equal(validatePotChestLocation(report({ potFateId: 1976, isReroll: false })), null);
  assert.equal(validatePotCycle(potCycle()), null);
});

test("positions off the map or at unload altitude are refused", () => {
  assert.notEqual(validateObservation(report({ dataId: 1, worldX: 5000 })), null);
  assert.notEqual(validateCarrotLocation(report({ worldZ: -1100 })), null);
  assert.notEqual(validatePotChestLocation(report({ potFateId: 1976, isReroll: true, worldY: 900 })), null);
  assert.notEqual(validateObservation(report({ dataId: 1, worldY: -600 })), null);
});

test("shared fields are checked for every kind", () => {
  assert.notEqual(validateObservation(report({ dataId: 1, territoryId: 132 })), null);
  assert.notEqual(validateCarrotLocation(report({ installationHash: "" })), null);
  assert.notEqual(validatePotChestLocation(report({ potFateId: 1976, isReroll: false, observedAtUtc: "yesterday" })), null);
  assert.notEqual(validatePotCycle(potCycle({ pluginVersion: undefined })), null);
});

test("a pot spawn more than a minute ahead is refused", () => {
  const now = Math.floor(Date.now() / 1000);
  assert.equal(validatePotCycle(potCycle({ spawnAtUnix: now + 30 })), null);
  assert.notEqual(validatePotCycle(potCycle({ spawnAtUnix: now + 9 * 60 })), null);
});

test("kind-specific fields are still checked", () => {
  assert.notEqual(validateObservation(report()), null);
  assert.notEqual(validateCarrotLocation(report({ objectBaseId: 1 })), null);
  assert.notEqual(validatePotChestLocation(report({ potFateId: 1, isReroll: false })), null);
  assert.notEqual(validatePotCycle(potCycle({ instanceKey: "short" })), null);
});
