extends SceneTree
## Explicit offline authoring entry point; edits only saved coast water resources.
func _initialize() -> void:
	_build.call_deferred()

func _build() -> void:
	var helper: Node = load("res://Tools/Linvel/LinvelWaterAuthoring.gd").new()
	root.add_child(helper)
	if "--bake-shore" in OS.get_cmdline_user_args():
		var camera := Camera3D.new()
		root.add_child(camera)
		camera.current = true
		var city := load("res://Scenes/Regions/Linvel/LinvelCity/LinvelCity.tscn").instantiate() as Node3D
		city.process_mode = Node.PROCESS_MODE_DISABLED
		root.add_child(city)
		(city.get_node("Terrain3D") as Terrain3D).set_camera(camera)
		helper.reparent(city)
		await process_frame
		print("WATER_SHORE "+JSON.stringify(await helper.bake_shore()))
	helper.load_shore()
	if "--smooth-depth" in OS.get_cmdline_user_args(): print("WATER_DEPTH "+JSON.stringify(helper.smooth_depth()))
	var result: Dictionary = helper.build_water()
	print("WATER_BUILD "+JSON.stringify(result))
	helper.free()
	quit(0)
