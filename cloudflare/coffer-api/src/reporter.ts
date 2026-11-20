export function reporterKey(ip: string): string {
  if (!ip.includes(":")) {
    return ip;
  }

  const [head, tail] = ip.split("::", 2);
  const headGroups = head ? head.split(":") : [];
  const tailGroups = tail ? tail.split(":") : [];
  const groups = tail === undefined
    ? headGroups
    : [...headGroups, ...Array(Math.max(0, 8 - headGroups.length - tailGroups.length)).fill("0"), ...tailGroups];

  // Clients can rotate addresses inside their /64, so only the prefix identifies them.
  return groups.slice(0, 4).map(group => (parseInt(group, 16) || 0).toString(16)).join(":") + "::/64";
}

export async function reporterHash(request: Request, env: { REPORTER_SALT?: string }): Promise<string | null> {
  const ip = request.headers.get("CF-Connecting-IP");
  if (ip === null) {
    return null;
  }

  const bytes = new TextEncoder().encode(`${env.REPORTER_SALT ?? ""}|${reporterKey(ip.trim())}`);
  const digest = await crypto.subtle.digest("SHA-256", bytes);
  return [...new Uint8Array(digest)].map(b => b.toString(16).padStart(2, "0")).join("");
}
