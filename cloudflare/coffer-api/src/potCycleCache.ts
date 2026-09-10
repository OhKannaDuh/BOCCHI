/** Isolate + Cache API for pot-cycle GET hits (not misses). */

export const POT_HIT_CACHE_TTL_SECONDS = 20;

interface MemoryEntry {
  expiresAt: number;
  body: string;
}

const memoryHits = new Map<string, MemoryEntry>();

function normalizeKey(instanceKey: string): string {
  return instanceKey.trim().toUpperCase();
}

function potCacheRequest(instanceKey: string): Request {
  return new Request(`https://bocchi-pot.internal/${normalizeKey(instanceKey)}`);
}

function potHitResponse(body: string): Response {
  return new Response(body, {
    status: 200,
    headers: {
      "Content-Type": "application/json",
      "Cache-Control": `public, max-age=${POT_HIT_CACHE_TTL_SECONDS}`,
    },
  });
}

export async function readPotHitCache(instanceKey: string): Promise<Response | undefined> {
  const key = normalizeKey(instanceKey);
  const entry = memoryHits.get(key);
  if (entry !== undefined) {
    if (entry.expiresAt > Date.now()) {
      return potHitResponse(entry.body);
    }

    memoryHits.delete(key);
  }

  const cached = await caches.default.match(potCacheRequest(key));
  if (cached === undefined) {
    return undefined;
  }

  const body = await cached.clone().text();
  memoryHits.set(key, {
    expiresAt: Date.now() + POT_HIT_CACHE_TTL_SECONDS * 1000,
    body,
  });
  return cached;
}

export function rememberPotHitPayload(instanceKey: string, payload: unknown): void {
  const key = normalizeKey(instanceKey);
  memoryHits.set(key, {
    expiresAt: Date.now() + POT_HIT_CACHE_TTL_SECONDS * 1000,
    body: JSON.stringify(payload),
  });
}

export async function writePotHitCache(instanceKey: string, response: Response): Promise<void> {
  await caches.default.put(potCacheRequest(instanceKey), response.clone());
}

export async function invalidatePotHitCache(instanceKey: string): Promise<void> {
  const key = normalizeKey(instanceKey);
  memoryHits.delete(key);
  await caches.default.delete(potCacheRequest(key));
}
