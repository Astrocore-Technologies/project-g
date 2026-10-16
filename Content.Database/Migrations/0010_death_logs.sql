CREATE TABLE project_g_death_logs (
    character_id uuid NOT NULL REFERENCES project_g_characters(character_id),
    death_id bigint NOT NULL CHECK(death_id > 0),
    model_version integer NOT NULL CHECK(model_version = 1),
    state jsonb NOT NULL CHECK(octet_length(state::text) <= 262144),
    PRIMARY KEY(character_id, death_id)
);
