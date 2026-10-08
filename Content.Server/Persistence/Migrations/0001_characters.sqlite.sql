CREATE TABLE characters (
    character_id TEXT PRIMARY KEY,
    token_hash BLOB NOT NULL UNIQUE CHECK (length(token_hash) = 32),
    owner_id TEXT NOT NULL,
    revision INTEGER NOT NULL CHECK (revision >= 0),
    model_version INTEGER NOT NULL CHECK (model_version = 1),
    state TEXT NOT NULL CHECK (length(CAST(state AS BLOB)) <= 8192)
);
