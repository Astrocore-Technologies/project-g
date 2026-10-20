extends Node3D
## Autonomous environment review. Controls here do not change PC gameplay input.
@export_range(1,9,1) var initial_view := 1
@onready var city: Node3D = $LinvelCity
@onready var camera: Camera3D = $PreviewCamera
@onready var figure: CharacterBody3D = $ScaleFigure
@onready var terrain: Terrain3D = $LinvelCity/World/Terrain3D
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

func _ready() -> void:
	camera.make_current()
	terrain.set_camera(camera)
	for part in city.get_node("World/CityParts").get_children():
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
		begin_walk_check()
		while walk_active:
			await get_tree().physics_frame
		print("MASTERPLAN_WALK "+JSON.stringify(walk_result))
		for item in [[1,"panorama"],[7,"top-down"],[3,"central-square"],[4,"grand-harbor"],[5,"arena-terrace"],[6,"arrival-view"],[8,"landward-gate"],[9,"beech-tree-style"],[2,"gameplay-scale"]]:
			show_view(item[0])
			await RenderingServer.frame_post_draw
			await RenderingServer.frame_post_draw
			print("MASTERPLAN_CAPTURE "+JSON.stringify(capture(item[1])))
		get_tree().quit(0 if report.ok and walk_result.get("ok",false) else 1)

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
		var node := city.get_node_or_null("World/CityParts/"+part.id)
		var expected_scene: String = part.scene
		if city.get_meta("whole_city_art_revision",0)>0:
			expected_scene = "res://Scenes/Regions/Linvel/Art01/Parts/"+part.id+".tscn"
		if node==null or node.scene_file_path!=expected_scene:
			problems.append({"district_not_saved_instance":part.id})
	var expected_buildings := 0
	for b in data.buildings:
		var node := city.get_node_or_null("World/CityParts/"+b.part+"/"+b.id)
		if node==null:
			problems.append({"missing_building":b.id})
		elif b.role!="ship_clearance":
			expected_buildings += 1
			var p := Vector3(b.position[0],b.position[1],b.position[2])
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
				query.collision_mask = 4
				if not space.intersect_shape(query,1).is_empty():
					problems.append({"blocked":route.id,"point":p})
				clearance += 1
	return {"ok":problems.is_empty(),"districts":data.districts.size(),"buildings":expected_buildings,"terrain_regions":terrain.data.get_region_count(),"ground_samples":grounds,"clearance_samples":clearance,"foundation_checks":foundation_checks,"problems":problems,"human_playtest_approved":false,"server_navigation_exported":false}

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
