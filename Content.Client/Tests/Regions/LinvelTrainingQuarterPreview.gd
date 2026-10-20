extends "res://Tests/Regions/LinvelCityCenterPreview.gd"
## Review only: uses the same editable city, without network or persistent state.

func _ready() -> void:
	camera.make_current()
	terrain.set_camera(camera)
	show_view(1)
	print("LINVEL_TRAINING_PREVIEW_READY · 1 arena · 2 walk · 3 Dorn yard · 4 whole city · 5 stand scale")
	if "--training-review-smoke" in OS.get_cmdline_user_args():
		await get_tree().physics_frame
		await get_tree().physics_frame
		await RenderingServer.frame_post_draw
		var checks := training_checks()
		var center_checks := review_physics()
		var paint := review_walkway()
		print("TRAINING_CHECKS " + JSON.stringify(checks))
		print("CENTER_REGRESSION " + JSON.stringify(center_checks))
		print("CENTER_PAINT " + JSON.stringify(paint))
		print("TRAINING_CAPTURE " + JSON.stringify(capture_training("arena-overview")))
		show_view(5)
		await RenderingServer.frame_post_draw
		await RenderingServer.frame_post_draw
		print("STAND_CAPTURE " + JSON.stringify(capture_training("stand-large-scale")))
		show_view(3)
		await RenderingServer.frame_post_draw
		await RenderingServer.frame_post_draw
		print("DORN_CAPTURE " + JSON.stringify(capture_training("dorn-yard")))
		show_view(4)
		await RenderingServer.frame_post_draw
		await RenderingServer.frame_post_draw
		print("CITY_CAPTURE " + JSON.stringify(capture_training("city-with-training")))
		get_tree().quit(0 if not checks.values().has(false) and not center_checks.values().has(false) and paint.ok else 1)

func show_view(index: int) -> void:
	view = index
	camera.far = 450
	if index == 1:
		camera.projection = Camera3D.PROJECTION_ORTHOGONAL
		camera.size = 48
		camera.position = Vector3(33,72,28)
		camera.look_at(Vector3(77,10,-23))
	elif index == 2:
		camera.projection = Camera3D.PROJECTION_PERSPECTIVE
		camera.fov = 50
		follow_figure()
	elif index == 3:
		camera.projection = Camera3D.PROJECTION_ORTHOGONAL
		camera.size = 32
		camera.position = Vector3(44,49,48)
		camera.look_at(Vector3(76,11,13))
	elif index == 4:
		camera.projection = Camera3D.PROJECTION_ORTHOGONAL
		camera.size = 102
		camera.position = Vector3(-27,130,101)
		camera.look_at(Vector3(36,11,-6))
	else:
		camera.projection = Camera3D.PROJECTION_ORTHOGONAL
		camera.size = 15.5
		camera.position = Vector3(61,26,-28)
		camera.look_at(Vector3(77,12,-46.5))
		figure.position = Vector3(69,11,-42.5)
		figure.velocity = Vector3.ZERO

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.physical_keycode >= KEY_1 and event.physical_keycode <= KEY_5:
			show_view(event.physical_keycode - KEY_0)

func capture_training(label: String) -> Dictionary:
	var directory := ProjectSettings.globalize_path("res://").path_join("../.artifacts/linvel-training").simplify_path()
	DirAccess.make_dir_recursive_absolute(directory)
	var path := directory.path_join(label + ".png")
	var error := get_viewport().get_texture().get_image().save_png(path)
	return {"path":path,"error":error,"draw_calls":Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME),"primitives":Performance.get_monitor(Performance.RENDER_TOTAL_PRIMITIVES_IN_FRAME)}

func training_checks() -> Dictionary:
	var stand := city.get_node_or_null("World/CityParts/TrainingQuarter/TrainingArena/CoveredStand") as Node3D
	var bounds := AABB()
	var first := true
	if stand:
		for mesh in stand.find_children("*","MeshInstance3D",true,false):
			var local_bounds: AABB = (stand.global_transform.affine_inverse() * mesh.global_transform) * mesh.get_aabb()
			bounds = local_bounds if first else bounds.merge(local_bounds)
			first = false
	var stand_center := stand.global_position if stand else Vector3(77,10,-46.5)
	return {
		"covered_stand_present":city.has_node("World/CityParts/TrainingQuarter/TrainingArena/CoveredStand"),
		"stand_width_14m":is_equal_approx(bounds.size.x,14.0),
		"stand_depth_7m":is_equal_approx(bounds.size.z,7.0),
		"arena_ground":figure.test_move(Transform3D(Basis.IDENTITY,Vector3(77,11,-22)),Vector3(0,-2,0)),
		"yard_ground":figure.test_move(Transform3D(Basis.IDENTITY,Vector3(76,11,16)),Vector3(0,-2,0)),
		"connection_clear":not figure.test_move(Transform3D(Basis.IDENTITY,Vector3(39,10.95,-4)),Vector3(20,0,0)),
		"west_entry_clear":not figure.test_move(Transform3D(Basis.IDENTITY,Vector3(50,10.95,-22)),Vector3(12,0,0)),
		"south_entry_clear":not figure.test_move(Transform3D(Basis.IDENTITY,Vector3(77,10.95,0)),Vector3(0,0,-11)),
		"oval_fence_blocks":figure.test_move(Transform3D(Basis.IDENTITY,Vector3(97,10.95,-22)),Vector3(5,0,0)),
		"dummy_blocks":figure.test_move(Transform3D(Basis.IDENTITY,Vector3(75,10.95,7)),Vector3(0,0,5)),
		"central_aisle_clear":not figure.test_move(Transform3D(Basis.IDENTITY,stand_center+Vector3(0,1.95,0)),Vector3(0,0,-0.4)),
		"stand_aisle_ground":figure.test_move(Transform3D(Basis.IDENTITY,stand_center+Vector3(0,3,0)),Vector3(0,-3,0)),
		"stand_front_passage":not figure.test_move(Transform3D(Basis.IDENTITY,Vector3(69,10.95,-40.5)),Vector3(16,0,0)),
		"arena_sand":terrain.data.get_control_base_id(Vector3(77,0,-22))==3,
		"yard_sand":terrain.data.get_control_base_id(Vector3(76,0,16))==3,
		"paved_connection":terrain.data.get_control_base_id(Vector3(45,0,-4))==1,
		"grass_border":terrain.data.get_control_base_id(Vector3(108,0,-18))==0
	}
