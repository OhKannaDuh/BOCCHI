Cloudflare Worker that accepts anonymous treasure-coffer and chewed-carrot observations from BOCCHI, plus pot-cycle sync.

Payload shape for coffers matches AOCC (`POST /api/v1/observations`) so the plugin URL can point at either API.

Unlike AOCC’s pot-reveal-only filter, this API accepts **any positive coffer `dataId`** in Occult Crescent territories (**1252** South Horn, **1346** North Horn).

**End-user use in BOCCHI (Config → Treasure Hunter → Share chest locations; default on):**
- **Pot cycles** (`/api/v1/pot-cycles`) — share Magic Pot spawn anchors per instance.
- **Carrot locations** (`/api/v1/carrot-locations`) — Carrot Hunt downloads accepted pads and merges with baked list; clients upload sightings.
- **Pot-chest locations** (`/api/v1/pot-chest-locations`) — Magic Pot chest spots (per FATE and reroll flag); clients upload sightings when community pads are on.
- **Coffer candidates** (`/api/v1/candidates` + observation POST) — Treasure Hunt downloads accepted spots and unions them with the baked map; clients upload bronze/silver opens.

## Paid-plan behaviour

- Cron every **5 minutes** (processors + pot-cycle prune + processed observation prune).
- Coffer/carrot/pot-chest POST clusters **only the new row** (duplicates skip clustering). Cron still drains up to **500** pending rows per kind per run. A new spot is created in one statement, so simultaneous reports share one candidate.
- Cluster updates increment centroid/counts in D1 (no full re-aggregate), then cron deletes processed rows older than **14 days**. Reporters per candidate live in `candidate_reporters`, which the prune keeps.
- A pad auto-accepts after **three different reporters**. A reporter is a salted SHA-256 of the sender IP (IPv6 by /64), not the client-sent installation hash. Rows from before this (or local dev without `CF-Connecting-IP`) fall back to the installation hash.
- Catalog cache is busted only when a pad is **newly accepted** (or after admin review), not on every centroid nudge.
- Public catalogs omit pads with **Y below −250** (unload / inside-floor ghosts). POSTs with that altitude are rejected; cron also rejects any that already got accepted. Hamlet basement (~−162) is kept.
- Public catalogs (`/api/v1/candidates`, `/api/v1/carrot-locations`, `/api/v1/pot-chest-locations`) are compact (id + zone + position), with isolate memory cache + Cache API + `Cache-Control: max-age=300`.
- Pot-cycle GET **hits** are cached **20s** (misses are not). A new POST invalidates that instance key.
- Pot-cycle prune deletes up to **25k × 20 rounds** per cron (no free-tier write cap).
- Unique index on `(instance_key, pot_fate_id, spawn_at_unix)` + `INSERT OR IGNORE`.
- Separate IP rate limits: **60/min** observations, carrots, and pot-chest locations, **120/min** pot-cycle GET/POST.
- Traces sampled at **1%**; logs stay at 100%.

## Local setup

```powershell
cd cloudflare/coffer-api
npm install
npm run db:migrate:local
npm run dev
```

- `GET http://localhost:8787/health`
- `POST http://localhost:8787/api/v1/observations`
- `GET http://localhost:8787/api/v1/candidates?territoryId=1252` (accepted catalog for hunt routing)
- `POST http://localhost:8787/api/v1/carrot-locations`
- `GET http://localhost:8787/api/v1/carrot-locations?territoryId=1252` (accepted carrot pads)
- `POST http://localhost:8787/api/v1/pot-chest-locations`
- `GET http://localhost:8787/api/v1/pot-chest-locations?territoryId=1252` (accepted pot-chest pads)
- `POST http://localhost:8787/api/v1/pot-cycles`
- `GET http://localhost:8787/api/v1/pot-cycles?instanceKey=...`

## Deploy

```powershell
npx wrangler login
npx wrangler d1 create bocchi-coffer-observations
# Paste the returned database_id into wrangler.jsonc → d1_databases[0].database_id
npm run db:migrate:remote
npm run deploy
```

Copy into BOCCHI is not required — the plugin posts to:

`https://bocchi-coffer-api.kagekazu.workers.dev/api/v1/observations`

(URL is hardcoded; sync follows community pads — default on, while in Occult Crescent.)

Optional admin token:

```powershell
npx wrangler secret put ADMIN_TOKEN
```

Salt for reporter hashes (set it before deploying, so stored hashes cannot be turned back into IPs):

```powershell
npx wrangler secret put REPORTER_SALT
```

Tests (Node 22.18+, no extra packages):

```powershell
npm test
```

## Privacy

Submissions are anonymous (no character or account names). Stored coffer fields are territory, coffer data id, world coordinates, coffer type label, installation hash, plugin version, and observed time.

Pot-cycle rows store an instance fingerprint hash, territory, datacenter id, pot fate id, spawn unix time, installation hash, plugin version, and observed time.

Carrot location rows store territory, world coordinates, object base id (`2010139`), installation hash, plugin version, and observed time. Every observation also stores a salted hash of the sender IP (`reporter_hash`), used only to count different reporters. Candidates auto-accept after three different reporters within ~1.5 yalms.
