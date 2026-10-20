@tool
extends "res://Tools/Linvel/LinvelCityCenterAuthoring.gd"
## Explicit append-only authoring. The saved part and Terrain3D remain editable.
## Never re-run this to replace hand edits. No runtime generation or combat rules.

const TRAINING_PART := "res://Scenes/Regions/Linvel/Parts/TrainingQuarter.tscn"
const ARENA_CENTER := Vector2(77, -22)
const ARENA_RADII := Vector2(22.5, 17.5)

func collider(parent: Node3D, label: String, p: Vector3, dims: Vector3, yaw := 0.0) -> void:
	var body := StaticBody3D.new()
	own(parent, body, label)
	body.position = p
	body.rotation.y = yaw
	body.collision_layer = 4
	body.add_to_group("surface_geometry", true)
	var shape := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = dims
	shape.shape = box
	own(body, shape, "Shape")

func fence_segment(parent: Node3D, a: Vector2, b: Vector2, label: String) -> void:
	var delta := b - a
	var midpoint := (a + b) * 0.5
	var yaw := -atan2(delta.y, delta.x)
	var model := add_asset(parent, "orchard_fence", Vector3(midpoint.x, 10, midpoint.y), rad_to_deg(yaw), label)
	# Existing four-metre post spacing; caps overlap at each joint.
	model.scale.x = delta.length() / 4.0
	collider(parent, label + "Collision", Vector3(midpoint.x, 10.675, midpoint.y), Vector3(delta.length() + 0.18, 1.35, 0.24), yaw)

func arena(parent: Node3D) -> void:
	var rail := own(parent, Node3D.new(), "LowOvalFence") as Node3D
	for index in range(36):
		var a := TAU * float(index) / 36.0
		var b := TAU * float(index + 1) / 36.0
		var middle := (a + b) * 0.5
		var west_entry := cos(middle) < -0.93 and absf(sin(middle) * ARENA_RADII.y) < 4.5
		var south_entry := sin(middle) > 0.93 and absf(cos(middle) * ARENA_RADII.x) < 4.6
		if west_entry or south_entry:
			continue
		fence_segment(rail, ARENA_CENTER + Vector2(cos(a), sin(a)) * ARENA_RADII, ARENA_CENTER + Vector2(cos(b), sin(b)) * ARENA_RADII, "Rail%02d" % index)
	arena_details(parent)

func arena_details(parent: Node3D) -> void:
	var stand := add_asset(parent, "arena_covered_stand", Vector3(77, 10, -44.5), 0, "CoveredStand")
	stand.set_meta("source_library", "art-source/linvel/arena/arena_covered_stand.blend")
	# Match the six shallow aisle steps and seat tiers, preserving walkable access.
	collider(stand, "Plinth", Vector3(0, 0.09, 0), Vector3(8, 0.18, 4.4))
	for row in range(3):
		for side in [-1, 1]:
			var h := 0.26 + row * 0.3
			collider(stand, "Seat%d_%d" % [row, side], Vector3(side * 2.05, 0.18 + h * 0.5, -( -1.15 + row * 1.05)), Vector3(2.97, h + 0.09, 1.1))
		for sub in range(2):
			var h := 0.15 * (row * 2 + sub + 1)
			collider(stand, "Step%d_%d" % [row, sub], Vector3(0, 0.18 + h * 0.5, -(-1.15 + row * 1.05 - 0.26 + sub * 0.525)), Vector3(1.25, h, 0.54))
	for x in [-3.6, 3.6]:
		for z in [-1.8, 1.8]:
			collider(stand, "Post%s_%s" % [x,z], Vector3(x,1.75,z),Vector3(0.58,3.5,0.58))
	collider(stand, "RearGuard", Vector3(0, 1.45, -1.8), Vector3(7.35, 0.65, 0.16))
	for x in [66, 88]:
		add_asset(parent, "public_bench", Vector3(x, 10, -42), 0, "NorthBench%d" % x)
	for p in [Vector3(52,10,-27), Vector3(52,10,-17), Vector3(72,10,-1), Vector3(82,10,-1)]:
		add_asset(parent, "guild_banner_pole", p, 0, "EntryBanner%d" % parent.get_child_count())
	for p in [Vector3(59,10,-12), Vector3(95,10,-12)]:
		add_asset(parent, "training_weapon_rack", p, 0, "ArenaRack%d" % parent.get_child_count())

func dorn_yard(parent: Node3D) -> void:
	var pavilion := add_asset(parent, "dorn_pavilion", Vector3(76,10,2), 0, "DornPavilion")
	for x in [-2.8,2.8]:
		for z in [-1.55,1.55]:
			collider(pavilion,"Support%s_%s" % [x,z],Vector3(x,1.6,z),Vector3(0.4,3.2,0.4))
	for row in range(2):
		for col in range(3):
			var p := Vector3(66 + col * 9,10,11 + row * 10)
			var dummy := add_asset(parent,"training_dummy",p,180,"Dummy%d_%d" % [row,col])
			dummy.set_meta("presentation_only",true)
			collider(dummy,"DummyObstacle",Vector3(0,0.85,0),Vector3(0.72,1.7,0.6))
	for x in [69,83]:
		add_asset(parent,"training_weapon_rack",Vector3(x,10,5),0,"DornRack%d" % x)
	for index in range(7):
		fence_segment(parent,Vector2(62 + index * 4,27),Vector2(66 + index * 4,27),"SouthRail%d" % index)
	for index in range(5):
		fence_segment(parent,Vector2(90,7 + index * 4),Vector2(90,11 + index * 4),"EastRail%d" % index)
	for z in [12,21]:
		add_asset(parent,"public_bench",Vector3(94,10,z),-90,"RestBench%d" % z)

func extend_native_terrain(terrain: Terrain3D) -> Dictionary:
	# Existing core cells/heights are untouched, except the small paved connection.
	# Native Terrain3D data already covers this area as holes: expose only this part.
	var sand := Terrain3DTextureAsset.new()
	sand.id = 3
	sand.name = "TrainingSand"
	sand.albedo_texture = load(ASSETS + "Beech/Revision2/StylePass/GroundPath.res")
	sand.normal_texture = terrain.assets.get_texture(0).normal_texture
	sand.normal_depth = 0.0
	sand.ao_strength = 0.0
	sand.uv_scale = 0.2
	terrain.assets.set_texture(3,sand)
	var exposed := 0
	for z in range(-57,35):
		for x in range(44,113):
			var p := Vector3(x,0,z)
			if not terrain.data.get_control_hole(p):
				continue
			terrain.data.set_control_hole(p,false)
			terrain.data.set_height(p,lerpf(ground_height(43,z),10.0,smoothstep(43.0,53.0,x)))
			terrain.data.set_control_auto(p,false)
			terrain.data.set_control_navigation(p,true)
			var oval := Vector2((x - 77.0)/21.9,(z + 22.0)/16.9).length_squared() <= 1.0
			var yard := x >= 62 and x <= 89 and z >= 7 and z <= 26
			var paved := x >= 48 and x <= 104 and z >= -50 and z <= 30
			var road := x <= 54 and z >= -7 and z <= -1 or x >= 48 and x <= 54 and z >= -26 and z <= -1
			var layer := 3 if oval or yard else (1 if paved or road else 0)
			terrain.data.set_control_base_id(p,layer)
			exposed += 1
	for z in range(-7,0):
		for x in range(35,44):
			terrain.data.set_control_base_id(Vector3(x,0,z),1)
	terrain.data.update_maps(Terrain3DRegion.TYPE_MAX,true,true)
	terrain.data.save_directory(terrain.data_directory)
	ResourceSaver.save(terrain.assets,CITY + "Terrain/CenterAssets.tres")
	return {"new_cells":exposed,"existing_heights_changed":false,"connection_paint_cells":63}

func build_training_quarter() -> Dictionary:
	if ResourceLoader.exists(TRAINING_PART):
		return {"error":"Existing editable part: refusing to rebuild."}
	city = get_parent() as Node3D
	painted = load(CITY + "Materials/linvel_painted.tres")
	part = Node3D.new()
	part.name = "TrainingQuarter"
	part.set_meta("part_id","linvel-training-02")
	part.set_meta("review_status","Awaiting user review")
	part.set_meta("arena_surface_size_metres",Vector2(45,35))
	part.set_meta("gameplay_rules","Visual layout only; no combat or NPC authority")
	var arena_group := own(part,Node3D.new(),"TrainingArena") as Node3D
	arena(arena_group)
	if not arena_group.has_node("CoveredStand"):
		part.free()
		return {"error":"Import the arena stand before building. Nothing saved."}
	var yard := own(part,Node3D.new(),"DornYard") as Node3D
	dorn_yard(yard)
	var landscaping := own(part,Node3D.new(),"GreenBorder") as Node3D
	for p in [Vector3(47,10,-44),Vector3(106,10,-43),Vector3(106,10,12),Vector3(55,10,24)]:
		add_asset(landscaping,"orchard_tree",p,0,"Tree%d" % landscaping.get_child_count())
	for p in [Vector3(108,10,-18),Vector3(108,10,-14),Vector3(53,10,29),Vector3(57,10,30)]:
		add_asset(landscaping,"garden_shrub",p,0,"Shrub%d" % landscaping.get_child_count())
	var details := own(part,Node3D.new(),"StreetDetails") as Node3D
	for p in [Vector3(45,10,-9),Vector3(56,10,-4),Vector3(96,10,1),Vector3(99,10,-35)]:
		add_asset(details,"street_lantern",p,0,"Lantern%d" % details.get_child_count())
	var packed := PackedScene.new()
	var pack_error := packed.pack(part)
	if pack_error != OK:
		part.free()
		return {"error":pack_error}
	var save_error := ResourceSaver.save(packed,TRAINING_PART)
	part.free()
	if save_error != OK:
		return {"error":save_error}
	var terrain_report := extend_native_terrain(city.get_node("World/Terrain3D"))
	var instance: Node3D = load(TRAINING_PART).instantiate()
	city.get_node("World/CityParts").add_child(instance)
	instance.owner = city
	city.set_meta("linvel_city_stage","training-02-awaiting-review")
	city.get_node("World/CityParts/CityCenter").set_meta("review_status","Approved by user 2026-10-20")
	city.get_node("World/Terrain3D").set_meta("review_part","training-02")
	return {"saved":true,"part":TRAINING_PART,"placements":placements.size(),"terrain":terrain_report}

func finish_missing_arena_details() -> Dictionary:
	city = get_parent() as Node3D
	painted = load(CITY + "Materials/linvel_painted.tres")
	part = ResourceLoader.load(TRAINING_PART,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	var arena_group := part.get_node("TrainingArena") as Node3D
	if arena_group.has_node("CoveredStand"):
		part.free()
		return {"error":"Details already present; preserve manual edits."}
	if not ResourceLoader.exists(ASSETS + "arena_covered_stand.glb"):
		part.free()
		return {"error":"Import the stand first."}
	arena_details(arena_group)
	if not arena_group.has_node("CoveredStand"):
		part.free()
		return {"error":"Imported stand instantiation failed."}
	var packed := PackedScene.new()
	packed.pack(part)
	var error := ResourceSaver.save(packed,TRAINING_PART)
	part.free()
	if error == OK:
		reattach_saved_part()
	return {"saved":error==OK,"added":placements.size()}

func reattach_saved_part() -> Dictionary:
	city = get_parent() as Node3D
	var saved: PackedScene = ResourceLoader.load(TRAINING_PART,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE)
	var instance := saved.instantiate()
	if instance.scene_file_path != TRAINING_PART or not instance.has_node("TrainingArena/CoveredStand"):
		instance.free()
		return {"error":"Saved part is incomplete or has no source path."}
	var old := city.get_node_or_null("World/CityParts/TrainingQuarter")
	if old:
		old.get_parent().remove_child(old)
		old.free()
	city.get_node("World/CityParts").add_child(instance)
	instance.owner = city
	return {"source":instance.scene_file_path,"children":instance.get_child_count()}

func finish_review_access() -> Dictionary:
	city = get_parent() as Node3D
	var editing_part := city.scene_file_path == TRAINING_PART
	if editing_part:
		part = city
	elif city.has_node("World/CityParts"):
		part = ResourceLoader.load(TRAINING_PART,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	else:
		return {"error":"Open LinvelCity or TrainingQuarter before authoring."}
	var stand := part.get_node("TrainingArena/CoveredStand") as Node3D
	if not stand.has_node("WalkableAisle"):
		for child in stand.get_children():
			if child.name.begins_with("Step"):
				stand.remove_child(child)
				child.free()
		var body := StaticBody3D.new()
		own(stand,body,"WalkableAisle")
		body.collision_layer = 4
		body.add_to_group("surface_geometry",true)
		var shape := CollisionShape3D.new()
		var wedge := ConvexPolygonShape3D.new()
		var points := PackedVector3Array()
		for x in [-0.625,0.625]:
			points.append(Vector3(x,0.0,2.84))
			points.append(Vector3(x,0.0,-1.50))
			points.append(Vector3(x,1.24,-1.50))
		wedge.points = points
		shape.shape = wedge
		own(body,shape,"Shape")
	# Keep racks inside the oval, clear of its rail and both entry openings.
	var arena_group := part.get_node("TrainingArena")
	for child in arena_group.get_children():
		if child is Node3D and child.get_meta("linvel_asset_id","")=="training_weapon_rack":
			child.position.x = 62.0 if child.position.x < 77.0 else 92.0
	var pavilion := part.get_node("DornYard/DornPavilion")
	var index := 0
	for child in pavilion.get_children():
		if child is StaticBody3D and child.name.begins_with("Support"):
			child.position.x = -3.0 if index < 2 else 3.0
			child.position.z = -1.7 if index % 2 == 0 else 1.7
			index += 1
	if editing_part:
		return {"edited_live_part":true,"aisle":stand.has_node("WalkableAisle"),"supports":index}
	var packed := PackedScene.new()
	packed.pack(part)
	var error := ResourceSaver.save(packed,TRAINING_PART)
	part.free()
	if error == OK:
		return reattach_saved_part()
	return {"error":error}
