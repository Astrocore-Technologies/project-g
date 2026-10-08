-- Character document v1.
CREATE TABLE project_g_characters (
    character_id uuid PRIMARY KEY,
    token_hash bytea NOT NULL UNIQUE CHECK (octet_length(token_hash) = 32),
    owner_id uuid NOT NULL,
    revision bigint NOT NULL CHECK (revision >= 0),
    model_version integer NOT NULL CHECK (model_version = 1),
    state jsonb NOT NULL CHECK (octet_length(state::text) <= 8192),
    updated_at timestamptz NOT NULL DEFAULT now()
);
