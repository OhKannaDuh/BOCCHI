/**
 * Secrets are not emitted by `wrangler types` (only wrangler.jsonc bindings).
 * Merge both ambient `Env` and `Cloudflare.Env` — generated Env extends
 * `__BaseEnv_Env` directly, not via Cloudflare.Env.
 */
interface Env {
  ADMIN_TOKEN?: string;
  REPORTER_SALT?: string;
}

declare namespace Cloudflare {
  interface Env {
    ADMIN_TOKEN?: string;
  REPORTER_SALT?: string;
  }
}
