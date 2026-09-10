/** Isolate + Cache API for public accepted-catalog GETs (coffers / carrots). */

export const CATALOG_CACHE_TTL_SECONDS = 300;

type CatalogKind = "coffers" | "carrots";

interface MemoryEntry {
  expiresAt: number;
  body: string;
}

const memoryCatalogs = new Map<string, MemoryEntry>();

function memoryKey(
  kind: CatalogKind,
  territoryId: string | null,
  dataId: string | null,
): string {
  return `${kind}:${territoryId ?? ""}:${dataId ?? ""}`;
}

/** Stable synthetic URL — Cache API keys are Request URLs. */
function catalogCacheRequest(
  kind: CatalogKind,
  territoryId: string | null,
  dataId: string | null = null,
): Request {
  const url = new URL(`https://bocchi-catalog.internal/${kind}`);
  if (territoryId !== null) {
    url.searchParams.set("territoryId", territoryId);
  }

  if (dataId !== null) {
    url.searchParams.set("dataId", dataId);
  }

  return new Request(url.toString(), { method: "GET" });
}

function catalogParams(requestUrl: URL): {
  territoryId: string | null;
  dataId: string | null;
} {
  return {
    territoryId: requestUrl.searchParams.get("territoryId"),
    dataId: requestUrl.searchParams.get("dataId"),
  };
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

function readMemoryCatalog(
  kind: CatalogKind,
  territoryId: string | null,
  dataId: string | null,
): Response | undefined {
  const key = memoryKey(kind, territoryId, dataId);
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

function writeMemoryCatalog(
  kind: CatalogKind,
  territoryId: string | null,
  dataId: string | null,
  body: string,
): void {
  memoryCatalogs.set(memoryKey(kind, territoryId, dataId), {
    expiresAt: Date.now() + CATALOG_CACHE_TTL_SECONDS * 1000,
    body,
  });
}

export async function readCatalogCache(
  kind: CatalogKind,
  requestUrl: URL,
): Promise<Response | undefined> {
  const { territoryId, dataId } = catalogParams(requestUrl);
  const fromMemory = readMemoryCatalog(kind, territoryId, dataId);
  if (fromMemory !== undefined) {
    return fromMemory;
  }

  const cached = await caches.default.match(catalogCacheRequest(kind, territoryId, dataId));
  if (cached === undefined) {
    return undefined;
  }

  const body = await cached.clone().text();
  writeMemoryCatalog(kind, territoryId, dataId, body);
  return cached;
}

export async function writeCatalogCache(
  kind: CatalogKind,
  requestUrl: URL,
  response: Response,
): Promise<void> {
  const { territoryId, dataId } = catalogParams(requestUrl);
  await caches.default.put(catalogCacheRequest(kind, territoryId, dataId), response.clone());
}

/** Store a catalog JSON body in isolate memory (Cache API write can run in waitUntil). */
export function rememberCatalogPayload(
  kind: CatalogKind,
  requestUrl: URL,
  payload: unknown,
): void {
  const { territoryId, dataId } = catalogParams(requestUrl);
  writeMemoryCatalog(kind, territoryId, dataId, JSON.stringify(payload));
}

/** Drop cached catalogs after a pad is newly accepted or an admin review. */
export async function invalidateAcceptedCatalogCaches(): Promise<void> {
  memoryCatalogs.clear();
  const territories: Array<string | null> = [null, "1252", "1346"];
  const deletes: Promise<boolean>[] = [];
  for (const territoryId of territories) {
    deletes.push(caches.default.delete(catalogCacheRequest("coffers", territoryId)));
    deletes.push(caches.default.delete(catalogCacheRequest("carrots", territoryId)));
  }

  await Promise.all(deletes);
}
