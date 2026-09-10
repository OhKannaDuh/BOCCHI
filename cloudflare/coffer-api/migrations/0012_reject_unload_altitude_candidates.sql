-- Layout / live objects at unload altitude cluster as accepted pads (Y −500 to −980).
-- Hamlet basement is around Y −162, so −250 keeps real pads and drops ghosts.
UPDATE observation_candidates
SET status = 'rejected',
    review_note = 'Unload / inside-floor altitude (centroid_y < -250).',
    reviewed_at_utc = CURRENT_TIMESTAMP,
    updated_at_utc = CURRENT_TIMESTAMP
WHERE status = 'accepted'
  AND centroid_y < -250;

UPDATE carrot_candidates
SET status = 'rejected',
    review_note = 'Unload / inside-floor altitude (centroid_y < -250).',
    reviewed_at_utc = CURRENT_TIMESTAMP,
    updated_at_utc = CURRENT_TIMESTAMP
WHERE status = 'accepted'
  AND centroid_y < -250;
