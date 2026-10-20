@tool
extends "res://Tools/Linvel/LinvelHarborAuthoring.gd"
## Local review correction, preserving existing placements and all other parts.

func add_plate(parent: Node3D, name_: String, from_: Vector2, to_: Vector2, height: float, material: Material) -> void:
	var size := to_-from_
	var center := (from_+to_)*0.5
	var floor_mesh := BoxMesh.new()
	floor_mesh.size = Vector3(size.x,0.16,size.y)
	var floor_node := MeshInstance3D.new()
	own(parent,floor_node,name_)
	floor_node.mesh = floor_mesh
	floor_node.material_override = material
	floor_node.position = Vector3(center.x,height-0.08,center.y)
	solid(parent,name_+"Collision",floor_node.position,floor_mesh.size,0,true)

func add_flat_quad(parent: Node3D, name_: String, points: Array[Vector3], material: Material, collision := false) -> void:
	var tool := SurfaceTool.new()
	tool.begin(Mesh.PRIMITIVE_TRIANGLES)
	for index in [0,1,2,0,2,3]:
		tool.add_vertex(points[index])
	tool.generate_normals()
	var mesh := tool.commit()
	var visual := MeshInstance3D.new()
	own(parent,visual,name_)
	visual.mesh = mesh
	visual.material_override = material
	if collision:
		var body := StaticBody3D.new()
		own(parent,body,name_+"Collision")
		body.collision_layer = 4
		body.add_to_group("surface_geometry",true)
		var shape := CollisionShape3D.new()
		shape.shape = mesh.create_trimesh_shape()
		own(body,shape,"Shape")

func rigid_floor_surfaces() -> void:
	if part.has_node("RigidPaving"):
		return
	var material := ShaderMaterial.new()
	material.shader = load(CITY+"Materials/RigidPaving.gdshader")
	material.set_shader_parameter("paving_texture",load(CITY+"Terrain/PavingAlbedo.res"))
	material.set_shader_parameter("tiles_per_metre",0.5)
	ResourceSaver.save(material,CITY+"Materials/RigidPaving.tres")
	material.take_over_path(CITY+"Materials/RigidPaving.tres")
	var floors := own(part,Node3D.new(),"RigidPaving") as Node3D
	add_plate(floors,"UpperWest",Vector2(-94,32),Vector2(-41.45,50.6),8.50,material)
	add_plate(floors,"StairHead",Vector2(-41.45,32),Vector2(-34.55,48.98),8.50,material)
	add_plate(floors,"UpperEast",Vector2(-34.55,32),Vector2(-26,50.6),8.50,material)
	add_plate(floors,"RampHead",Vector2(-26,32),Vector2(-15,43),8.50,material)
	add_flat_quad(floors,"StraightCartRamp",[Vector3(-26,8.50,43),Vector3(-26,6.985,58),Vector3(-15,6.985,58),Vector3(-15,8.50,43)],material,true)
	# All quay strips are flat; only their outline follows the bay.
	for index in range(20):
		var x1 := -94.0+index*3.95
		var x2 := x1+3.95
		add_flat_quad(floors,"LowerQuay%02d" % index,[Vector3(x1,6.985,52.2),Vector3(x1,6.985,shore_z(x1)),Vector3(x2,6.985,shore_z(x2)),Vector3(x2,6.985,52.2)],material)

func lighthouse_headland() -> Dictionary:
	var terrain := city.get_node("World/Terrain3D") as Terrain3D
	var modified := 0
	var center := Vector2(-104,64)
	for z in range(46,83):
		for x in range(-123,-85):
			var p := Vector3(x,0,z)
			var radius := Vector2(x,z).distance_to(center)
			if radius>17.0:
				continue
			var headland_height := lerpf(QUAY,3.5,smoothstep(14.5,17.0,radius))
			var previous := terrain.data.get_height(p)
			var h := maxf(previous,headland_height) if is_finite(previous) else headland_height
			terrain.data.set_control_hole(p,false)
			terrain.data.set_height(p,h)
			terrain.data.set_control_auto(p,false)
			terrain.data.set_control_navigation(p,h>SEA)
			terrain.data.set_control_base_id(p,1 if radius<11.0 else (0 if h>SEA else 3))
			modified += 1
	# Padding hidden seabed heights prevents tall hole-edge vertices at terrain LODs.
	for z in range(46,128):
		for x in range(-128,32):
			var p := Vector3(x,0,z)
			if terrain.data.get_control_hole(p):
				terrain.data.set_height(p,3.5)
	terrain.data.update_maps(Terrain3DRegion.TYPE_MAX,true,true)
	terrain.data.save_directory(terrain.data_directory)
	var tower := part.get_node("WaterfrontBuildings/HarborSignalTower") as Node3D
	tower.position = Vector3(-104,6.985,64)
	tower.scale = Vector3.ONE*3.0
	tower.set_meta("height_metres",33.21)
	tower.set_meta("role","Harbor lighthouse; enlarged by user request")
	var grounds := own(part,Node3D.new(),"LighthouseGrounds") as Node3D
	var material: Material = load(CITY+"Materials/RigidPaving.tres")
	var platform_mesh := CylinderMesh.new()
	platform_mesh.top_radius = 11.0
	platform_mesh.bottom_radius = 11.0
	platform_mesh.height = 0.16
	platform_mesh.radial_segments = 64
	var platform := MeshInstance3D.new()
	own(grounds,platform,"LighthousePlaza")
	platform.position = Vector3(-104,6.905,64)
	platform.mesh = platform_mesh
	platform.material_override = material
	var body := StaticBody3D.new()
	own(grounds,body,"PlazaCollision")
	body.position = platform.position
	body.collision_layer = 4
	body.add_to_group("surface_geometry",true)
	var shape := CollisionShape3D.new()
	var cylinder := CylinderShape3D.new()
	cylinder.radius = 11
	cylinder.height = 0.16
	shape.shape = cylinder
	own(body,shape,"Shape")
	var quay := part.get_node("QuayAndAccess")
	for label in ["QuayWall00","QuayCap00","QuayWall01","QuayCap01"]:
		var old := quay.get_node_or_null(label)
		if old:
			quay.remove_child(old)
			old.free()
	# Relocate cargo away from the expanded beacon forecourt.
	for child in part.get_node("HarborDetails").get_children():
		if child is Node3D and str(child.name).begins_with("CargoCrate"):
			child.position.x += 4
	return {"headland_cells":modified,"lighthouse_height_m":33.21,"plaza_diameter_m":22.0}

func finish_review_geometry() -> Dictionary:
	city = get_parent() as Node3D
	if city.scene_file_path != "res://Scenes/Regions/Linvel/LinvelCity.tscn":
		return {"error":"Open LinvelCity before this correction."}
	part = ResourceLoader.load(HARBOR_PART,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	if part.has_meta("review_geometry_v2"):
		part.free()
		return {"error":"Correction already applied; preserve manual edits."}
	rigid_floor_surfaces()
	var report := lighthouse_headland()
	part.set_meta("review_geometry_v2",true)
	var packed := PackedScene.new()
	packed.pack(part)
	var error := ResourceSaver.save(packed,HARBOR_PART)
	part.free()
	if error!=OK:
		return {"error":error}
	var old := city.get_node("World/CityParts/HarborQuarter")
	old.get_parent().remove_child(old)
	old.free()
	var saved: PackedScene = ResourceLoader.load(HARBOR_PART,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE)
	var instance := saved.instantiate()
	city.get_node("World/CityParts").add_child(instance)
	instance.owner = city
	return report
