@tool
extends Node
# One-shot editor repair; never attached to the playable region.
const A := "res://Assets/World/Linvel/FoxHills/"
func terrain_node() -> Terrain3D:
 var scene := EditorInterface.get_edited_scene_root()
 return scene.get_node("FoxHills/Geometry/Terrain") if scene.has_node("FoxHills/Geometry/Terrain") else scene.get_node("Geometry/Terrain")
func dock_node() -> Node:
 for n in EditorInterface.get_base_control().find_children("*","",true,false):
  if n.get_script() and str(n.get_script().resource_path).contains("asset_dock"):
   return n
 return null
func context() -> Dictionary:
 var scene := EditorInterface.get_edited_scene_root()
 var terrain := terrain_node()
 var camera := EditorInterface.get_editor_viewport_3d(0).get_camera_3d()
 var result := {"scene":scene.scene_file_path,"selection":[], "camera":str(camera.global_position),"camera_forward":str(-camera.global_basis.z),"textures":terrain.assets.get_texture_count()}
 for n in EditorInterface.get_selection().get_selected_nodes(): result.selection.append(str(n.get_path()))
 var dock:=dock_node()
 if dock: result["dock"]={"visible":dock.visible,"initialized":dock.get("_initialized"),"plugin_terrain":str(dock.get("plugin").get("terrain")),"texture_entries":dock.get("texture_list").get("entries").size(),"mesh_entries":dock.get("mesh_list").get("entries").size()}
 return result
func select_terrain() -> Dictionary:
 var terrain:=terrain_node()
 EditorInterface.get_selection().clear()
 EditorInterface.get_selection().add_node(terrain)
 EditorInterface.edit_node(terrain)
 return context()
func audit_path(path: Array) -> Array:
 var terrain:=terrain_node()
 var result: Array=[]
 for segment in range(1,path.size()):
  var from:Vector2=path[segment-1]
  var to:Vector2=path[segment]
  var steps := ceili(from.distance_to(to)/2.0)
  var previous: float=terrain.data.get_height(Vector3(from.x,0,from.y))
  var worst: float=0.0
  var worst_at:=from
  for i in range(1,steps+1):
   var at := from.lerp(to,float(i)/steps)
   var height: float=terrain.data.get_height(Vector3(at.x,0,at.y))
   var slope: float=absf(height-previous)/(from.distance_to(to)/steps)
   if slope>worst: worst=slope; worst_at=at
   previous=height
  result.append({"segment":segment,"slope":worst,"at":[worst_at.x,worst_at.y]})
 return result
func audit_roads() -> Dictionary:
 var layout: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(A+"layout.json"))
 var failures: Array = []
 for name in ["route","side_route","ramps"]:
  var paths: Array = layout[name] if name=="ramps" else [layout[name]]
  for pi in paths.size():
   var path: Array=[]
   for p in paths[pi]: path.append(Vector2(p[0],p[2]))
   for item in audit_path(path):
    if item.slope>.5:
     item["path"]=name; item["branch"]=pi; failures.append(item)
 return {"failures":failures}
# Preserve every height sample and edits outside the two road corridors.
# Work on detached copies, save them, and only then bind the completed revision.
func repair_road() -> Dictionary:
 var terrain:=terrain_node()
 var root:Node3D=terrain.get_parent().get_parent()
 var directory:=A+"Terrain/DataRoadConnected"
 if DirAccess.dir_exists_absolute(ProjectSettings.globalize_path(directory)):
  return {"success":false,"error":"Road repair revision already exists."}
 var layout:Dictionary=JSON.parse_string(FileAccess.get_file_as_string(A+"layout.json"))
 var old_route:Array=[]
 for p in layout.route: old_route.append(Vector2(p[0],p[2]))
 var route:Array=old_route.slice(0,10)
 route.append_array([Vector2(55,-300),Vector2(55,-385),Vector2(110,-420),Vector2(250,-430)])
 var old_tail:Array=old_route.slice(9)
 var new_tail:Array=route.slice(9)
 var paths:Array=[route]
 for name in ["side_route","ramps"]:
  var profiles:Array=layout[name] if name=="ramps" else [layout[name]]
  for profile in profiles:
   var path:Array=[]
   for p in profile: path.append(Vector2(p[0],p[2]))
   paths.append(path)
 var snapshots:Array=[]
 var changed_pixels:=0
 for location in terrain.data.get_region_locations():
  var original:Terrain3DRegion=terrain.data.get_region(location)
  var region:Terrain3DRegion=original.duplicate(true)
  region.set_location(location)
  var colors:Image=original.get_color_map().duplicate()
  var bits:PackedByteArray=original.get_control_map().get_data()
  var size:int=original.get_region_size()
  for iz in size:
   for ix in size:
    var point:=Vector2(location.x*size+ix,location.y*size+iz)*terrain.vertex_spacing
    if minf(distance_to(point,old_tail),distance_to(point,new_tail))>=8.0: continue
    var road:=INF
    for path in paths: road=minf(road,distance_to(point,path))
    var x:float=point.x
    var z:float=point.y
    var slope:float=maxf(absf(terrain.data.get_height(Vector3(x+2,0,z))-terrain.data.get_height(Vector3(x-2,0,z))),absf(terrain.data.get_height(Vector3(x,0,z+2))-terrain.data.get_height(Vector3(x,0,z-2))))/4.0
    var dry:float=smoothstep(.1,.8,sin(x/57.0)*cos(z/71.0)+.4*sin((x+z)/29.0))
    var id:int=2 if slope>1.05 else 1 if road<4.5 else 3 if dry>.7 else 0
    var color:=Color(.40,.51,.27).lerp(Color(.48,.53,.30),dry)
    if id==2: color=Color(.46,.47,.43)
    if road<6.5 and slope<1.05: color=color.lerp(Color(.56,.46,.32),1.0-smoothstep(3.5,6.5,road))
    var wash:float=.022*sin(x/80.0)*cos(z/67.0)+.009*cos((x-z)/29.0)
    colors.set_pixel(ix,iz,Color(color.r+wash,color.g+wash,color.b+wash*.5,.65))
    var offset:int=(iz*size+ix)*4
    bits.encode_u32(offset,(bits.decode_u32(offset)&0x07ffffff)|(id<<27))
    changed_pixels+=1
  colors.generate_mipmaps()
  region.set_color_map(colors)
  region.set_control_map(Image.create_from_data(size,size,false,Image.FORMAT_RF,bits))
  # Height equality is checked before any resource or scene is written.
  if region.get_height_map().get_data()!=original.get_height_map().get_data():
   return {"success":false,"error":"Heightfield changed unexpectedly."}
  region.set_modified(true)
  snapshots.append({"region":region,"file":original.resource_path.get_file()})
 DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(directory))
 var dependencies:Array[Resource]=[]
 for snapshot in snapshots:
  var region:Terrain3DRegion=snapshot.region
  var path:String=directory+"/"+snapshot.file
  var error:int=ResourceSaver.save(region,path,ResourceSaver.FLAG_COMPRESS)
  if error!=OK: return {"success":false,"error":error,"file":path}
  region.take_over_path(path)
  dependencies.append(region)
 terrain.data_directory=directory
 terrain.set_meta("terrain_region_resources",dependencies)
 root.set_meta("surface_source_directory",directory)
 var points:Array=[]
 var metres:=0.0
 for p in route:
  var at:=Vector3(p.x,terrain.data.get_height(Vector3(p.x,0,p.y)),p.y)
  if not points.is_empty():
   var previous:Array=points.back()
   metres+=at.distance_to(Vector3(previous[0],previous[1],previous[2]))
  points.append([at.x,at.y,at.z])
 layout.route=points
 layout.route_metres=metres
 layout.walking_seconds=metres/3.5
 layout["road_repair"]={"id":"quarry_base_bypass","heightfield_preserved":true,"old_tail":old_tail.map(func(p):return [p.x,p.y]),"new_tail":new_tail.map(func(p):return [p.x,p.y])}
 FileAccess.open(A+"layout.json",FileAccess.WRITE).store_string(JSON.stringify(layout,"\t"))
 return {"success":true,"changed_pixels":changed_pixels,"heightfield_preserved":true,"directory":directory,"route_metres":metres,"walking_seconds":metres/3.5}
func distance_to(point:Vector2,path:Array) -> float:
 var best:=INF
 for i in range(path.size()-1):
  var from:Vector2=path[i]
  var delta:Vector2=path[i+1]-from
  best=minf(best,point.distance_to(from+delta*clampf((point-from).dot(delta)/delta.length_squared(),0.0,1.0)))
 return best
func record_road_samples() -> Dictionary:
 var terrain:=terrain_node()
 var layout:Dictionary=JSON.parse_string(FileAccess.get_file_as_string(A+"layout.json"))
 var profiles:Array=[]
 var count:=0
 var unpainted:Array=[]
 for name in ["route","side_route","ramps"]:
  var paths:Array=layout[name] if name=="ramps" else [layout[name]]
  for branch in paths.size():
   var path:Array=paths[branch]
   var samples:Array=[]
   for segment in range(1,path.size()):
    var from:=Vector2(path[segment-1][0],path[segment-1][2])
    var to:=Vector2(path[segment][0],path[segment][2])
    var steps:=ceili(from.distance_to(to)/2.0)
    for i in range(0,steps+1):
     if segment>1 and i==0: continue
     var at:=from.lerp(to,float(i)/steps)
     var position:=Vector3(at.x,0,at.y)
     position.y=terrain.data.get_height(position)
     var region:Terrain3DRegion=terrain.data.get_regionp(position)
     var location:Vector2i=region.get_location()
     var pixel:Vector2i=Vector2i((at/terrain.vertex_spacing).round())-location*region.get_region_size()
     pixel=pixel.clamp(Vector2i.ZERO,Vector2i.ONE*(region.get_region_size()-1))
     var bits:PackedByteArray=region.get_control_map().get_data()
     var id:int=bits.decode_u32((pixel.y*region.get_region_size()+pixel.x)*4)>>27
     if id!=1: unpainted.append([name,branch,at.x,at.y,id])
     samples.append([position.x,position.y,position.z])
     count+=1
   profiles.append({"path":name,"branch":branch,"samples":samples})
 layout["road_samples"]=profiles
 FileAccess.open(A+"layout.json",FileAccess.WRITE).store_string(JSON.stringify(layout,"\t"))
 return {"samples":count,"unpainted":unpainted}
func native_regions() -> Array:
 var result: Array=[]
 for m in terrain_node().data.get_method_list():
  if "region" in m.name: result.append({"name":m.name,"args":m.args})
 return result
func audit_candidate() -> Array:
 return audit_path([Vector2(40,-240),Vector2(55,-300),Vector2(55,-385),Vector2(110,-420),Vector2(250,-430)])
