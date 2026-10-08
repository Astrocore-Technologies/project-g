CREATE TABLE project_g_world_nodes (
    node_key TEXT PRIMARY KEY,
    owner_id uuid NOT NULL,
    revision bigint NOT NULL CHECK (revision >= 0),
    model_version integer NOT NULL,
    state jsonb NOT NULL
);
CREATE TABLE project_g_world_audit (
    node_key TEXT NOT NULL,
    revision bigint NOT NULL,
    ordinal INTEGER NOT NULL,
    actor TEXT NOT NULL,
    operation TEXT NOT NULL,
    reason TEXT NOT NULL,
    timestamp_ms bigint NOT NULL,
    PRIMARY KEY (node_key, revision, ordinal),
    FOREIGN KEY (node_key) REFERENCES project_g_world_nodes(node_key)
);
