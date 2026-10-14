extends Node3D
class_name LinvelBeechPreview

@export var camera_path: NodePath = ^"PreviewCamera"
@export var figure_path: NodePath = ^"PlayerFigure"
var camera: Camera3D
var figure: Node3D
var status: Label
var places: Array[Node3D] = []
var focus := Vector3.ZERO
var offset := Vector3(0,24,24)
var overview := false
var selected := 1
var draw_ranges: Dictionary = {}
var focus_materials: Array[ShaderMaterial] = []
var terrain: Terrain3D
var route_text := ""

func overview_visibility(enabled: bool) -> void:
	$BeechValley/ForestAtmosphere.environment.fog_enabled = not enabled
	for node in $BeechValley.find_children("*", "GeometryInstance3D", true, false):
		if not draw_ranges.has(node):
			draw_ranges[node] = node.visibility_range_end
		node.visibility_range_end = 0.0 if enabled else draw_ranges[node]

func _ready() -> void:
	camera = get_node(camera_path)
	terrain = $BeechValley/Geometry/Terrain
	camera.make_current()
	terrain.set_camera(camera)
	terrain.set_physics_process(true)
	# Packed scenes may embed material copies; update every actual instance.
	for visual in $BeechValley.find_children("*", "GeometryInstance3D", true, false):
		if visual.material_override is ShaderMaterial:
			var material: ShaderMaterial = visual.material_override
			if not focus_materials.has(material): focus_materials.append(material)
	var profile: Dictionary=JSON.parse_string(FileAccess.get_file_as_string("res://Assets/World/Linvel/Beech/Revision2/layout.json"))
	var seconds: int=roundi(profile.walking_seconds)
	route_text="%.3f км / %d:%02d"%[profile.route_metres/1000.0,seconds/60,seconds%60]
	figure = get_node_or_null(figure_path)
	for p in $BeechValley/Places.get_children():
		if p.has_node("Center"):
			places.append(p)
	var layer := CanvasLayer.new()
	add_child(layer)
	var panel := PanelContainer.new()
	panel.position = Vector2(20,20)
	layer.add_child(panel)
	status = Label.new()
	status.add_theme_font_size_override("font_size",20)
	status.add_theme_constant_override("outline_size",2)
	panel.add_child(status)
	show_point(1)

func show_point(index: int) -> void:
	selected = clampi(index,0,places.size()-1)
	overview = false
	overview_visibility(false)
	focus = places[selected].get_node("Center").global_position
	# Inspect each activity from an adjacent playable viewpoint with the same
	# gameplay camera, rather than showing only its road-centre marker.
	var viewing_offsets := [Vector2(-4,8),Vector2.ZERO,Vector2(21,-8),Vector2(-12,6),Vector2(13,-6)]
	focus.x += viewing_offsets[selected].x
	focus.z += viewing_offsets[selected].y
	focus.y = ground_height(focus)
	camera.projection = Camera3D.PROJECTION_PERSPECTIVE
	camera.fov = 50
	camera.far = 800
	camera.global_position = focus+offset
	camera.look_at(focus)
	if figure != null:
		figure.visible = true
		figure.global_position = focus+Vector3(0,1,0)
	status.text = "  БУКОВАЯ ДОЛИНА · "+str(places[selected].get_meta("display_name"))+"  \n  1–5: места · 6: лес · Tab: общий план · стрелки: осмотр  \n  Terrain3D · 1000 × 1000 м · "+route_text+" · осмотр без сервера  "

func show_overview() -> void:
	overview = true
	overview_visibility(true)
	if figure != null:
		figure.visible = false
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.size = 1150
	camera.far = 2200
	camera.position = Vector3(0,1100,500)
	camera.look_at(Vector3(0,35,0))
	status.text = "  БУКОВАЯ ДОЛИНА · общий план  \n  1–5: игровая камера · Tab: вернуться к месту  \n  1000 × 1000 м · овраг, гребни и три дорожные петли  "

func show_forest_path() -> void:
	overview = false
	overview_visibility(false)
	var x := -265.0
	var z := 231.0
	var y: float = ground_height(Vector3(x,0,z))
	focus = Vector3(x,y,z)
	camera.projection = Camera3D.PROJECTION_PERSPECTIVE
	camera.fov = 50
	camera.far = 800
	camera.position = focus+offset
	camera.look_at(focus)
	figure.visible = true
	figure.global_position = focus+Vector3(0,1,0)
	status.text = "  БУКОВАЯ ДОЛИНА · лесная тропа  \n  1–5: места · 6: лес · Tab: общий план · стрелки: осмотр  "

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.physical_keycode >= KEY_1 and event.physical_keycode <= KEY_5:
			show_point(event.physical_keycode-KEY_1)
		elif event.physical_keycode == KEY_6:
			show_forest_path()
		elif event.physical_keycode == KEY_TAB:
			if overview:
				show_point(selected)
			else:
				show_overview()

func _process(delta: float) -> void:
	if measuring:
		performance_samples.append({"frame_ms":delta*1000.0,"cpu_ms":RenderingServer.viewport_get_measured_render_time_cpu(get_viewport().get_viewport_rid()),"gpu_ms":RenderingServer.viewport_get_measured_render_time_gpu(get_viewport().get_viewport_rid()),"memory_mib":Performance.get_monitor(Performance.MEMORY_STATIC)/1048576.0,"video_mib":Performance.get_monitor(Performance.RENDER_VIDEO_MEM_USED)/1048576.0,"draw_calls":Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME),"primitives":Performance.get_monitor(Performance.RENDER_TOTAL_PRIMITIVES_IN_FRAME)})
	if figure != null:
		for material in focus_materials: material.set_shader_parameter("focus_world", Vector3.ONE*-10000 if overview else figure.global_position)
	if overview:
		return
	var motion := Vector3(float(Input.is_physical_key_pressed(KEY_RIGHT))-float(Input.is_physical_key_pressed(KEY_LEFT)),0,float(Input.is_physical_key_pressed(KEY_DOWN))-float(Input.is_physical_key_pressed(KEY_UP)))
	if motion.length_squared() > 0:
		focus += motion.normalized()*18*delta
		focus.x=clampf(focus.x,-490,490)
		focus.z=clampf(focus.z,-490,490)
		var y: float=ground_height(focus)
		if not is_nan(y): focus.y=y
		camera.position = focus+offset
		var camera_floor: float=terrain.data.get_height(camera.position)
		if not is_nan(camera_floor): camera.position.y=maxf(camera.position.y,camera_floor+3.0)
		camera.look_at(focus)
		if figure!=null: figure.global_position=focus+Vector3(0,1,0)

func ground_height(point: Vector3) -> float:
	# Layer 1 includes real bridge/working decks and native ground; decorative
	# blockers on layer 4 cannot make the preview capsule stand on a tree.
	var query:=PhysicsRayQueryParameters3D.create(Vector3(point.x,200,point.z),Vector3(point.x,-50,point.z),1)
	var hit: Dictionary=get_world_3d().direct_space_state.intersect_ray(query)
	return hit.position.y if not hit.is_empty() else terrain.data.get_height(point)


var performance_samples: Array[Dictionary] = []
var measuring := false

func begin_performance_sample() -> void:
	performance_samples.clear()
	measuring=true
	RenderingServer.viewport_set_measure_render_time(get_viewport().get_viewport_rid(),true)

func finish_performance_sample() -> Dictionary:
	measuring=false
	if performance_samples.is_empty(): return {"error":"No rendered frames sampled."}
	var frames: Array[float]=[]
	var cpu: Array[float]=[]
	var gpu: Array[float]=[]
	for row in performance_samples:
		frames.append(row.frame_ms)
		cpu.append(row.cpu_ms)
		gpu.append(row.gpu_ms)
	frames.sort()
	cpu.sort()
	gpu.sort()
	var last: Dictionary=performance_samples[-1]
	return {"frames":frames.size(),"frame_median_ms":frames[frames.size()/2],"frame_p95_ms":frames[int(frames.size()*.95)],"cpu_p95_ms":cpu[int(cpu.size()*.95)],"gpu_p95_ms":gpu[int(gpu.size()*.95)],"memory_mib":last.memory_mib,"video_mib":last.video_mib,"draw_calls":last.draw_calls,"primitives":last.primitives,"viewport":get_viewport().get_visible_rect().size,"synthetic_capsules":60 if has_node("CapacityFigures") else 1}

func show_capacity_figures(enabled: bool) -> void:
	if has_node("CapacityFigures"):
		get_node("CapacityFigures").free()
	if not enabled: return
	var parent:=Node3D.new()
	parent.name="CapacityFigures"
	add_child(parent)
	var packed: PackedScene=load("res://Scenes/Actors/Primitives/PlayerFigure.tscn")
	for i in range(60):
		var node: Node3D=packed.instantiate()
		parent.add_child(node)
		var p: Vector3=focus+Vector3((i%12-5.5)*1.8,0,(i/12-2)*2.0)
		p.y=ground_height(p)+1
		node.global_position=p
