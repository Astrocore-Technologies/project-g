@tool
extends Node
## Explicit editor actions. Never attached to the saved city and never runs at startup.
const BASE := "res://Assets/World/Linvel/City/Production/"
const COAST := "res://Scenes/Regions/Linvel/LinvelCity/Parts/CoastAndBackdrop.tscn"
const FIELD_ORIGIN := Vector2(-480,-240)
const FIELD_SIZE := Vector2(960,880)
const FIELD_PIXELS := 512
var shore_image: Image
var natural_land := PackedByteArray()
var heights := PackedFloat32Array()
var bake_report: Dictionary = {}

func reload_world() -> Dictionary:
	EditorInterface.reload_scene_from_path("res://Scenes/Regions/Linvel/LinvelCity/LinvelCity.tscn")
	EditorInterface.reload_scene_from_path.call_deferred("res://Scenes/World.tscn")
	return {"queued":true}

func load_shore() -> Dictionary:
	shore_image = (load(BASE+"Textures/coast_shore_field.res") as Texture2D).get_image()
	return {"width":shore_image.get_width()}

func begin_bake() -> Dictionary:
	bake_report = {"done":false,"stage":"native heights"}
	_bake_async.call_deferred()
	return bake_report

func _bake_async() -> void:
	var result := await bake_shore()
	bake_report = result
	bake_report["done"] = true

func _point(x: int, y: int) -> Vector2:
	return FIELD_ORIGIN + (Vector2(x,y)+Vector2(.5,.5))/float(FIELD_PIXELS)*FIELD_SIZE

func bake_shore() -> Dictionary:
	var terrain := get_parent().get_node("Terrain3D") as Terrain3D
	var layout: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://Assets/World/Linvel/City/Masterplan/layout.json"))
	var polygons: Array[PackedVector2Array] = []
	for platform in layout.platforms:
		var poly := PackedVector2Array()
		for p in platform.points: poly.append(Vector2(p[0],p[1]))
		polygons.append(poly)
	var barriers: Array = []
	for link in layout.links:
		if link.kind == "breakwater": barriers.append(link)
	var count := FIELD_PIXELS*FIELD_PIXELS
	var distance := PackedFloat32Array()
	distance.resize(count)
	natural_land.resize(count)
	heights.resize(count)
	var dx := FIELD_SIZE.x/FIELD_PIXELS
	var dy := FIELD_SIZE.y/FIELD_PIXELS
	var diagonal := Vector2(dx,dy).length()
	for y in FIELD_PIXELS:
		if y%16==0: await get_tree().process_frame
		for x in FIELD_PIXELS:
			var index := y*FIELD_PIXELS+x
			var p := _point(x,y)
			var h := terrain.data.get_height(Vector3(p.x,0,p.y))
			if is_nan(h): h = -16
			heights[index] = h
			natural_land[index] = 1 if h>=-.05 else 0
			var land := h>=-.05
			if not land:
				for poly in polygons:
					if Geometry2D.is_point_in_polygon(p,poly):
						land = true
						break
			if not land:
				for link in barriers:
					var a := Vector2(link.start[0],link.start[2])
					var b := Vector2(link.end[0],link.end[2])
					if p.distance_to(Geometry2D.get_closest_point_to_segment(p,a,b)) < float(link.width)*.5:
						land = true
						break
			distance[index] = 0 if land else 10000
	# Two-pass chamfer field, in world metres. Static authoring cost only.
	for y in FIELD_PIXELS:
		if y%32==0: await get_tree().process_frame
		for x in FIELD_PIXELS:
			var i := y*FIELD_PIXELS+x
			if x>0: distance[i] = minf(distance[i],distance[i-1]+dx)
			if y>0: distance[i] = minf(distance[i],distance[i-FIELD_PIXELS]+dy)
			if x>0 and y>0: distance[i] = minf(distance[i],distance[i-FIELD_PIXELS-1]+diagonal)
			if x<FIELD_PIXELS-1 and y>0: distance[i] = minf(distance[i],distance[i-FIELD_PIXELS+1]+diagonal)
	for y in range(FIELD_PIXELS-1,-1,-1):
		if y%32==0: await get_tree().process_frame
		for x in range(FIELD_PIXELS-1,-1,-1):
			var i := y*FIELD_PIXELS+x
			if x<FIELD_PIXELS-1: distance[i] = minf(distance[i],distance[i+1]+dx)
			if y<FIELD_PIXELS-1: distance[i] = minf(distance[i],distance[i+FIELD_PIXELS]+dy)
			if x<FIELD_PIXELS-1 and y<FIELD_PIXELS-1: distance[i] = minf(distance[i],distance[i+FIELD_PIXELS+1]+diagonal)
			if x>0 and y<FIELD_PIXELS-1: distance[i] = minf(distance[i],distance[i+FIELD_PIXELS-1]+diagonal)
	shore_image = Image.create(FIELD_PIXELS,FIELD_PIXELS,false,Image.FORMAT_RGBA8)
	var shallow_pixels := 0
	for y in FIELD_PIXELS:
		if y%32==0: await get_tree().process_frame
		for x in FIELD_PIXELS:
			var i := y*FIELD_PIXELS+x
			var p := _point(x,y)
			var depth := maxf(-heights[i],0)
			# Natural underwater shelves stay outside the deep working harbor.
			var working_harbor := p.x>-195 and p.x<120 and p.y>90 and p.y<250
			if natural_land[i]==0 and distance[i]>0 and distance[i]<30 and not working_harbor:
				depth = minf(depth,lerpf(.8,8.2,smoothstep(0,30,distance[i])))
				shallow_pixels += 1
			shore_image.set_pixel(x,y,Color(clampf(depth/16,0,1),clampf(distance[i]/32,0,1),1 if distance[i]==0 else 0,1))
	var texture := ImageTexture.create_from_image(shore_image)
	assert(ResourceSaver.save(texture,BASE+"Textures/coast_shore_field.res")==OK)
	shore_image.save_png(BASE+"Textures/coast_shore_field.png")
	return {"pixels":FIELD_PIXELS,"shallow_pixels":shallow_pixels,"bounds":str(Rect2(FIELD_ORIGIN,FIELD_SIZE)),"basis":"native Terrain3D + saved platform footprints + breakwaters"}

func _field(p: Vector2) -> Color:
	var uv := (p-FIELD_ORIGIN)/FIELD_SIZE*float(FIELD_PIXELS)
	return shore_image.get_pixel(clampi(int(uv.x),0,FIELD_PIXELS-1),clampi(int(uv.y),0,FIELD_PIXELS-1))

func _new_mesh(parent: Node3D, label: String, mesh: Mesh, material: Material) -> MeshInstance3D:
	var node := parent.get_node_or_null(label) as MeshInstance3D
	if not node:
		node = MeshInstance3D.new()
		node.name = label
		parent.add_child(node)
		node.owner = parent
	node.mesh = mesh
	node.material_override = material
	node.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	return node

func _shader(path: String) -> Shader:
	var shader := Shader.new()
	shader.code = FileAccess.get_file_as_string(path)
	shader.take_over_path(path)
	return shader

func build_water() -> Dictionary:
	assert(shore_image!=null,"Bake shore first")
	var part := ResourceLoader.load(COAST,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate() as Node3D
	var material := ShaderMaterial.new()
	material.shader = _shader(BASE+"Sea.gdshader")
	material.set_shader_parameter("shore_field",ResourceLoader.load(BASE+"Textures/coast_shore_field.res","",ResourceLoader.CACHE_MODE_REPLACE))
	material.set_shader_parameter("water_paint",load("res://Assets/World/Linvel/Beech/Revision2/StylePass/WaterColour.res"))
	material.set_shader_parameter("foam_paint",load("res://Assets/World/Linvel/Beech/Revision2/StylePass/WaterFoam.res"))
	material.resource_name = "Linvel painted sea — calm harbor / open coast"
	assert(ResourceSaver.save(material,BASE+"Sea.tres")==OK)
	material.take_over_path(BASE+"Sea.tres")
	var grid := PlaneMesh.new()
	grid.size = Vector2(800,640)
	grid.subdivide_width = 199
	grid.subdivide_depth = 159
	var arrays := grid.surface_get_arrays(0)
	var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
	var normals: PackedVector3Array = arrays[Mesh.ARRAY_NORMAL]
	var uvs: PackedVector2Array = arrays[Mesh.ARRAY_TEX_UV]
	var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
	for i in vertices.size(): vertices[i] += Vector3(0,0,200)
	for rect in [Rect2(-6000,-6000,5600,12000),Rect2(400,-6000,5600,12000),Rect2(-400,-6000,800,5880),Rect2(-400,520,800,5480)]:
		var a: Vector2 = rect.position
		var b: Vector2 = rect.end
		for p in [a,Vector2(b.x,a.y),b,a,b,Vector2(a.x,b.y)]:
			indices.append(vertices.size())
			vertices.append(Vector3(p.x,0,p.y))
			normals.append(Vector3.UP)
			uvs.append(Vector2.ZERO)
	arrays[Mesh.ARRAY_VERTEX] = vertices
	arrays[Mesh.ARRAY_NORMAL] = normals
	arrays[Mesh.ARRAY_TEX_UV] = uvs
	arrays[Mesh.ARRAY_INDEX] = indices
	arrays[Mesh.ARRAY_TANGENT] = null
	var continuous := ArrayMesh.new()
	continuous.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES,arrays)
	var ocean := part.get_node("OpenSea") as MeshInstance3D
	ocean.mesh = continuous
	ocean.material_override = material
	ocean.position = Vector3(0,-.05,0)
	ocean.extra_cull_margin = .5
	ocean.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	# One surface keeps depth sampling, transparency sorting and material identical.
	var old_far := part.get_node_or_null("FarSea")
	if old_far: old_far.free()
	var bed_material := ShaderMaterial.new()
	bed_material.shader = _shader(BASE+"CoastalSeabed.gdshader")
	bed_material.set_shader_parameter("sand_paint",load("res://Assets/World/Linvel/Beech/Revision2/StylePass/GroundPath.res"))
	bed_material.set_shader_parameter("rock_paint",load("res://Assets/World/Linvel/Beech/Revision2/StylePass/GroundRock.res"))
	bed_material.set_shader_parameter("water_paint",load("res://Assets/World/Linvel/Beech/Revision2/StylePass/WaterColour.res"))
	assert(ResourceSaver.save(bed_material,BASE+"CoastalSeabed.tres")==OK)
	bed_material.take_over_path(BASE+"CoastalSeabed.tres")
	var bed := SurfaceTool.new()
	bed.begin(Mesh.PRIMITIVE_TRIANGLES)
	var bed_triangles := 0
	for y in range(-120,520,4):
		for x in range(-400,400,4):
			var points := [Vector2(x,y),Vector2(x+4,y),Vector2(x+4,y+4),Vector2(x,y+4)]
			var valid := true
			for p in points:
				var f := _field(p)
				if f.b>.5 or f.r>=.505: valid = false
			if not valid: continue
			for i in [0,1,2,0,2,3]:
				var p: Vector2 = points[i]
				bed.set_uv(p*.1)
				bed.add_vertex(Vector3(p.x,-maxf(_field(p).r*16,.2),p.y))
			bed_triangles += 2
	bed.generate_normals()
	_new_mesh(part,"CoastalSeabed",bed.commit(),bed_material)
	part.set_meta("water_style_revision",1)
	var packed := PackedScene.new()
	assert(packed.pack(part)==OK)
	assert(ResourceSaver.save(packed,COAST)==OK)
	part.free()
	return {"water_triangles":64000,"far_triangles":8,"bed_triangles":bed_triangles,"material":BASE+"Sea.tres","runtime_generator":false}

func inspect() -> Dictionary:
	var root := get_parent()
	var rows: Dictionary = {}
	var examples: Dictionary = {}
	for node in root.find_children("*", "GeometryInstance3D", true, false):
		var material: Material = node.material_override
		var key := material.resource_path if material else "mesh_surface"
		rows[key] = int(rows.get(key, 0)) + 1
		if not examples.has(key): examples[key] = str(root.get_path_to(node))
	var terrain := root.get_node("Terrain3D") as Terrain3D
	var heights := []
	for p in [Vector3(-110,0,145),Vector3(0,0,160),Vector3(100,0,160),Vector3(140,0,110),Vector3(-180,0,220),Vector3(0,0,250)]:
		heights.append({"xz":str(p),"height":terrain.data.get_height(p)})
	return {"materials":rows,"examples":examples,"terrain_samples":heights}
