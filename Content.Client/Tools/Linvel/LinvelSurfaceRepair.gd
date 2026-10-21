@tool
extends "res://Tools/Linvel/LinvelArtFinish.gd"
## Explicit native scene repair only; never attached to a finished city.
var terrain: Terrain3D
var surface_nodes: Array[MeshInstance3D] = []

func collect_floors(node: Node) -> void:
	if node.has_meta("original_asset") or node.has_meta("art_asset"):
		return
	if node is MeshInstance3D and (node.has_meta("route_width_m") or node.name in ["FlatTerrace","VisibleSurface","CompactPavedCourt","TavernEntryBridge"]):
		surface_nodes.append(node)
	for child in node.get_children(): collect_floors(child)

func projected_height(a: Vector3,b: Vector3,c: Vector3,p: Vector2) -> float:
	var aa := Vector2(a.x,a.z)
	var bb := Vector2(b.x,b.z)
	var cc := Vector2(c.x,c.z)
	var det := (bb-aa).cross(cc-aa)
	if absf(det)<.00001: return NAN
	var u := (p-aa).cross(cc-aa)/det
	var v := (bb-aa).cross(p-aa)/det
	if u<-.0001 or v<-.0001 or u+v>1.0001: return NAN
	return a.y+u*(b.y-a.y)+v*(c.y-a.y)

func floor_hits(p: Vector2) -> Array:
	var hits: Array = []
	for node in surface_nodes:
		if not node.visible: continue
		var inverse := node.global_transform.affine_inverse()
		var local := inverse*Vector3(p.x,0,p.y)
		var bounds := node.mesh.get_aabb()
		if local.x<bounds.position.x-.01 or local.x>bounds.end.x+.01 or local.z<bounds.position.z-.01 or local.z>bounds.end.z+.01: continue
		for s in node.mesh.get_surface_count():
			var arrays := node.mesh.surface_get_arrays(s)
			var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
			var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
			for i in range(0,indices.size(),3):
				var h := projected_height(vertices[indices[i]],vertices[indices[i+1]],vertices[indices[i+2]],Vector2(local.x,local.z))
				if is_finite(h):
					hits.append({"path":str(city.get_path_to(node)),"height":(node.global_transform*Vector3(local.x,h,local.z)).y})
					break
	return hits

func diagnose() -> Dictionary:
	load_layout()
	terrain = city.get_node("World/Terrain3D")
	collect_floors(city.get_node("World/CityParts"))
	var results: Array = []
	for link in layout.links:
		if link.kind not in ["stairs","ramp"]: continue
		var a := vec(link.start)
		var b := vec(link.end)
		var checks: Array = []
		for j in [1,3,5,7,9]:
			var p := a.lerp(b,j/10.0)
			checks.append({"point":p,"expected":p.y,"terrain":terrain.data.get_height(p),"floors":floor_hits(Vector2(p.x,p.z))})
		results.append({"link":link.id,"checks":checks})
	var report := {"floor_meshes":surface_nodes.size(),"links":results}
	var file := FileAccess.open("res://../.artifacts/linvel-surface-repair/diagnosis.json",FileAccess.WRITE)
	file.store_string(JSON.stringify(report,"\t"))
	return report

func active_height(p: Vector2) -> float:
	var h := -INF
	for i in polygons.size():
		if i in [9,11]: continue
		if Geometry2D.is_point_in_polygon(p,polygons[i]):
			h = maxf(h,float(layout.platforms[i].height))
	return h

func terrain_surface_height(p: Vector2, old: float) -> float:
	var h := active_height(p)
	if is_finite(h):
		h -= .8
	else:
		h = old
		var nearest := INF
		for i in polygons.size():
			if i in [9,11]: continue
			var poly := polygons[i]
			for j in poly.size():
				var distance := p.distance_to(Geometry2D.get_closest_point_to_segment(p,poly[j],poly[(j+1)%poly.size()]))
				if distance<nearest:
					nearest = distance
					if distance<6: h = maxf(old,float(layout.platforms[i].height)-.8-distance*.12)
	# The service yard is level ground, with paving held slightly above its underlay.
	if p.x>5 and p.x<89 and p.y> -156 and p.y< -132:
		h = 32 if active_height(p)<0 else 31.2
	if p.x>=29 and p.x<=67 and p.y>=-156 and p.y<=-130:
		h = 31.2
	for route in layout.links+layout.streets:
		if route.get("kind","") in ["pier","bridge","breakwater"]: continue
		var points: Array = route.get("points",[route.get("start"),route.get("end")])
		for i in range(points.size()-1):
			var a := vec(points[i])
			var b := vec(points[i+1])
			var aa := Vector2(a.x,a.z)
			var bb := Vector2(b.x,b.z)
			var t := (p-aa).dot(bb-aa)/(bb-aa).length_squared()
			if t<-.025 or t>1.025: continue
			if p.distance_to(aa+(bb-aa)*clampf(t,0,1))<float(route.width)*.5+3:
				h = minf(h,lerpf(a.y,b.y,clampf(t,0,1))-.8)
	return maxf(3,h)

func settle_native_earth() -> Dictionary:
	var count := 0
	for z in range(-240,145,2):
		for x in range(-212,211,2):
			var point := Vector2(x,z)
			var position := Vector3(x,0,z)
			var old := terrain.data.get_height(position)
			if not is_finite(old): old = 3
			var target := terrain_surface_height(point,old)
			if absf(target-old)<.01: continue
			# No widening of the coastline or work outside the already dry city.
			if old<1 and active_height(point)<0: continue
			terrain.data.set_height(position,target)
			terrain.data.set_control_hole(position,false)
			terrain.data.set_control_auto(position,false)
			terrain.data.set_control_base_id(position,0)
			count += 1
	terrain.data.update_maps(Terrain3DRegion.TYPE_MAX,true,true)
	terrain.data.save_directory(terrain.data_directory)
	return {"native_cells":count,"under_paving_clearance_m":.8}

func flat_mesh(mesh: Mesh) -> ArrayMesh:
	var result := ArrayMesh.new()
	for s in mesh.get_surface_count():
		var arrays := mesh.surface_get_arrays(s)
		var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
		var normals: PackedVector3Array = arrays[Mesh.ARRAY_NORMAL]
		var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
		var uvs: PackedVector2Array = arrays[Mesh.ARRAY_TEX_UV] if arrays[Mesh.ARRAY_TEX_UV]!=null else PackedVector2Array()
		var st := SurfaceTool.new()
		st.begin(Mesh.PRIMITIVE_TRIANGLES)
		for j in range(0,indices.size(),3):
			var a := vertices[indices[j]]
			var b := vertices[indices[j+1]]
			var c := vertices[indices[j+2]]
			var normal := (b-a).cross(c-a).normalized()
			if normals.size()>0 and normal.dot(normals[indices[j]])<0: normal = -normal
			for k in 3:
				var index := indices[j+k]
				st.set_normal(normal)
				if uvs.size()>0: st.set_uv(uvs[index])
				st.add_vertex(vertices[index])
		st.index()
		st.commit(result)
		result.surface_set_material(s,mesh.surface_get_material(s))
	return result

func harden_surfaces(node: Node) -> int:
	if node.has_meta("original_asset") or node.has_meta("art_asset"): return 0
	var count := 0
	if node is MeshInstance3D and node.mesh is ArrayMesh:
		node.mesh = flat_mesh(node.mesh)
		count += 1
	for child in node.get_children(): count += harden_surfaces(child)
	return count

func side_wall_height(p: Vector3, side: Vector3) -> Vector2:
	var neighbor := active_height(Vector2(p.x+side.x*.7,p.z+side.z*.7))
	if not is_finite(neighbor): neighbor = terrain.data.get_height(p)
	if not is_finite(neighbor): neighbor = p.y-.8
	var upper := maxf(neighbor+.08,p.y+.14)
	if neighbor<p.y-.6: upper = p.y+.7
	return Vector2(minf(neighbor,p.y)-.65,upper)

func close_link_sides(link: Dictionary) -> void:
	var root := part.get_node(link.id) as Node3D
	drop(root.get_node_or_null("StoneSideRetainingWalls"))
	var group := own(root,Node3D.new(),"StoneSideRetainingWalls") as Node3D
	var a := vec(link.start)
	var b := vec(link.end)
	var normal := Vector3(b.z-a.z,0,a.x-b.x).normalized()
	var n := ceili(a.distance_to(b)/1.0)
	for sign_value in [-1,1]:
		var side: Vector3 = normal*float(sign_value)
		var st := SurfaceTool.new()
		st.begin(Mesh.PRIMITIVE_TRIANGLES)
		for j in n:
			var aa := a.lerp(b,float(j)/n)+side*(float(link.width)*.5+.06)
			var bb := a.lerp(b,float(j+1)/n)+side*(float(link.width)*.5+.06)
			var ha := side_wall_height(aa,side)
			var hb := side_wall_height(bb,side)
			var al := Vector3(aa.x,ha.x,aa.z)
			var ah := Vector3(aa.x,ha.y,aa.z)
			var bl := Vector3(bb.x,hb.x,bb.z)
			var bh := Vector3(bb.x,hb.y,bb.z)
			var thickness := side*.45
			emit_quad(st,al,ah,bh,bl)
			emit_quad(st,bl+thickness,bh+thickness,ah+thickness,al+thickness)
			emit_quad(st,ah,ah+thickness,bh+thickness,bh)
		st.generate_normals()
		st.index()
		var mesh := flat_mesh(st.commit())
		var node := own(group,MeshInstance3D.new(),"RetainingSide_%d"%sign_value) as MeshInstance3D
		node.mesh = mesh
		node.material_override = paving
		var body := own(group,StaticBody3D.new(),"SideSolid_%d"%sign_value) as StaticBody3D
		body.collision_layer = 4
		body.collision_mask = 0
		var cs := own(body,CollisionShape3D.new(),"Shape") as CollisionShape3D
		cs.shape = mesh.create_trimesh_shape()

func rebuild_wall_batches() -> void:
	drop(part.get_node_or_null("WallRenderBatches"))
	var groups := {}
	for run in part.get_children():
		for source in run.get_children():
			if not source.has_meta("wall_module_length_m"): continue
			for child in source.get_children():
				if not child is MeshInstance3D: continue
				if not groups.has(child.mesh): groups[child.mesh] = {"transforms":[],"paths":PackedStringArray()}
				groups[child.mesh].transforms.append(source.transform*child.transform)
				groups[child.mesh].paths.append(str(part.get_path_to(child)))
				child.visible = false
	var batches := own(part,Node3D.new(),"WallRenderBatches") as Node3D
	batches.set_script(load("res://Tools/Linvel/LinvelDecorBatchEditor.gd"))
	var i := 0
	for mesh in groups:
		var multi := MultiMesh.new()
		multi.transform_format = MultiMesh.TRANSFORM_3D
		multi.mesh = mesh
		multi.instance_count = groups[mesh].transforms.size()
		for j in multi.instance_count: multi.set_instance_transform(j,groups[mesh].transforms[j])
		var node := own(batches,MultiMeshInstance3D.new(),"CityWall_%d"%i) as MultiMeshInstance3D
		node.multimesh = multi
		node.material_override = paving
		node.set_meta("source_mesh_paths",groups[mesh].paths)
		i += 1

func repair_walls() -> void:
	var fresh: Node3D = (prefabs.city_wall_10m as PackedScene).instantiate()
	var model := fresh.get_child(0) as MeshInstance3D
	for run in part.get_children():
		for source in run.get_children():
			if not source.has_meta("wall_module_length_m"): continue
			for child in source.get_children():
				if child is MeshInstance3D:
					child.mesh = model.mesh
					child.material_override = paving
			var body := source.get_node("WallSolid") as StaticBody3D
			body.position.y = 3.3
			var collision := body.get_node("Shape") as CollisionShape3D
			var shape := BoxShape3D.new()
			shape.size = Vector3(8,11.8,2)
			collision.shape = shape
			source.set_meta("above_ground_height_m",9.8)
	fresh.free()
	rebuild_wall_batches()
	part.set_meta("wall_height_m",9.8)

func repair_towers(node: Node) -> int:
	var count := 0
	if node.get_meta("art_asset","")=="harbor_watchtower":
		var old := node.get_node("Art") as Node3D
		var tf := old.transform
		drop(old)
		var art := place(node,"watchtower_door_fixed","Art",tf.origin)
		art.transform = tf
		if part.name=="GateQuarter":
			if str(node.name).begins_with("NorthGateTower"):
				art.scale.y /= .82
			else:
				art.scale.y *= 1.3
		node.set_meta("art_asset","watchtower_door_fixed")
		count += 1
	else:
		for child in node.get_children(): count += repair_towers(child)
	return count

func apply_repairs() -> Dictionary:
	load_layout()
	if city.scene_file_path!=REVIEW+"LinvelCity_Art01.tscn" or city.get_meta("surface_repair_revision",0)>0:
		return {"error":"Only the uncorrected Art01; preserve subsequent hand edits"}
	terrain = city.get_node("World/Terrain3D")
	prefab("city_wall_10m",BASE+"Models/city_wall_10m.glb",paving)
	prefab("watchtower_door_fixed",BASE+"Models/watchtower_door_fixed.glb",null)
	var earth := settle_native_earth()
	var hard_surfaces := 0
	var side_walls := 0
	var towers := 0
	var saved_parts: Array[String] = []
	for label in ["CityCenter","ResidentialTerraces","CraftsQuarter","UpperTown","TrainingQuarter","SanctuaryQuarter","HarborQuarter","GateQuarter","OuterDefenses"]:
		part = ResourceLoader.load(REVIEW+"Parts/"+label+".tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
		hard_surfaces += harden_surfaces(part)
		for link in layout.links:
			if link.part==label and link.kind in ["ramp","stairs"]:
				close_link_sides(link)
				side_walls += 2
		towers += repair_towers(part)
		if label=="OuterDefenses": repair_walls()
		var packed := PackedScene.new()
		assert(packed.pack(part)==OK)
		assert(ResourceSaver.save(packed,REVIEW+"Parts/"+label+".tscn")==OK)
		saved_parts.append(label)
		part.free()
	var parent := city.get_node("World/CityParts")
	for label in saved_parts:
		drop(parent.get_node(label))
		var node: Node3D = ResourceLoader.load(REVIEW+"Parts/"+label+".tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
		parent.add_child(node)
		node.owner = city
	city.set_meta("surface_repair_revision",1)
	var report := {"native_earth":earth,"hard_surface_meshes":hard_surfaces,"stone_sides":side_walls,"tower_doors_fixed":towers,"wall_height_m":9.8,"saved_parts":saved_parts}
	var file := FileAccess.open("res://../.artifacts/linvel-surface-repair/authoring-report.json",FileAccess.WRITE)
	file.store_string(JSON.stringify(report,"\t"))
	return report

func apply_one_district(label: String) -> Dictionary:
	load_layout()
	if city.scene_file_path!=REVIEW+"LinvelCity_Art01.tscn" or label not in ["CityCenter","ResidentialTerraces","CraftsQuarter","UpperTown","TrainingQuarter","SanctuaryQuarter","HarborQuarter","GateQuarter","OuterDefenses"]:
		return {"error":"Unexpected source or district"}
	terrain = city.get_node("World/Terrain3D")
	prefabs.city_wall_10m = load(PREFABS+"city_wall_10m.tscn")
	prefabs.watchtower_door_fixed = load(PREFABS+"watchtower_door_fixed.tscn")
	part = ResourceLoader.load(REVIEW+"Parts/"+label+".tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	var hardened := harden_surfaces(part)
	var sides := 0
	for link in layout.links:
		if link.part==label and link.kind in ["ramp","stairs"]:
			close_link_sides(link)
			sides += 2
	var doors := repair_towers(part)
	if label=="OuterDefenses": repair_walls()
	var packed := PackedScene.new()
	assert(packed.pack(part)==OK)
	assert(ResourceSaver.save(packed,REVIEW+"Parts/"+label+".tscn")==OK)
	part.free()
	return {"district":label,"hard_meshes":hardened,"side_walls":sides,"doors":doors}

func finish_saved_districts() -> Dictionary:
	load_layout()
	if city.scene_file_path!=REVIEW+"LinvelCity_Art01.tscn": return {"error":"Unexpected source"}
	var parent := city.get_node("World/CityParts")
	for label in ["CityCenter","ResidentialTerraces","CraftsQuarter","UpperTown","TrainingQuarter","SanctuaryQuarter","HarborQuarter","GateQuarter","OuterDefenses"]:
		drop(parent.get_node(label))
		var node: Node3D = ResourceLoader.load(REVIEW+"Parts/"+label+".tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
		parent.add_child(node)
		node.owner = city
	city.set_meta("surface_repair_revision",1)
	return {"districts_reloaded":9,"wall_height_m":9.8,"native_ground_repaired":true}

func lower_exposed_seams() -> Dictionary:
	var changed := 0
	for z in range(-174,145,2):
		for x in range(-205,207,2):
			var p := Vector2(x,z)
			if is_finite(active_height(p)): continue
			if x>5 and x<89 and z> -156 and z< -132: continue
			var near := false
			var lowest := INF
			for i in polygons.size():
				if i in [9,11]: continue
				var d := INF
				for j in polygons[i].size():
					d = minf(d,p.distance_to(Geometry2D.get_closest_point_to_segment(p,polygons[i][j],polygons[i][(j+1)%polygons[i].size()])))
				if d<8: near = true
				if d<14: lowest = minf(lowest,float(layout.platforms[i].height))
			if not near or not is_finite(lowest): continue
			var position := Vector3(x,0,z)
			var old := terrain.data.get_height(position)
			var target := maxf(3,lowest-.8)
			if is_finite(old) and old>target:
				terrain.data.set_height(position,target)
				changed += 1
	terrain.data.update_maps(Terrain3DRegion.TYPE_MAX,true,true)
	terrain.data.save_directory(terrain.data_directory)
	return {"lowered_exposed_seam_cells":changed}

func paint_mask_polygon(image: Image, poly: PackedVector2Array, expand := .9) -> void:
	var bounds := Rect2(poly[0],Vector2.ZERO)
	for p in poly: bounds = bounds.expand(p)
	bounds = bounds.grow(expand+1)
	for z in range(maxi(-256,floori(bounds.position.y)),mini(256,ceili(bounds.end.y))):
		for x in range(maxi(-256,floori(bounds.position.x)),mini(256,ceili(bounds.end.x))):
			var p := Vector2(x+.5,z+.5)
			var inside := Geometry2D.is_point_in_polygon(p,poly)
			if not inside:
				for i in poly.size():
					if p.distance_to(Geometry2D.get_closest_point_to_segment(p,poly[i],poly[(i+1)%poly.size()]))<=expand:
						inside = true
						break
			if inside: image.set_pixel(x+256,z+256,Color.WHITE)

func install_foundation_mask() -> Dictionary:
	var mask := Image.create(512,512,false,Image.FORMAT_R8)
	mask.fill(Color.BLACK)
	for i in polygons.size():
		if i in [9,11]: continue
		paint_mask_polygon(mask,polygons[i],.3)
	for road in layout.streets+layout.links:
		if road.get("kind","") in ["pier","bridge","breakwater"]: continue
		var points: Array = road.get("points",[road.get("start"),road.get("end")])
		for i in range(points.size()-1):
			# Flat roads have level native shoulders, not a void cut beneath them.
			if absf(float(points[i][1])-float(points[i+1][1]))<.1: continue
			var a := Vector2(points[i][0],points[i][2])
			var b := Vector2(points[i+1][0],points[i+1][2])
			var r := Vector2((b-a).y,-(b-a).x).normalized()*float(road.width)*.5
			paint_mask_polygon(mask,PackedVector2Array([a-r,b-r,b+r,a+r]),0)
	# Match the actual 36 x 24 m entry court; the former extra metre exposed water.
	paint_mask_polygon(mask,PackedVector2Array([Vector2(30,-155),Vector2(66,-155),Vector2(66,-131),Vector2(30,-131)]),0)
	var infill_path := BASE+"CityCourtyardPlan.json"
	if FileAccess.file_exists(infill_path):
		var infill: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(infill_path))
		for pocket in infill.pockets:
			for cell in pocket.cells:
				for z in range(cell[1],cell[1]+2):
					for x in range(cell[0],cell[0]+2): mask.set_pixel(x+256,z+256,Color.WHITE)
	var texture := ImageTexture.create_from_image(mask)
	assert(ResourceSaver.save(texture,BASE+"CityFoundationMask.res",ResourceSaver.FLAG_COMPRESS)==OK)
	texture.take_over_path(BASE+"CityFoundationMask.res")
	mask.save_png(BASE+"Textures/city_foundation_mask.png")
	var material := terrain.material.duplicate(true) as Terrain3DMaterial
	material.shader_override = load(BASE+"CityTerrain.gdshader")
	material.shader_override_enabled = true
	terrain.material = material
	material.set_shader_param("city_foundation_mask",texture)
	material.set_shader_param("city_foundation_enabled",true)
	assert(material.get_shader_param("city_foundation_mask")!=null)
	assert(ResourceSaver.save(material,BASE+"CityTerrainMaterial.tres")==OK)
	material.take_over_path(BASE+"CityTerrainMaterial.tres")
	return {"city_only_shader":BASE+"CityTerrain.gdshader","mask_size":512,"native_collision_preserved":true}

func close_masonry_corners() -> int:
	var count := 0
	for platform in layout.platforms:
		if platform.part!=str(part.name) or platform.id in ["GardenApproach","GateApproach"]: continue
		var index: int = layout.platforms.find(platform)
		var group := own(part,Node3D.new(),platform.id+"_MasonryCorners") as Node3D
		for p in polygons[index]:
			if not route_clear(p,1.25): continue
			var top := float(platform.height)+.06
			var base := 3.0
			var mesh := BoxMesh.new()
			mesh.size = Vector3(1.25,top-base,1.25)
			var node := own(group,MeshInstance3D.new(),"Corner_%d"%count) as MeshInstance3D
			node.mesh = mesh
			node.position = Vector3(p.x,(base+top)*.5,p.y)
			node.material_override = paving
			collision_box(group,"CornerSolid_%d"%count,node.position,mesh.size)
			count += 1
	return count

func landing_opening(p: Vector2, h: float, edge_a: Vector2, edge_b: Vector2) -> bool:
	# Flat streets also cross terrace edges, notably UpperMainStreet at the gate.
	# Reserve half a rail beyond the road so an end post cannot protrude into it.
	for street in layout.streets:
		for i in range(street.points.size()-1):
			var aa: Array = street.points[i]
			var bb: Array = street.points[i+1]
			if absf(float(aa[1])-h)>.1 or absf(float(bb[1])-h)>.1: continue
			var a := Vector2(aa[0],aa[2])
			var b := Vector2(bb[0],bb[2])
			# A parallel promenade needs a guardrail, not an opening along its length.
			if Geometry2D.segment_intersects_segment(a,b,edge_a,edge_b)==null: continue
			var t := (p-a).dot(b-a)/(b-a).length_squared()
			if t>=0 and t<=1 and p.distance_to(a+(b-a)*t)<float(street.width)*.5+2.1:
				return true
	for link in layout.links:
		if link.kind not in ["ramp","stairs","bridge","pier"]: continue
		var a := Vector2(link.start[0],link.start[2])
		var b := Vector2(link.end[0],link.end[2])
		var t := (p-a).dot(b-a)/(b-a).length_squared()
		if t>=0 and t<=1 and p.distance_to(a+(b-a)*t)<float(link.width)*.5+2.1 and lerpf(link.start[1],link.end[1],t)<h+.15:
			return true
		for endpoint in [link.start,link.end]:
			if absf(float(endpoint[1])-h)<.1 and p.distance_to(Vector2(endpoint[0],endpoint[2]))<float(link.width)*.5+1.1:
				return true
	return false

func rebuild_edge_rails() -> int:
	drop(part.get_node_or_null("ArtDecorBatches"))
	var count := 0
	for platform in layout.platforms:
		if platform.part!=str(part.name) or platform.id in ["GardenApproach","GateApproach"]: continue
		drop(part.get_node_or_null(platform.id+"_EdgeBalustrades"))
		var group := own(part,Node3D.new(),platform.id+"_EdgeBalustrades") as Node3D
		var poly: PackedVector2Array = polygons[layout.platforms.find(platform)]
		var h := float(platform.height)
		for edge in poly.size():
			var a := poly[edge]
			var b := poly[(edge+1)%poly.size()]
			var direction := (b-a).normalized()
			var normal := Vector2(-direction.y,direction.x)
			if Geometry2D.is_point_in_polygon((a+b)*.5+normal*.1,poly): normal = -normal
			var n := maxi(1,ceili(a.distance_to(b)/3.95))
			for j in n:
				var p := a.lerp(b,(j+.5)/n)
				var outside := active_height(p+normal*1.5)
				if is_finite(outside) and outside>=h-.8: continue
				if landing_opening(p,h,a,b): continue
				place(group,"stone_balustrade","Rail_%03d"%count,Vector3(p.x,h+.035,p.y),Vector3((a.distance_to(b)/n+.1)/4.1,1,1),-rad_to_deg(direction.angle()))
				count += 1
	bake_decor_batches()
	return count

func finish_district_enclosure(label: String) -> Dictionary:
	load_layout()
	if city.scene_file_path!=REVIEW+"LinvelCity_Art01.tscn" or city.get_meta("surface_enclosure_"+label,false): return {"error":"Unexpected source or already enclosed"}
	terrain = city.get_node("World/Terrain3D")
	prefabs.stone_balustrade = load(PREFABS+"stone_balustrade.tscn")
	part = ResourceLoader.load(REVIEW+"Parts/"+label+".tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	var corners := close_masonry_corners()
	var rails := rebuild_edge_rails()
	var packed := PackedScene.new()
	assert(packed.pack(part)==OK)
	assert(ResourceSaver.save(packed,REVIEW+"Parts/"+label+".tscn")==OK)
	part.free()
	city.set_meta("surface_enclosure_"+label,true)
	return {"district":label,"corner_masonry":corners,"edge_rails":rails}

func finish_ground_rendering() -> Dictionary:
	load_layout()
	if city.scene_file_path!=REVIEW+"LinvelCity_Art01.tscn" or city.get_meta("surface_foundation_mask",false): return {"error":"Unexpected source or already masked"}
	terrain = city.get_node("World/Terrain3D")
	var ground := lower_exposed_seams()
	var render := install_foundation_mask()
	city.set_meta("surface_foundation_mask",true)
	return {"ground":ground,"render":render}

func trim_lighthouse_landing() -> Dictionary:
	load_layout()
	if city.scene_file_path!=REVIEW+"LinvelCity_Art01.tscn" or city.get_meta("lighthouse_landing_trimmed",false): return {"error":"Unexpected source or already trimmed"}
	part = ResourceLoader.load(REVIEW+"Parts/HarborQuarter.tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	var floor_node := part.get_node("LighthouseHeadland/FlatTerrace") as MeshInstance3D
	var link: Dictionary
	for candidate in layout.links:
		if candidate.id=="LighthouseCauseway": link = candidate
	var a := Vector2(link.start[0],link.start[2])
	var b := Vector2(link.end[0],link.end[2])
	var r := Vector2((b-a).y,-(b-a).x).normalized()*(float(link.width)*.5+.08)
	var cut := PackedVector2Array([a-r,b-r,b+r,a+r])
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	for surface in floor_node.mesh.get_surface_count():
		var arrays := floor_node.mesh.surface_get_arrays(surface)
		var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
		var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
		for j in range(0,indices.size(),3):
			var triangle := PackedVector2Array()
			for k in 3:
				var p := vertices[indices[j+k]]
				triangle.append(Vector2(p.x,p.z))
			for piece in Geometry2D.clip_polygons(triangle,cut):
				var order := Geometry2D.triangulate_polygon(piece)
				for k in range(0,order.size(),3):
					var aa := piece[order[k]]
					var bb := piece[order[k+1]]
					var cc := piece[order[k+2]]
					st.set_normal(Vector3.UP)
					if (bb-aa).cross(cc-aa)>0:
						var swap := bb
						bb = cc
						cc = swap
					st.add_vertex(Vector3(aa.x,5,aa.y))
					st.add_vertex(Vector3(bb.x,5,bb.y))
					st.add_vertex(Vector3(cc.x,5,cc.y))
	st.index()
	var mesh := st.commit()
	floor_node.mesh = mesh
	part.get_node("LighthouseHeadland/FlatTerraceCollision/Shape").shape = mesh.create_trimesh_shape()
	var packed := PackedScene.new()
	assert(packed.pack(part)==OK)
	assert(ResourceSaver.save(packed,REVIEW+"Parts/HarborQuarter.tscn")==OK)
	part.free()
	city.set_meta("lighthouse_landing_trimmed",true)
	return {"bridge_floor_overlap_removed":true}

func refresh_rail_openings(label: String) -> Dictionary:
	load_layout()
	if city.scene_file_path!=REVIEW+"LinvelCity_Art01.tscn": return {"error":"Unexpected source"}
	prefabs.stone_balustrade = load(PREFABS+"stone_balustrade.tscn")
	part = ResourceLoader.load(REVIEW+"Parts/"+label+".tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	var rails := rebuild_edge_rails()
	var packed := PackedScene.new()
	assert(packed.pack(part)==OK)
	assert(ResourceSaver.save(packed,REVIEW+"Parts/"+label+".tscn")==OK)
	part.free()
	return {"district":label,"rails":rails,"cut_landings_open":true}

func bind_city_mask() -> Dictionary:
	load_layout()
	terrain = city.get_node("World/Terrain3D")
	var shader := Shader.new()
	shader.code = FileAccess.get_file_as_string(BASE+"CityTerrain.gdshader")
	assert(ResourceSaver.save(shader,BASE+"CityTerrain.gdshader")==OK)
	shader.take_over_path(BASE+"CityTerrain.gdshader")
	terrain.material.shader_override = shader
	terrain.material.shader_override_enabled = true
	terrain.material.set_shader_param("city_foundation_mask",load(BASE+"CityFoundationMask.res"))
	terrain.material.set_shader_param("city_foundation_enabled",true)
	var bound: Variant = terrain.material.get_shader_param("city_foundation_mask")
	assert(bound!=null,"Do not save a material without its exclusion texture")
	assert(ResourceSaver.save(terrain.material,BASE+"CityTerrainMaterial.tres")==OK)
	return {"mask_bound":bound!=null,"enabled":terrain.material.get_shader_param("city_foundation_enabled")}

func finish_physical_landings() -> Dictionary:
	load_layout()
	if city.get_meta("physical_landings_finished",false): return {"error":"Already finished"}
	part = ResourceLoader.load(REVIEW+"Parts/HarborQuarter.tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	var floor_node := part.get_node("LighthouseHeadland/FlatTerrace") as MeshInstance3D
	var mesh := floor_node.mesh as ArrayMesh
	var arrays := mesh.surface_get_arrays(0)
	var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
	for j in range(0,indices.size(),3):
		var swap := indices[j+1]
		indices[j+1] = indices[j+2]
		indices[j+2] = swap
	arrays[Mesh.ARRAY_INDEX] = indices
	var fixed := ArrayMesh.new()
	fixed.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES,arrays)
	floor_node.mesh = fixed
	part.get_node("LighthouseHeadland/FlatTerraceCollision/Shape").shape = fixed.create_trimesh_shape()
	var packed := PackedScene.new()
	assert(packed.pack(part)==OK)
	assert(ResourceSaver.save(packed,REVIEW+"Parts/HarborQuarter.tscn")==OK)
	part.free()
	part = ResourceLoader.load(REVIEW+"Parts/GateQuarter.tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	for x in [36,56]:
		var mesh_box := BoxMesh.new()
		mesh_box.size = Vector3(8.05,1.0,8.05)
		var node := own(part,MeshInstance3D.new(),"GateTowerPlinth_%d"%x) as MeshInstance3D
		node.mesh = mesh_box
		node.position = Vector3(x,31.5,-158)
		node.material_override = paving
		var body := own(part,StaticBody3D.new(),"GateTowerPlinthGround_%d"%x) as StaticBody3D
		body.position = node.position
		body.collision_layer = 1
		body.collision_mask = 0
		var cs := own(body,CollisionShape3D.new(),"Shape") as CollisionShape3D
		var shape := BoxShape3D.new()
		shape.size = mesh_box.size
		cs.shape = shape
	packed = PackedScene.new()
	assert(packed.pack(part)==OK)
	assert(ResourceSaver.save(packed,REVIEW+"Parts/GateQuarter.tscn")==OK)
	part.free()
	city.set_meta("physical_landings_finished",true)
	return {"lighthouse_floor_winding_corrected":true,"gate_tower_ground_plinths":2}

func restore_upper_town_floor() -> Dictionary:
	load_layout()
	if city.get_meta("upper_floor_legacy_cut_removed",false): return {"error":"Already restored"}
	part = ResourceLoader.load(REVIEW+"Parts/UpperTown.tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	var polygon: PackedVector2Array = polygons[5]
	var a := Vector2(49,-39)
	var b := Vector2(49,-83)
	var r := Vector2(6.1,0)
	var cut := PackedVector2Array([a-r,b-r,b+r,a+r])
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var pieces := Geometry2D.clip_polygons(polygon,cut)
	for piece in pieces:
		var order := Geometry2D.triangulate_polygon(piece)
		for j in range(0,order.size(),3):
			var aa := piece[order[j]]
			var bb := piece[order[j+1]]
			var cc := piece[order[j+2]]
			if (bb-aa).cross(cc-aa)<0:
				var swap := bb
				bb = cc
				cc = swap
			st.set_normal(Vector3.UP)
			st.add_vertex(Vector3(aa.x,32,aa.y))
			st.add_vertex(Vector3(bb.x,32,bb.y))
			st.add_vertex(Vector3(cc.x,32,cc.y))
	st.index()
	var mesh := st.commit()
	var node := part.get_node("UpperTown/FlatTerrace") as MeshInstance3D
	node.mesh = mesh
	part.get_node("UpperTown/FlatTerraceCollision/Shape").shape = mesh.create_trimesh_shape()
	var packed := PackedScene.new()
	assert(packed.pack(part)==OK)
	assert(ResourceSaver.save(packed,REVIEW+"Parts/UpperTown.tscn")==OK)
	part.free()
	city.set_meta("upper_floor_legacy_cut_removed",true)
	return {"removed_inactive_gate_and_garden_cuts":true,"active_ramp_open":true}

func rebuild_mask_binding() -> Dictionary:
	load_layout()
	terrain = city.get_node("World/Terrain3D")
	return install_foundation_mask()
