extends Node3D
## Art-only inspection: no network, combat, rewards or persistent gameplay state.
@export_range(0.0,0.4) var wind_strength := .10
@export var camera_offset := Vector3(0,24,24)
@export var lighting_presets: Array[Resource] = [preload("res://Assets/ArtDirection/ForestReference/Lighting/Day.tres"),preload("res://Assets/ArtDirection/ForestReference/Lighting/Diffuse.tres"),preload("res://Assets/ArtDirection/ForestReference/Lighting/Evening.tres")]
@onready var camera: Camera3D = $GameCamera
@onready var terrain: Terrain3D = $Terrain
@onready var figure: MeshInstance3D = $PlayerScale
@onready var sun: DirectionalLight3D = $SpringSun
@onready var environment: Environment = $Atmosphere.environment
var focus := Vector3(2,0,7)
var mode := 1
var view_index := 0
var status: Label
var profile: Array[Dictionary] = []
var measuring := false
var foliage_materials: Array[ShaderMaterial] = []
var crowns: Array[MeshInstance3D] = []

func _ready() -> void:
 camera.make_current()
 terrain.set_camera(camera)
 terrain.set_physics_process(true)
 for visual in $BeechGroups.find_children("*","MeshInstance3D",true,false):
  if "Leaves" in str(visual.name):
   crowns.append(visual)
   if not foliage_materials.has(visual.material_override): foliage_materials.append(visual.material_override)
 for m in foliage_materials: m.set_shader_parameter("wind_strength",wind_strength)
 $Grass.get_child(0).material_override.set_shader_parameter("wind_strength",wind_strength)
 var layer := CanvasLayer.new()
 layer.name="InspectionHUD"
 add_child(layer)
 var panel := PanelContainer.new()
 panel.position=Vector2(18,18)
 layer.add_child(panel)
 status=Label.new()
 status.add_theme_font_size_override("font_size",18)
 status.add_theme_constant_override("outline_size",2)
 panel.add_child(status)
 RenderingServer.viewport_set_measure_render_time(get_viewport().get_viewport_rid(),true)
 set_lighting(1)
 show_view(0)

func ground_height(point: Vector3) -> float:
 var ray := PhysicsRayQueryParameters3D.create(Vector3(point.x,30,point.z),Vector3(point.x,-10,point.z),1)
 var hit := get_world_3d().direct_space_state.intersect_ray(ray)
 return hit.position.y if not hit.is_empty() else terrain.data.get_height(point)

func show_view(index: int) -> Dictionary:
 view_index=clampi(index,0,2)
 var points := [Vector3(2,0,7),Vector3(8,0,-11),Vector3(13,0,8)]
 focus=points[view_index]
 position_camera()
 return {"view":view_index,"focus":focus,"camera_offset":camera_offset,"fov":camera.fov}

func position_camera() -> void:
 focus.y=ground_height(focus)
 camera.global_position=focus+camera_offset
 camera.look_at(focus)
 figure.global_position=focus+Vector3(0,.9,0)
 update_hud()

func set_lighting(preset_index: int) -> Dictionary:
 mode=clampi(preset_index,1,3)
 var preset: Resource = lighting_presets[mode-1]
 sun.rotation_degrees=preset.get("sun_rotation_degrees")
 sun.light_color=preset.get("sun_colour")
 sun.light_energy=preset.get("sun_energy")
 environment.ambient_light_color=preset.get("ambient_colour")
 environment.ambient_light_energy=preset.get("ambient_energy")
 environment.fog_light_color=preset.get("fog_colour")
 environment.fog_density=preset.get("fog_density")
 update_hud()
 return {"lighting":mode,"sun_energy":sun.light_energy,"ambient_energy":environment.ambient_light_energy}

func update_hud() -> void:
 if status==null: return
 var lighting_label: String = lighting_presets[mode-1].get("display_name")
 var views := ["Тропа и ручей","Под буковыми кронами","Лесной навес"]
 status.text="  ЭЛИАРА · визуальный эталон 80 × 80 м  \n  "+lighting_label+" · "+views[view_index]+"  \n  1–3: свет · F1–F3: виды · стрелки / ПКМ: осмотр  "

func _unhandled_input(event: InputEvent) -> void:
 if event is InputEventKey and event.pressed and not event.echo:
  if event.physical_keycode>=KEY_1 and event.physical_keycode<=KEY_3: set_lighting(event.physical_keycode-KEY_1+1)
  elif event.physical_keycode>=KEY_F1 and event.physical_keycode<=KEY_F3: show_view(event.physical_keycode-KEY_F1)
 elif event is InputEventMouseButton and event.pressed and event.button_index==MOUSE_BUTTON_RIGHT:
  var from := camera.project_ray_origin(event.position)
  var ray := PhysicsRayQueryParameters3D.create(from,from+camera.project_ray_normal(event.position)*200,1)
  var hit := get_world_3d().direct_space_state.intersect_ray(ray)
  if not hit.is_empty():
   focus=hit.position
   focus.x=clampf(focus.x,-33,33);focus.z=clampf(focus.z,-33,33)
   position_camera()

func _process(delta: float) -> void:
 var motion := Vector3(float(Input.is_physical_key_pressed(KEY_RIGHT))-float(Input.is_physical_key_pressed(KEY_LEFT)),0,float(Input.is_physical_key_pressed(KEY_DOWN))-float(Input.is_physical_key_pressed(KEY_UP)))
 if motion.length_squared()>0:
  focus+=motion.normalized()*8.0*delta
  focus.x=clampf(focus.x,-33,33);focus.z=clampf(focus.z,-33,33)
  position_camera()
 # Cache the crowns once; keep their shadows when the camera hides their
 # colour pass. This art preview follows the concept's visibility requirement.
 var sight := focus+Vector3(0,1.0,0)-camera.global_position
 for crown in crowns:
  var box := crown.mesh.get_aabb()
  var centre: Vector3 = crown.global_transform*box.get_center()
  var radius: float = maxf(box.size.x,box.size.z)*.55*crown.global_basis.get_scale().x
  var t: float = (centre-camera.global_position).dot(sight)/maxf(sight.length_squared(),.01)
  var blocked: bool = t>.02 and t<.92 and centre.distance_to(camera.global_position+sight*t)<radius
  var casting: int = GeometryInstance3D.SHADOW_CASTING_SETTING_SHADOWS_ONLY if blocked else GeometryInstance3D.SHADOW_CASTING_SETTING_ON
  if crown.cast_shadow!=casting: crown.cast_shadow=casting
 if measuring:
  var viewport := get_viewport().get_viewport_rid()
  profile.append({"frame_ms":delta*1000,"cpu_ms":RenderingServer.viewport_get_measured_render_time_cpu(viewport),"gpu_ms":RenderingServer.viewport_get_measured_render_time_gpu(viewport),"calls":Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME),"primitives":Performance.get_monitor(Performance.RENDER_TOTAL_PRIMITIVES_IN_FRAME)})

func begin_profile() -> Dictionary:
 profile.clear();measuring=true
 return {"measuring":true,"view":view_index,"light":mode}

func end_profile() -> Dictionary:
 measuring=false
 if profile.is_empty(): return {"frames":0}
 var gpu: Array[float]=[]
 var cpu: Array[float]=[]
 for frame in profile:
  gpu.append(frame.gpu_ms);cpu.append(frame.cpu_ms)
 gpu.sort();cpu.sort()
 var p95 := mini(int(profile.size()*.95),profile.size()-1)
 return {"frames":profile.size(),"cpu_p95_ms":cpu[p95],"gpu_p95_ms":gpu[p95],"last":profile.back(),"renderer":"Mobile","resolution":get_viewport().get_visible_rect().size,"trees":$BeechGroups.get_child_count(),"grass_tufts":get_meta("grass_tuft_count",0),"terrain_regions":terrain.data.get_region_count()}


