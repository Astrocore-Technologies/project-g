@tool
extends Node
## Editor-only native review of the actual World scene. No saved runtime node.
var report: Dictionary = {}

func restart_preserving_scenes() -> Dictionary:
	var paths := EditorInterface.get_open_scenes()
	var directory := ProjectSettings.globalize_path("res://../.artifacts/linvel-water/before-editor-restart")
	for path in paths:
		if not FileAccess.file_exists(path): continue
		var target: String = directory.path_join(str(path).trim_prefix("res://"))
		DirAccess.make_dir_recursive_absolute(target.get_base_dir())
		assert(DirAccess.copy_absolute(ProjectSettings.globalize_path(path),target)==OK)
	# Remove this temporary node before saving the user's open scenes.
	get_parent().remove_child(self)
	queue_free()
	EditorInterface.save_all_scenes()
	EditorInterface.restart_editor.call_deferred(true)
	return {"preserved_open_scenes":paths,"backup":directory,"restart_queued":true}

func begin_capture() -> Dictionary:
	report = {"done":false}
	_capture.call_deferred()
	return report

func _capture() -> void:
	var world := get_parent()
	var terrain := world.get_node("LinvelCity/Terrain3D") as Terrain3D
	var editor_viewport := EditorInterface.get_editor_viewport_3d(0)
	var original := editor_viewport.get_camera_3d()
	var viewport := SubViewport.new()
	viewport.size = Vector2i(1280,720)
	viewport.world_3d = editor_viewport.find_world_3d()
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	add_child(viewport)
	var camera := Camera3D.new()
	viewport.add_child(camera)
	camera.fov = 64
	camera.near = .1
	camera.far = 10000
	camera.cull_mask = 1
	camera.make_current()
	terrain.set_camera(camera)
	var directory := ProjectSettings.globalize_path("res://../.artifacts/linvel-water/final/editor")
	DirAccess.make_dir_recursive_absolute(directory)
	var shots := []
	for shot in [["harbor",Vector3(-246,128,309),Vector3(-42,10,145)],["shore",Vector3(185,23,171),Vector3(145,0,151)],["pier",Vector3(-73,6,155),Vector3(-80,0,178)]]:
		camera.global_position = shot[1]
		camera.look_at(shot[2])
		await get_tree().create_timer(1.0).timeout
		await RenderingServer.frame_post_draw
		var path: String = directory.path_join(shot[0]+".png")
		shots.append({"path":path,"error":viewport.get_texture().get_image().save_png(path)})
	original.make_current()
	terrain.set_camera(original)
	viewport.queue_free()
	var seabed := world.get_node("LinvelCity/CityParts/CoastAndBackdrop/CoastalSeabed") as MeshInstance3D
	report = {"done":true,"scene":world.scene_file_path,"suns":world.find_children("*","DirectionalLight3D",true,false).size(),"environments":world.find_children("*","WorldEnvironment",true,false).size(),"seabed_triangles":seabed.mesh.get_faces().size()/3,"shots":shots}
	var file := FileAccess.open(directory.path_join("report.json"),FileAccess.WRITE)
	file.store_string(JSON.stringify(report,"\t"))
