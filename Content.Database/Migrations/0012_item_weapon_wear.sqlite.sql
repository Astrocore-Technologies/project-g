CREATE TABLE item_weapon_wear (
    instance_id TEXT PRIMARY KEY NOT NULL,
    total_wear INTEGER NOT NULL CHECK(total_wear >= 0)
);
