CREATE TABLE character_owned_echoes (
    character_id TEXT NOT NULL REFERENCES characters(character_id),
    instance_id TEXT NOT NULL UNIQUE,
    model_version INTEGER NOT NULL CHECK(model_version = 1),
    state TEXT NOT NULL CHECK(length(CAST(state AS BLOB)) <= 8192),
    PRIMARY KEY (character_id, instance_id)
);
