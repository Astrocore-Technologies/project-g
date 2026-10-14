extends Node
## Presentation only. Static crowns are indexed once; only cells near the
## camera-to-actor segment are examined, at a bounded update frequency.
@export_range(.05,.5) var update_interval := .10
@export_range(0.0,3.0) var actor_clearance := .65
const CELL := 32.0
var focus: Node3D
var region: Node3D
var camera: Camera3D
var cells: Dictionary = {}
var hidden: Dictionary = {}
var max_radius := 1.0
var elapsed := 0.0
var enabled := true

func initialize(actor: Node3D,world_region: Node3D) -> void:
 focus=actor
 region=world_region
 camera=get_viewport().get_camera_3d()
 for crown in region.find_children("*","MeshInstance3D",true,false):
  if not (crown.is_in_group("forest_crown") or crown.is_in_group("forest_upper_branches")) or crown.mesh==null: continue
  var box: AABB=crown.mesh.get_aabb()
  var centre: Vector3=crown.global_transform*box.get_center()
  var scale: Vector3=crown.global_basis.get_scale().abs()
  var radius: float=maxf(box.size.x*scale.x,box.size.z*scale.z)*.52
  max_radius=maxf(max_radius,radius)
  var key:=Vector2i(floori(centre.x/CELL),floori(centre.z/CELL))
  if not cells.has(key): cells[key]=[]
  cells[key].append({"node":crown,"centre":centre,"radius":radius,"shadow":crown.cast_shadow})

func set_enabled(value: bool) -> void:
 enabled=value
 if not value: restore_crowns()

func restore_crowns() -> void:
 for crown in hidden:
  if is_instance_valid(crown): crown.cast_shadow=hidden[crown]
 hidden.clear()

func _process(delta: float) -> void:
 if not enabled or not is_instance_valid(focus): return
 elapsed+=delta
 if elapsed<update_interval: return
 elapsed=0
 if not is_instance_valid(camera) or not camera.current:
  camera=get_viewport().get_camera_3d()
 if not is_instance_valid(camera): return
 var start:=camera.global_position
 var target:=focus.global_position+Vector3(0,.7,0)
 var ray:=target-start
 var length_squared:=maxf(ray.length_squared(),.01)
 var margin:=max_radius+actor_clearance
 var first:=Vector2i(floori((minf(start.x,target.x)-margin)/CELL),floori((minf(start.z,target.z)-margin)/CELL))
 var last:=Vector2i(floori((maxf(start.x,target.x)+margin)/CELL),floori((maxf(start.z,target.z)+margin)/CELL))
 var next: Dictionary={}
 for x in range(first.x,last.x+1):
  for z in range(first.y,last.y+1):
   for entry in cells.get(Vector2i(x,z),[]):
    var t: float=(entry.centre-start).dot(ray)/length_squared
    if t<=.02 or t>=.94: continue
    if entry.centre.distance_to(start+ray*t)>entry.radius+actor_clearance: continue
    var crown: MeshInstance3D=entry.node
    if not is_instance_valid(crown): continue
    next[crown]=entry.shadow
    if crown.cast_shadow!=GeometryInstance3D.SHADOW_CASTING_SETTING_SHADOWS_ONLY:
     crown.cast_shadow=GeometryInstance3D.SHADOW_CASTING_SETTING_SHADOWS_ONLY
 for crown in hidden:
  if not next.has(crown) and is_instance_valid(crown): crown.cast_shadow=hidden[crown]
 hidden=next

func _exit_tree() -> void:
 restore_crowns()
 focus=null
 region=null
 camera=null
 cells.clear()
