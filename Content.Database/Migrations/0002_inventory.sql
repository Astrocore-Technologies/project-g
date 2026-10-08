-- Additive inventory v1; character rows remain unchanged.
CREATE TABLE project_g_inventory (
    character_id uuid PRIMARY KEY REFERENCES project_g_characters(character_id),
    model_version integer NOT NULL CHECK (model_version = 1),
    state jsonb NOT NULL CHECK (octet_length(state::text) <= 8192)
);
