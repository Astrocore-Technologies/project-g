@tool
extends "res://Tools/Linvel/LinvelCityCenterAuthoring.gd"
## One explicit append, never runtime generation. Manual edits live in the saved part.

const HARBOR_PART := "res://Scenes/Regions/Linvel/Parts/HarborQuarter.tscn"
const SEA := 5.74
const QUAY := 6.96

func shore_z(x: float) -> float:
	return 64.0 + 0.004 * pow(x + 55.0, 2.0)

func harbor_height(x: float, z: float) -> float:
	if x < -100.0 or x > -10.0:
		return 3.5
	# Rigid civic terraces, not stone paving draped over a rounded soil slope.
	var step_edge := 49.0 if x>=-41.5 and x<=-34.5 else 50.0
	var land := 8.48 if z<=step_edge else QUAY
	if x>=-26.0:
		land = lerpf(8.48,QUAY,clampf((z-43.0)/15.0,0.0,1.0))
	var h := lerpf(land,3.5,smoothstep(shore_z(x)-0.3,shore_z(x)+1.2,z))
	if z>=57.0:
		var end_fade := maxf(1.0-smoothstep(-106.0,-98.0,x),smoothstep(-16.0,-8.0,x))
		h = lerpf(h,3.5,end_fade)
	return h

func solid(parent: Node3D, label: String, p: Vector3, dimensions: Vector3, yaw := 0.0, walkable := false) -> StaticBody3D:
	var body := StaticBody3D.new()
	own(parent,body,label)
	body.position = p
	body.rotation.y = yaw
	body.collision_layer = 4
	body.add_to_group("surface_geometry",true)
	if not walkable:
		body.add_to_group("surface_occluder",true)
		body.set_meta("surface_nonwalkable_top",true)
	var shape := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = dimensions
	shape.shape = box
	own(body,shape,"Shape")
	return body

func slope(parent: Node3D, label: String, center: Vector3, width: float, depth: float, rise: float) -> void:
	var body := StaticBody3D.new()
	own(parent,body,label)
	body.position = center
	body.collision_layer = 4
	body.add_to_group("surface_geometry",true)
	var collision := CollisionShape3D.new()
	var shape := ConvexPolygonShape3D.new()
	var points := PackedVector3Array()
	for x in [-width/2.0,width/2.0]:
		points.append(Vector3(x,0,depth/2.0))
		points.append(Vector3(x,0,-depth/2.0))
		points.append(Vector3(x,rise,-depth/2.0))
	shape.points = points
	collision.shape = shape
	own(body,collision,"Shape")

func water(parent: Node3D) -> void:
	# Existing hand-painted water shader/textures, with an independent bay material.
	var material: ShaderMaterial = load(ASSETS + "Beech/Revision2/VisualStyle/Materials/ForestWater.tres").duplicate()
	material.shader = load(CITY + "Materials/HarborWater.gdshader")
	material.set_shader_parameter("shallow_colour",Color(0.20,0.59,0.65))
	material.set_shader_parameter("deep_colour",Color(0.045,0.25,0.39))
	material.set_shader_parameter("foam_colour",Color(0.78,0.91,0.87))
	material.set_shader_parameter("flow_speed",0.22)
	material.set_shader_parameter("depth_colour_distance",2.0)
	material.set_shader_parameter("minimum_opacity",0.78)
	material.set_shader_parameter("opacity_depth",0.5)
	material.set_shader_parameter("shore_fade_distance",0.18)
	ResourceSaver.save(material,CITY + "Materials/HarborWater.tres")
	material.take_over_path(CITY + "Materials/HarborWater.tres")
	var mesh := PlaneMesh.new()
	mesh.size = Vector2(170,100)
	mesh.subdivide_width = 24
	mesh.subdivide_depth = 18
	var surface := MeshInstance3D.new()
	own(parent,surface,"ShelteredBayWater")
	surface.position = Vector3(-48,SEA,96)
	surface.mesh = mesh
	surface.material_override = material
	surface.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
	surface.set_meta("presentation_only",true)
	surface.set_meta("sea_level_metres",SEA)

func quay_edge(parent: Node3D) -> void:
	# The wall follows the actual waterfront; neither road nor stair crosses it.
	for index in range(20):
		var x1 := -96.0 + index * 4.0
		var x2 := x1 + 4.0
		var a := Vector2(x1,shore_z(x1))
		var b := Vector2(x2,shore_z(x2))
		var delta := b-a
		var midpoint := (a+b)*0.5
		var model := add_asset(parent,"harbor_seawall",Vector3(midpoint.x,QUAY-1.415,midpoint.y),rad_to_deg(-atan2(delta.y,delta.x)),"QuayWall%02d" % index)
		model.scale.x = delta.length()/4.0
		# Its cap is flush with the quay and deck. No invisible barrier at pier entries.
		solid(parent,"QuayCap%02d" % index,Vector3(midpoint.x,QUAY-0.14,midpoint.y),Vector3(delta.length()+0.1,0.28,0.88),-atan2(delta.y,delta.x),true)
	for x in [-92,-88,-84,-80,-76,-72,-68,-64,-60,-56,-52,-48,-44,-32,-28]:
		add_asset(parent,"terrace_retaining_wall",Vector3(x,5.52,50.6),0,"UpperRetaining%d" % x)
	for x in [-39.7,-36.3]:
		var stair := add_asset(parent,"terrace_stairs",Vector3(x,QUAY,50.6),0,"QuayStair%s" % x)
		slope(stair,"WalkableStair",Vector3(0,0,0),2.8,3.4,1.55)
		for sign in [-1,1]:
			solid(stair,"Parapet%d" % sign,Vector3(sign*1.56,1.0,0),Vector3(0.32,1.5,3.4))
	# The parallel stair units share a central parapet. Cart access uses the east slope.
	for x in [-88,-72,-56,-25]:
		add_asset(parent,"harbor_bollard",Vector3(x,QUAY,shore_z(x)-1.25),0,"QuayBollard%d" % x)

func pier(parent: Node3D, x: float, first_z: float, count: int, label: String) -> void:
	var group := own(parent,Node3D.new(),label) as Node3D
	for index in range(count):
		var model := add_asset(group,"dock_straight",Vector3(x,SEA,first_z+index*6),0,"Deck%d" % index)
		solid(model,"WalkableDeck",Vector3(0,1.13,0),Vector3(2.5,0.18,6.02),0,true)
	var landing_z := first_z+(count-1)*6+5.0
	var landing := add_asset(group,"dock_landing",Vector3(x,SEA,landing_z),0,"Landing")
	solid(landing,"WalkableDeck",Vector3(0,1.13,0),Vector3(5,0.18,4.02),0,true)
	add_asset(group,"harbor_bollard",Vector3(x-1.8,QUAY,landing_z+0.8),0,"LandingBollard")
	add_asset(group,"mooring_rope",Vector3(x+1.6,QUAY,landing_z-0.7),0,"SpareMooringLine")

func waterfront_buildings(parent: Node3D) -> void:
	var warehouse := add_asset(parent,"harbor_warehouse",Vector3(-78,QUAY,56),0,"LocalCargoWarehouse")
	body_for(warehouse,Vector3(8.8,6.2,8.5),Vector3(-0.2,3.1,0.7))
	var shed := add_asset(parent,"boatshed_open",Vector3(-54,QUAY,56),0,"OpenBoatWorkshop")
	for x in [-2.6,2.6]:
		for z in [-3.15,-1.05,1.05,3.15]:
			solid(shed,"Post%s_%s" % [x,z],Vector3(x,1.6,z),Vector3(0.45,3.2,0.45))
	solid(shed,"BackSlats",Vector3(0,1.5,-3.15),Vector3(5.2,2.7,0.2))
	solid(shed,"Floor",Vector3(0,0.065,0),Vector3(5.2,0.13,6.3),0,true)
	var repair := add_asset(parent,"fishing_rowboat",Vector3(-54,QUAY+0.95,56),0,"BoatOnCradle")
	solid(repair,"HullObstacle",Vector3(0,0.1,0),Vector3(1.5,0.85,4.0))
	var closed := add_asset(parent,"boatshed_closed",Vector3(-25,QUAY,59),0,"FishingGearStore")
	body_for(closed,Vector3(5.35,3.3,6.45),Vector3(0,1.65,0))
	var tower := add_asset(parent,"harbor_watchtower",Vector3(-92,QUAY,61),0,"HarborSignalTower")
	body_for(tower,Vector3(3.4,8.5,3.4),Vector3(0,4.25,0))

func props(parent: Node3D) -> void:
	for p in [Vector3(-63,QUAY,60),Vector3(-44,QUAY,59),Vector3(-19,QUAY,64)]:
		var model := add_asset(parent,"fishing_net_rack",p,0,"NetRack%d" % parent.get_child_count())
		solid(model,"RackObstacle",Vector3(0,1,0),Vector3(2.45,2,0.65))
	for p in [Vector3(-85,QUAY,59),Vector3(-85.9,QUAY,58),Vector3(-85,QUAY+0.75,59)]:
		var crate := add_asset(parent,"cargo_crate",p,12,"CargoCrate%d" % parent.get_child_count())
		solid(crate,"CrateObstacle",Vector3(0,0.375,0),Vector3(0.9,0.75,0.7))
	for p in [Vector3(-69,QUAY,58),Vector3(-68,QUAY,58.3)]:
		var barrel := add_asset(parent,"cargo_barrel",p,0,"Barrel%d" % parent.get_child_count())
		solid(barrel,"BarrelObstacle",Vector3(0,0.55,0),Vector3(0.8,1.1,0.8))
	add_asset(parent,"cargo_sacks",Vector3(-72,QUAY,57),0,"CargoSacks")
	add_asset(parent,"handcart",Vector3(-28,8.48,38),-90,"CartWaitingAboveRamp")
	for p in [Vector3(-45,8.48,43),Vector3(-33,QUAY,55),Vector3(-66,QUAY,63),Vector3(-17,QUAY,59)]:
		add_asset(parent,"street_lantern",p,0,"HarborLantern%d" % parent.get_child_count())
	for p in [Vector3(-48,8.48,37),Vector3(-79,8.48,35),Vector3(-96,8.48,40)]:
		add_asset(parent,"orchard_tree",p,0,"Tree%d" % parent.get_child_count())
	for p in [Vector3(-97,8.48,33),Vector3(-98,8.48,36),Vector3(-13,8.48,40)]:
		add_asset(parent,"garden_shrub",p,0,"Shrub%d" % parent.get_child_count())
	for p in [Vector3(-98,4.4,71),Vector3(-96,4.3,73),Vector3(-100,4.6,69),Vector3(-13,4.4,72),Vector3(-11,4.6,70),Vector3(-15,4.5,73)]:
		var rock := add_asset(parent,"coastal_boulder_a" if parent.get_child_count()%2==0 else "coastal_boulder_b",p,float(parent.get_child_count()*31%360),"CoastalRock%d" % parent.get_child_count())
		rock.scale = Vector3.ONE*1.4

func extend_harbor_terrain(terrain: Terrain3D) -> Dictionary:
	var added := 0
	var connection := 0
	for z in range(30,121):
		for x in range(-112,15):
			var p := Vector3(x,0,z)
			if not terrain.data.get_control_hole(p):
				continue
			var land := x>=-100 and x<=-10 and z<=shore_z(x)+1.2
			var seabed := z>=57
			if not land and not seabed:
				continue
			terrain.data.set_control_hole(p,false)
			var h := harbor_height(x,z)
			terrain.data.set_height(p,h)
			terrain.data.set_control_auto(p,false)
			terrain.data.set_control_navigation(p,h>SEA)
			var paved := x>=-94 and x<=-15 and z>=32 and z<shore_z(x)-0.25
			var layer := 1 if paved else (3 if h<SEA else 0)
			terrain.data.set_control_base_id(p,layer)
			added += 1
	# Only paint a named route on the accepted center; preserve all its heights.
	for z in range(33,44):
		for x in range(-42,0):
			var horizontal := z<=39 and x<=-3
			var to_stair := x>=-42 and x<=-34
			if horizontal or to_stair:
				terrain.data.set_control_base_id(Vector3(x,0,z),1)
				connection += 1
	terrain.data.update_maps(Terrain3DRegion.TYPE_MAX,true,true)
	terrain.data.save_directory(terrain.data_directory)
	return {"new_cells":added,"connection_paint_cells":connection,"previous_heights_changed":false}

func build_harbor() -> Dictionary:
	city = get_parent() as Node3D
	if city.scene_file_path != "res://Scenes/Regions/Linvel/LinvelCity.tscn":
		return {"error":"Open LinvelCity before authoring."}
	if ResourceLoader.exists(HARBOR_PART):
		return {"error":"Existing editable harbor: refusing to regenerate."}
	for id in ["dock_straight","dock_landing","harbor_seawall","harbor_warehouse","boatshed_open","boatshed_closed","fishing_sailboat","fishing_rowboat"]:
		if not ResourceLoader.exists(ASSETS+id+".glb"):
			return {"missing_asset":id}
	painted = load(CITY + "Materials/linvel_painted.tres")
	part = Node3D.new()
	part.name = "HarborQuarter"
	part.set_meta("part_id","linvel-harbor-03")
	part.set_meta("review_status","Awaiting user review")
	part.set_meta("presentation_only","No ship travel, fishing or economy rules")
	var shore := own(part,Node3D.new(),"QuayAndAccess") as Node3D
	quay_edge(shore)
	var piers := own(part,Node3D.new(),"FishingPiers") as Node3D
	pier(piers,-58,67,3,"WestPier")
	pier(piers,-34,69,2,"EastPier")
	var buildings := own(part,Node3D.new(),"WaterfrontBuildings") as Node3D
	waterfront_buildings(buildings)
	var boats := own(part,Node3D.new(),"MooredBoats") as Node3D
	add_asset(boats,"fishing_sailboat",Vector3(-60.6,SEA,74),0,"LocalFishingSailboat")
	add_asset(boats,"fishing_rowboat",Vector3(-30.3,SEA,74),0,"EastRowboat")
	add_asset(boats,"fishing_rowboat",Vector3(-54.2,SEA,79),180,"WestRowboat")
	var details := own(part,Node3D.new(),"HarborDetails") as Node3D
	props(details)
	var bay := own(part,Node3D.new(),"Bay") as Node3D
	water(bay)
	if not part.has_node("FishingPiers/WestPier/Landing") or buildings.get_child_count()<5:
		part.free()
		return {"error":"Incomplete harbor; nothing saved."}
	var packed := PackedScene.new()
	var error := packed.pack(part)
	if error == OK:
		error = ResourceSaver.save(packed,HARBOR_PART)
	part.free()
	if error != OK:
		return {"error":error}
	var terrain_report := extend_harbor_terrain(city.get_node("World/Terrain3D"))
	var saved: PackedScene = ResourceLoader.load(HARBOR_PART,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE)
	var instance := saved.instantiate()
	if instance.scene_file_path != HARBOR_PART:
		instance.free()
		return {"error":"Saved harbor missing source path."}
	city.get_node("World/CityParts").add_child(instance)
	instance.owner = city
	city.set_meta("linvel_city_stage","harbor-03-awaiting-review")
	city.get_node("World/CityParts/TrainingQuarter").set_meta("review_status","Approved to continue by user 2026-10-20")
	city.get_node("World/Terrain3D").set_meta("review_part","harbor-03")
	return {"saved":true,"part":HARBOR_PART,"placements":placements.size(),"terrain":terrain_report,"sea_level":SEA,"quay_height":QUAY}

func finish_harbor_waterfront() -> Dictionary:
	city = get_parent() as Node3D
	if city.scene_file_path != "res://Scenes/Regions/Linvel/LinvelCity.tscn":
		return {"error":"Open LinvelCity before the harbor correction."}
	var terrain := city.get_node("World/Terrain3D") as Terrain3D
	var corrected := 0
	# This is entirely in the new harbor, beyond both previously approved parts.
	for z in range(44,121):
		for x in range(-112,15):
			var p := Vector3(x,0,z)
			if terrain.data.get_control_hole(p):
				continue
			terrain.data.set_height(p,harbor_height(x,z))
			corrected += 1
	terrain.data.update_maps(Terrain3DRegion.TYPE_HEIGHT,true,true)
	terrain.data.save_directory(terrain.data_directory)
	var material: ShaderMaterial = load(CITY+"Materials/HarborWater.tres")
	material.shader = load(CITY+"Materials/HarborWater.gdshader")
	ResourceSaver.save(material,CITY+"Materials/HarborWater.tres")
	part = ResourceLoader.load(HARBOR_PART,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	painted = load(CITY+"Materials/linvel_painted.tres")
	var quay := part.get_node("QuayAndAccess")
	for x in [-44,-32]:
		if not quay.has_node("UpperRetaining%d" % x):
			add_asset(quay,"terrace_retaining_wall",Vector3(x,5.52,50.6),0,"UpperRetaining%d" % x)
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
	return {"corrected_cells":corrected,"rigid_terraces":true,"linear_cart_ramp":true,"previous_parts_changed":false}
