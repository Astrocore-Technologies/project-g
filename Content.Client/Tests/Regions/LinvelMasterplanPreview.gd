extends Node3D
## Autonomous environment review. Controls here do not change PC gameplay input.
@export_range(1,9,1) var initial_view := 1
@export var city_path: NodePath = ^"LinvelCity"
@onready var city: Node3D = get_node(city_path)
@onready var camera: Camera3D = $PreviewCamera
@onready var figure: CharacterBody3D = $ScaleFigure
@onready var terrain: Terrain3D = city.get_node("Terrain3D")
var view := 1
var target := Vector3.INF
var labels: Array[Label3D] = []
var walk_active := false
var walk_index := 0
var walk_time := 0.0
var walk_stuck := 0.0
var walk_result: Dictionary = {}
var walk_points: Array[Vector3] = []
var walk_route := "Harbor > lower town > main square > upper town > arena"
var harbor_stage := ""
var harbor_review_report: Dictionary = {}

func _ready() -> void:
	camera.make_current()
	terrain.set_camera(camera)
	for part in city.get_node("CityParts").get_children():
		for child in part.get_children():
			if child is Marker3D and child.has_meta("public_label"):
				var label := Label3D.new()
				label.text = str(child.get_meta("public_label"))
				label.position = child.global_position+Vector3(0,7,0)
				label.billboard = BaseMaterial3D.BILLBOARD_ENABLED
				label.font_size = 64
				label.pixel_size = .055
				label.modulate = Color("fff5db")
				label.outline_modulate = Color("304b4b")
				label.no_depth_test = true
				label.visible = false
				add_child(label)
				labels.append(label)
	show_view(initial_view)
	if "--gate-exit-review" in OS.get_cmdline_user_args():
		await get_tree().physics_frame
		await get_tree().physics_frame
		var gate_report := gate_checks()
		print("GATE_EXIT_CHECKS "+JSON.stringify(gate_report))
		var walk_ok := true
		if "--gate-exit-walk" in OS.get_cmdline_user_args():
			begin_surface_walk({"id":"NorthGateExit","start":[46,32,-176],"end":[48,32,-140]})
			while walk_active: await get_tree().physics_frame
			print("GATE_EXIT_WALK "+JSON.stringify(walk_result))
			walk_ok = walk_result.get("ok",false)
		for shot in gate_exit_views():
			show_surface_view(shot)
			await RenderingServer.frame_post_draw
			await RenderingServer.frame_post_draw
			print("GATE_EXIT_CAPTURE "+JSON.stringify(capture(shot[0])))
		get_tree().quit(0 if gate_report.ok and walk_ok else 1)
		return
	if "--surface-review-capture" in OS.get_cmdline_user_args() or "--surface-review-smoke" in OS.get_cmdline_user_args():
		await get_tree().physics_frame
		await get_tree().physics_frame
		var surface_report := surface_checks()
		print("SURFACE_CHECKS "+JSON.stringify(surface_report))
		var city_report := review_checks()
		var dry_report := landfill_checks()
		var wall_report := gate_checks()
		var view_report := surface_view_checks()
		print("SURFACE_CITY_CHECKS "+JSON.stringify(city_report))
		print("SURFACE_DRY_CHECKS "+JSON.stringify(dry_report))
		print("SURFACE_WALL_CHECKS "+JSON.stringify(wall_report))
		print("SURFACE_VIEW_CHECKS "+JSON.stringify(view_report))
		var walks_ok := true
		if "--surface-review-smoke" in OS.get_cmdline_user_args():
			var layout: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://Assets/World/Linvel/City/Masterplan/layout.json"))
			for link in layout.links:
				if link.kind not in ["ramp","stairs"]: continue
				begin_surface_walk(link)
				while walk_active: await get_tree().physics_frame
				print("SURFACE_WALK "+JSON.stringify(walk_result))
				walks_ok = walks_ok and walk_result.get("ok",false)
		for shot in surface_views():
			show_surface_view(shot)
			await RenderingServer.frame_post_draw
			await RenderingServer.frame_post_draw
			print("SURFACE_CAPTURE "+JSON.stringify(capture(shot[0])))
		show_view(1)
		await RenderingServer.frame_post_draw
		await RenderingServer.frame_post_draw
		print("SURFACE_CAPTURE "+JSON.stringify(capture("panorama")))
		get_tree().quit(0 if "--surface-review-capture" in OS.get_cmdline_user_args() or (surface_report.ok and city_report.ok and dry_report.ok and wall_report.ok and view_report.ok and walks_ok) else 1)
		return
	if "--gate-review-smoke" in OS.get_cmdline_user_args():
		await get_tree().physics_frame
		await get_tree().physics_frame
		var route_report := review_checks()
		var dry_report := landfill_checks()
		var gate_report := gate_checks()
		print("GATE_CITY_CHECKS "+JSON.stringify(route_report))
		print("GATE_DRY_CHECKS "+JSON.stringify(dry_report))
		print("GATE_CHECKS "+JSON.stringify(gate_report))
		begin_gate_walk_check()
		while walk_active: await get_tree().physics_frame
		print("GATE_WALK "+JSON.stringify(walk_result))
		for item in [[7,"top-down"],[1,"panorama"],[8,"gate-overview"]]:
			show_view(item[0])
			await RenderingServer.frame_post_draw
			await RenderingServer.frame_post_draw
			print("GATE_CAPTURE "+JSON.stringify(capture(item[1])))
		for i in 2:
			show_gate_view(i)
			await RenderingServer.frame_post_draw
			await RenderingServer.frame_post_draw
			print("GATE_CAPTURE "+JSON.stringify(capture("gate-player" if i==0 else "entry-court")))
		get_tree().quit(0 if route_report.ok and dry_report.ok and gate_report.ok and walk_result.get("ok",false) else 1)
		return
	print("LINVEL_MASTERPLAN_READY | 1 panorama | 2 walk/arrows/RMB | 3 square | 4 harbor | 5 arena | 6 arrival | 7 plan | 8 gate | 9 tree style | L labels")
	if "--tree-style-capture" in OS.get_cmdline_user_args():
		show_view(9)
		await RenderingServer.frame_post_draw
		await RenderingServer.frame_post_draw
		print("ART_TREE_CAPTURE "+JSON.stringify(capture("beech-tree-style")))
		get_tree().quit(0)
		return
	if "--tavern-review-smoke" in OS.get_cmdline_user_args():
		await get_tree().physics_frame
		await get_tree().physics_frame
		begin_tavern_walk_check()
		while walk_active:
			await get_tree().physics_frame
		print("ART_TAVERN_WALK "+JSON.stringify(walk_result))
		show_view(9)
		await RenderingServer.frame_post_draw
		await RenderingServer.frame_post_draw
		print("ART_TREE_CAPTURE "+JSON.stringify(capture("beech-tree-style")))
		get_tree().quit(0 if walk_result.get("ok",false) else 1)
		return
	if "--masterplan-review-smoke" in OS.get_cmdline_user_args():
		await get_tree().physics_frame
		await get_tree().physics_frame
		await RenderingServer.frame_post_draw
		var report := review_checks()
		print("MASTERPLAN_CHECKS "+JSON.stringify(report))
		var dry_land := {"ok":true}
		if city.get_meta("dry_city_landfill_revision",0)>0:
			dry_land = landfill_checks()
			print("LANDFILL_CHECKS "+JSON.stringify(dry_land))
		begin_walk_check()
		while walk_active:
			await get_tree().physics_frame
		print("MASTERPLAN_WALK "+JSON.stringify(walk_result))
		for item in [[1,"panorama"],[7,"top-down"],[3,"central-square"],[4,"grand-harbor"],[5,"arena-terrace"],[6,"arrival-view"],[8,"landward-gate"],[9,"beech-tree-style"],[2,"gameplay-scale"]]:
			show_view(item[0])
			await RenderingServer.frame_post_draw
			await RenderingServer.frame_post_draw
			print("MASTERPLAN_CAPTURE "+JSON.stringify(capture(item[1])))
		get_tree().quit(0 if report.ok and dry_land.ok and walk_result.get("ok",false) else 1)

func show_view(index: int) -> void:
	view = index
	for label in labels:
		label.visible = index==7
	camera.far = 2400
	camera.near = .8
	camera.projection = Camera3D.PROJECTION_PERSPECTIVE
	camera.fov = 47
	if index==1:
		camera.position = Vector3(-275,185,420)
		camera.fov = 45
		camera.look_at(Vector3(0,22,38))
	elif index==2:
		figure.position = Vector3(-36,5.1,117)
		figure.velocity = Vector3.ZERO
		target = Vector3.INF
		follow_figure()
	elif index==3:
		camera.position = Vector3(-83,92,103)
		camera.look_at(Vector3(0,30,-3))
	elif index==4:
		camera.position = Vector3(-246,128,309)
		camera.look_at(Vector3(-42,10,145))
	elif index==5:
		camera.position = Vector3(221,133,9)
		camera.look_at(Vector3(132,40,-107))
	elif index==6:
		camera.position = Vector3(-35,7,137)
		camera.fov = 68
		camera.look_at(Vector3(0,34,-17))
	elif index==7:
		camera.projection = Camera3D.PROJECTION_ORTHOGONAL
		camera.size = 470
		camera.position = Vector3(0,700,-3)
		camera.rotation_degrees = Vector3(-90,0,0)
	elif index==9:
		camera.position = Vector3(-141,22,48)
		camera.fov = 50
		camera.look_at(Vector3(-126,18.8,61))
	else:
		camera.position = Vector3(103,79,-225)
		camera.look_at(Vector3(33,39,-112))

func follow_figure() -> void:
	var p := figure.position-Vector3(0,.9,0)
	camera.position = p+Vector3(0,sin(deg_to_rad(55)),cos(deg_to_rad(55)))*36
	camera.look_at(p)

func _physics_process(delta: float) -> void:
	if view!=2:
		return
	if walk_active:
		step_walk_check(delta)
		follow_figure()
		return
	var direction := Vector3(float(Input.is_physical_key_pressed(KEY_RIGHT))-float(Input.is_physical_key_pressed(KEY_LEFT)),0,float(Input.is_physical_key_pressed(KEY_DOWN))-float(Input.is_physical_key_pressed(KEY_UP)))
	if direction.length_squared()>.01:
		target = Vector3.INF
	elif target.is_finite():
		direction = target-figure.position
		direction.y = 0
		if direction.length()<.4:
			direction = Vector3.ZERO
			target = Vector3.INF
	figure.velocity.x = direction.normalized().x*5
	figure.velocity.z = direction.normalized().z*5
	figure.velocity.y -= 20*delta
	figure.move_and_slide()
	if figure.position.y < -4:
		figure.position = Vector3(-36,5.1,117)
		figure.velocity = Vector3.ZERO
		target = Vector3.INF
	follow_figure()

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.physical_keycode>=KEY_1 and event.physical_keycode<=KEY_9:
			show_view(event.physical_keycode-KEY_0)
		elif event.physical_keycode==KEY_L:
			for label in labels:
				label.visible = not label.visible
	if view==2 and event is InputEventMouseButton and event.pressed and event.button_index==MOUSE_BUTTON_RIGHT:
		var origin := camera.project_ray_origin(event.position)
		var query := PhysicsRayQueryParameters3D.create(origin,origin+camera.project_ray_normal(event.position)*1000,1)
		var hit := get_world_3d().direct_space_state.intersect_ray(query)
		if not hit.is_empty():
			target = hit.position

func capture(label: String) -> Dictionary:
	var output := "linvel-production" if city.get_meta("whole_city_art_revision",0)>0 else "linvel-masterplan"
	if city.get_meta("dry_city_landfill_revision",0)>0:
		output = "linvel-landfill/art01" if city.get_meta("whole_city_art_revision",0)>0 else "linvel-landfill/masterplan"
	if city.get_meta("entry_revision",0)>0:
		output = "linvel-gate"
	if "--surface-review-capture" in OS.get_cmdline_user_args() or city.get_meta("surface_repair_revision",0)>0:
		output = "linvel-surface-repair/after" if city.get_meta("surface_repair_revision",0)>0 else "linvel-surface-repair/before"
	if "--gate-exit-review" in OS.get_cmdline_user_args():
		output = "linvel-gate-exit/after" if "--gate-exit-after" in OS.get_cmdline_user_args() else "linvel-gate-exit/before"
	if not harbor_stage.is_empty(): output = "linvel-harbor-repair/"+harbor_stage+"/runtime"
	var directory := ProjectSettings.globalize_path("res://../.artifacts/"+output).simplify_path()
	DirAccess.make_dir_recursive_absolute(directory)
	var path := directory.path_join(label+".png")
	return {"path":path,"error":get_viewport().get_texture().get_image().save_png(path),"draw_calls":Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME),"primitives":Performance.get_monitor(Performance.RENDER_TOTAL_PRIMITIVES_IN_FRAME)}

func review_checks() -> Dictionary:
	var data: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://Assets/World/Linvel/City/Masterplan/layout.json"))
	var problems: Array = []
	var grounds := 0
	var foundation_checks := 0
	var clearance := 0
	var space := get_world_3d().direct_space_state
	for part in data.districts:
		var node := city.get_node_or_null("CityParts/"+part.id)
		var expected_scene: String = part.scene
		if city.get_meta("whole_city_art_revision",0)>0:
			expected_scene = "res://Scenes/Regions/Linvel/LinvelCity/Parts/"+part.id+".tscn"
		if node==null or node.scene_file_path!=expected_scene:
			problems.append({"district_not_saved_instance":part.id})
	var expected_buildings := 0
	for b in data.buildings:
		var node := city.get_node_or_null("CityParts/"+b.part+"/"+b.id)
		if node==null:
			problems.append({"missing_building":b.id})
		elif b.role!="ship_clearance":
			expected_buildings += 1
			var p := Vector3(b.position[0],b.position[1],b.position[2])
			if city.get_meta("entry_revision",0)>0:
				p = node.global_position
			var ray := PhysicsRayQueryParameters3D.create(p+Vector3(0,1.5,0),p-Vector3(0,3,0),1)
			var hit := space.intersect_ray(ray)
			if hit.is_empty() or absf(hit.position.y-p.y)>.45:
				problems.append({"foundation":b.id,"point":p,"actual":hit.get("position")})
			foundation_checks += 1
	if expected_buildings!=87 or terrain.data.get_region_count()!=16:
		problems.append({"counts":[expected_buildings,terrain.data.get_region_count()]})
	if city.get_meta("mainland_extended",false):
		for p in [Vector3(-250,0,-60),Vector3(260,0,-55),Vector3(-120,0,-285),Vector3(120,0,-285)]:
			var h: float = terrain.data.get_height(p)
			if not is_finite(h) or h<2:
				problems.append({"missing_mainland":p,"height":h})
		for p in [Vector3(-94,0,183),Vector3(-39,0,183),Vector3(21,0,180)]:
			var h: float = terrain.data.get_height(p)
			if not is_finite(h) or h > -4.5:
				problems.append({"insufficient_harbor_draft":p,"seabed_height":h})
	for route in data.streets+data.links:
		if route.get("kind","")=="breakwater":
			continue
		var points: Array = route.get("points",[route.get("start"),route.get("end")])
		for i in range(points.size()-1):
			var a := Vector3(points[i][0],points[i][1],points[i][2])
			var b := Vector3(points[i+1][0],points[i+1][1],points[i+1][2])
			var n := maxi(1,ceili(a.distance_to(b)/2))
			for j in range(n+1):
				var p := a.lerp(b,float(j)/n)
				var ray := PhysicsRayQueryParameters3D.create(p+Vector3(0,2,0),p-Vector3(0,3,0),1)
				var hit := space.intersect_ray(ray)
				grounds += 1
				if hit.is_empty() or absf(hit.position.y-p.y)>.45:
					problems.append({"ground":route.id,"point":p,"actual":hit.get("position")})
				var query := PhysicsShapeQueryParameters3D.new()
				query.shape = figure.get_node("Collision").shape
				query.transform = Transform3D(Basis.IDENTITY,p+Vector3(0,1.02,0))
				query.collision_mask = 5
				if not space.intersect_shape(query,1).is_empty():
					problems.append({"blocked":route.id,"point":p})
				clearance += 1
	return {"ok":problems.is_empty(),"districts":data.districts.size(),"buildings":expected_buildings,"terrain_regions":terrain.data.get_region_count(),"ground_samples":grounds,"clearance_samples":clearance,"foundation_checks":foundation_checks,"problems":problems,"human_playtest_approved":false,"server_navigation_exported":false}

func landfill_checks() -> Dictionary:
	# Independent world-space checks at both former channels and district seams.
	# A physical ray must hit real ground above the sea; hiding water is insufficient.
	var points: Array[Vector3] = [Vector3(190,0,-100),Vector3(182,0,-30),Vector3(163,0,48),Vector3(160,0,80),Vector3(-188,0,-45),Vector3(-180,0,20),Vector3(-164,0,60),Vector3(72,0,-54),Vector3(90,0,-51),Vector3(-54,0,-50),Vector3(-132,0,48)]
	var problems: Array = []
	var samples: Array = []
	var space := get_world_3d().direct_space_state
	for p in points:
		var query := PhysicsRayQueryParameters3D.create(p+Vector3(0,100,0),p-Vector3(0,2,0),1)
		var hit := space.intersect_ray(query)
		if hit.is_empty() or hit.position.y<=1:
			problems.append({"dry_ground_missing":p,"actual":hit.get("position")})
		samples.append({"point":p,"ground":hit.get("position")})
	for p in [Vector3(-94,0,183),Vector3(-39,0,183),Vector3(21,0,180)]:
		var h: float = terrain.data.get_height(p)
		if not is_finite(h) or h > -4.5:
			problems.append({"harbor_berth_filled":p,"height":h})
	return {"ok":problems.is_empty(),"physical_ground_samples":samples,"dry_points":points.size(),"deep_berths":3,"problems":problems}

func begin_walk_check() -> Dictionary:
	show_view(2)
	walk_route = "Harbor > lower town > main square > upper town > arena"
	walk_points = [Vector3(-36,4,110),Vector3(-36,12,81),Vector3(-36,12,78),Vector3(-24,12,78),Vector3(0,12,78),Vector3(0,23,42),Vector3(0,23,26),Vector3(12,23,22),Vector3(12,23,-2),Vector3(23,23,-11),Vector3(49,23,-11),Vector3(49,23,-39),Vector3(49,32,-85),Vector3(49,32,-104),Vector3(75,32,-108),Vector3(100,36,-108),Vector3(113,36,-109),Vector3(133,36,-109)]
	walk_active = true
	walk_index = 0
	walk_time = 0
	walk_stuck = 0
	walk_result = {"ok":false,"status":"running"}
	return {"started":true,"waypoints":walk_points.size(),"capsule_height_m":1.8}

func show_gate_view(index := 0) -> void:
	view = 8
	for label in labels: label.visible = false
	camera.projection = Camera3D.PROJECTION_PERSPECTIVE
	camera.near = .15
	camera.fov = 67
	if index==0:
		camera.position = Vector3(50,33.8,-181)
		camera.look_at(Vector3(0,49,-32))
	else:
		camera.position = Vector3(73,56,-179)
		camera.fov = 55
		camera.look_at(Vector3(36,35,-130))

func gate_checks() -> Dictionary:
	var problems: Array = []
	var space := get_world_3d().direct_space_state
	var walls := city.get_node("CityParts/OuterDefenses")
	var probes: Array = walls.get_meta("wall_probe_points",[])
	for item in probes:
		var p := Vector3(item[0],item[1],item[2])
		var normal := Vector3(item[3],0,item[4])
		var hit := space.intersect_ray(PhysicsRayQueryParameters3D.create(p-normal*3,p+normal*3,4))
		if hit.is_empty(): problems.append({"wall_collision_missing":p})
	var clearance := 0
	var rail_bounds: Array = []
	collect_gate_rail_bounds(city.get_node("CityParts"),rail_bounds)
	var visual_blockers: Dictionary = {}
	for x in [41.0,46.0,51.0]:
		for z in range(-169,-144,2):
			var p := Vector3(x,32,z)
			var ground := space.intersect_ray(PhysicsRayQueryParameters3D.create(p+Vector3(0,1,0),p-Vector3(0,1,0),1))
			var shape := PhysicsShapeQueryParameters3D.new()
			shape.shape = figure.get_node("Collision").shape
			shape.collision_mask = 4
			shape.transform = Transform3D(Basis.IDENTITY,p+Vector3(0,1.02,0))
			if ground.is_empty() or not space.intersect_shape(shape,1).is_empty():
				problems.append({"gate_passage_blocked_or_no_ground":p})
			var passage := AABB(p+Vector3(-.35,.2,-.35),Vector3(.7,1.55,.7))
			for rail in rail_bounds:
				if passage.intersects(rail.bounds): visual_blockers[rail.path] = true
			clearance += 1
	for path in visual_blockers: problems.append({"gate_visual_rail_blocker":path})
	for p in [Vector3(15,32,-147),Vector3(-21,32,-190),Vector3(8,32,-202),Vector3(72,32,-186)]:
		var hit := space.intersect_ray(PhysicsRayQueryParameters3D.create(p+Vector3(0,1,0),p-Vector3(0,1,0),1))
		if hit.is_empty(): problems.append({"entry_foundation_missing":p})
	return {"ok":problems.is_empty(),"wall_modules":probes.size(),"gate_clearance_samples":clearance,"clear_gate_width_m":12,"visual_rail_blockers":visual_blockers.size(),"problems":problems}

func collect_gate_rail_bounds(node: Node, bounds: Array) -> void:
	# Inspect actual drawn batches as well as ordinary meshes: source nodes may
	# be hidden and decorative rails may have no physics bodies.
	if node is MultiMeshInstance3D and node.is_visible_in_tree() and node.multimesh!=null:
		var multi: MultiMesh = node.multimesh
		var label := str(node.name).to_lower()
		if "balustrade" in label or "fence" in label:
			var count := multi.instance_count if multi.visible_instance_count<0 else multi.visible_instance_count
			for i in count:
				var transform: Transform3D = node.global_transform*multi.get_instance_transform(i)
				bounds.append({"path":str(node.get_path())+"#%d"%i,"bounds":transform*multi.mesh.get_aabb()})
	elif node is MeshInstance3D and node.is_visible_in_tree() and node.mesh!=null:
		var label: String = node.mesh.resource_name.to_lower()
		if "balustrade" in label or "fence" in label:
			bounds.append({"path":str(node.get_path()),"bounds":node.global_transform*node.mesh.get_aabb()})
	for child in node.get_children(): collect_gate_rail_bounds(child,bounds)

func gate_exit_views() -> Array:
	return [["inside-looking-out",Vector3(46,33.8,-141),Vector3(46,34,-176)],
	["outside-looking-in",Vector3(46,33.8,-176),Vector3(47,34,-139)]]

func harbor_views() -> Array:
	return [["court-edge",Vector3(71,37,-153),Vector3(65,31.8,-143)],
	["road-east-shoulder",Vector3(68,37,-172),Vector3(46,32,-185)],
	["road-west-shoulder",Vector3(18,39,-190),Vector3(47,32,-185)],
	["pier-1-entry",Vector3(-105,12,122),Vector3(-105,4,146)],
	["pier-2-entry",Vector3(-50,12,128),Vector3(-50,4,151)],
	["pier-3-entry",Vector3(10,12,130),Vector3(10,4,151)],
	["pier-4-entry",Vector3(65,12,130),Vector3(65,4,151)],
	["lighthouse-entry",Vector3(-145,11,107),Vector3(-171,5,140)],
	["quay-face-west",Vector3(-135,4.5,155),Vector3(-125,3,134)],
	["quay-face-east",Vector3(-20,4.5,164),Vector3(-15,3,140)],
	["cargo-support",Vector3(-142,13,151),Vector3(-142,4,126)]]

func harbor_checks() -> Dictionary:
	var layout: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://Assets/World/Linvel/City/Masterplan/layout.json"))
	var rails: Array = []
	collect_gate_rail_bounds(city.get_node("CityParts"),rails)
	var problems: Array = []
	var blockers: Dictionary = {}
	var samples := 0
	var stone_overlap_samples := 0
	var space := get_world_3d().direct_space_state
	for link in layout.links:
		if link.kind not in ["pier","bridge"]: continue
		var a := Vector3(link.start[0],link.start[1],link.start[2])
		var b := Vector3(link.end[0],link.end[1],link.end[2])
		for j in 21:
			var p := a.lerp(b,j/20.0)
			var volume := AABB(p+Vector3(-.4,.15,-.4),Vector3(.8,1.6,.8))
			for rail in rails:
				if volume.intersects(rail.bounds): blockers[rail.path] = true
			var hit := space.intersect_ray(PhysicsRayQueryParameters3D.create(p+Vector3.UP*.5,p-Vector3.UP,1))
			if hit.is_empty(): problems.append({"missing_landing_ground":str(p),"link":link.id})
			samples += 1
		if link.kind=="pier":
			for j in 12:
				for offset in [-3.0,0.0,3.0]:
					var p := Vector2(a.x+offset,a.z+.25+j)
					for path in ["HarborPromenade","LowerQuay/FlatTerrace"]:
						var floor_node := city.get_node("CityParts/HarborQuarter/"+path) as MeshInstance3D
						if is_finite(visual_mesh_height(floor_node,p)):
							problems.append({"stone_intersects_timber":str(p),"floor":path})
					stone_overlap_samples += 1
	for path in blockers: problems.append({"rail_blocks_harbor_route":path})
	for group in city.get_node("CityParts/HarborQuarter").get_children():
		if not str(group.name).begins_with("DockCargo_"): continue
		for cargo in group.get_children():
			var p: Vector3 = cargo.global_position
			var hit := space.intersect_ray(PhysicsRayQueryParameters3D.create(p+Vector3.UP*.3,p-Vector3.UP*.3,1))
			if hit.is_empty(): problems.append({"unsupported_cargo":str(cargo.get_path())})
	return {"ok":problems.is_empty(),"link_ground_samples":samples,"stone_overlap_samples":stone_overlap_samples,"visual_rail_blockers":blockers.size(),"problems":problems}

func begin_harbor_review(stage: String) -> Dictionary:
	harbor_stage = stage
	harbor_review_report = {"stage":stage,"done":false}
	run_harbor_review.call_deferred(stage)
	return harbor_review_report

func run_harbor_review(stage: String) -> void:
	await get_tree().physics_frame
	var report := harbor_checks()
	print("HARBOR_CHECKS "+JSON.stringify(report))
	var walks: Array = []
	if stage=="after":
		var layout: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://Assets/World/Linvel/City/Masterplan/layout.json"))
		for link in layout.links:
			if link.kind not in ["pier","bridge"]: continue
			begin_surface_walk(link)
			while walk_active: await get_tree().physics_frame
			walks.append(walk_result.duplicate())
			print("HARBOR_WALK "+JSON.stringify(walk_result))
	figure.visible = false
	var shots: Array = []
	for shot in harbor_views():
		show_surface_view(shot)
		await get_tree().create_timer(.4).timeout
		await RenderingServer.frame_post_draw
		shots.append(capture(shot[0]))
	harbor_review_report = {"stage":stage,"done":true,"checks":report,"walks":walks,"shots":shots}
	var file := FileAccess.open("res://../.artifacts/linvel-harbor-repair/"+stage+"/runtime-report.json",FileAccess.WRITE)
	file.store_string(JSON.stringify(harbor_review_report,"\t"))
	print("HARBOR_REVIEW_DONE "+JSON.stringify(harbor_review_report))

func begin_gate_walk_check() -> Dictionary:
	show_view(2)
	figure.position = Vector3(39,32.9,-219)
	figure.velocity = Vector3.ZERO
	walk_route = "Foregate orchards > arch > entry court > main street > guild square"
	walk_points = [Vector3(39,32,-219),Vector3(47,32,-190),Vector3(46,32,-170),Vector3(46,32,-158),Vector3(48,32,-145),Vector3(48,32,-125),Vector3(49,32,-104),Vector3(49,32,-83),Vector3(49,23,-39),Vector3(49,23,-11),Vector3(23,23,-11)]
	walk_active = true
	walk_index = 0
	walk_time = 0
	walk_stuck = 0
	walk_result = {"ok":false,"status":"running"}
	return {"started":true,"waypoints":walk_points.size()}

func surface_views() -> Array:
	return [["wall-height",Vector3(-202,19.8,18),Vector3(-181,23,18)],
	["upper-ascent-bottom",Vector3(49,24.8,-35),Vector3(49,29,-67)],
	["upper-ascent-top",Vector3(49,33.8,-88),Vector3(49,27,-52)],
	["arena-ascent-bottom",Vector3(70,33.8,-108),Vector3(97,37,-108)],
	["arena-ascent-top",Vector3(104,37.8,-108),Vector3(78,34,-108)],
	["inspection-yard",Vector3(84,35,-134),Vector3(77,32,-149)],
	["tower-door",Vector3(36,34.5,-148),Vector3(36,34.5,-155)],
	["market-garden",Vector3(31,35,-141),Vector3(23,32,-151)],
	["civic-stair",Vector3(0,13.8,82),Vector3(0,20,54)],
	["west-homes-stair",Vector3(-126,19.8,-44),Vector3(-126,26,-70)],
	["cart-ascent",Vector3(-99,13.8,82),Vector3(-99,17,48)],
	["east-civic-ascent",Vector3(89,21.8,0),Vector3(63,24,0)],
	["arena-retaining-front",Vector3(132,21.8,-27),Vector3(132,31,-62)],
	["civic-west-wall",Vector3(-72,19.8,-15),Vector3(-58,25,-10)],
	["civic-south-wall",Vector3(23,13.8,70),Vector3(23,21,43)],
	["west-residential-wall",Vector3(-136,19.8,-39),Vector3(-140,26,-76)],
	["upper-west-wall",Vector3(-72,30.8,-85),Vector3(-44,32,-81)],
	["sanctuary-south-wall",Vector3(50,13.8,70),Vector3(68,20,70)],
	["harbor-terrace-wall",Vector3(42,5.8,137),Vector3(45,12,96)],
	["civic-masonry-corner",Vector3(-60,13.8,54),Vector3(-48,21,40)]]

func surface_view_checks() -> Dictionary:
	# A capture under a terrace or inside a building cannot verify a player view.
	var space := get_world_3d().direct_space_state
	var problems: Array = []
	var sphere := SphereShape3D.new()
	sphere.radius = .15
	for shot in surface_views():
		var p: Vector3 = shot[1]
		var floor_hit := space.intersect_ray(PhysicsRayQueryParameters3D.create(p,p-Vector3.UP*5,1))
		var query := PhysicsShapeQueryParameters3D.new()
		query.shape = sphere
		query.transform.origin = p
		query.collision_mask = 5
		if floor_hit.is_empty() or not space.intersect_shape(query,1).is_empty():
			problems.append({"camera":shot[0],"position":str(p),"reason":"No accessible floor or camera inside solid geometry"})
	# The stair cut also needs its furnishings moved onto supported court pieces.
	for i in 3:
		var rack := city.get_node("CityParts/TrainingQuarter/DornWeaponRack_%d"%i) as Node3D
		var p := rack.global_position
		var floor_hit := space.intersect_ray(PhysicsRayQueryParameters3D.create(p+Vector3.UP*.5,p-Vector3.UP*20,1))
		if floor_hit.is_empty() or absf(p.y-float(floor_hit.position.y))>.35:
			problems.append({"prop":str(rack.name),"reason":"Weapon rack has no court support"})
	return {"ok":problems.is_empty(),"camera_positions":surface_views().size(),"court_props":3,"problems":problems}

func show_surface_view(shot: Array) -> void:
	view = 8
	for label in labels: label.visible = false
	camera.projection = Camera3D.PROJECTION_PERSPECTIVE
	camera.near = .12
	camera.fov = 64
	camera.position = shot[1]
	camera.look_at(shot[2])

func surface_checks() -> Dictionary:
	var data: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://Assets/World/Linvel/City/Masterplan/layout.json"))
	var space := get_world_3d().direct_space_state
	var problems: Array = []
	var count := 0
	for link in data.links:
		if link.kind not in ["ramp","stairs"]: continue
		var a := Vector3(link.start[0],link.start[1],link.start[2])
		var b := Vector3(link.end[0],link.end[1],link.end[2])
		var side := Vector3(b.z-a.z,0,a.x-b.x).normalized()
		for offset in [-.4,0,.4]:
			for i in range(1,20):
				var p: Vector3 = a.lerp(b,i/20.0)+side*float(link.width)*float(offset)
				var shape := PhysicsShapeQueryParameters3D.new()
				shape.shape = figure.get_node("Collision").shape
				shape.collision_mask = 5
				shape.transform = Transform3D(Basis.IDENTITY,p+Vector3(0,1.02,0))
				if not space.intersect_shape(shape,1).is_empty(): problems.append({"obstructed_capsule":link.id,"point":p})
				var h: float = terrain.data.get_height(p)
				if is_finite(h) and h>p.y-.1: problems.append({"terrain_through_stone":link.id,"point":p,"height":h})
				if link.id=="ArenaEastStair":
					var court := city.get_node("CityParts/TrainingQuarter/DornCourt") as MeshInstance3D
					var overhead := visual_mesh_height(court,Vector2(p.x,p.z))
					if is_finite(overhead) and overhead>p.y+.45: problems.append({"visual_court_ceiling_over_stair":p,"ceiling":overhead})
				count += 1
	return {"ok":problems.is_empty(),"link_capsule_and_native_height_samples":count,"problems":problems}

func visual_mesh_height(node: MeshInstance3D, p: Vector2) -> float:
	var local := node.global_transform.affine_inverse()*Vector3(p.x,0,p.y)
	for surface in node.mesh.get_surface_count():
		var arrays := node.mesh.surface_get_arrays(surface)
		var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
		var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
		for i in range(0,indices.size(),3):
			var va := vertices[indices[i]]
			var vb := vertices[indices[i+1]]
			var vc := vertices[indices[i+2]]
			var a := Vector2(va.x,va.z)
			var b := Vector2(vb.x,vb.z)
			var c := Vector2(vc.x,vc.z)
			var q := Vector2(local.x,local.z)
			var determinant := (b-a).cross(c-a)
			if absf(determinant)<.00001: continue
			var u := (q-a).cross(c-a)/determinant
			var v := (b-a).cross(q-a)/determinant
			if u>=0 and v>=0 and u+v<=1:
				return (node.global_transform*Vector3(local.x,va.y+u*(vb.y-va.y)+v*(vc.y-va.y),local.z)).y
	return NAN

func begin_surface_walk(link: Dictionary) -> void:
	show_view(2)
	var a := Vector3(link.start[0],link.start[1],link.start[2])
	var b := Vector3(link.end[0],link.end[1],link.end[2])
	figure.position = a+Vector3(0,.9,0)
	figure.velocity = Vector3.ZERO
	walk_route = link.id+" both directions"
	walk_points = [a,a.lerp(b,.25),a.lerp(b,.5),a.lerp(b,.75),b,a.lerp(b,.75),a.lerp(b,.5),a.lerp(b,.25),a]
	walk_active = true
	walk_index = 0
	walk_time = 0
	walk_stuck = 0
	walk_result = {"ok":false,"status":"running"}

func begin_tavern_walk_check() -> Dictionary:
	show_view(2)
	figure.position = Vector3(-137,12.9,87)
	figure.velocity = Vector3.ZERO
	walk_route = "Tavern courtyard > entry steps > open doorway > ground floor > courtyard"
	walk_points = [Vector3(-137,12,83.6),Vector3(-137,12.565,82),Vector3(-137,12.542,79),Vector3(-137,12.542,75),Vector3(-137,12.542,79),Vector3(-137,12.565,82),Vector3(-137,12,85)]
	walk_index = 0
	walk_time = 0
	walk_stuck = 0
	walk_active = true
	walk_result = {"ok":false,"status":"running"}
	return {"started":true,"waypoints":walk_points.size()}

func step_walk_check(delta: float) -> void:
	walk_time += delta
	var goal := walk_points[walk_index]
	var direction := goal-figure.position
	direction.y = 0
	if direction.length()<.18 and absf(figure.position.y-.9-goal.y)<.22:
		walk_index += 1
		walk_stuck = 0
		if walk_index>=walk_points.size():
			walk_active = false
			figure.velocity = Vector3.ZERO
			walk_result = {"ok":true,"waypoints":walk_index,"elapsed_seconds":walk_time,"capsule_height_m":1.8,"route":walk_route}
			return
		direction = walk_points[walk_index]-figure.position
		direction.y = 0
	var before := figure.position
	var speed := minf(12,direction.length()/delta)
	figure.velocity.x = direction.normalized().x*speed
	figure.velocity.z = direction.normalized().z*speed
	figure.velocity.y -= 20*delta
	figure.move_and_slide()
	var moved := figure.position-before
	moved.y = 0
	walk_stuck = walk_stuck+delta if moved.length()<.02 else 0.0
	if walk_stuck>3 or walk_time>55 or figure.position.y < -3:
		walk_active = false
		walk_result = {"ok":false,"waypoint":walk_index,"goal":goal,"position":figure.position,"elapsed_seconds":walk_time,"stuck_seconds":walk_stuck}
