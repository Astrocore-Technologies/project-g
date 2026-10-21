@tool
extends "res://Tools/Linvel/LinvelSurfaceRepair.gd"
## Explicit authoring repairs for harbor landings and dry road shoulders.
## This helper is never attached to the saved city.

func mesh_report(node: Node3D, rows: Array) -> void:
	if node is MeshInstance3D and node.mesh!=null:
		var bounds: AABB = relative_transform(node)*node.mesh.get_aabb()
		rows.append({"node":str(part.get_path_to(node)),"visible":node.visible,"bounds":str(bounds),"mesh":node.mesh.resource_name})
	for child in node.get_children():
		if child is Node3D: mesh_report(child,rows)

func inspect() -> Dictionary:
	load_layout()
	var report := {}
	for label in ["HarborQuarter","GateQuarter","GardensQuarter"]:
		part = ResourceLoader.load(REVIEW+"Parts/"+label+".tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
		var rows: Array = []
		for path in (["GreatPier_1/TimberDeck_00","GreatPier_1/VisibleSurface","LowerQuay","DockCargo_-67","DockCargo_-9"] if label=="HarborQuarter" else (["EntryCourt/CompactPavedCourt"] if label=="GateQuarter" else ["RoadToValley"])):
			var node := part.get_node_or_null(path) as Node3D
			if node: mesh_report(node,rows)
		report[label] = rows
		part.free()
	var file := FileAccess.open("res://../.artifacts/linvel-harbor-repair/diagnosis.json",FileAccess.WRITE)
	file.store_string(JSON.stringify(report,"\t"))
	return report

func save_current_part() -> void:
	var packed := PackedScene.new()
	assert(packed.pack(part)==OK)
	assert(ResourceSaver.save(packed,REVIEW+"Parts/"+str(part.name)+".tscn")==OK)
	part.free()

func pier_cut(poly: PackedVector2Array, link: Dictionary) -> Array[PackedVector2Array]:
	var a := Vector2(link.start[0],link.start[2])
	var b := Vector2(link.end[0],link.end[2])
	var forward := (b-a).normalized()
	var side := Vector2(forward.y,-forward.x)*(float(link.width)*.5+.06)
	return Geometry2D.clip_polygons(poly,PackedVector2Array([a-side,b+forward-side,b+forward+side,a+side]))

func quay_wood_joints() -> Dictionary:
	load_layout()
	part = ResourceLoader.load(REVIEW+"Parts/HarborQuarter.tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	if part.get_meta("harbor_joints_revision",0)>0:
		part.free()
		return {"error":"Already repaired; preserve manual changes"}
	var flat := part.get_node("LowerQuay/FlatTerrace") as MeshInstance3D
	var pieces: Array[PackedVector2Array] = [polygons[0]]
	for link in layout.links:
		if link.kind!="pier": continue
		var next: Array[PackedVector2Array] = []
		for poly in pieces: next.append_array(pier_cut(poly,link))
		pieces = next
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	for poly in pieces:
		var order := Geometry2D.triangulate_polygon(poly)
		for j in range(0,order.size(),3):
			var a := poly[order[j]]
			var b := poly[order[j+1]]
			var c := poly[order[j+2]]
			if (b-a).cross(c-a)<0:
				var swap := b
				b = c
				c = swap
			st.set_normal(Vector3.UP)
			for p in [a,b,c]: st.add_vertex(Vector3(p.x,4,p.y))
	st.index()
	flat.mesh = st.commit()
	# A dark solid core supports the narrow joints between the existing planks.
	var core_material := StandardMaterial3D.new()
	core_material.albedo_color = Color("553521")
	core_material.roughness = 1
	core_material.diffuse_mode = BaseMaterial3D.DIFFUSE_TOON
	for link in layout.links:
		if link.kind!="pier": continue
		var root := part.get_node(link.id) as Node3D
		drop(root.get_node_or_null("TimberDeckCore"))
		var core := own(root,MeshInstance3D.new(),"TimberDeckCore") as MeshInstance3D
		var mesh := BoxMesh.new()
		mesh.size = Vector3(float(link.width),.16,float(link.end[2])-float(link.start[2])+.04)
		core.mesh = mesh
		core.material_override = core_material
		core.position = (vec(link.start)+vec(link.end))*.5-Vector3(0,.17,0)
	var moved := 0
	for group in part.get_children():
		if not str(group.name).begins_with("DockCargo_"): continue
		var shift := 0.0
		for attempt in 24:
			var fits := true
			for cargo in group.get_children():
				for offset in [Vector2(-1.3,-1.3),Vector2(1.3,-1.3),Vector2(-1.3,1.3),Vector2(1.3,1.3)]:
					var p: Vector2 = Vector2(cargo.position.x,cargo.position.z-shift)+offset
					if not Geometry2D.is_point_in_polygon(p,polygons[0]): fits = false
			if fits: break
			shift += 1
		if shift>0:
			group.position.z -= shift
			moved += group.get_child_count()
	drop(part.get_node_or_null("ArtDecorBatches"))
	var rails := rebuild_edge_rails()
	part.set_meta("harbor_joints_revision",1)
	save_current_part()
	return {"pier_stone_overlaps_removed":4,"timber_cores":4,"cargo_relocated":moved,"harbor_rails":rails}

func court_foundation() -> Dictionary:
	load_layout()
	part = ResourceLoader.load(REVIEW+"Parts/GateQuarter.tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	var node := part.get_node("EntryCourt/CompactPavedCourt") as MeshInstance3D
	var mesh := BoxMesh.new()
	mesh.size = Vector3(36,1,24)
	node.mesh = mesh
	node.position = Vector3(48,31.515,-143)
	save_current_part()
	return {"court_has_solid_edge_fascia":true}

func native_shoulders_and_shore() -> Dictionary:
	load_layout()
	terrain = city.get_node("World/Terrain3D") as Terrain3D
	var count := 0
	# Native ground meets each flat foregate road edge at the same level.
	for street in layout.streets:
		if street.part!="GardensQuarter": continue
		for i in range(street.points.size()-1):
			var a := vec(street.points[i])
			var b := vec(street.points[i+1])
			for z in range(floori(minf(a.z,b.z)-12),ceili(maxf(a.z,b.z)+12)):
				for x in range(floori(minf(a.x,b.x)-12),ceili(maxf(a.x,b.x)+12)):
					var point := Vector2(x,z)
					var d := point.distance_to(Geometry2D.get_closest_point_to_segment(point,Vector2(a.x,a.z),Vector2(b.x,b.z)))
					if d>float(street.width)*.5+4: continue
					var position := Vector3(x,0,z)
					var old := terrain.data.get_height(position)
					var blend := 1-smoothstep(float(street.width)*.5+2,float(street.width)*.5+4,d)
					terrain.data.set_height(position,lerpf(old,a.y,blend))
					count += 1
	for z in range(-158,-127):
		for x in range(27,70):
			var p := Vector2(x,z)
			if active_height(p)>32.1: continue
			var inside := x>=30 and x<=66 and z>=-155 and z<=-131
			terrain.data.set_height(Vector3(x,0,z),31.8 if inside else 32.0)
	# The seaward retaining wall already reaches -8 m. Keep terrain behind it
	# below the waterline instead of letting coarse LODs form green teeth.
	var shore_cells := 0
	var poly := polygons[0]
	for z in range(118,152):
		for x in range(-174,105):
			var p := Vector2(x,z)
			if active_height(p)>4.5: continue
			var d := INF
			for j in range(4,poly.size()-1):
				if (poly[j].y+poly[j+1].y)*.5<120: continue
				d = minf(d,p.distance_to(Geometry2D.get_closest_point_to_segment(p,poly[j],poly[j+1])))
			if d>6: continue
			var position := Vector3(x,0,z)
			var old := terrain.data.get_height(position)
			if is_finite(old) and old> -1.5:
				terrain.data.set_height(position,-1.5)
				shore_cells += 1
	terrain.data.update_maps(Terrain3DRegion.TYPE_MAX,true,true)
	terrain.data.save_directory(terrain.data_directory)
	var mask := install_foundation_mask()
	return {"road_shoulder_samples":count,"quay_seabed_samples":shore_cells,"mask":mask}

func refresh_saved_parts() -> Dictionary:
	load_layout()
	var parent := city.get_node("World/CityParts")
	for label in ["HarborQuarter","GateQuarter"]:
		drop(parent.get_node(label))
		var node: Node3D = ResourceLoader.load(REVIEW+"Parts/"+label+".tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
		parent.add_child(node)
		node.owner = city
	return {"reloaded":["HarborQuarter","GateQuarter"]}

func finish_island_shore() -> Dictionary:
	load_layout()
	terrain = city.get_node("World/Terrain3D") as Terrain3D
	var poly := polygons[8]
	var count := 0
	for z in range(135,191):
		for x in range(-214,-155):
			var p := Vector2(x,z)
			var d := INF
			for i in poly.size(): d = minf(d,p.distance_to(Geometry2D.get_closest_point_to_segment(p,poly[i],poly[(i+1)%poly.size()])))
			if d>5 or active_height(p)>5.1: continue
			var position := Vector3(x,0,z)
			var old := terrain.data.get_height(position)
			if is_finite(old) and old> -1.5:
				terrain.data.set_height(position,-1.5)
				count += 1
	terrain.data.update_maps(Terrain3DRegion.TYPE_HEIGHT,true,true)
	terrain.data.save_directory(terrain.data_directory)
	return {"island_shore_samples":count}

func finish_pier_crossing_roads() -> Dictionary:
	load_layout()
	part = ResourceLoader.load(REVIEW+"Parts/HarborQuarter.tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	if part.get_meta("pier_road_joints_finished",false):
		part.free()
		return {"error":"Already trimmed; preserve manual edits"}
	surface_nodes.clear()
	collect_floors(part)
	var changed: Array = []
	for node in surface_nodes:
		if not node.visible or node.mesh.get_aabb().size.y>.001: continue
		var transform := relative_transform(node)
		var height := (transform*node.mesh.get_aabb().get_center()).y
		if height<3.95 or height>4.1: continue
		var inverse := transform.affine_inverse()
		var st := SurfaceTool.new()
		st.begin(Mesh.PRIMITIVE_TRIANGLES)
		for surface in node.mesh.get_surface_count():
			var arrays := node.mesh.surface_get_arrays(surface)
			var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
			var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
			for j in range(0,indices.size(),3):
				var polygon := PackedVector2Array()
				for k in 3:
					var p := transform*vertices[indices[j+k]]
					polygon.append(Vector2(p.x,p.z))
				var pieces: Array[PackedVector2Array] = [polygon]
				for link in layout.links:
					if link.kind!="pier": continue
					var next: Array[PackedVector2Array] = []
					for poly in pieces: next.append_array(pier_cut(poly,link))
					pieces = next
				for poly in pieces:
					var order := Geometry2D.triangulate_polygon(poly)
					for n in range(0,order.size(),3):
						var a := poly[order[n]]
						var b := poly[order[n+1]]
						var c := poly[order[n+2]]
						if (b-a).cross(c-a)<0:
							var swap := b
							b = c
							c = swap
						st.set_normal((transform.basis.transposed()*Vector3.UP).normalized())
						for p in [a,b,c]: st.add_vertex(inverse*Vector3(p.x,height,p.y))
		st.index()
		node.mesh = st.commit()
		changed.append(str(part.get_path_to(node)))
	part.set_meta("pier_road_joints_finished",true)
	save_current_part()
	return {"trimmed_stone_floor_nodes":changed}

func finish_cargo_bay() -> Dictionary:
	load_layout()
	part = ResourceLoader.load(REVIEW+"Parts/HarborQuarter.tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	var group := part.get_node("DockCargo_-142") as Node3D
	# Stage cargo beside the warehouse, landward of the reserved promenade.
	group.position = Vector3(-8,0,-24)
	for cargo in group.get_children():
		var p: Vector3 = group.position+cargo.position
		assert(Geometry2D.is_point_in_polygon(Vector2(p.x,p.z),polygons[0]))
		assert(route_clear(Vector2(p.x,p.z),1.2))
		assert(building_clear(Vector2(p.x,p.z),1.2))
	save_current_part()
	return {"grounded_cargo_staged_off_the_promenade":3}

func finish_cliff_material() -> Dictionary:
	load_layout()
	terrain = city.get_node("World/Terrain3D") as Terrain3D
	var shader := Shader.new()
	shader.code = FileAccess.get_file_as_string(BASE+"CityTerrain.gdshader")
	assert(ResourceSaver.save(shader,BASE+"CityTerrain.gdshader")==OK)
	shader.take_over_path(BASE+"CityTerrain.gdshader")
	terrain.material.shader_override = shader
	var rock := load("res://Assets/World/Linvel/Beech/Revision2/StylePass/GroundRock.res") as Texture2D
	terrain.material.set_shader_param("city_cliff_paint",rock)
	terrain.material.set_shader_param("city_cliffs_enabled",true)
	assert(terrain.material.get_shader_param("city_cliff_paint")!=null)
	assert(ResourceSaver.save(terrain.material,BASE+"CityTerrainMaterial.tres")==OK)
	return {"steep_faces":"triplanar painted rock","rock_source":rock.resource_path,"forest_materials_changed":false}

var editor_capture_report: Dictionary = {}

func begin_editor_capture(stage: String) -> Dictionary:
	editor_capture_report = {"stage":stage,"done":false}
	capture_editor_views.call_deferred(stage)
	return editor_capture_report

func capture_editor_views(stage: String) -> void:
	# Use the editor's live World3D in a separate viewport. The editor controller
	# continuously overwrites its own camera, so its texture is not a QA capture.
	var editor_viewport := EditorInterface.get_editor_viewport_3d(0)
	var original := editor_viewport.get_camera_3d()
	var viewport := SubViewport.new()
	viewport.size = Vector2i(1280,720)
	viewport.world_3d = editor_viewport.find_world_3d()
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	add_child(viewport)
	var camera := Camera3D.new()
	viewport.add_child(camera)
	camera.projection = Camera3D.PROJECTION_PERSPECTIVE
	camera.fov = 64
	camera.near = .1
	camera.far = 10000
	camera.cull_mask = 1
	camera.make_current()
	var terrain := get_parent().get_node("World/Terrain3D") as Terrain3D
	terrain.set_camera(camera)
	var directory := ProjectSettings.globalize_path("res://../.artifacts/linvel-harbor-repair/"+stage+"/editor")
	DirAccess.make_dir_recursive_absolute(directory)
	var shots: Array = []
	for shot in [["road-shoulder",Vector3(68,37,-172),Vector3(46,32,-185)],["court-edge",Vector3(71,37,-153),Vector3(65,31.8,-143)],["pier-entry",Vector3(-105,12,122),Vector3(-105,4,145)],["lighthouse-entry",Vector3(-145,11,107),Vector3(-171,5,140)],["quay-face",Vector3(-80,4.5,158),Vector3(-65,3,135)]]:
		camera.global_position = shot[1]
		camera.look_at(shot[2])
		await get_tree().create_timer(.8).timeout
		await RenderingServer.frame_post_draw
		var path: String = directory.path_join(shot[0]+".png")
		if viewport.get_camera_3d()!=camera:
			shots.append({"error":"Editor replaced review camera"})
			break
		shots.append({"path":path,"error":viewport.get_texture().get_image().save_png(path)})
	original.make_current()
	terrain.set_camera(original)
	viewport.queue_free()
	editor_capture_report = {"stage":stage,"done":true,"shots":shots}
