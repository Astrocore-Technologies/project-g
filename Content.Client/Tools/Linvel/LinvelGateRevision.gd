@tool
extends "res://Tools/Linvel/LinvelArtFinish.gd"
## Explicit one-time authoring. Native district scenes remain the editable source.
var terrain: Terrain3D
var wall_count := 0
var wall_checks: Array = []
var changed_parts: Array[String] = []

func save_part() -> void:
	var packed := PackedScene.new()
	assert(packed.pack(part)==OK)
	assert(ResourceSaver.save(packed,REVIEW+"Parts/"+part.name+".tscn")==OK)
	changed_parts.append(str(part.name))

func open_part(label: String) -> void:
	part = ResourceLoader.load(REVIEW+"Parts/"+label+".tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	drop(part.get_node_or_null("ArtDecorBatches"))

func prepare_prefabs() -> void:
	for key in ["townhouse_a","market_fruit","market_bread","handcart","cargo_crate","cargo_barrel","orchard_fence","crop_row","guild_banner_pole","street_lantern","flower_planter","public_bench","city_beech_0","city_beech_2"]:
		prefabs[key] = load(PREFABS+key+".tscn")
	prefab("low_city_wall_8m",BASE+"Models/low_city_wall_8m.glb",paving)
	prefab("north_gate_arch_12m",BASE+"Models/north_gate_arch_12m.glb",paving)

func ground_patch(parent: Node3D, label: String, center: Vector3, size: Vector2, grass := false, solid := false) -> void:
	var node := own(parent,MeshInstance3D.new(),label) as MeshInstance3D
	var mesh := PlaneMesh.new()
	mesh.size = size
	node.mesh = mesh
	node.position = center
	if grass:
		var mat := StandardMaterial3D.new()
		mat.albedo_texture = load(BASE+"Textures/grass_paint.res")
		mat.uv1_scale = Vector3(size.x*.045,size.y*.045,1)
		mat.diffuse_mode = BaseMaterial3D.DIFFUSE_TOON
		mat.roughness = 1
		node.material_override = mat
	else:
		node.material_override = paving
	if solid:
		var body := own(parent,StaticBody3D.new(),label+"Ground") as StaticBody3D
		body.position = center-Vector3(0,.12,0)
		body.collision_layer = 1
		body.collision_mask = 0
		var shape := BoxShape3D.new()
		shape.size = Vector3(size.x,.24,size.y)
		var cs := own(body,CollisionShape3D.new(),"Shape") as CollisionShape3D
		cs.shape = shape

func landscape_approach() -> Dictionary:
	var garden: PackedVector2Array = polygons[9]
	var changed := 0
	for z in range(-258,-134,2):
		for x in range(-68,130,2):
			var p := Vector2(x,z)
			var inside := Geometry2D.is_point_in_polygon(p,garden)
			var d := 0.0
			if not inside:
				d = INF
				for i in garden.size():
					d = minf(d,p.distance_to(Geometry2D.get_closest_point_to_segment(p,garden[i],garden[(i+1)%garden.size()])))
			var gate := x>=20 and x<=88 and z>=-170 and z<=-138
			if (d>18 and not gate) or (z> -156 and not gate):
				continue
			var position := Vector3(x,0,z)
			var old := terrain.data.get_height(position)
			if not is_finite(old): old = 3
			var fade := 1.0-smoothstep(0,18,d)
			var h := lerpf(old,31.97,fade)
			terrain.data.set_height(position,h)
			terrain.data.set_control_hole(position,false)
			terrain.data.set_control_auto(position,false)
			terrain.data.set_control_base_id(position,0)
			terrain.data.set_control_navigation(position,true)
			changed += 1
	terrain.data.update_maps(Terrain3DRegion.TYPE_MAX,true,true)
	terrain.data.save_directory(terrain.data_directory)
	return {"native_approach_cells":changed,"height_m":31.97}

func gardens() -> void:
	open_part("GardensQuarter")
	drop(part.get_node_or_null("GardenApproach"))
	drop(part.get_node_or_null("GardenApproach_EdgeBalustrades"))
	var beds := own(part,Node3D.new(),"ForegateOrchards") as Node3D
	for i in 6:
		place(beds,"crop_row","VegetableRow_%02d"%i,Vector3(-24+i*4,32,-174),Vector3(.8,.8,1.5))
	for i in 7:
		place(beds,"orchard_fence","OrchardBoundary_%02d"%i,Vector3(-34+i*5,32,-166),Vector3.ONE*1.2)
	for p in [Vector3(13,32,-181),Vector3(77,32,-207)]:
		place(beds,"handcart","FarmCart_%d"%beds.get_child_count(),p)
	for i in 5:
		place(beds,"city_beech_2","ApproachTree_%d"%i,Vector3(-30+i*16,32,-222),Vector3.ONE*.57,float(i*71))
	part.get_node("Anchor_Garden").set_meta("public_label","Предместье и сады") if part.has_node("Anchor_Garden") else false
	bake_decor_batches()
	save_part()
	part.free()

func gates() -> void:
	open_part("GateQuarter")
	for child in part.get_children():
		if str(child.name).begins_with("LandwardWall") or str(child.name).begins_with("GateApproach"):
			drop(child)
	for i in 2:
		var key := "NorthGateTower_%d"%(i+1)
		var tower := part.get_node(key) as Node3D
		tower.position = Vector3(36+i*20,32,-158)
		tower.get_node("Art").scale.y *= .82
		var collision := part.get_node(key+"Collision") as Node3D
		collision.position = tower.position
	var tower_points := [Vector3(-157,29,-106),Vector3(-117,29,-135),Vector3(147,36,-162),Vector3(196,36,-133),Vector3(194,36,-75)]
	for i in tower_points.size():
		var p: Vector3 = tower_points[i]
		p.y = maxf(p.y,terrain.data.get_height(p)-.15)
		part.get_node("WallTower_%d"%(i+1)).position = p
		part.get_node("WallTower_%dCollision"%(i+1)).position = p
	place(part,"north_gate_arch_12m","NorthGateArch",Vector3(46,32,-158))
	# The arch is open: only its piers and elevated lintel have collision.
	for x in [39.4,52.6]:
		collision_box(part,"GatePier_%d"%int(x),Vector3(x,35,-158),Vector3(1.2,6,2.5))
	collision_box(part,"GateUpperLintel",Vector3(46,41.4,-158),Vector3(12.5,1.5,2.5))
	var court := own(part,Node3D.new(),"EntryCourt") as Node3D
	ground_patch(court,"CompactPavedCourt",Vector3(48,32.015,-143),Vector2(36,24),false,true)
	var guard := place(court,"townhouse_a","GuardHouse",Vector3(15,32,-147),Vector3(1.15,.9,1.15))
	guard.set_meta("public_function","Караулка; служебный двор")
	collision_box(court,"GuardHouseSolid",Vector3(15,36,-147),Vector3(7.8,8,6.5))
	for p in [Vector3(7,32,-140),Vector3(22,32,-140)]:
		place(court,"cargo_crate","InspectionCrate_%d"%court.get_child_count(),p)
	place(court,"handcart","InspectionCart",Vector3(75,32,-143))
	for x in [72,77,82]:
		place(court,"orchard_fence","HitchingRail_%d"%x,Vector3(x,32,-151))
	for p in [Vector3(31,32,-143),Vector3(64,32,-143)]:
		place(court,"guild_banner_pole","EntryStandard_%d"%court.get_child_count(),p,Vector3.ONE*1.3)
	for p in [Vector3(33,32,-134),Vector3(65,32,-134),Vector3(36,32,-169),Vector3(56,32,-169)]:
		place(court,"street_lantern","EntryLantern_%d"%court.get_child_count(),p)
	part.get_node("Anchor_Gate").position = Vector3(46,32,-158)
	bake_decor_batches()
	save_part()
	part.free()

func upper_town() -> void:
	open_part("UpperTown")
	var group := own(part,Node3D.new(),"EntryNeighbourhood") as Node3D
	# Existing houses form two street fronts; retain their IDs and collisions.
	for item in [["UpperHome_03",Vector3(18,32,-111)],["UpperHome_04",Vector3(68,32,-135)],["UpperHome_10",Vector3(26,32,-72)]]:
		part.get_node(item[0]).position = item[1]
		part.get_node(item[0]+"Collision").position = item[1]
	for p in [Vector3(68,32,-119),Vector3(24,32,-151)]:
		var key := "BreadStall" if p.x>50 else "ProduceStall"
		place(group,"market_bread" if p.x>50 else "market_fruit",key,p,Vector3.ONE*1.2,90 if p.x>50 else -90)
	for patch in [[Vector3(-41,32.025,-136),Vector2(10,26)],[Vector3(-6,32.025,-151),Vector2(49,9)],[Vector3(13,32.025,-102),Vector2(15,7)],[Vector3(71,32.025,-63),Vector2(12,14)]]:
		ground_patch(group,"GardenBed_%d"%group.get_child_count(),patch[0],patch[1],true)
	for i in 5:
		place(group,"orchard_fence","ResidentialCourtRail_%d"%i,Vector3(-35+i*7,32,-144),Vector3.ONE*1.2)
	for p in [Vector3(-40,32,-141),Vector3(6,32,-151),Vector3(13,32,-103)]:
		place(group,"city_beech_0","StreetBeech_%d"%group.get_child_count(),p,Vector3.ONE*.45)
	for p in [Vector3(59,32,-128),Vector3(38,32,-138)]:
		place(group,"public_bench","EntryBench_%d"%group.get_child_count(),p,Vector3.ONE,90)
	bake_decor_batches()
	save_part()
	part.free()

func wall_run(points: Array, label: String) -> void:
	var run := own(part,Node3D.new(),label) as Node3D
	for k in range(points.size()-1):
		var a := vec(points[k])
		var b := vec(points[k+1])
		var n := ceili(Vector2(a.x,a.z).distance_to(Vector2(b.x,b.z))/7.8)
		for j in n:
			var start := a.lerp(b,float(j)/n)
			var end := a.lerp(b,float(j+1)/n)
			var center := (start+end)*.5
			var h := terrain.data.get_height(center)
			if is_finite(h): center.y = maxf(center.y,h-.15)
			var length := Vector2(start.x,start.z).distance_to(Vector2(end.x,end.z))+.24
			var node := place(run,"low_city_wall_8m","Curtain_%03d"%wall_count,center,Vector3(length/8,1,1),-rad_to_deg(atan2(b.z-a.z,b.x-a.x)))
			node.set_meta("wall_module_length_m",length)
			collision_box(node,"WallSolid",Vector3(0,1,0),Vector3(8,7,2))
			wall_checks.append([center.x,center.y+1,center.z,sin(node.rotation.y),cos(node.rotation.y)])
			wall_count += 1

func defenses() -> void:
	part = Node3D.new()
	part.name = "OuterDefenses"
	part.set_meta("public_function","Невысокая сухопутная стена; гавань открыта")
	wall_run([[-162,4,120],[-173,4,96],[-185,12,48],[-187,18,16],[-177,18,-40],[-157,29,-106],[-117,29,-135],[-56,32,-164],[25,32,-164],[36,32,-158]],"WestCurtain")
	wall_run([[56,32,-158],[90,36,-163],[147,36,-162],[196,36,-133],[194,36,-75],[180,20,-34],[173,20,12],[152,20,72],[94,4,114],[96,4,128]],"EastCurtain")
	part.set_meta("module_count",wall_count)
	part.set_meta("wall_probe_points",wall_checks)
	# Sources remain individual editable instances. Batch only their repeated render mesh.
	var groups := {}
	for run in part.get_children():
		for source in run.get_children():
			for child in source.get_children():
				if child is MeshInstance3D:
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
		var node := own(batches,MultiMeshInstance3D.new(),"WallMesh_%d"%i) as MultiMeshInstance3D
		node.multimesh = multi
		node.material_override = paving
		node.set_meta("source_mesh_paths",groups[mesh].paths)
		i += 1
	save_part()
	part.free()

func finish_coastal_ends() -> Dictionary:
	load_layout()
	if city.get_meta("entry_revision",0)!=1 or city.get_meta("wall_coastal_ends",false):
		return {"error":"Coastal ends already reviewed or no entry revision"}
	terrain = city.get_node("World/Terrain3D")
	prepare_prefabs()
	# Add only the two missing terminal connections; preserve the authored wall.
	open_part("OuterDefenses")
	wall_count = int(part.get_meta("module_count"))
	wall_checks = part.get_meta("wall_probe_points")
	wall_run([[-162,4,120],[-173,4,96]],"WestCoastalEnd")
	wall_run([[94,4,114],[96,4,128]],"EastCoastalEnd")
	part.set_meta("module_count",wall_count)
	part.set_meta("wall_probe_points",wall_checks)
	save_part()
	part.free()
	var parent := city.get_node("World/CityParts")
	drop(parent.get_node("OuterDefenses"))
	var node: Node3D = ResourceLoader.load(REVIEW+"Parts/OuterDefenses.tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	parent.add_child(node)
	node.owner = city
	city.set_meta("wall_coastal_ends",true)
	return {"coastal_ends":2,"wall_modules":wall_count}

func apply_revision() -> Dictionary:
	load_layout()
	if city.scene_file_path!=REVIEW+"LinvelCity_Art01.tscn" or city.get_meta("entry_revision",0)>0:
		return {"error":"Only unmodified Art01 entry; preserve subsequent hand edits"}
	terrain = city.get_node("World/Terrain3D")
	prepare_prefabs()
	var landscape := landscape_approach()
	gardens()
	gates()
	upper_town()
	defenses()
	var parent := city.get_node("World/CityParts")
	for label in changed_parts:
		drop(parent.get_node_or_null(label))
		var packed: PackedScene = ResourceLoader.load(REVIEW+"Parts/"+label+".tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE)
		var node := packed.instantiate()
		parent.add_child(node)
		node.owner = city
	city.set_meta("entry_revision",1)
	city.set_meta("entry_design","art-source/linvel/gate-revision/design.md")
	var report := {"changed_parts":changed_parts,"wall_modules":wall_count,"native_terrain":landscape,"clear_gate_width_m":12,"arch_passage_m":8,"server_navigation_exported":false}
	var file := FileAccess.open("res://../.artifacts/linvel-gate/authoring-report.json",FileAccess.WRITE)
	file.store_string(JSON.stringify(report,"\t"))
	return report

func settle_defense_foundations() -> Dictionary:
	load_layout()
	if city.get_meta("entry_revision",0)!=1 or city.get_meta("wall_foundations_settled",false):
		return {"error":"Foundations already reviewed or no entry revision"}
	terrain = city.get_node("World/Terrain3D")
	var walls: Node3D = ResourceLoader.load(REVIEW+"Parts/OuterDefenses.tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	var modules: Array[Vector3] = []
	var probes: Array = []
	for run in walls.get_children():
		for source in run.get_children():
			if source.has_meta("wall_module_length_m"):
				var p: Vector3 = source.position
				modules.append(p)
				var normal: Vector3 = source.basis.z.normalized()
				probes.append([p.x,p.y+1,p.z,normal.x,normal.z])
	walls.set_meta("wall_probe_points",probes)
	var count := 0
	for z in range(-182,145,2):
		for x in range(-208,217,2):
			var p := Vector2(x,z)
			var covered := false
			for i in polygons.size():
				if i not in [9,11] and Geometry2D.is_point_in_polygon(p,polygons[i]):
					covered = true
					break
			if covered: continue
			var distance := INF
			var target := 0.0
			for module in modules:
				var d := p.distance_squared_to(Vector2(module.x,module.z))
				if d<distance:
					distance = d
					target = module.y-.07
			distance = sqrt(distance)
			if distance>18: continue
			var position := Vector3(x,0,z)
			var old := terrain.data.get_height(position)
			if not is_finite(old): old = 3
			var h := lerpf(old,target,1.0-smoothstep(5,18,distance))
			if h<=old: continue
			terrain.data.set_height(position,h)
			terrain.data.set_control_hole(position,false)
			terrain.data.set_control_auto(position,false)
			terrain.data.set_control_base_id(position,0)
			count += 1
	terrain.data.update_maps(Terrain3DRegion.TYPE_MAX,true,true)
	terrain.data.save_directory(terrain.data_directory)
	var packed := PackedScene.new()
	assert(packed.pack(walls)==OK)
	assert(ResourceSaver.save(packed,REVIEW+"Parts/OuterDefenses.tscn")==OK)
	walls.free()
	var parent := city.get_node("World/CityParts")
	drop(parent.get_node("OuterDefenses"))
	var node: Node3D = ResourceLoader.load(REVIEW+"Parts/OuterDefenses.tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	parent.add_child(node)
	node.owner = city
	city.set_meta("wall_foundations_settled",true)
	return {"native_foundation_cells":count,"wall_probes":probes.size()}

func finish_tower_and_sightline() -> Dictionary:
	load_layout()
	if city.get_meta("entry_sightline_reviewed",false):
		return {"error":"Already reviewed; preserve manual changes"}
	terrain = city.get_node("World/Terrain3D")
	# Level this corner locally; adjacent curtain descends toward the coast.
	for z in range(-84,-64,2):
		for x in range(184,206,2):
			var p := Vector3(x,0,z)
			var d := Vector2(x-194,z+75).length()
			if d>10: continue
			var old := terrain.data.get_height(p)
			var h := lerpf(old,35.93,1.0-smoothstep(4,10,d))
			if h>old:
				terrain.data.set_height(p,h)
	terrain.data.update_maps(Terrain3DRegion.TYPE_MAX,true,true)
	terrain.data.save_directory(terrain.data_directory)
	part = ResourceLoader.load(REVIEW+"Parts/UpperTown.tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	for key in ["UpperHome_03","UpperHome_03Collision"]:
		part.get_node(key).position.x = 18
	save_part()
	part.free()
	var parent := city.get_node("World/CityParts")
	drop(parent.get_node("UpperTown"))
	var node: Node3D = ResourceLoader.load(REVIEW+"Parts/UpperTown.tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	parent.add_child(node)
	node.owner = city
	city.set_meta("entry_sightline_reviewed",true)
	return {"corner_tower_footing_m":35.93,"house_sightline_x":18}

func clear_gate_paving() -> Dictionary:
	load_layout()
	if city.get_meta("entry_paving_capped",false):
		return {"error":"Already capped"}
	terrain = city.get_node("World/Terrain3D")
	var count := 0
	for z in range(-240,-128,2):
		for x in range(20,90,2):
			var p := Vector2(x,z)
			var on_paving := x>=29 and x<=67 and z>=-156 and z<=-130
			for road in layout.streets:
				if road.id not in ["RoadToValley","UpperMainStreet"]: continue
				for i in range(road.points.size()-1):
					var a := Vector2(road.points[i][0],road.points[i][2])
					var b := Vector2(road.points[i+1][0],road.points[i+1][2])
					if p.distance_to(Geometry2D.get_closest_point_to_segment(p,a,b))<float(road.width)*.5+1.0:
						on_paving = true
			if not on_paving: continue
			var position := Vector3(x,0,z)
			if terrain.data.get_height(position)>31.85:
				terrain.data.set_height(position,31.85)
				count += 1
	terrain.data.update_maps(Terrain3DRegion.TYPE_MAX,true,true)
	terrain.data.save_directory(terrain.data_directory)
	city.set_meta("entry_paving_capped",true)
	return {"rigid_road_native_clearance_cells":count}
