CREATE TABLE project_g_item_weapon_wear (
    instance_id uuid PRIMARY KEY,
    total_wear bigint NOT NULL CHECK(total_wear >= 0)
);
