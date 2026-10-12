
extends Node3D
var camera: Camera3D
var samples: Array[float]=[]
var cpu_samples: Array[float]=[]
var gpu_samples: Array[float]=[]
var frame:=0
var last:int
var region_id:="fox"
var output:="D:/projects/project-g/.artifacts/fox-hills/benchmark-fox.json"
var started:int
func _ready() -> void:
 for arg in OS.get_cmdline_user_args():
  if arg.begins_with("--region="): region_id=arg.trim_prefix("--region=")
  if arg.begins_with("--output="): output=arg.trim_prefix("--output=")
 camera=Camera3D.new()
 add_child(camera)
 camera.make_current()
 camera.fov=50
 camera.far=1200
 var path:="res://Scenes/Regions/Linvel/FoxHills.tscn" if region_id=="fox" else "res://Scenes/Regions/Linvel/GardenValleys.tscn"
 var region:Node3D=load(path).instantiate()
 add_child(region)
 var terrain:Terrain3D=region.get_node("Geometry/Terrain")
 terrain.set_camera(camera)
 var point:=Vector3(54,0,-38) if region_id=="fox" else Vector3(-270,0,193)
 point.y=terrain.data.get_height(point)
 camera.position=point+Vector3(0,22,22)
 camera.look_at(point)
 for i in range(15):
  var actor:=MeshInstance3D.new()
  var mesh:=CapsuleMesh.new()
  mesh.height=2
  mesh.radius=.32
  mesh.radial_segments=12
  mesh.rings=4
  actor.mesh=mesh
  var material:=StandardMaterial3D.new()
  material.albedo_color=Color(.30,.42,.62)
  material.roughness=1.0
  actor.material_override=material
  var p:=point+Vector3(float(i%5)*1.7-3.4,0,float(i/5)*1.7+4.5)
  p.y=terrain.data.get_height(p)+1
  actor.position=p
  add_child(actor)
 RenderingServer.viewport_set_measure_render_time(get_viewport().get_viewport_rid(),true)
 last=Time.get_ticks_usec()
 started=Time.get_ticks_msec()
func _process(_delta:float) -> void:
 frame+=1
 var now:int=Time.get_ticks_usec()
 var ms:float=(now-last)/1000.0
 last=now
 if frame<120:return
 samples.append(ms)
 cpu_samples.append(Performance.get_monitor(Performance.TIME_PROCESS)*1000.0)
 gpu_samples.append(RenderingServer.viewport_get_measured_render_time_gpu(get_viewport().get_viewport_rid()))
 if samples.size()<300:return
 samples.sort()
 var result:={
 "region":region_id,"renderer":RenderingServer.get_current_rendering_method(),
 "driver":RenderingServer.get_current_rendering_driver_name(),"figures":15,"measured_frames":samples.size(),
 "frame_ms_median":samples[150],"frame_ms_p95":samples[285],
 "cpu_process_ms_mean":cpu_samples.reduce(func(a,b):return a+b,0.0)/cpu_samples.size(),
 "gpu_ms_mean":gpu_samples.reduce(func(a,b):return a+b,0.0)/gpu_samples.size(),
 "draw_calls":Performance.get_monitor(Performance.RENDER_TOTAL_DRAW_CALLS_IN_FRAME),
 "primitives":Performance.get_monitor(Performance.RENDER_TOTAL_PRIMITIVES_IN_FRAME),
 "runtime_ms":Time.get_ticks_msec()-started,
 "note":"15 local capsule meshes; not a 15-client network or combat load test."
 }
 FileAccess.open(output,FileAccess.WRITE).store_string(JSON.stringify(result,"\t"))
 get_viewport().get_texture().get_image().save_png(output.trim_suffix(".json")+".png")
 print("LINVEL_BENCHMARK_OK ",JSON.stringify(result))
 get_tree().quit()
