extends Node3D
## Standalone review of the same editable city scene; no network, identity or database.

@onready var camera: Camera3D = $PreviewCamera
@onready var city: Node3D = $LinvelCity
@onready var terrain: Terrain3D = $LinvelCity/World/Terrain3D
@onready var figure: CharacterBody3D = $ScaleFigure
var view := 1

func _ready() -> void:
	camera.make_current()
	terrain.set_camera(camera)
	show_view(1)
	print("LINVEL_CENTER_PREVIEW_READY · 1 overview · 2 gameplay 55 degrees + arrows to walk · 3 lower approach")
	print(JSON.stringify(review_state()))
	if "--city-review-smoke" in OS.get_cmdline_user_args():
		await get_tree().physics_frame
		await get_tree().physics_frame
		await RenderingServer.frame_post_draw
		var physics := review_physics()
		var paint := review_walkway()
		print("CITY_PAVING_PHYSICS " + JSON.stringify(physics))
		print("CITY_PAVING_PAINT " + JSON.stringify(paint))
		print("CITY_PAVING_CAPTURE " + JSON.stringify(capture_review("stone-base-final")))
		get_tree().quit(0 if not physics.values().has(false) and paint.ok else 1)

func show_view(index: int) -> void:
	view = index
	camera.far = 350.0
	if view == 1:
		camera.projection = Camera3D.PROJECTION_ORTHOGONAL
		camera.size = 58.0
		camera.position = Vector3(-42, 82, 62)
		camera.look_at(Vector3(0, 12, -3))
	elif view == 2:
		camera.projection = Camera3D.PROJECTION_PERSPECTIVE
		camera.fov = 50.0
		follow_figure()
	else:
		camera.projection = Camera3D.PROJECTION_PERSPECTIVE
		camera.fov = 48.0
		camera.position = Vector3(-27, 27, 43)
		camera.look_at(Vector3(0, 11, -5))

func follow_figure() -> void:
	var target := figure.position - Vector3(0, 0.9, 0)
	var distance := 33.941124
	camera.position = target + Vector3(0, sin(deg_to_rad(55.0)), cos(deg_to_rad(55.0))) * distance
	camera.look_at(target)

func _physics_process(delta: float) -> void:
	if figure.position.y < 0:
		figure.position = Vector3(6, 11.3, 8)
		figure.velocity = Vector3.ZERO
	var direction := Vector3.ZERO
	if view == 2:
		direction = Vector3(float(Input.is_physical_key_pressed(KEY_RIGHT)) - float(Input.is_physical_key_pressed(KEY_LEFT)), 0, float(Input.is_physical_key_pressed(KEY_DOWN)) - float(Input.is_physical_key_pressed(KEY_UP)))
	figure.velocity.x = direction.normalized().x * 3.5
	figure.velocity.z = direction.normalized().z * 3.5
	figure.velocity.y -= 20.0 * delta
	figure.move_and_slide()
	if view == 2:
		follow_figure()

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.physical_keycode >= KEY_1 and event.physical_keycode <= KEY_3:
			show_view(event.physical_keycode - KEY_0)

func review_state() -> Dictionary:
	return {"scene": city.scene_file_path, "native_terrain_regions": terrain.data.get_region_count(), "terrain_directory": terrain.data_directory, "square_height": terrain.data.get_height(Vector3.ZERO), "lower_approach_height": terrain.data.get_height(Vector3(0, 0, 30)), "figure_height_metres": 1.8, "database_used": false}

func capture_review(label: String) -> Dictionary:
	var directory := ProjectSettings.globalize_path("res://").path_join("../.artifacts/linvel-city-center").simplify_path()
	DirAccess.make_dir_recursive_absolute(directory)
	var path := directory.path_join(label.validate_filename() + ".png")
	var error := get_viewport().get_texture().get_image().save_png(path)
	return {"path": path, "error": error, "draw_calls": Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME), "primitives": Performance.get_monitor(Performance.RENDER_TOTAL_PRIMITIVES_IN_FRAME)}

func review_physics() -> Dictionary:
	# Real Jolt shape sweeps, independent of rendering and network/server fixtures.
	return {
		"square_ground": figure.test_move(Transform3D(Basis.IDENTITY, Vector3(6, 11, 8)), Vector3(0, -2, 0)),
		"lower_ground": figure.test_move(Transform3D(Basis.IDENTITY, Vector3(0, 9.5, 30)), Vector3(0, -2, 0)),
		"stair_ground": figure.test_move(Transform3D(Basis.IDENTITY, Vector3(1.7, 11, 22.6)), Vector3(0, -3, 0)),
		"guild_blocks": figure.test_move(Transform3D(Basis.IDENTITY, Vector3(0, 10.9, -15)), Vector3(0, 0, -12)),
		"fountain_blocks": figure.test_move(Transform3D(Basis.IDENTITY, Vector3(1, 10.9, 0)), Vector3(-4, 0, 0)),
		"east_passage_clear": not figure.test_move(Transform3D(Basis.IDENTITY, Vector3(7, 10.9, 0)), Vector3(4, 0, 0)),
		"north_junction_clear": not figure.test_move(Transform3D(Basis.IDENTITY, Vector3(31, 10.9, -26)), Vector3(0, 0, 14))
	}

func review_walkway() -> Dictionary:
	var samples: Array[Dictionary] = []
	var ok := true
	for sample in [[31, -26, 1], [31, -18, 1], [31, -10, 1], [0, -20, 1], [-35, -21, 0], [-34, -16, 0], [37, -18, 0], [12, 10, 0], [-26, -7, 1], [26, -11, 1]]:
		var layer := terrain.data.get_control_base_id(Vector3(sample[0], 0, sample[1]))
		ok = ok and layer == sample[2]
		samples.append({"x": sample[0], "z": sample[1], "layer": layer, "expected": sample[2]})
	return {"ok": ok, "samples": samples}
