CREATE TABLE character_echoes (
    character_id TEXT PRIMARY KEY REFERENCES characters(character_id),
    model_version INTEGER NOT NULL CHECK(model_version = 1),
    state TEXT NOT NULL CHECK(length(CAST(state AS BLOB)) <= 8192)
);
