@tool
extends "res://Tools/Linvel/LinvelArtFinish.gd"
## Enclosed unbuilt pockets between rigid city terraces become level courtyards.
## Native Terrain3D remains outside and beneath them. Explicit editor action only.
const PLAN := "res://Assets/World/Linvel/City/Production/CityCourtyardPlan.json"
var blocked: Dictionary = {}
var outside: Dictionary = {}
var pockets: Array = []

func active_floor(p: Vector2) -> int:
	for i in range(polygons.size()-1,-1,-1):
		if i in [8,9,11]: continue
		if Geometry2D.is_point_in_polygon(p,polygons[i]): return i
	return -1

func route_reserved(p: Vector2) -> bool:
	for road in layout.streets+layout.links:
		if road.get("kind","") in ["bridge","pier","breakwater"]: continue
		var points: Array = road.get("points",[road.get("start"),road.get("end")])
		for i in range(points.size()-1):
			var a := Vector2(points[i][0],points[i][2])
			var b := Vector2(points[i+1][0],points[i+1][2])
			var t := (p-a).dot(b-a)/(b-a).length_squared()
			if t>=0 and t<=1 and p.distance_to(a+(b-a)*t)<float(road.width)*.5+.6: return true
	return false

func build_plan() -> Dictionary:
	load_layout()
	blocked.clear()
	outside.clear()
	pockets.clear()
	var all_points := PackedVector2Array()
	for i in polygons.size():
		if i not in [8,9,11]: all_points.append_array(polygons[i])
	var hull := Geometry2D.convex_hull(all_points)
	var cells := {}
	for z in range(-168,146,2):
		for x in range(-180,188,2):
			var key := Vector2i(x,z)
			var p := Vector2(x+1,z+1)
			if not Geometry2D.is_point_in_polygon(p,hull):
				outside[key] = true
				continue
			var index := active_floor(p)
			if index>=0 or route_reserved(p):
				blocked[key] = index
			else:
				cells[key] = true
	var directions := [Vector2i(2,0),Vector2i(-2,0),Vector2i(0,2),Vector2i(0,-2)]
	while not cells.is_empty():
		var seed: Vector2i = cells.keys()[0]
		var queue: Array[Vector2i] = [seed]
		cells.erase(seed)
		var head := 0
		var boundary := false
		var height := INF
		var owner_part := "CityCenter"
		while head<queue.size():
			var key := queue[head]
			head += 1
			for d in directions:
				var next: Vector2i = key+d
				if outside.has(next) or next.x< -180 or next.x>=188 or next.y< -168 or next.y>=146:
					boundary = true
				if blocked.has(next) and int(blocked[next])>=0:
					var platform: Dictionary = layout.platforms[int(blocked[next])]
					if float(platform.height)<height:
						height = float(platform.height)
						owner_part = platform.part
				if cells.has(next):
					cells.erase(next)
					queue.append(next)
		if not boundary and is_finite(height):
			var packed_cells: Array = []
			for cell in queue: packed_cells.append([cell.x,cell.y])
			pockets.append({"part":owner_part,"height":height,"cells":packed_cells,"area_m2":queue.size()*4})
	return {"pockets":pockets,"total_area_m2":pockets.reduce(func(sum,p): return sum+p.area_m2,0)}

func plan_summary() -> Dictionary:
	var report := build_plan()
	var summary: Array = []
	for pocket in pockets: summary.append({"part":pocket.part,"height":pocket.height,"area_m2":pocket.area_m2})
	var file := FileAccess.open("res://../.artifacts/linvel-surface-repair/courtyard-plan.json",FileAccess.WRITE)
	file.store_string(JSON.stringify(report,"\t"))
	return {"pockets":summary,"total_area_m2":report.total_area_m2}

func emit_cell(st: SurfaceTool, x: float, z: float, height: float) -> void:
	var pieces: Array[PackedVector2Array] = [PackedVector2Array([Vector2(x,z),Vector2(x+2,z),Vector2(x+2,z+2),Vector2(x,z+2)])]
	for i in polygons.size():
		if i in [8,9,11]: continue
		var clipped: Array[PackedVector2Array] = []
		for piece in pieces: clipped.append_array(Geometry2D.clip_polygons(piece,polygons[i]))
		pieces = clipped
	for piece in pieces:
		var order := Geometry2D.triangulate_polygon(piece)
		for i in range(0,order.size(),3):
			var a := piece[order[i]]
			var b := piece[order[i+1]]
			var c := piece[order[i+2]]
			if (b-a).cross(c-a)<0:
				var swap := b
				b = c
				c = swap
			st.set_normal(Vector3.UP)
			st.set_uv(a*.045)
			st.add_vertex(Vector3(a.x,height,a.y))
			st.set_uv(b*.045)
			st.add_vertex(Vector3(b.x,height,b.y))
			st.set_uv(c*.045)
			st.add_vertex(Vector3(c.x,height,c.y))

func apply_infill() -> Dictionary:
	load_layout()
	if city.scene_file_path!=REVIEW+"LinvelCity_Art01.tscn" or city.get_meta("courtyard_infill",false): return {"error":"Unexpected scene or already filled"}
	var plan := build_plan()
	var terrain := city.get_node("World/Terrain3D") as Terrain3D
	var texture := load(BASE+"CityFoundationMask.res") as ImageTexture
	var image := texture.get_image()
	var material := StandardMaterial3D.new()
	material.albedo_texture = load(BASE+"Textures/grass_paint.res")
	material.diffuse_mode = BaseMaterial3D.DIFFUSE_TOON
	material.roughness = 1
	var by_district := {}
	for i in pockets.size():
		var pocket: Dictionary = pockets[i]
		if not by_district.has(pocket.part):
			by_district[pocket.part] = ResourceLoader.load(REVIEW+"Parts/"+pocket.part+".tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
		part = by_district[pocket.part]
		var group := part.get_node_or_null("LevelCourtyards") as Node3D
		if group==null: group = own(part,Node3D.new(),"LevelCourtyards") as Node3D
		var st := SurfaceTool.new()
		st.begin(Mesh.PRIMITIVE_TRIANGLES)
		for cell in pocket.cells:
			emit_cell(st,cell[0],cell[1],pocket.height+.015)
			for z in range(cell[1],cell[1]+2):
				for x in range(cell[0],cell[0]+2): image.set_pixel(x+256,z+256,Color.WHITE)
			for offset in [Vector2i(0,0),Vector2i(2,0),Vector2i(0,2),Vector2i(2,2)]:
				var p := Vector3(cell[0]+offset.x,0,cell[1]+offset.y)
				terrain.data.set_height(p,pocket.height-.8)
		st.index()
		var mesh := st.commit()
		var node := own(group,MeshInstance3D.new(),"Courtyard_%02d"%i) as MeshInstance3D
		node.mesh = mesh
		node.material_override = material
		node.set_meta("ground_height_m",pocket.height)
		node.set_meta("source_plan",PLAN)
		var body := own(group,StaticBody3D.new(),"CourtyardGround_%02d"%i) as StaticBody3D
		body.collision_layer = 1
		body.collision_mask = 0
		var shape := own(body,CollisionShape3D.new(),"Shape") as CollisionShape3D
		shape.shape = mesh.create_trimesh_shape()
	for label in by_district:
		part = by_district[label]
		var packed := PackedScene.new()
		assert(packed.pack(part)==OK)
		assert(ResourceSaver.save(packed,REVIEW+"Parts/"+label+".tscn")==OK)
		part.free()
	texture.update(image)
	assert(ResourceSaver.save(texture,BASE+"CityFoundationMask.res",ResourceSaver.FLAG_COMPRESS)==OK)
	image.save_png(BASE+"Textures/city_foundation_mask.png")
	terrain.material.set_shader_param("city_foundation_mask",texture)
	assert(ResourceSaver.save(terrain.material,BASE+"CityTerrainMaterial.tres")==OK)
	terrain.data.update_maps(Terrain3DRegion.TYPE_MAX,true,true)
	terrain.data.save_directory(terrain.data_directory)
	var file := FileAccess.open(PLAN,FileAccess.WRITE)
	file.store_string(JSON.stringify(plan,"\t"))
	city.set_meta("courtyard_infill",true)
	return {"pockets":pockets.size(),"area_m2":plan.total_area_m2,"districts":by_district.keys()}

func finish_training_verge() -> Dictionary:
	load_layout()
	if city.get_meta("training_verge_finished",false): return {"error":"Already finished"}
	part = ResourceLoader.load(REVIEW+"Parts/UpperTown.tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	var triangle := PackedVector2Array([Vector2(60,-151),Vector2(90,-151),Vector2(86,-113)])
	var cells: Array = []
	for z in range(-152,-112,2):
		for x in range(60,92,2):
			var p := Vector2(x+1,z+1)
			if Geometry2D.is_point_in_polygon(p,triangle) and active_floor(p)<0 and not route_reserved(p): cells.append([x,z])
	var terrain := city.get_node("World/Terrain3D") as Terrain3D
	var texture := load(BASE+"CityFoundationMask.res") as ImageTexture
	var image := texture.get_image()
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	for cell in cells:
		emit_cell(st,cell[0],cell[1],32.015)
		for z in range(cell[1],cell[1]+2):
			for x in range(cell[0],cell[0]+2): image.set_pixel(x+256,z+256,Color.WHITE)
		for offset in [Vector2i(0,0),Vector2i(2,0),Vector2i(0,2),Vector2i(2,2)]: terrain.data.set_height(Vector3(cell[0]+offset.x,0,cell[1]+offset.y),31.2)
	st.index()
	var mesh := st.commit()
	var group := part.get_node_or_null("LevelCourtyards") as Node3D
	if group==null: group = own(part,Node3D.new(),"LevelCourtyards") as Node3D
	var node := own(group,MeshInstance3D.new(),"TrainingSideGarden") as MeshInstance3D
	node.mesh = mesh
	var material := StandardMaterial3D.new()
	material.albedo_texture = load(BASE+"Textures/grass_paint.res")
	material.diffuse_mode = BaseMaterial3D.DIFFUSE_TOON
	material.roughness = 1
	node.material_override = material
	var body := own(group,StaticBody3D.new(),"TrainingGardenGround") as StaticBody3D
	body.collision_layer = 1
	body.collision_mask = 0
	var shape := own(body,CollisionShape3D.new(),"Shape") as CollisionShape3D
	shape.shape = mesh.create_trimesh_shape()
	var packed := PackedScene.new()
	assert(packed.pack(part)==OK)
	assert(ResourceSaver.save(packed,REVIEW+"Parts/UpperTown.tscn")==OK)
	part.free()
	texture.update(image)
	assert(ResourceSaver.save(texture,BASE+"CityFoundationMask.res",ResourceSaver.FLAG_COMPRESS)==OK)
	image.save_png(BASE+"Textures/city_foundation_mask.png")
	var plan: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(PLAN))
	plan.pockets.append({"part":"UpperTown","height":32,"cells":cells,"area_m2":cells.size()*4})
	plan.total_area_m2 += cells.size()*4
	var file := FileAccess.open(PLAN,FileAccess.WRITE)
	file.store_string(JSON.stringify(plan,"\t"))
	terrain.data.update_maps(Terrain3DRegion.TYPE_MAX,true,true)
	terrain.data.save_directory(terrain.data_directory)
	city.set_meta("training_verge_finished",true)
	return {"garden_area_m2":cells.size()*4,"height_m":32}

func open_arena_stair_sky() -> Dictionary:
	load_layout()
	if city.get_meta("arena_stair_open_sky",false): return {"error":"Already opened"}
	part = ResourceLoader.load(REVIEW+"Parts/TrainingQuarter.tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	var court := PackedVector2Array([Vector2(117.5,-73.5),Vector2(156.5,-73.5),Vector2(156.5,-54.5),Vector2(117.5,-54.5)])
	var cut := PackedVector2Array([Vector2(126.9,-83),Vector2(137.1,-83),Vector2(137.1,-40),Vector2(126.9,-40)])
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	for piece in Geometry2D.clip_polygons(court,cut):
		var order := Geometry2D.triangulate_polygon(piece)
		for j in range(0,order.size(),3):
			var a := piece[order[j]]
			var b := piece[order[j+1]]
			var c := piece[order[j+2]]
			if (b-a).cross(c-a)<0:
				var swap := b
				b = c
				c = swap
			st.set_normal(Vector3.UP)
			st.add_vertex(Vector3(a.x,36.085,a.y))
			st.add_vertex(Vector3(b.x,36.085,b.y))
			st.add_vertex(Vector3(c.x,36.085,c.y))
	st.index()
	var node := part.get_node("DornCourt") as MeshInstance3D
	node.position = Vector3.ZERO
	node.mesh = st.commit()
	# The former centre rack must follow the usable court, not hang over the cut.
	(part.get_node("DornWeaponRack_1") as Node3D).position = Vector3(148,36,-69)
	var packed := PackedScene.new()
	assert(packed.pack(part)==OK)
	assert(ResourceSaver.save(packed,REVIEW+"Parts/TrainingQuarter.tscn")==OK)
	part.free()
	city.set_meta("arena_stair_open_sky",true)
	return {"court_ceiling_removed_above_stair":true}
