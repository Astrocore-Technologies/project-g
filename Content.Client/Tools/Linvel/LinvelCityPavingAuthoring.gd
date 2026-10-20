@tool
extends Node
## Bake the existing library paving for Terrain3D. Never rebuild the city or its heights.

const ROOT := "res://Assets/World/Linvel/"
const OUTPUT := ROOT + "City/Terrain/"
const PART := "res://Scenes/Regions/Linvel/Parts/CityCenter.tscn"
var bake_status := "not-started"

func meshes_in(node: Node, output: Array[MeshInstance3D]) -> void:
	if node is MeshInstance3D:
		output.append(node)
	for child in node.get_children():
		meshes_in(child, output)

func bake_existing_paving() -> Dictionary:
	if ResourceLoader.exists(OUTPUT + "PavingAlbedo.res"):
		bake_status = "existing-source-preserved"
		return {"status": bake_status}
	if bake_status == "rendering":
		return {"status": bake_status}
	var terrain := get_parent().get_node("World/Terrain3D") as Terrain3D
	var reference: Texture2D = terrain.assets.get_texture(0).albedo_texture
	var size := reference.get_width()
	var reference_format := reference.get_image().get_format()
	var viewport := SubViewport.new()
	viewport.size = Vector2i(size, size)
	viewport.own_world_3d = true
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	add_child(viewport)
	var world := Node3D.new()
	viewport.add_child(world)
	var environment := WorldEnvironment.new()
	var settings := Environment.new()
	settings.background_mode = Environment.BG_COLOR
	settings.background_color = Color(0.43, 0.39, 0.31)
	environment.environment = settings
	world.add_child(environment)
	var source: Node3D = load(ROOT + "cobblestone_paving.glb").instantiate()
	world.add_child(source)
	var material := StandardMaterial3D.new()
	material.shading_mode = BaseMaterial3D.SHADING_MODE_UNSHADED
	material.albedo_texture = load(ROOT + "adventurers_guild_linvel_painted_atlas.png")
	var meshes: Array[MeshInstance3D] = []
	meshes_in(source, meshes)
	for mesh in meshes:
		mesh.material_override = material
	var camera := Camera3D.new()
	world.add_child(camera)
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.size = 2.0
	camera.position = Vector3(0, 4, 0)
	camera.look_at(Vector3.ZERO, Vector3.FORWARD)
	camera.current = true
	bake_status = "rendering"
	await RenderingServer.frame_post_draw
	await RenderingServer.frame_post_draw
	var image := viewport.get_texture().get_image()
	image.convert(reference_format)
	image.generate_mipmaps()
	var texture := ImageTexture.create_from_image(image)
	var error := ResourceSaver.save(texture, OUTPUT + "PavingAlbedo.res", ResourceSaver.FLAG_COMPRESS)
	image.save_png(ProjectSettings.globalize_path(OUTPUT + "PavingAlbedo.png"))
	viewport.queue_free()
	bake_status = "saved" if error == OK else "save-failed"
	return {"status": bake_status, "resolution": size, "format": reference_format, "error": error}

func apply_city_paving() -> Dictionary:
	if not ResourceLoader.exists(OUTPUT + "PavingAlbedo.res"):
		return {"error": "Bake the existing paving first."}
	var city := get_parent() as Node3D
	var terrain := city.get_node("World/Terrain3D") as Terrain3D
	var paving := Terrain3DTextureAsset.new()
	paving.name = "CityPaving"
	paving.id = 1
	paving.albedo_texture = load(OUTPUT + "PavingAlbedo.res")
	paving.normal_texture = terrain.assets.get_texture(0).normal_texture
	paving.uv_scale = 0.5 # The source module is 2 x 2 metres.
	paving.normal_depth = 0.0
	paving.ao_strength = 0.0
	terrain.assets.set_texture(1, paving)
	ResourceSaver.save(terrain.assets, OUTPUT + "CenterAssets.tres")
	var painted := 0
	for z in range(-35, 44):
		for x in range(-31, 35):
			var p := Vector3(x, 0, z)
			if terrain.data.get_control_hole(p):
				continue
			var in_block := z <= 21
			var tree_bed := x >= 10 and x <= 14 and z >= 8 and z <= 12
			if in_block or terrain.data.get_control_base_id(p) == 1:
				terrain.data.set_control_base_id(p, 0 if tree_bed else 1)
				painted += 1
	terrain.data.update_maps(Terrain3DRegion.TYPE_CONTROL, true, true)
	terrain.data.save_directory(terrain.data_directory)
	# Keep the old geometric paving available in the scene, but use the same native
	# Terrain3D layer for the square and streets. No duplicate floor is rendered.
	var part: Node3D = ResourceLoader.load(PART, "PackedScene", ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	var floor_node := part.get_node("TerraceAndPaving/PavingSurface0") as MultiMeshInstance3D
	var original := floor_node.multimesh
	var kept: Array[Transform3D] = []
	for index in range(original.instance_count):
		var placement := original.get_instance_transform(index)
		var p := placement.origin
		if p.x > 10 and p.x < 14 and p.z > 8 and p.z < 12:
			continue
		kept.append(placement)
	var modified: MultiMesh = original.duplicate()
	modified.instance_count = kept.size()
	for index in range(kept.size()):
		modified.set_instance_transform(index, kept[index])
	floor_node.multimesh = modified
	floor_node.visible = false
	if not part.has_meta("native_city_paving"):
		for group_name in ["MarketAndSquare", "StreetDetails"]:
			var group := part.get_node(group_name) as Node3D
			group.position.y -= 0.08
		part.set_meta("native_city_paving", true)
	var packed := PackedScene.new()
	packed.pack(part)
	var result := ResourceSaver.save(packed, PART)
	part.free()
	return {"saved": result == OK, "painted_cells": painted, "tree_bed_tiles_removed": original.instance_count - kept.size(), "heights_changed": false, "buildings_moved": false}
