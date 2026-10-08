CREATE TABLE project_g_ground_items (
    instance_id uuid PRIMARY KEY,
    seed_id text NOT NULL UNIQUE,
    definition_id text NOT NULL,
    x real NOT NULL,
    z real NOT NULL,
    claimed_by uuid NULL REFERENCES project_g_characters(character_id)
);
