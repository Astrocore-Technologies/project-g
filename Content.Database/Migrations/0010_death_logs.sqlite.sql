CREATE TABLE character_death_logs (
    character_id TEXT NOT NULL REFERENCES characters(character_id),
    death_id INTEGER NOT NULL CHECK(death_id > 0),
    model_version INTEGER NOT NULL CHECK(model_version = 1),
    state TEXT NOT NULL CHECK(length(CAST(state AS BLOB)) <= 262144),
    PRIMARY KEY(character_id, death_id)
);
