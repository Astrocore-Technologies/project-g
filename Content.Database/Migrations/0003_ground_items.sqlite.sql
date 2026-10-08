CREATE TABLE ground_items (
    instance_id TEXT PRIMARY KEY,
    seed_id TEXT NOT NULL UNIQUE,
    definition_id TEXT NOT NULL,
    x REAL NOT NULL,
    z REAL NOT NULL,
    claimed_by TEXT NULL REFERENCES characters(character_id)
);
