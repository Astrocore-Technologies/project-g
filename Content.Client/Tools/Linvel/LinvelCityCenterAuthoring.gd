@tool
extends Node
## Explicit first-placement tool. It refuses to rebuild an existing part or sculpted terrain.
## The saved .tscn and native Terrain3D files are the editable working sources.

const ASSETS := "res://Assets/World/Linvel/"
const CITY := ASSETS + "City/"
const PART := "res://Scenes/Regions/Linvel/Parts/CityCenter.tscn"
const TERRAIN_DATA := CITY + "Terrain/CenterData"
var city: Node3D
var part: Node3D
var painted: ShaderMaterial
var placements: Array[Dictionary] = []

func own(parent: Node, child: Node, label: String) -> Node:
	child.name = label
	parent.add_child(child)
	child.owner = part
	return child

func gather_meshes(node: Node, output: Array[MeshInstance3D]) -> void:
	if node is MeshInstance3D:
		output.append(node)
	for child in node.get_children():
		gather_meshes(child, output)

func inspect_assets() -> Array[Dictionary]:
	var result: Array[Dictionary] = []
	for id in ["adventurers_guild", "market_shop", "townhouse_a", "townhouse_b", "square_fountain", "cobblestone_paving", "terrace_stairs"]:
		var model: Node3D = load(ASSETS + id + ".glb").instantiate()
		var meshes: Array[MeshInstance3D] = []
		gather_meshes(model, meshes)
		var entry := {"asset": id, "mesh_parts": meshes.size(), "surfaces": 0}
		for mesh in meshes:
			entry.surfaces += mesh.mesh.get_surface_count()
		result.append(entry)
		model.free()
	return result

func add_asset(parent: Node, id: String, position: Vector3, yaw: float, label: String) -> Node3D:
	var model: Node3D = load(ASSETS + id + ".glb").instantiate()
	own(parent, model, label)
	model.position = position
	model.rotation_degrees.y = yaw
	model.set_meta("linvel_asset_id", id)
	model.set_meta("source_library", "art-source/linvel/linvel_asset_library.blend")
	# Mesh resources are shared; per-instance overrides preserve the existing source models.
	var meshes: Array[MeshInstance3D] = []
	gather_meshes(model, meshes)
	for mesh in meshes:
		mesh.material_override = painted
		mesh.gi_mode = GeometryInstance3D.GI_MODE_STATIC
	placements.append({"name": label, "asset": id, "position": position, "yaw": yaw})
	return model

func body_for(parent: Node3D, dimensions: Vector3, offset: Vector3) -> void:
	var body := StaticBody3D.new()
	own(parent, body, "BuildingCollision")
	body.position = offset
	body.collision_layer = 4
	body.add_to_group("surface_geometry", true)
	body.add_to_group("surface_occluder", true)
	body.set_meta("surface_nonwalkable_top", true)
	var shape := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = dimensions
	shape.shape = box
	own(body, shape, "Shape")

func ground_height(x: float, z: float) -> float:
	# Flat civic terrace, 1.52m lower approach. The east lane is a gentle ramp.
	var descent := smoothstep(20.7, 22.0, z)
	var ramp := smoothstep(13.0, 39.0, z)
	descent = lerpf(descent, ramp, smoothstep(25.0, 29.0, x))
	return 10.0 - 1.52 * descent

func walking_texture_id(x: float, z: float) -> int:
	var square := absf(x) <= 20.0 and absf(z) <= 18.0
	var main_path := absf(x - 31.0) < 4.0 or (absf(x) < 3.5 and z > 18.0)
	var guild_approach := absf(x) <= 8.0 and z >= -21.5 and z <= -18.0
	var north_link := x >= 8.0 and x <= 34.0 and z >= -20.5 and z <= -17.5
	var shop_fronts := x >= -23.5 and x <= -20.0 and absf(z) <= 18.0
	return 1 if square or main_path or guild_approach or north_link or shop_fronts else 0

func setup_terrain(terrain: Terrain3D) -> void:
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(TERRAIN_DATA))
	terrain.vertex_spacing = 1.0
	terrain.change_region_size(Terrain3D.SIZE_128)
	terrain.mesh_size = 48
	terrain.mesh_lods = 6
	terrain.collision.mode = Terrain3DCollision.FULL_GAME
	terrain.collision.layer = 1
	terrain.add_to_group("surface_terrain", true)
	var assets := Terrain3DAssets.new()
	var paint_source: Terrain3DAssets = load(ASSETS + "Beech/Revision2/VisualStyle/TerrainAssets.tres")
	for index in [0, 1, 2]:
		var source: Terrain3DTextureAsset = paint_source.get_texture(index)
		var texture: Terrain3DTextureAsset = source.duplicate()
		texture.id = index
		assets.set_texture(index, texture)
	if ResourceLoader.exists(CITY + "Terrain/PavingAlbedo.res"):
		var paving_texture := Terrain3DTextureAsset.new()
		paving_texture.id = 1
		paving_texture.name = "CityPaving"
		paving_texture.albedo_texture = load(CITY + "Terrain/PavingAlbedo.res")
		paving_texture.normal_texture = assets.get_texture(0).normal_texture
		paving_texture.uv_scale = 0.5
		paving_texture.normal_depth = 0.0
		paving_texture.ao_strength = 0.0
		assets.set_texture(1, paving_texture)
	terrain.assets = assets
	var material := Terrain3DMaterial.new()
	material.world_background = Terrain3DMaterial.NONE
	material.auto_shader = false
	material.dual_scaling = false
	material.shader_override = load(ASSETS + "Gardens/Terrain/painted_terrain.gdshader")
	material.shader_override_enabled = true
	material.set_shader_param("enable_macro_variation", false)
	material.set_shader_param("blend_sharpness", 0.35)
	terrain.material = material
	var heights := Image.create(256, 256, false, Image.FORMAT_RF)
	for iz in range(256):
		for ix in range(256):
			heights.set_pixel(ix, iz, Color(ground_height(ix - 128.0, iz - 128.0), 0, 0, 1))
	var maps: Array[Image] = [heights, null, null]
	terrain.data.import_images(maps, Vector3(-128, 0, -128), 0.0, 1.0)
	for iz in range(256):
		for ix in range(256):
			var p := Vector3(ix - 128.0, 0, iz - 128.0)
			var in_part := absf(p.x) <= 43.0 and p.z >= -40.0 and p.z <= 43.0
			terrain.data.set_control_hole(p, not in_part)
			if not in_part:
				continue
			terrain.data.set_control_auto(p, false)
			terrain.data.set_control_navigation(p, true)
			terrain.data.set_control_base_id(p, walking_texture_id(p.x, p.z))
	terrain.data.update_maps(Terrain3DRegion.TYPE_MAX, true, true)
	terrain.data.save_directory(TERRAIN_DATA)
	terrain.data_directory = TERRAIN_DATA
	ResourceSaver.save(assets, CITY + "Terrain/CenterAssets.tres")
	assets.take_over_path(CITY + "Terrain/CenterAssets.tres")
	ResourceSaver.save(material, CITY + "Terrain/CenterMaterial.tres")
	material.take_over_path(CITY + "Terrain/CenterMaterial.tres")
	var dependencies: Array[Resource] = []
	for file in DirAccess.get_files_at(TERRAIN_DATA):
		if file.ends_with(".res"):
			dependencies.append(load(TERRAIN_DATA.path_join(file)))
	terrain.set_meta("terrain_region_resources", dependencies)
	terrain.set_meta("review_part", "center-01")

func paving(parent: Node3D) -> void:
	# Reuse the existing paving mesh, including its imported LODs, in shared MultiMeshes.
	var source: Node3D = load(ASSETS + "cobblestone_paving.glb").instantiate()
	var meshes: Array[MeshInstance3D] = []
	gather_meshes(source, meshes)
	for index in range(meshes.size()):
		var mesh := meshes[index]
		var mm := MultiMesh.new()
		mm.transform_format = MultiMesh.TRANSFORM_3D
		mm.mesh = mesh.mesh
		mm.instance_count = 20 * 18
		for z in range(18):
			for x in range(20):
				var transform := Transform3D(Basis.IDENTITY, Vector3(-19.0 + x * 2.0, 10.005, -17.0 + z * 2.0))
				mm.set_instance_transform(z * 20 + x, transform * mesh.transform)
		var instance := MultiMeshInstance3D.new()
		instance.multimesh = mm
		instance.material_override = painted
		instance.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		own(parent, instance, "PavingSurface" + str(index))
	source.free()

func populate() -> void:
	var buildings := own(part, Node3D.new(), "Buildings") as Node3D
	var streets := own(part, Node3D.new(), "TerraceAndPaving") as Node3D
	var market := own(part, Node3D.new(), "MarketAndSquare") as Node3D
	var planting := own(part, Node3D.new(), "Planting") as Node3D
	var details := own(part, Node3D.new(), "StreetDetails") as Node3D
	var reserves := own(part, Node3D.new(), "NextParts") as Node3D
	var guild := add_asset(buildings, "adventurers_guild", Vector3(0, 10, -26), 0, "AdventurersGuild")
	body_for(guild, Vector3(9.0, 12.5, 9.0), Vector3(0, 6.25, -0.5))
	for entry in [["WeaponShop", -26.0, -7.5], ["ArmorShop", -26.0, 7.5]]:
		var shop := add_asset(buildings, "market_shop", Vector3(entry[1], 10, entry[2]), 90, entry[0])
		body_for(shop, Vector3(5.8, 6.2, 5.6), Vector3(0, 3.1, -0.4))
	var house := add_asset(buildings, "townhouse_a", Vector3(26, 10, -11), -90, "EastCornerHouse")
	body_for(house, Vector3(6.2, 7.0, 5.5), Vector3(0, 3.5, 0))
	var narrow := add_asset(buildings, "townhouse_b", Vector3(25, 10, 4), -90, "EastLaneHouse")
	body_for(narrow, Vector3(4.8, 9.0, 5.8), Vector3(0, 4.5, 0))
	paving(streets)
	add_asset(market, "square_fountain", Vector3(-3, 10.09, 0), 0, "SquareFountain")
	# Stalls flank the west edge; no cart or decoration blocks the main crossing.
	add_asset(market, "market_bread", Vector3(-14, 10.09, -9), 90, "BreadStall")
	add_asset(market, "market_fruit", Vector3(-14, 10.09, 7), 90, "FruitStall")
	for entry in [["WestBench", -9.0, -5.0, 0.0], ["SouthBench", -3.0, 7.0, 180.0], ["EastBench", 6.0, 1.0, -90.0]]:
		add_asset(details, "public_bench", Vector3(entry[1], 10.09, entry[2]), entry[3], entry[0])
	for entry in [[-7, -19], [7, -19]]:
		add_asset(details, "guild_banner_pole", Vector3(entry[0], 10.09, entry[1]), 0, "GuildStandard" + str(entry[0]))
	for entry in [[-19, -16], [19, -16], [-19, 16], [19, 16]]:
		add_asset(details, "street_lantern", Vector3(entry[0], 10.09, entry[1]), 0, "Lantern" + str(entry[0]) + "_" + str(entry[1]))
	for entry in [[-7.5, -20], [7.5, -20], [-19, -1], [18, 8], [17, -9]]:
		add_asset(details, "flower_planter", Vector3(entry[0], 10.09, entry[1]), 0, "Planter" + str(entry[0]) + "_" + str(entry[1]))
	for entry in [[12, 10], [-35, -21], [35, -26], [-34, 29], [18, 33]]:
		var tree := add_asset(planting, "orchard_tree", Vector3(entry[0], ground_height(entry[0], entry[1]), entry[1]), 18, "OrchardTree" + str(entry[0]) + "_" + str(entry[1]))
		tree.set_meta("foliage_for_review", true)
	for entry in [[-34, -16], [-34, -12], [37, -22], [37, -18], [19, 33], [22, 33]]:
		add_asset(planting, "garden_shrub", Vector3(entry[0], ground_height(entry[0], entry[1]), entry[1]), 0, "Shrub" + str(entry[0]) + "_" + str(entry[1]))
	# The source stair rises 8 * .19m = 1.52m; its front already points south (+Z).
	for x in [-1.7, 1.7]:
		add_asset(streets, "terrace_stairs", Vector3(x, 8.48, 22.6), 0, "SouthStairs" + str(x))
	for x in [-18.0, -14.0, -10.0, -6.0, 6.0, 10.0, 14.0, 18.0, 22.0]:
		add_asset(streets, "terrace_retaining_wall", Vector3(x, 7.04, 21.1), 0, "SouthRetainingWall" + str(x))
		add_asset(streets, "stone_balustrade", Vector3(x, 10.04, 20.9), 0, "SouthBalustrade" + str(x))
	for entry in [["TavernReserve", Vector3(0, 8.48, 36)], ["HarborApproach", Vector3(-35, 8.48, 36)], ["ArenaAndDornApproach", Vector3(39, 10, -4)]]:
		var marker := Marker3D.new()
		own(reserves, marker, entry[0])
		marker.position = entry[1]
		marker.set_meta("not_built_in_part_01", true)
	part.set_meta("part_id", "linvel-center-01")
	part.set_meta("review_status", "Awaiting user review; do not extend automatically")
	part.set_meta("square_size_metres", Vector2(40, 36))

func build_first_slice() -> Dictionary:
	city = get_parent() as Node3D
	if city.name != "LinvelCity":
		return {"error": "Run only under LinvelCity."}
	if city.has_node("World/CityParts") or FileAccess.file_exists(PART):
		return {"error": "Existing part preserved. Edit its scene manually; no rebuild."}
	var terrain := city.get_node("World/Terrain3D") as Terrain3D
	if terrain.data.get_region_count() != 0 or not terrain.data_directory.is_empty():
		return {"error": "Existing Terrain3D sculpt preserved; initial import refused."}
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(CITY + "Materials"))
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(CITY + "Terrain"))
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(PART.get_base_dir()))
	painted = ShaderMaterial.new()
	painted.shader = load(CITY + "Materials/linvel_painted.gdshader")
	painted.set_shader_parameter("albedo_texture", load(ASSETS + "adventurers_guild_linvel_painted_atlas.png"))
	ResourceSaver.save(painted, CITY + "Materials/linvel_painted.tres")
	painted.take_over_path(CITY + "Materials/linvel_painted.tres")
	setup_terrain(terrain)
	part = Node3D.new()
	part.name = "CityCenter"
	populate()
	var packed := PackedScene.new()
	var error := packed.pack(part)
	if error != OK:
		part.free()
		return {"error": "Could not pack city part.", "code": error}
	error = ResourceSaver.save(packed, PART)
	part.free()
	if error != OK:
		return {"error": "Could not save city part.", "code": error}
	var holder := Node3D.new()
	holder.name = "CityParts"
	city.get_node("World").add_child(holder)
	holder.owner = city
	var instance: Node3D = load(PART).instantiate()
	holder.add_child(instance)
	instance.owner = city
	city.set_meta("linvel_city_stage", "center-01-awaiting-review")
	city.set_meta("linvel_city_next", "Continue only after user review")
	return {"part": PART, "placements": placements.size(), "terrain_regions": terrain.data.get_region_count(), "height_range": terrain.data.get_height_range(), "surface_extent": "86 x 83 m", "server_connected": false}

func validate_first_slice() -> Dictionary:
	city = get_parent() as Node3D
	var terrain := city.get_node("World/Terrain3D") as Terrain3D
	var problems: Array[String] = []
	var samples: Array[Dictionary] = []
	for p in [Vector3(0, 0, 0), Vector3(0, 0, -26), Vector3(-26, 0, -7.5), Vector3(26, 0, -11), Vector3(0, 0, 30), Vector3(31, 0, 30)]:
		var height := terrain.data.get_height(p)
		var expected := ground_height(p.x, p.z)
		if not is_finite(height) or absf(height - expected) > 0.01:
			problems.append("Terrain mismatch at " + str(p))
		samples.append({"x": p.x, "z": p.z, "height": height, "expected": expected})
	var root := city.get_node_or_null("World/CityParts/CityCenter")
	if root == null:
		problems.append("Missing editable center instance")
	return {"ok": problems.is_empty(), "problems": problems, "terrain_samples": samples, "terrain_data": terrain.data_directory, "regions": terrain.data.get_region_count()}

func add_lighting() -> Dictionary:
	city = get_parent() as Node3D
	if city.has_node("World/Lighting"):
		return {"preserved": true}
	var lighting := Node3D.new()
	lighting.name = "Lighting"
	city.get_node("World").add_child(lighting)
	lighting.owner = city
	var world := WorldEnvironment.new()
	world.name = "SpringAtmosphere"
	lighting.add_child(world)
	world.owner = city
	var environment := Environment.new()
	var sky := Sky.new()
	var sky_material := ProceduralSkyMaterial.new()
	sky_material.sky_top_color = Color(0.30, 0.56, 0.77)
	sky_material.sky_horizon_color = Color(0.77, 0.85, 0.83)
	sky_material.ground_bottom_color = Color(0.45, 0.52, 0.39)
	sky_material.ground_horizon_color = Color(0.77, 0.85, 0.83)
	sky.sky_material = sky_material
	environment.sky = sky
	environment.background_mode = Environment.BG_SKY
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.ambient_light_color = Color(0.78, 0.84, 0.91)
	environment.ambient_light_energy = 0.35
	environment.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	environment.tonemap_exposure = 1.0
	world.environment = environment
	var sun := DirectionalLight3D.new()
	sun.name = "SpringSun"
	lighting.add_child(sun)
	sun.owner = city
	sun.rotation_degrees = Vector3(-52, -35, 0)
	sun.light_color = Color(1.0, 0.92, 0.79)
	sun.light_energy = 1.0
	sun.shadow_enabled = true
	sun.directional_shadow_max_distance = 150.0
	return {"created": true}

func finish_review_surfaces() -> Dictionary:
	city = get_parent() as Node3D
	var terrain := city.get_node("World/Terrain3D") as Terrain3D
	# Reuse the game's existing painted grass/path rather than the old diagnostic tiles.
	var forest: Terrain3DAssets = load(ASSETS + "Beech/Revision2/VisualStyle/TerrainAssets.tres")
	# Copy the complete compatible set (albedo + normal, resolution and format).
	# Mixing diagnostic 256px textures with the painted set breaks terrain arrays.
	for index in [0, 1, 2]:
		if index == 1 and terrain.assets.get_texture(index).name == "CityPaving":
			continue
		var texture: Terrain3DTextureAsset = forest.get_texture(index).duplicate()
		texture.id = index
		terrain.assets.set_texture(index, texture)
	ResourceSaver.save(terrain.assets, CITY + "Terrain/CenterAssets.tres")
	# Add only the missing colliders; never regenerate or reposition the saved art.
	part = load(PART).instantiate()
	for stair in part.get_node("TerraceAndPaving").get_children():
		if not str(stair.name).begins_with("SouthStairs") or stair.has_node("WalkableRamp"):
			continue
		var body := StaticBody3D.new()
		own(stair, body, "WalkableRamp")
		body.collision_layer = 1
		body.add_to_group("surface_geometry", true)
		var shape := CollisionShape3D.new()
		var wedge := ConvexPolygonShape3D.new()
		wedge.points = PackedVector3Array([Vector3(-1.42, -0.1, -1.6), Vector3(1.42, -0.1, -1.6), Vector3(-1.42, -0.1, 1.6), Vector3(1.42, -0.1, 1.6), Vector3(-1.42, 1.52, -1.6), Vector3(1.42, 1.52, -1.6), Vector3(-1.42, 0.02, 1.6), Vector3(1.42, 0.02, 1.6)])
		shape.shape = wedge
		own(body, shape, "Shape")
	for rail in part.get_node("TerraceAndPaving").get_children():
		if str(rail.name).begins_with("SouthBalustrade") and not rail.has_node("BuildingCollision"):
			body_for(rail, Vector3(4.0, 1.1, 0.45), Vector3(0, 0.55, 0))
	var fountain := part.get_node("MarketAndSquare/SquareFountain") as Node3D
	if not fountain.has_node("BasinCollision"):
		var body := StaticBody3D.new()
		own(fountain, body, "BasinCollision")
		body.collision_layer = 4
		body.add_to_group("surface_geometry", true)
		var shape := CollisionShape3D.new()
		var basin := CylinderShape3D.new()
		basin.radius = 2.85
		basin.height = 0.65
		shape.shape = basin
		shape.position.y = 0.325
		own(body, shape, "Shape")
	var packed := PackedScene.new()
	packed.pack(part)
	var error := ResourceSaver.save(packed, PART)
	part.free()
	return {"saved": error == OK, "native_terrain_sculpt_preserved": true, "source_models_changed": false}

func correct_rock_walkway() -> Dictionary:
	city = get_parent() as Node3D
	var terrain := city.get_node("World/Terrain3D") as Terrain3D
	var repainted := 0
	# Only fix the erroneous rock underlay of this square. Preserve all heights,
	# holes, placement edits and any cells already repainted by hand to another layer.
	for z in range(-22, 22):
		for x in range(-43, 44):
			var p := Vector3(x, 0, z)
			if terrain.data.get_control_base_id(p) != 2:
				continue
			terrain.data.set_control_base_id(p, walking_texture_id(x, z))
			repainted += 1
	terrain.data.update_maps(Terrain3DRegion.TYPE_CONTROL, true, true)
	terrain.data.save_directory(terrain.data_directory)
	return {"repainted_cells": repainted, "heights_changed": false, "placements_changed": false, "terrain_data": terrain.data_directory}
