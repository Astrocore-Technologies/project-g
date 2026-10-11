extends Node3D
class_name LinvelGardenPreview
var camera: Camera3D
var terrain: Terrain3D
var figure: MeshInstance3D
var status: Label
var places: Array[Node3D] = []
var focus := Vector3.ZERO
var distance := 18.0
var selected := 0
var overview := false
var ranges: Dictionary = {}
var material: ShaderMaterial
var profile: Dictionary

func _ready() -> void:
	camera = $PreviewCamera
	terrain = $GardenValleys/Geometry/Terrain
	# Bind the preview camera explicitly; Terrain3D must not depend on sibling order.
	camera.make_current()
	terrain.set_camera(camera)
	terrain.set_physics_process(true)
	figure = $PlayerFigure
	material = load("res://Assets/World/Linvel/Gardens/painted_gardens.tres")
	profile = JSON.parse_string(FileAccess.get_file_as_string("res://Assets/World/Linvel/Gardens/layout.json"))
	for node in $GardenValleys/Places.get_children():
		if node.has_node("Center"):
			places.append(node)
	var layer := CanvasLayer.new()
	add_child(layer)
	status = Label.new()
	status.position = Vector2(20,20)
	status.add_theme_font_size_override("font_size",20)
	status.add_theme_constant_override("outline_size",3)
	layer.add_child(status)
	show_point(2)

func set_overview(value: bool) -> void:
	overview = value
	$GardenValleys/GardenAtmosphere.environment.fog_enabled = not value
	for node in $GardenValleys.find_children("*","GeometryInstance3D",true,false):
		if not ranges.has(node):
			ranges[node] = node.visibility_range_end
		node.visibility_range_end = 0.0 if value else ranges[node]
	figure.visible = not value

func show_point(index: int) -> void:
	selected = clampi(index,0,places.size()-1)
	set_overview(false)
	focus = places[selected].get_node("Center").global_position
	camera.projection = Camera3D.PROJECTION_PERSPECTIVE
	camera.fov = 50
	camera.far = 450
	update_camera()
	status.text = "САДОВЫЕ ДОЛИНЫ · "+str(places[selected].get_meta("display_name"))+"\n1–6: места · Tab: общий план · стрелки: осмотр · +/−: приближение\nДорога 1,08 км · около 5:10 пешком · осмотр без сервера"

func show_overview() -> void:
	set_overview(true)
	camera.projection = Camera3D.PROJECTION_ORTHOGONAL
	camera.size = 1030
	camera.far = 1700
	camera.position = Vector3(0,850,390)
	camera.look_at(Vector3(0,12,0))
	status.text = "САДОВЫЕ ДОЛИНЫ · общий план\n960 × 800 м · холмы, сельские дороги, ручей и сады\nTab: вернуться · 1–6: места"

func update_camera() -> void:
	camera.position = focus+Vector3(0,distance,distance)
	camera.look_at(focus)
	figure.position = focus+Vector3(0,1,0)

func _unhandled_input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo:
		if event.physical_keycode >= KEY_1 and event.physical_keycode <= KEY_6:
			show_point(event.physical_keycode-KEY_1)
		elif event.physical_keycode == KEY_TAB:
			show_point(selected) if overview else show_overview()
		elif event.physical_keycode in [KEY_EQUAL,KEY_KP_ADD]:
			distance = maxf(12,distance-3)
			if not overview:
				update_camera()
		elif event.physical_keycode in [KEY_MINUS,KEY_KP_SUBTRACT]:
			distance = minf(60,distance+3)
			if not overview:
				update_camera()

func _process(delta: float) -> void:
	material.set_shader_parameter("focus_world",Vector3.ONE*-10000 if overview else figure.position)
	if overview:
		return
	var motion := Vector3(float(Input.is_physical_key_pressed(KEY_RIGHT))-float(Input.is_physical_key_pressed(KEY_LEFT)),0,float(Input.is_physical_key_pressed(KEY_DOWN))-float(Input.is_physical_key_pressed(KEY_UP)))
	if motion.length_squared()>.1:
		focus += motion.normalized()*18*delta
		focus.y = terrain.data.get_height(focus)
		update_camera()
