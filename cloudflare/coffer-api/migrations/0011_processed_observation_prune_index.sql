CREATE INDEX IF NOT EXISTS idx_observations_processed_received
ON observations(processed, received_at_utc);

CREATE INDEX IF NOT EXISTS idx_carrot_observations_processed_received
ON carrot_observations(processed, received_at_utc);
