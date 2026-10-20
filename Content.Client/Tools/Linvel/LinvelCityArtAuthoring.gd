@tool
extends Node
## Builds NEW review scenes from the approved native district scenes.
## Existing district scenes, collisions and the current main are never saved here.
const DATA := "res://Assets/World/Linvel/City/Masterplan/layout.json"
const BASE := "res://Assets/World/Linvel/City/Production/"
const PREFABS := "res://Scenes/Props/Linvel/Production/"
const REVIEW := "res://Scenes/Regions/Linvel/Art01/"
var city: Node3D
var part: Node3D
var layout: Dictionary
var catalog: Dictionary = {}
var prefabs: Dictionary = {}
var parts: Dictionary = {}
var paving: Material
var painted: Material
var decorated := 0
var polygons: Array[PackedVector2Array] = []

func own(parent: Node, child: Node, label: String) -> Node:
	child.name = label
	parent.add_child(child)
	child.owner = part
	return child

func drop(node: Node) -> void:
	if node != null:
		node.get_parent().remove_child(node)
		node.free()

func vec(a: Array) -> Vector3:
	return Vector3(a[0],a[1],a[2])

func flatten(source: Node, destination: Node3D, accumulated: Transform3D, material: Material) -> void:
	var tf := accumulated
	if source is Node3D:
		tf = accumulated * source.transform
	if source is MeshInstance3D:
		var mesh := MeshInstance3D.new()
		mesh.name = source.name
		mesh.mesh = source.mesh
		mesh.transform = tf
		if material != null:
			mesh.material_override = material
		else:
			mesh.material_override = source.material_override
			for i in mesh.mesh.get_surface_count():
				var original := mesh.mesh.surface_get_material(i)
				if original is StandardMaterial3D:
					var copy := original.duplicate() as StandardMaterial3D
					copy.diffuse_mode = BaseMaterial3D.DIFFUSE_TOON
					copy.roughness = .9
					mesh.set_surface_override_material(i,copy)
		destination.add_child(mesh)
		mesh.owner = destination
	for child in source.get_children():
		flatten(child,destination,tf,material)

func prefab(key: String, path: String, material: Material) -> void:
	var source: PackedScene = ResourceLoader.load(path,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE)
	assert(source != null,"Import the asset before authoring: "+path)
	var imported := source.instantiate()
	var root := Node3D.new()
	root.name = key.to_pascal_case()
	root.set_meta("original_asset",key)
	flatten(imported,root,Transform3D.IDENTITY,material)
	imported.free()
	var packed := PackedScene.new()
	assert(packed.pack(root)==OK)
	assert(ResourceSaver.save(packed,PREFABS+key+".tscn")==OK)
	root.free()
	prefabs[key] = ResourceLoader.load(PREFABS+key+".tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE)

func place(parent: Node3D, key: String, label: String, position: Vector3, scale := Vector3.ONE, yaw := 0.0) -> Node3D:
	var node := (prefabs[key] as PackedScene).instantiate() as Node3D
	own(parent,node,label)
	node.position = position
	node.scale = scale
	node.rotation.y = deg_to_rad(yaw)
	decorated += 1
	return node

func top_height(p: Vector2) -> float:
	var h := -8.0
	for i in polygons.size():
		if Geometry2D.is_point_in_polygon(p,polygons[i]):
			h = maxf(h,layout.platforms[i].height)
	return h

func route_clear(p: Vector2, radius: float) -> bool:
	for road in layout.streets+layout.links:
		var points: Array = road.get("points",[road.get("start"),road.get("end")])
		for i in range(points.size()-1):
			var a := Vector2(points[i][0],points[i][2])
			var b := Vector2(points[i+1][0],points[i+1][2])
			if p.distance_to(Geometry2D.get_closest_point_to_segment(p,a,b)) < float(road.width)*.5+radius:
				return false
	return true

func building_clear(p: Vector2, radius: float) -> bool:
	for b in layout.buildings:
		if b.role=="ship_clearance":
			continue
		var q := (p-Vector2(b.position[0],b.position[2])).rotated(deg_to_rad(float(b.yaw)))
		if absf(q.x)<float(b.dimensions[0])*.5+radius and absf(q.y)<float(b.dimensions[1])*.5+radius:
			return false
	return true

func key_for(b: Dictionary, index: int) -> String:
	match b.role:
		"guild": return "adventurers_guild"
		"tavern": return "grand_harbor_tavern"
		"healing_house": return "healing_house"
		"sanctuary": return "harbor_shrine"
		"public_hall": return "healing_house"
		"landmark","gate","wall": return "harbor_watchtower"
		"orchard": return "orchard_cottage"
		"warehouse": return "harbor_warehouse" if index%2==0 else "boatshed_closed"
		"workshop": return ["smith_workshop","carpenter_workshop","watermill","market_shop"][index%4]
		"market_shop": return "market_shop"
		"ship_clearance": return "merchant_ship_large"
	return "townhouse_b" if b.kit=="tall_house" else "townhouse_a"

func replace_building(b: Dictionary, index: int) -> void:
	part = parts[b.part]
	var old := part.get_node(b.id) as Node3D
	var transform := Transform3D(old.basis.orthonormalized(),old.position)
	var metadata := {}
	for key in old.get_meta_list():
		metadata[key] = old.get_meta(key)
	drop(old)
	var root := own(part,Node3D.new(),b.id) as Node3D
	root.transform = transform
	for key in metadata:
		root.set_meta(key,metadata[key])
	root.set_meta("not_final_art",false)
	var key := key_for(b,index)
	var info: Dictionary = catalog[key]
	var source := vec(info.dimensions_m)
	var fit := Vector3(float(b.dimensions[0])*.95/source.x,float(b.dimensions[2])*.97/source.z,float(b.dimensions[1])*.95/source.y)
	if key=="grand_harbor_tavern":
		fit = Vector3(1.13,1.13,1.0)
	if key=="merchant_ship_large":
		fit = Vector3.ONE
		root.position.x = [-94.0,-39.0,21.0][int(b.id.right(1))-1]
		root.set_meta("hull_length_m",44)
		root.set_meta("waterline_m",0)
	var model := place(root,key,"Art",Vector3.ZERO,fit)
	if key!="merchant_ship_large":
		model.position.y = -float(info.bounds_blender_m[2][0])*fit.y
	root.set_meta("art_asset",key)
	if key=="grand_harbor_tavern":
		drop(part.get_node_or_null(b.id+"Collision"))
		for i in info.collision_boxes_blender.size():
			var box: Array = info.collision_boxes_blender[i]
			var p := Vector3(box[0][0],box[0][2],-box[0][1])*fit
			var s := Vector3(box[1][0],box[1][2],box[1][1])*fit
			collision_box(root,"InteriorShell_%02d"%i,p,s)
		# Flush ramp under the three visible shallow entrance steps.
		var ramp := BoxShape3D.new()
		var body := own(root,StaticBody3D.new(),"EntryRamp") as StaticBody3D
		body.collision_layer = 4
		body.collision_mask = 0
		body.position = Vector3(0,.21,6.85)
		body.rotation.x = deg_to_rad(22)
		var cs := own(body,CollisionShape3D.new(),"Shape") as CollisionShape3D
		ramp.size = Vector3(3.35,.15,1.35)
		cs.shape = ramp
		root.set_meta("ground_floor_interior",true)

func collision_box(parent: Node3D, label: String, position: Vector3, size: Vector3) -> void:
	var body := own(parent,StaticBody3D.new(),label) as StaticBody3D
	body.position = position
	body.collision_layer = 4
	body.collision_mask = 0
	var cs := own(body,CollisionShape3D.new(),"Shape") as CollisionShape3D
	var shape := BoxShape3D.new()
	shape.size = size
	cs.shape = shape

func finish_surfaces(node: Node) -> void:
	if node is MeshInstance3D:
		if node.has_meta("route_width_m") or node.name in ["FlatTerrace","RetainingMass","VisibleSurface","TavernEntryBridge"] or str(node.name).begins_with("LandwardWall"):
			node.material_override = paving
	for child in node.get_children():
		finish_surfaces(child)

func trim_terraces() -> void:
	for i in layout.platforms.size():
		var terrace: Dictionary = layout.platforms[i]
		part = parts[terrace.part]
		var trim := own(part,Node3D.new(),terrace.id+"_EdgeBalustrades") as Node3D
		var poly := polygons[i]
		for edge in poly.size():
			var a := poly[edge]
			var b := poly[(edge+1)%poly.size()]
			var direction := (b-a).normalized()
			var normal := Vector2(-direction.y,direction.x)
			var count := maxi(1,ceili(a.distance_to(b)/4.0))
			for j in count:
				var p := a.lerp(b,(float(j)+.5)/count)
				var lo := minf(top_height(p+normal*.6),top_height(p-normal*.6))
				var hi := maxf(top_height(p+normal*.6),top_height(p-normal*.6))
				if lo>float(terrace.height)-.6 or hi>float(terrace.height)+.1 or not route_clear(p,1.0) or not building_clear(p,.35):
					continue
				place(trim,"stone_balustrade","Edge_%02d_%03d"%[edge,j],Vector3(p.x,terrace.height+.015,p.y),Vector3(a.distance_to(b)/count/4.1,1,1),-rad_to_deg(direction.angle()))

func civic_and_groves() -> void:
	part = parts.CityCenter
	drop(part.get_node("FountainEnvelope"))
	place(part,"square_fountain","NavigatorsFountain",Vector3(0,23,10),Vector3.ONE*1.08)
	for x in [-24,24]:
		for z in [-5,9,23]:
			drop(part.get_node("MarketEnvelope_%d_%d"%[x,z]))
			place(part,"market_bread" if z==9 else "market_fruit","Market_%d_%d"%[x,z],Vector3(x,23,z),Vector3.ONE*1.25,90 if x<0 else -90)
	for x in [-17,17]:
		place(part,"guild_banner_pole","GuildStandard_%d"%x,Vector3(x,23,-17),Vector3.ONE*1.25)
	for x in [-14,20]:
		for z in [0,21]:
			var p := Vector2(x,z)
			if route_clear(p,1):
				place(part,"public_bench","SquareBench_%d_%d"%[x,z],Vector3(x,23,z),Vector3.ONE*1.15,90 if x<0 else -90)
	part = parts.ResidentialTerraces
	for node in part.get_children():
		if str(node.name).begins_with("CanopyEnvelope"):
			var p: Vector3 = node.position-Vector3(0,6,0)
			var label := str(node.name).replace("CanopyEnvelope","CourtyardTree")
			drop(node)
			place(part,"blossoming_tree" if decorated%3==0 else "orchard_tree",label,p,Vector3.ONE*1.65,float(decorated*67%360))
	# Human-scale street furnishings stay outside the reserved route widths.
	for street in layout.streets:
		part = parts[street.part]
		var decor := own(part,Node3D.new(),street.id+"_Furnishings") as Node3D
		for i in range(street.points.size()-1):
			var a := vec(street.points[i])
			var b := vec(street.points[i+1])
			var forward := (b-a).normalized()
			var side := Vector3(-forward.z,0,forward.x)
			var n := maxi(1,ceili(a.distance_to(b)/22))
			for j in n:
				for sign in [-1,1]:
					var p: Vector3 = a.lerp(b,(j+.5)/n)+side*sign*(float(street.width)*.5+1.0)
					var xz := Vector2(p.x,p.z)
					if absf(top_height(xz)-p.y)<.2 and route_clear(xz,.4) and building_clear(xz,.8):
						place(decor,"street_lantern","Lantern_%d_%d_%d"%[i,j,sign],p,Vector3.ONE*1.05)
						if j%2==0:
							place(decor,"flower_planter","Planter_%d_%d_%d"%[i,j,sign],p+forward*1.7)

func training() -> void:
	part = parts.TrainingQuarter
	for node in part.get_children():
		if str(node.name).begins_with("ArenaRail_") or str(node.name).begins_with("StandTier_") or str(node.name).begins_with("TrainingDummyEnvelope_") or node.name=="DornShelterEnvelope":
			drop(node)
	for i in 52:
		var a := i*TAU/52
		if absf(a-PI)<.23 or absf(a-PI*.5)<.24 or (a>3.7 and a<5.7):
			continue
		var p := Vector3(133+31*cos(a),36.1,-109+24*sin(a))
		place(part,"orchard_fence","ArenaFence_%02d"%i,p,Vector3(1.1,1.25,1),-rad_to_deg(a)+90)
	for i in 2:
		place(part,"arena_covered_stand","CoveredStand_%d"%i,Vector3(125+i*16,36,-137),Vector3(1.1,1.1,1.0))
	for i in 3:
		place(part,"training_dummy","TrainingDummy_%d"%i,Vector3(121+i*12,36.15,-98),Vector3.ONE*1.08)
	place(part,"dorn_pavilion","DornPavilion",Vector3(164,36,-65),Vector3(1.6,1.6,1.55))
	for i in 3:
		place(part,"training_weapon_rack","DornWeaponRack_%d"%i,Vector3(124+i*9,36,-58),Vector3.ONE*1.1)

func harbor() -> void:
	part = parts.HarborQuarter
	for link in layout.links:
		if link.kind!="pier":
			continue
		var root := part.get_node(link.id) as Node3D
		for node in root.get_children():
			if node is MeshInstance3D and (node.name=="VisibleSurface" or str(node.name).begins_with("Support")):
				node.visible = false
		var a := vec(link.start)
		var b := vec(link.end)
		var n := ceili(a.distance_to(b)/12)
		for i in n:
			var p := a.lerp(b,(i+.5)/n)
			p.y = 4.05-1.22*2
			place(root,"dock_straight","TimberDeck_%02d"%i,p,Vector3(3.6,2,a.distance_to(b)/n/6))
			for sign in [-1,1]:
				place(root,"harbor_bollard","Bollard_%02d_%d"%[i,sign],Vector3(p.x+sign*3.8,4.05,p.z))
	for x in [-142,-105,-67,-9,34,78]:
		var group := own(part,Node3D.new(),"DockCargo_%d"%x) as Node3D
		for i in 3:
			place(group,"cargo_crate" if i==0 else "cargo_barrel", "Cargo_%d"%i,Vector3(x+i*1.5,4,133-float(i%2)),Vector3.ONE*1.2)
	for i in 5:
		place(part,"fishing_sailboat" if i%2==0 else "fishing_rowboat","FishingBoat_%02d"%i,Vector3(83+i*4.1,0,152+i*7.3),Vector3.ONE*1.35,-18)

func gardens_and_landscape() -> void:
	part = parts.GardensQuarter
	for node in part.get_children():
		if str(node.name).begins_with("OrchardPlot_"):
			node.visible = false
	for row in 3:
		for col in 8:
			var p := Vector3(-30+col*17,32,-226+row*14)
			var xz := Vector2(p.x,p.z)
			if top_height(xz)>31 and route_clear(xz,3) and building_clear(xz,4):
				place(part,"orchard_tree" if col%3 else "blossoming_tree","Orchard_%d_%d"%[row,col],p,Vector3.ONE*1.3,float(row*97+col*63))
	var terrain := city.get_node("World/Terrain3D") as Terrain3D
	part = parts.CoastAndBackdrop
	var rng := RandomNumberGenerator.new()
	rng.seed = 20261020
	for side in [-1,1]:
		var grove := own(part,Node3D.new(),"WestLandscape" if side<0 else "EastLandscape") as Node3D
		for i in 64:
			var p := Vector3(side*rng.randf_range(208,376),0,rng.randf_range(-360,27))
			p.y = terrain.data.get_height(p)
			if not is_finite(p.y) or p.y<8:
				continue
			var s := rng.randf_range(1.3,2.05)
			place(grove,"cypress_tree" if i%3 else "orchard_tree","Tree_%03d"%i,p,Vector3.ONE*s,rng.randf_range(0,360))
	for node in part.get_children():
		if str(node.name).begins_with("MountainMass_"):
			node.visible = false
	var ridge := own(part,Node3D.new(),"DistantMountainRidge") as Node3D
	for i in 15:
		var p := Vector3(-675+i*94,12,-480-float(i%3)*85)
		var scale := Vector3(58+float(i%3)*13,37+float((i*7)%4)*8,55)
		place(ridge,"rock_spire","Peak_%02d"%i,p,scale,float(i*71%360))
	var sea := part.get_node("OpenSea") as MeshInstance3D
	var water := ShaderMaterial.new()
	water.shader = load(BASE+"Sea.gdshader")
	sea.material_override = water

func preflight() -> Dictionary:
	var source: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(DATA))
	var missing: Array[String] = []
	for info in JSON.parse_string(FileAccess.get_file_as_string(BASE+"catalog.json")):
		var path: String = BASE+"Models/"+info.key+".glb"
		if not ResourceLoader.exists(path):
			missing.append(path)
	for path in ["res://Assets/World/Linvel/arena_covered_stand.glb","res://Assets/World/Linvel/FoxHills/rock_spire.glb",BASE+"Limestone.tres"]:
		if not ResourceLoader.exists(path):
			missing.append(path)
	var protected_sources := {}
	for q in source.districts:
		protected_sources[q.scene] = FileAccess.get_sha256(q.scene)
	return {"ok":missing.is_empty(),"missing":missing,"source_scene_hashes":protected_sources,"output_directory":REVIEW,"existing_sources_will_be_written":false}

func refresh_imports() -> Dictionary:
	var filesystem := EditorInterface.get_resource_filesystem()
	if filesystem.is_scanning():
		return {"scanning":true}
	filesystem.scan()
	return {"scan_requested":true}

func reimport_missing() -> Dictionary:
	var missing: PackedStringArray = []
	for info in JSON.parse_string(FileAccess.get_file_as_string(BASE+"catalog.json")):
		var path: String = BASE+"Models/"+info.key+".glb"
		if not ResourceLoader.exists(path):
			missing.append(path)
	EditorInterface.get_resource_filesystem().reimport_files(missing)
	return {"reimported":missing}

func save_review_copy() -> Dictionary:
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(REVIEW+"Parts"))
	for q in layout.districts:
		var district := parts[q.id] as Node3D
		district.set_meta("art_revision",1)
		district.set_meta("review_stage","Whole-city art pass 01; user review pending")
		var saved := PackedScene.new()
		assert(saved.pack(district)==OK)
		assert(ResourceSaver.save(saved,REVIEW+"Parts/"+q.id+".tscn")==OK)
		district.free()
	# Work on a detached duplicate, never on the edited original main scene.
	var review := city.duplicate() as Node3D
	review.scene_file_path = ""
	for child in review.get_children():
		if child.get_script()!=null:
			drop(child)
	var parent := review.get_node("World/CityParts")
	for q in layout.districts:
		drop(parent.get_node(q.id))
		var saved: PackedScene = ResourceLoader.load(REVIEW+"Parts/"+q.id+".tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE)
		var instance := saved.instantiate()
		parent.add_child(instance)
		instance.owner = review
	review.set_meta("linvel_city_stage","whole-city-art-pass-01")
	review.set_meta("art_direction","Stylized Hand-Painted Anime 3D")
	review.set_meta("whole_city_art_revision",1)
	review.set_meta("source_blockout_scene",city.scene_file_path)
	var main := PackedScene.new()
	assert(main.pack(review)==OK)
	assert(ResourceSaver.save(main,REVIEW+"LinvelCity_Art01.tscn")==OK)
	review.free()
	return {"saved":REVIEW+"LinvelCity_Art01.tscn","districts":layout.districts.size(),"architecture":87,"large_ships":3,"art_instances":decorated,"prefabs":prefabs.size(),"source_main_untouched":true}

func build_review_copy() -> Dictionary:
	city = get_parent() as Node3D
	if FileAccess.file_exists(REVIEW+"LinvelCity_Art01.tscn"):
		return {"error":"Existing review: edit native art scenes; regeneration refused"}
	var check := preflight()
	if not check.ok:
		return check
	layout = JSON.parse_string(FileAccess.get_file_as_string(DATA))
	for p in layout.platforms:
		var polygon := PackedVector2Array()
		for a in p.points:
			polygon.append(Vector2(a[0],a[1]))
		polygons.append(polygon)
	for info in JSON.parse_string(FileAccess.get_file_as_string(BASE+"catalog.json")):
		catalog[info.key] = info
	paving = load(BASE+"Limestone.tres")
	painted = load("res://Assets/World/Linvel/City/Materials/linvel_painted.tres")
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(PREFABS))
	for key in catalog:
		prefab(key,BASE+"Models/"+key+".glb",painted)
	prefab("arena_covered_stand","res://Assets/World/Linvel/arena_covered_stand.glb",painted)
	prefab("rock_spire","res://Assets/World/Linvel/FoxHills/rock_spire.glb",null)
	for q in layout.districts:
		part = ResourceLoader.load(q.scene,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
		parts[q.id] = part
		finish_surfaces(part)
	for i in layout.buildings.size():
		replace_building(layout.buildings[i],i)
	trim_terraces()
	civic_and_groves()
	training()
	harbor()
	gardens_and_landscape()
	var report := save_review_copy()
	for path in check.source_scene_hashes:
		assert(FileAccess.get_sha256(path)==check.source_scene_hashes[path],"Source was unexpectedly changed: "+path)
	return report
