CREATE TABLE project_g_item_owners (instance_id uuid PRIMARY KEY, owner_key text NOT NULL);
CREATE INDEX project_g_item_owners_by_owner ON project_g_item_owners(owner_key);
INSERT INTO project_g_item_owners SELECT (item->>'InstanceId')::uuid, 'c:' || inventory.character_id::text
FROM project_g_inventory inventory, LATERAL jsonb_array_elements(inventory.state->'Items') item;
INSERT INTO project_g_item_owners SELECT instance_id, 'g:' || instance_id::text FROM project_g_ground_items WHERE claimed_by IS NULL;

INSERT INTO project_g_item_owners SELECT instance_id, 'd:' || instance_id::text FROM project_g_ground_items WHERE claimed_by IS NOT NULL AND instance_id NOT IN (SELECT instance_id FROM project_g_item_owners);
