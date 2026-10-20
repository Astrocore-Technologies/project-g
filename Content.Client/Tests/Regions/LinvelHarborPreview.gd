extends "res://Tests/Regions/LinvelTrainingQuarterPreview.gd"
## Standalone environment review; no network, persistence or swimming gameplay.

func _ready() -> void:
	camera.make_current()
	terrain.set_camera(camera)
	show_view(1)
	print("LINVEL_HARBOR_PREVIEW_READY · 1 harbor · 2 walk · 3 quay access · 4 whole city · 5 piers · 6 lighthouse · 7/8 pier joints")
	if "--harbor-review-smoke" in OS.get_cmdline_user_args():
		await get_tree().physics_frame
		await get_tree().physics_frame
		await RenderingServer.frame_post_draw
		var checks := harbor_checks()
		var center_checks := review_physics()
		var training := training_checks()
		var paint := review_walkway()
		print("HARBOR_CHECKS " + JSON.stringify(checks))
		print("CENTER_REGRESSION " + JSON.stringify(center_checks))
		print("TRAINING_REGRESSION " + JSON.stringify(training))
		print("CENTER_PAINT " + JSON.stringify(paint))
		print("HARBOR_CAPTURE " + JSON.stringify(capture_harbor("harbor-overview")))
		for entry in [[3,"quay-access"],[5,"fishing-piers"],[6,"lighthouse"],[7,"west-pier-joint"],[8,"east-pier-joint"],[4,"city-with-harbor"]]:
			show_view(entry[0])
			await RenderingServer.frame_post_draw
			await RenderingServer.frame_post_draw
			print("HARBOR_CAPTURE " + JSON.stringify(capture_harbor(entry[1])))
		get_tree().quit(0 if not checks.values().has(false) and not center_checks.values().has(false) and not training.values().has(false) and paint.ok else 1)

func show_view(index: int) -> void:
	view = index
	camera.far = 500
	if index == 1:
		camera.projection = Camera3D.PROJECTION_ORTHOGONAL
		camera.size = 90
		camera.position = Vector3(-138,108,155)
		camera.look_at(Vector3(-65,13,75))
	elif index == 2:
		camera.projection = Camera3D.PROJECTION_PERSPECTIVE
		camera.fov = 50
		follow_figure()
	elif index == 3:
		camera.projection = Camera3D.PROJECTION_ORTHOGONAL
		camera.size = 32
		camera.position = Vector3(-69,40,70)
		camera.look_at(Vector3(-36,8,45))
	elif index == 4:
		camera.projection = Camera3D.PROJECTION_ORTHOGONAL
		camera.size = 160
		camera.position = Vector3(-140,190,190)
		camera.look_at(Vector3(0,12,33))
	elif index == 5:
		camera.projection = Camera3D.PROJECTION_ORTHOGONAL
		camera.size = 57
		camera.position = Vector3(-94,50,143)
		camera.look_at(Vector3(-45,7,96))
	elif index == 6:
		camera.projection = Camera3D.PROJECTION_ORTHOGONAL
		camera.size = 46
		camera.position = Vector3(-134,43,99)
		camera.look_at(Vector3(-103,21,64))
	else:
		var center := Vector3(-62,7,64.2) if index == 7 else Vector3(-28,7,67)
		camera.projection = Camera3D.PROJECTION_ORTHOGONAL
		camera.size = 10
		camera.position = center+Vector3(-9,12,14)
		camera.look_at(center)

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.physical_keycode>=KEY_1 and event.physical_keycode<=KEY_8:
			show_view(event.physical_keycode-KEY_0)

func _physics_process(delta: float) -> void:
	# Review-only reset if the scale figure leaves land/decks; no swimming rule.
	if figure.position.y < 5.7:
		figure.position = Vector3(-38,8.2,58)
		figure.velocity = Vector3.ZERO
	super._physics_process(delta)

func capture_harbor(label: String) -> Dictionary:
	var directory := ProjectSettings.globalize_path("res://").path_join("../.artifacts/linvel-harbor").simplify_path()
	DirAccess.make_dir_recursive_absolute(directory)
	var path := directory.path_join(label + ".png")
	var error := get_viewport().get_texture().get_image().save_png(path)
	return {"path":path,"error":error,"draw_calls":Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME),"primitives":Performance.get_monitor(Performance.RENDER_TOTAL_PRIMITIVES_IN_FRAME)}

func harbor_checks() -> Dictionary:
	var plaza_clear := true
	for child in city.get_node("World/CityParts/HarborQuarter/HarborDetails").get_children():
		if child is Node3D and str(child.get_meta("linvel_asset_id","")).begins_with("coastal_boulder"):
			plaza_clear = plaza_clear and Vector2(child.position.x+104,child.position.z-64).length()>13.0
	return {
		"lighthouse_rocks_clear_of_plaza":plaza_clear,
		"rigid_upper_floor_present":city.has_node("World/CityParts/HarborQuarter/RigidPaving/UpperWest"),
		"lighthouse_scaled_3x":city.get_node("World/CityParts/HarborQuarter/WaterfrontBuildings/HarborSignalTower").scale.is_equal_approx(Vector3.ONE*3.0),
		"lighthouse_plaza_ground":figure.test_move(Transform3D(Basis.IDENTITY,Vector3(-113,8.2,64)),Vector3(0,-2,0)),
		"lighthouse_blocks":figure.test_move(Transform3D(Basis.IDENTITY,Vector3(-104,8,55)),Vector3(0,0,10)),
		"lighthouse_circulation_clear":not figure.test_move(Transform3D(Basis.IDENTITY,Vector3(-114,7.95,60)),Vector3(0,0,8)),
		"harbor_saved_instance":city.has_node("World/CityParts/HarborQuarter/Bay/ShelteredBayWater"),
		"city_connection_ground":figure.test_move(Transform3D(Basis.IDENTITY,Vector3(-38,9.5,39)),Vector3(0,-2,0)),
		"city_connection_clear":not figure.test_move(Transform3D(Basis.IDENTITY,Vector3(-3,9.45,36)),Vector3(-35,0,0)),
		"lower_quay_ground":figure.test_move(Transform3D(Basis.IDENTITY,Vector3(-38,8.2,58)),Vector3(0,-2,0)),
		"stairs_ground":figure.test_move(Transform3D(Basis.IDENTITY,Vector3(-39.7,10,50.6)),Vector3(0,-3,0)),
		"cart_ramp_ground":figure.test_move(Transform3D(Basis.IDENTITY,Vector3(-18,10,50)),Vector3(0,-3,0)),
		"cart_ramp_no_wall":not figure.test_move(Transform3D(Basis.IDENTITY,Vector3(-18,9.5,50)),Vector3(0,0,3)),
		"west_pier_start_ground":figure.test_move(Transform3D(Basis.IDENTITY,Vector3(-62,8.2,66)),Vector3(0,-2,0)),
		"west_pier_join_ground":figure.test_move(Transform3D(Basis.IDENTITY,Vector3(-62,8.2,76.2)),Vector3(0,-2,0)),
		"west_landing_ground":figure.test_move(Transform3D(Basis.IDENTITY,Vector3(-62,8.2,116.2)),Vector3(0,-2,0)),
		"east_landing_ground":figure.test_move(Transform3D(Basis.IDENTITY,Vector3(-28,8.2,107)),Vector3(0,-2,0)),
		"west_pier_walk_clear":not figure.test_move(Transform3D(Basis.IDENTITY,Vector3(-62,7.91,66)),Vector3(0,0,50)),
		"quay_prom_clear":not figure.test_move(Transform3D(Basis.IDENTITY,Vector3(-84,7.91,63)),Vector3(58,0,0)),
		"warehouse_blocks":figure.test_move(Transform3D(Basis.IDENTITY,Vector3(-78,7.91,63)),Vector3(0,0,-8)),
		"quay_paving":terrain.data.get_control_base_id(Vector3(-72,0,62))==1,
		"sea_level_matches":is_equal_approx(city.get_node("World/CityParts/HarborQuarter/Bay/ShelteredBayWater").position.y,5.74),
		"quay_deck_height":absf(terrain.data.get_height(Vector3(-58,0,62))-6.96)<0.01,
		"seabed_below_water":terrain.data.get_height(Vector3(-58,0,100))<5.74,
		"deep_berth":terrain.data.get_height(Vector3(-58,0,100))<0.1,
		"west_threshold_ground":figure.test_move(Transform3D(Basis.IDENTITY,Vector3(-62,8.2,64.1)),Vector3(0,-2,0)),
		"east_threshold_ground":figure.test_move(Transform3D(Basis.IDENTITY,Vector3(-28,8.2,66.9)),Vector3(0,-2,0)),
		"west_threshold_clear":not figure.test_move(Transform3D(Basis.IDENTITY,Vector3(-62,7.95,63)),Vector3(0,0,4)),
		"east_threshold_clear":not figure.test_move(Transform3D(Basis.IDENTITY,Vector3(-28,7.95,66)),Vector3(0,0,4))
	}
