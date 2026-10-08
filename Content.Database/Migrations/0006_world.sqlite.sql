CREATE TABLE world_nodes (
    node_key TEXT PRIMARY KEY,
    owner_id TEXT NOT NULL,
    revision INTEGER NOT NULL CHECK (revision >= 0),
    model_version INTEGER NOT NULL,
    state TEXT NOT NULL
);
CREATE TABLE world_audit (
    node_key TEXT NOT NULL,
    revision INTEGER NOT NULL,
    ordinal INTEGER NOT NULL,
    actor TEXT NOT NULL,
    operation TEXT NOT NULL,
    reason TEXT NOT NULL,
    timestamp_ms INTEGER NOT NULL,
    PRIMARY KEY (node_key, revision, ordinal),
    FOREIGN KEY (node_key) REFERENCES world_nodes(node_key)
);
