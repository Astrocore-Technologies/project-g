extends Node3D
class_name LinvelBeechPreview

@export var camera_path: NodePath = ^"PreviewCamera"
@export var figure_path: NodePath = ^"PlayerFigure"
var camera: Camera3D
var figure: Node3D
var status: Label
var places: Array[Node3D] = []
var focus := Vector3.ZERO
var offset := Vector3(0,12,12)
var overview := false
var selected := 1
var draw_ranges: Dictionary = {}
var focus_material: ShaderMaterial

func overview_visibility(enabled: bool) -> void:
	$BeechValley/ForestAtmosphere.environment.fog_enabled = not enabled
	for node in $BeechValley.find_children("*", "GeometryInstance3D", true, false):
		if not draw_ranges.has(node):
			draw_ranges[node] = node.visibility_range_end
		node.visibility_range_end = 0.0 if enabled else draw_ranges[node]

func _ready() -> void:
	camera = get_node(camera_path)
	focus_material = load("res://Assets/World/Linvel/Beech/painted_forest.tres")
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
	camera.projection = Camera3D.PROJECTION_PERSPECTIVE
	camera.fov = 50
	camera.far = 350
	camera.global_position = focus+offset
	camera.look_at(focus)
	if figure != null:
		figure.visible = true
		figure.global_position = focus+Vector3(0,1,0)
	status.text = "  БУКОВАЯ ДОЛИНА · "+str(places[selected].get_meta("display_name"))+"  \n  1–5: места · Tab: общий план · стрелки: осмотр  \n  Основная тропа: 1,51 км · 7:12 пешком · осмотр без сервера  "

func show_overview() -> void:
	overview = true
	overview_visibility(true)
	if figure != null:
		figure.visible = false
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.size = 1000
	camera.far = 1600
	camera.position = Vector3(0,850,350)
	camera.look_at(Vector3.ZERO)
	status.text = "  БУКОВАЯ ДОЛИНА · общий план  \n  1–5: игровая камера · Tab: вернуться к месту  \n  960 × 800 м · город остаётся западнее этой области  "

func show_forest_path() -> void:
	overview = false
	overview_visibility(false)
	var profile: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://Assets/World/Linvel/Beech/layout.json"))
	var x := -175.0
	var z := 150.0
	var gx: float = (x+480.0)/float(profile.spacing)
	var gz: float = (z+400.0)/float(profile.spacing)
	var ix: int = int(gx)
	var iz: int = int(gz)
	var rows: Array = profile.heights
	var y: float = lerpf(lerpf(rows[iz][ix],rows[iz][ix+1],gx-ix),lerpf(rows[iz+1][ix],rows[iz+1][ix+1],gx-ix),gz-iz)
	focus = Vector3(x,y,z)
	camera.projection = Camera3D.PROJECTION_PERSPECTIVE
	camera.fov = 50
	camera.far = 350
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
	if focus_material != null and figure != null:
		focus_material.set_shader_parameter("focus_world", Vector3.ONE*-10000 if overview else figure.global_position)
	if overview:
		return
	var motion := Vector3(float(Input.is_physical_key_pressed(KEY_RIGHT))-float(Input.is_physical_key_pressed(KEY_LEFT)),0,float(Input.is_physical_key_pressed(KEY_DOWN))-float(Input.is_physical_key_pressed(KEY_UP)))
	if motion.length_squared() > 0:
		focus += motion.normalized()*18*delta
		camera.position = focus+offset
		camera.look_at(focus)
