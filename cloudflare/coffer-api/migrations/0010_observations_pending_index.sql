DROP INDEX IF EXISTS idx_observations_unprocessed;

CREATE INDEX IF NOT EXISTS idx_observations_pending
ON observations(processed, id);
