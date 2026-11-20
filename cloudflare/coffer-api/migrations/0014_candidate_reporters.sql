ALTER TABLE observations ADD COLUMN reporter_hash TEXT;
ALTER TABLE carrot_observations ADD COLUMN reporter_hash TEXT;
ALTER TABLE pot_chest_observations ADD COLUMN reporter_hash TEXT;

-- Kept when old observations are pruned, so a returning reporter is not counted again.
CREATE TABLE candidate_reporters (
    kind TEXT NOT NULL CHECK (kind IN ('coffers', 'carrots', 'pot-chests')),
    candidate_id INTEGER NOT NULL,
    reporter_hash TEXT NOT NULL,
    first_seen_at_utc TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
    PRIMARY KEY (kind, candidate_id, reporter_hash)
) WITHOUT ROWID;

INSERT OR IGNORE INTO candidate_reporters (kind, candidate_id, reporter_hash)
SELECT 'coffers', candidate_id, installation_hash FROM observation_candidate_members;

INSERT OR IGNORE INTO candidate_reporters (kind, candidate_id, reporter_hash)
SELECT 'carrots', candidate_id, installation_hash FROM carrot_candidate_members;

INSERT OR IGNORE INTO candidate_reporters (kind, candidate_id, reporter_hash)
SELECT 'pot-chests', candidate_id, installation_hash FROM pot_chest_candidate_members;
