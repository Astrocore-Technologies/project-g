-- Additive owner registry. A duplicate UUID aborts the migration; existing state is never reset.
CREATE TABLE item_owners (instance_id TEXT PRIMARY KEY, owner_key TEXT NOT NULL);
CREATE INDEX item_owners_by_owner ON item_owners(owner_key);
INSERT INTO item_owners SELECT lower(json_extract(item.value,'$.InstanceId')), 'c:' || inventory.character_id
FROM character_inventory inventory, json_each(inventory.state,'$.Items') item;
INSERT INTO item_owners SELECT instance_id, 'g:' || instance_id FROM ground_items WHERE claimed_by IS NULL;

INSERT INTO item_owners SELECT instance_id, 'd:' || instance_id FROM ground_items WHERE claimed_by IS NOT NULL AND instance_id NOT IN (SELECT instance_id FROM item_owners);
