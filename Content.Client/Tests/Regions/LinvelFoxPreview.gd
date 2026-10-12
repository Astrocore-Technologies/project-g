extends Node3D
var camera: Camera3D
var terrain: Terrain3D
var focus: Vector3
var distance := 32.0
var places: Array[Node3D] = []
var selected := 0
var overview := false
var status: Label
func _ready() -> void:
 camera = $PreviewCamera
 terrain = $FoxHills/Geometry/Terrain
 camera.make_current()
 terrain.set_camera(camera)
 terrain.set_physics_process(true)
 for p in $FoxHills/Places.get_children():
  places.append(p)
 var layer := CanvasLayer.new()
 add_child(layer)
 status = Label.new()
 status.position = Vector2(20,20)
 status.add_theme_font_size_override("font_size",20)
 status.add_theme_constant_override("outline_size",3)
 layer.add_child(status)
 show_point(2)
func show_point(index: int) -> void:
 selected = clampi(index,0,places.size()-1)
 overview = false
 focus = places[selected].get_node("Center").global_position
 if selected==2:
  focus.x+=14.0
  focus.z+=2.0
  focus.y=terrain.data.get_height(focus)
 camera.projection = Camera3D.PROJECTION_PERSPECTIVE
 camera.fov = 50
 camera.far = 1200
 $FoxHills/HillAtmosphere.environment.fog_enabled = true
 update_camera()
 status.text = "ЛИСЬИ ХОЛМЫ · "+str(places[selected].get_meta("display_name"))+"\n1–5: места · Tab: общий план · стрелки: осмотр · +/−: дистанция\nПодъёмы по обозначенным тропам · осмотр без сервера"
func show_overview() -> void:
 overview = true
 $FoxHills/HillAtmosphere.environment.fog_enabled = false
 camera.projection = Camera3D.PROJECTION_ORTHOGONAL
 camera.size = 1100
 camera.far = 2200
 camera.position = Vector3(0,1000,450)
 camera.look_at(Vector3(0,35,0))
 status.text = "ЛИСЬИ ХОЛМЫ · общий план\n960 × 960 м · около 6 минут пешком · обрывы и отдельные подъёмы\nTab: вернуться · 1–5: места"
func update_camera() -> void:
 camera.position = focus+(Vector3(-distance*.55,distance,-distance*.70) if selected==4 else Vector3(0,distance,distance))
 var floor_height: float=terrain.data.get_height(camera.position)
 if not is_nan(floor_height): camera.position.y=maxf(camera.position.y,floor_height+6.0)
 camera.look_at(focus)
func _unhandled_input(event: InputEvent) -> void:
 if event is InputEventKey and event.pressed and not event.echo:
  if event.physical_keycode>=KEY_1 and event.physical_keycode<=KEY_5:
   show_point(event.physical_keycode-KEY_1)
  elif event.physical_keycode==KEY_TAB:
   show_point(selected) if overview else show_overview()
  elif event.physical_keycode in [KEY_EQUAL,KEY_KP_ADD]:
   distance = maxf(16.0,distance-4)
   if not overview: update_camera()
  elif event.physical_keycode in [KEY_MINUS,KEY_KP_SUBTRACT]:
   distance = minf(100.0,distance+4)
   if not overview: update_camera()
func _process(delta: float) -> void:
 if overview: return
 var movement := Vector3(float(Input.is_physical_key_pressed(KEY_RIGHT))-float(Input.is_physical_key_pressed(KEY_LEFT)),0,float(Input.is_physical_key_pressed(KEY_DOWN))-float(Input.is_physical_key_pressed(KEY_UP)))
 if movement.length_squared()>.1:
  focus += movement.normalized()*25.0*delta
  var y: float = terrain.data.get_height(focus)
  if not is_nan(y): focus.y = y
  update_camera()
