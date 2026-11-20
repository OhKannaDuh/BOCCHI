/** Isolate + Cache API for public accepted-catalog GETs (coffers / carrots / pot-chests). */

export const CATALOG_CACHE_TTL_SECONDS = 300;

type CatalogKind = "coffers" | "carrots" | "pot-chests";

interface MemoryEntry {
  expiresAt: number;
  body: string;
}

const memoryCatalogs = new Map<string, MemoryEntry>();

function memoryKey(kind: CatalogKind, territoryId: string | null): string {
  return `${kind}:${territoryId ?? ""}`;
}

/** Stable synthetic URL — Cache API keys are Request URLs. */
function catalogCacheRequest(kind: CatalogKind, territoryId: string | null): Request {
  const url = new URL(`https://bocchi-catalog.internal/${kind}`);
  if (territoryId !== null) {
    url.searchParams.set("territoryId", territoryId);
  }

  return new Request(url.toString(), { method: "GET" });
}

function catalogTerritory(requestUrl: URL): string | null {
  return requestUrl.searchParams.get("territoryId");
}

function catalogResponse(body: string): Response {
  return new Response(body, {
    status: 200,
    headers: {
      "Content-Type": "application/json",
      "Cache-Control": `public, max-age=${CATALOG_CACHE_TTL_SECONDS}`,
    },
  });
}

function readMemoryCatalog(kind: CatalogKind, territoryId: string | null): Response | undefined {
  const key = memoryKey(kind, territoryId);
  const entry = memoryCatalogs.get(key);
  if (entry === undefined) {
    return undefined;
  }

  if (entry.expiresAt <= Date.now()) {
    memoryCatalogs.delete(key);
    return undefined;
  }

  return catalogResponse(entry.body);
}

function writeMemoryCatalog(kind: CatalogKind, territoryId: string | null, body: string): void {
  memoryCatalogs.set(memoryKey(kind, territoryId), {
    expiresAt: Date.now() + CATALOG_CACHE_TTL_SECONDS * 1000,
    body,
  });
}

export async function readCatalogCache(
  kind: CatalogKind,
  requestUrl: URL,
): Promise<Response | undefined> {
  const territoryId = catalogTerritory(requestUrl);
  const fromMemory = readMemoryCatalog(kind, territoryId);
  if (fromMemory !== undefined) {
    return fromMemory;
  }

  const cached = await caches.default.match(catalogCacheRequest(kind, territoryId));
  if (cached === undefined) {
    return undefined;
  }

  const body = await cached.clone().text();
  writeMemoryCatalog(kind, territoryId, body);
  return cached;
}

export async function writeCatalogCache(
  kind: CatalogKind,
  requestUrl: URL,
  response: Response,
): Promise<void> {
  await caches.default.put(catalogCacheRequest(kind, catalogTerritory(requestUrl)), response.clone());
}

/** Store a catalog JSON body in isolate memory (Cache API write can run in waitUntil). */
export function rememberCatalogPayload(
  kind: CatalogKind,
  requestUrl: URL,
  payload: unknown,
): void {
  writeMemoryCatalog(kind, catalogTerritory(requestUrl), JSON.stringify(payload));
}

/** Drop cached catalogs after a pad is newly accepted or an admin review. */
export async function invalidateAcceptedCatalogCaches(): Promise<void> {
  memoryCatalogs.clear();
  const territories: Array<string | null> = [null, "1252", "1346"];
  const deletes: Promise<boolean>[] = [];
  for (const territoryId of territories) {
    deletes.push(caches.default.delete(catalogCacheRequest("coffers", territoryId)));
    deletes.push(caches.default.delete(catalogCacheRequest("carrots", territoryId)));
    deletes.push(caches.default.delete(catalogCacheRequest("pot-chests", territoryId)));
  }

  await Promise.all(deletes);
}
