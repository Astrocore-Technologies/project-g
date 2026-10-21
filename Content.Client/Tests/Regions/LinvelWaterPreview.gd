extends "res://Tests/Regions/LinvelMasterplanPreview.gd"
## Uses the actual editable city. No replacement materials or generated scenery.
var water_report: Dictionary = {}

func _ready() -> void:
	if not has_node("World/LinvelCity/Terrain3D"):
		push_error("Water review must instance the actual World/LinvelCity composition")
		get_tree().quit(1)
		return
	super._ready()
	if "--water-review" in OS.get_cmdline_user_args():
		await get_tree().create_timer(2.0).timeout
		if "--water-hide" in OS.get_cmdline_user_args():
			city.get_node("CityParts/CoastAndBackdrop/OpenSea").hide()
			city.get_node("CityParts/CoastAndBackdrop/FarSea").hide()
		var stage := "candidate-01"
		for argument in OS.get_cmdline_user_args():
			if argument.begins_with("--water-stage="): stage = argument.trim_prefix("--water-stage=")
		begin_water_review(stage)
		while not water_report.get("done",false): await get_tree().process_frame
		var okay := true
		if "--water-regression" in OS.get_cmdline_user_args():
			var checks := {"city":review_checks(),"dry_ground":landfill_checks(),"surfaces":surface_checks(),"gates":gate_checks(),"harbor":harbor_checks(),"world":world_checks()}
			for check in checks.values(): okay = okay and check.get("ok",false)
			var directory := ProjectSettings.globalize_path("res://../.artifacts/linvel-water/"+stage)
			var file := FileAccess.open(directory.path_join("regression.json"),FileAccess.WRITE)
			file.store_string(JSON.stringify(checks,"\t"))
			print("WATER_REGRESSION "+JSON.stringify(checks))
		get_tree().quit(0 if okay else 1)

func world_checks() -> Dictionary:
	var world := $World
	var suns := world.find_children("*","DirectionalLight3D",true,false)
	var environments := world.find_children("*","WorldEnvironment",true,false)
	var sea := city.get_node("CityParts/CoastAndBackdrop/OpenSea") as MeshInstance3D
	return {"ok":suns.size()==1 and environments.size()==1 and not city.has_node("World") and not city.has_node("Lighting") and sea.material_override.resource_path.ends_with("Sea.tres"),"suns":suns.size(),"environments":environments.size(),"nested_world":city.has_node("World"),"water_surfaces":sea.mesh.get_surface_count(),"water_material":sea.material_override.resource_path}

func water_views() -> Array:
	return [["harbor-overview",Vector3(-246,128,309),Vector3(-42,10,145)],
	["pier-water",Vector3(-87,8,169),Vector3(-105,0,178)],
	["quay-water",Vector3(-20,5.5,164),Vector3(-15,0,139)],
	["outer-sea",Vector3(-175,9,216),Vector3(-185,0,300)],
	["coastal-shallows",Vector3(185,23,171),Vector3(145,0,151)],
	["city-panorama",Vector3(-275,185,420),Vector3(0,22,38)],
	["square-materials",Vector3(-83,64,80),Vector3(0,27,-3)],
	["gate-materials",Vector3(48,37,-187),Vector3(48,36,-150)]]

func begin_water_review(stage: String) -> Dictionary:
	water_report = {"done":false,"stage":stage}
	_capture_water.call_deferred(stage)
	return water_report

func _capture_water(stage: String) -> void:
	var directory := ProjectSettings.globalize_path("res://../.artifacts/linvel-water/"+stage)
	DirAccess.make_dir_recursive_absolute(directory)
	var shots: Array = []
	for shot in water_views():
		show_surface_view(shot)
		await get_tree().create_timer(1.2).timeout
		await RenderingServer.frame_post_draw
		var path: String = directory.path_join(shot[0]+".png")
		shots.append({"path":path,"error":get_viewport().get_texture().get_image().save_png(path),"draw_calls":Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME),"primitives":Performance.get_monitor(Performance.RENDER_TOTAL_PRIMITIVES_IN_FRAME),"frame_ms":Performance.get_monitor(Performance.TIME_PROCESS)*1000.0})
	# Same view separated in time proves water motion without moving the camera.
	show_surface_view(water_views()[1])
	await get_tree().create_timer(.5).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png(directory.path_join("motion-a.png"))
	await get_tree().create_timer(2.0).timeout
	await RenderingServer.frame_post_draw
	get_viewport().get_texture().get_image().save_png(directory.path_join("motion-b.png"))
	water_report = {"done":true,"stage":stage,"renderer":RenderingServer.get_current_rendering_method(),"shots":shots}
	var file := FileAccess.open(directory.path_join("report.json"),FileAccess.WRITE)
	file.store_string(JSON.stringify(water_report,"\t"))
	print("WATER_REVIEW_DONE "+JSON.stringify(water_report))
