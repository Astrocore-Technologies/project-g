CREATE TABLE project_g_owned_echoes (
    character_id uuid NOT NULL REFERENCES project_g_characters(character_id),
    instance_id uuid NOT NULL UNIQUE,
    model_version integer NOT NULL CHECK(model_version = 1),
    state jsonb NOT NULL CHECK(octet_length(state::text) <= 8192),
    PRIMARY KEY (character_id, instance_id)
);
