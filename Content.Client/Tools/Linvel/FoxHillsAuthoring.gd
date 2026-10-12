@tool
extends Node
class_name FoxHillsAuthoring
const A := "res://Assets/World/Linvel/FoxHills/"
const T := A+"Terrain/"
var root: Node3D
var route := [Vector2(-350,430),Vector2(-290,330),Vector2(-230,255),Vector2(-150,255),Vector2(0,170),Vector2(40,150),Vector2(40,45),Vector2(40,-40),Vector2(40,-130),Vector2(40,-240),Vector2(55,-300),Vector2(55,-385),Vector2(110,-420),Vector2(250,-430)]
var side_route := [Vector2(440,150),Vector2(340,145),Vector2(270,90),Vector2(150,90),Vector2(40,150)]
# Elliptical shelves have visible steep flanks, with named ramp directions.
var hills := [
 {"id":"WatchHill","x":40.0,"z":-40.0,"rx":92.0,"rz":88.0,"height":42.0,"ramps":[PI/2.0,-PI/2.0]},
 {"id":"OldFarmHill","x":-230.0,"z":110.0,"rx":77.0,"rz":66.0,"height":22.0,"ramps":[PI/2.0]},
 {"id":"HeatherCrest","x":280.0,"z":-80.0,"rx":89.0,"rz":75.0,"height":29.0,"ramps":[PI/2.0]},
 {"id":"WesternCrown","x":-310.0,"z":-270.0,"rx":83.0,"rz":88.0,"height":32.0,"ramps":[0.0]},
 {"id":"QuarryShoulder","x":260.0,"z":-300.0,"rx":92.0,"rz":78.0,"height":36.0,"ramps":[PI]}
]
func base_height(x: float,z: float) -> float:
 return 18.0+(480.0-z)*.025+5.0*sin(x/220.0)*cos(z/260.0)+1.8*cos((x+z)/170.0)
func height_at(x: float,z: float) -> float:
 var value: float = base_height(x,z)
 for h in hills:
  var dx: float = x-h.x
  var dz: float = z-h.z
  var q: float = sqrt(pow(dx/h.rx,2)+pow(dz/h.rz,2))
  var theta: float = atan2(dz/h.rz,dx/h.rx)
  var edge: float = q*(1.0+.065*sin(theta*3.0+h.x/31.0)+.035*cos(theta*7.0+h.z/47.0))
  var factor: float = 1.0-smoothstep(.96,1.045,edge)
  for angle in h.ramps:
   var along: float = dx*cos(float(angle))+dz*sin(float(angle))
   var across: float = absf(-dx*sin(float(angle))+dz*cos(float(angle)))
   if along>0.0:
    var corridor: float = 1.0-smoothstep(24.0,34.0,across)
    var ramp: float = 1.0-smoothstep(.40,2.10,q)
    factor = lerpf(factor,ramp,corridor)
  value += h.height*factor
 return value
func distance_to(point: Vector2,path: Array) -> float:
 var best: float = INF
 for i in range(path.size()-1):
  var a: Vector2 = path[i]
  var delta: Vector2 = path[i+1]-a
  best = minf(best,point.distance_to(a+delta*clampf((point-a).dot(delta)/delta.length_squared(),0.0,1.0)))
 return best
func add(parent: Node,node: Node,label: String) -> Node:
 node.name = label
 parent.add_child(node,true)
 node.owner = root
 return node
func tile(id: int,label: String) -> Terrain3DTextureAsset:
 var image := Image.create(256,256,false,Image.FORMAT_RGBA8)
 var normal := Image.create(256,256,false,Image.FORMAT_RGBA8)
 var palette := [Color(.84,.97,.78),Color(.94,.87,.73),Color(.88,.88,.82),Color(.93,.91,.82)]
 for z in range(256):
  for x in range(256):
   var wash: float = .95+.025*sin(TAU*x/256.0)*cos(TAU*z/256.0)+.018*sin(TAU*(3.0*x+2.0*z)/256.0)+.008*sin(TAU*(13.0*x-19.0*z)/256.0)
   image.set_pixel(x,z,Color(palette[id].r*wash,palette[id].g*wash,palette[id].b*wash,.65))
   normal.set_pixel(x,z,Color(.5,.5,1,1))
 image.generate_mipmaps()
 normal.generate_mipmaps()
 var albedo := ImageTexture.create_from_image(image)
 var normals := ImageTexture.create_from_image(normal)
 ResourceSaver.save(albedo,T+"Textures/"+label+"_albedo.res",ResourceSaver.FLAG_COMPRESS)
 albedo.take_over_path(T+"Textures/"+label+"_albedo.res")
 ResourceSaver.save(normals,T+"Textures/"+label+"_normal.res",ResourceSaver.FLAG_COMPRESS)
 normals.take_over_path(T+"Textures/"+label+"_normal.res")
 var result := Terrain3DTextureAsset.new()
 result.id = id
 result.name = label
 result.albedo_texture = albedo
 result.normal_texture = normals
 result.uv_scale = .12
 result.normal_depth = 0.0
 result.ao_strength = 0.0
 return result
func build_terrain() -> Dictionary:
 root = get_parent() as Node3D
 if root.has_node("Geometry"):
  return {"success":false,"error":"Existing terrain is preserved; edit it using Terrain3D brushes."}
 DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(T+"Data"))
 DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(T+"Textures"))
 root.add_to_group("surface_geometry",true)
 root.set_meta("region_id","linvel_fox_hills")
 root.set_meta("surface_source_directory",T+"Data")
 root.set_meta("surface_cell_size",1.0)
 root.set_meta("surface_max_climb",.5)
 root.set_meta("surface_cell_height",.05)
 root.set_meta("surface_detail_distance",12.0)
 root.set_meta("surface_detail_error",2.0)
 root.set_meta("controlled_hill_access",true)
 root.set_meta("surface_solid_terrain_los",true)
 var geometry := Node3D.new()
 add(root,geometry,"Geometry")
 var terrain := Terrain3D.new()
 terrain.vertex_spacing = 2.0
 terrain.mesh_size = 48
 terrain.mesh_lods = 6
 add(geometry,terrain,"Terrain")
 terrain.change_region_size(Terrain3D.SIZE_256)
 terrain.collision.mode = Terrain3DCollision.FULL_GAME
 terrain.collision.layer = 1
 terrain.add_to_group("surface_terrain",true)
 var assets := Terrain3DAssets.new()
 for spec in [[0,"HillMeadow"],[1,"CartRoad"],[2,"CliffStone"],[3,"DryGrass"]]:
  assets.set_texture(spec[0],tile(spec[0],spec[1]))
 terrain.assets = assets
 var material := Terrain3DMaterial.new()
 material.world_background = Terrain3DMaterial.NONE
 material.auto_shader = false
 material.dual_scaling = false
 material.shader_override = load("res://Assets/World/Linvel/Gardens/Terrain/painted_terrain.gdshader")
 material.shader_override_enabled = true
 material.set_shader_param("enable_macro_variation",false)
 material.set_shader_param("blend_sharpness",.35)
 terrain.material = material
 var heights := Image.create(512,512,false,Image.FORMAT_RF)
 var colors := Image.create(512,512,false,Image.FORMAT_RGBA8)
 var ids := PackedInt32Array()
 ids.resize(512*512)
 for iz in range(512):
  for ix in range(512):
   var x: float = -512.0+ix*2.0
   var z: float = -512.0+iz*2.0
   var y: float = height_at(x,z)
   var slope: float = maxf(absf(height_at(x+2,z)-height_at(x-2,z)),absf(height_at(x,z+2)-height_at(x,z-2)))/4.0
   var road: float = minf(distance_to(Vector2(x,z),route),distance_to(Vector2(x,z),side_route))
   var id: int = 2 if slope>1.05 else 1 if road<5.0 else 3 if sin(x/43.0+z/63.0)>.6 else 0
   ids[iz*512+ix] = id
   var broad: float = .045*sin(x/80.0)*cos(z/67.0)+.022*sin((x-z)/29.0)
   var color: Color = [Color(.46,.64,.33),Color(.66,.53,.34),Color(.58,.58,.51),Color(.60,.63,.35)][id]
   colors.set_pixel(ix,iz,Color(color.r+broad,color.g+broad,color.b+broad*.5,.65))
   heights.set_pixel(ix,iz,Color(y,0,0,1))
 terrain.data.import_images([heights,null,colors],Vector3(-512,0,-512),0.0,1.0)
 for iz in range(512):
  for ix in range(512):
   var p := Vector3(-512.0+ix*2.0,0,-512.0+iz*2.0)
   terrain.data.set_control_auto(p,false)
   terrain.data.set_control_navigation(p,true)
   terrain.data.set_control_base_id(p,ids[iz*512+ix])
   if absf(p.x)>480 or absf(p.z)>480:
    terrain.data.set_control_hole(p,true)
 terrain.data.update_maps(Terrain3DRegion.TYPE_MAX,true,true)
 terrain.data.save_directory(T+"Data")
 terrain.data_directory = T+"Data"
 ResourceSaver.save(assets,T+"fox_assets.tres")
 assets.take_over_path(T+"fox_assets.tres")
 ResourceSaver.save(material,T+"fox_material.tres")
 material.take_over_path(T+"fox_material.tres")
 var dependencies: Array[Resource] = []
 for file in DirAccess.get_files_at(T+"Data"):
  if file.ends_with(".res"):
   dependencies.append(load(T+"Data/"+file))
 terrain.set_meta("terrain_region_resources",dependencies)
 var places := Node3D.new()
 add(root,places,"Places")
 for spec in [["LowerPasture","Нижние пастбища",-310.0,310.0],["OldFarm","Заброшенное хозяйство",-230.0,105.0],["WatchHill","Сторожевой холм",40.0,-40.0],["HeatherHollow","Вересковая ложбина",270.0,90.0],["QuarryApproach","Дальний склон",150.0,-340.0]]:
  var place := Node3D.new()
  add(places,place,spec[0])
  place.set_meta("display_name",spec[1])
  var center := Marker3D.new()
  add(place,center,"Center")
  center.position = Vector3(spec[2],height_at(spec[2],spec[3]),spec[3])
 var points: Array = []
 var total: float = 0.0
 for i in range(route.size()):
  var p: Vector2 = route[i]
  var at := Vector3(p.x,height_at(p.x,p.y),p.y)
  points.append([at.x,at.y,at.z])
  if i>0:
   var previous := Vector3(points[i-1][0],points[i-1][1],points[i-1][2])
   total += previous.distance_to(at)
 var file := FileAccess.open(A+"layout.json",FileAccess.WRITE)
 file.store_string(JSON.stringify({"route":points,"side_route":side_route.map(func(p):return [p.x,height_at(p.x,p.y),p.y]),"hills":hills,"route_metres":total,"walking_seconds":total/3.5},"\t"))
 atmosphere()
 return {"success":true,"regions":terrain.data.get_region_count(),"height_range":terrain.data.get_height_range(),"route_metres":total,"minutes":total/210.0}
func atmosphere() -> void:
 var world := WorldEnvironment.new()
 var environment := Environment.new()
 environment.background_mode = Environment.BG_SKY
 var sky := Sky.new()
 var sky_mat := ProceduralSkyMaterial.new()
 sky_mat.sky_top_color = Color(.28,.49,.73)
 sky_mat.sky_horizon_color = Color(.78,.87,.91)
 sky_mat.ground_horizon_color = Color(.77,.83,.75)
 sky_mat.ground_bottom_color = Color(.30,.38,.29)
 sky.sky_material = sky_mat
 environment.sky = sky
 environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
 environment.ambient_light_color = Color(.70,.78,.85)
 environment.ambient_light_energy = .6
 environment.tonemap_mode = Environment.TONE_MAPPER_FILMIC
 environment.fog_enabled = true
 environment.fog_light_color = Color(.73,.81,.79)
 environment.fog_density = .00035
 world.environment = environment
 add(root,world,"HillAtmosphere")
 var sun := DirectionalLight3D.new()
 add(root,sun,"AfternoonSun")
 sun.rotation_degrees = Vector3(-50,-30,0)
 sun.light_color = Color(1.0,.93,.81)
 sun.light_energy = 1.25
 sun.shadow_enabled = true
 sun.directional_shadow_max_distance = 150.0
var meshes: Dictionary = {}
var painted: ShaderMaterial
func import_models() -> Dictionary:
 root = get_parent() as Node3D
 DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(A+"Meshes"))
 if painted==null and FileAccess.file_exists(A+"painted_hills.tres"):
  painted=load(A+"painted_hills.tres")
 var catalog: Array = []
 var file := FileAccess.open("D:/projects/project-g/art-source/linvel/fox-hills/catalog.json",FileAccess.READ)
 catalog = JSON.parse_string(file.get_as_text())
 for item in catalog:
  var source: Node3D = load(A+item.id+".glb").instantiate()
  var visual: MeshInstance3D = source.find_children("*","MeshInstance3D",true,false)[0]
  var mesh: Mesh = visual.mesh
  var path: String = A+"Meshes/"+item.id+".res"
  ResourceSaver.save(mesh,path,ResourceSaver.FLAG_COMPRESS)
  mesh.take_over_path(path)
  meshes[item.id] = mesh
  if painted==null:
   painted = load("res://Assets/World/Linvel/Gardens/painted_gardens.tres").duplicate(true)
   painted.set_shader_parameter("painted_atlas",visual.get_active_material(0).albedo_texture)
   ResourceSaver.save(painted,A+"painted_hills.tres")
   painted.take_over_path(A+"painted_hills.tres")
  source.free()
 for id in ["fieldstone_wall","handcart","farm_well","garden_boulder_a","meadow_patch"]:
  meshes[id] = load("res://Assets/World/Linvel/Gardens/Meshes/"+id+".res")
 return {"models":meshes.size()}
func asset(parent: Node,id: String,p: Vector2,yaw: float=0.0,size: float=1.0,label: String="") -> Node3D:
 var placement := Node3D.new()
 add(parent,placement,id if label.is_empty() else label)
 placement.position = Vector3(p.x,ground(p),p.y)
 placement.rotation.y = yaw
 placement.set_meta("asset_id",id)
 placement.set_meta("fox_asset_root",true)
 var visual := MeshInstance3D.new()
 add(placement,visual,"Visual")
 visual.mesh = meshes[id]
 visual.material_override = painted
 visual.scale = Vector3.ONE*size
 visual.visibility_range_end = 230.0 if id in ["hill_watchpost","ruined_homestead","shepherd_shelter"] else 170.0
 visual.visibility_range_end_margin = 24.0
 return placement
func ground(p: Vector2) -> float:
 var terrain: Terrain3D = root.get_node("Geometry/Terrain")
 return terrain.data.get_height(Vector3(p.x,0,p.y))
func box(placement: Node3D,label: String,offset: Vector3,size: Vector3,opaque: bool=true) -> void:
 var body := StaticBody3D.new()
 add(placement,body,label)
 body.position = offset
 body.collision_layer = 4
 body.add_to_group("surface_geometry",true)
 if opaque: body.add_to_group("surface_occluder",true)
 body.set_meta("surface_nonwalkable_top",true)
 body.set_meta("collision_part","structure")
 var collision := CollisionShape3D.new()
 var shape := BoxShape3D.new()
 shape.size = size
 collision.shape = shape
 add(body,collision,"Collision")
func build_objects() -> Dictionary:
 root = get_parent() as Node3D
 if root.has_node("DetailPlacements"): return {"success":false,"error":"Existing placements are preserved."}
 root.add_to_group("surface_geometry",true)
 import_models()
 var details := Node3D.new()
 add(root,details,"DetailPlacements")
 var pasture: Node3D = root.get_node("Places/LowerPasture")
 var shelter := asset(pasture,"shepherd_shelter",Vector2(-335,300),.2,1.0,"ShepherdShelter")
 for x in [-2.5,2.5]:
  for z in [-1.8,1.8]:
   box(shelter,"PostBody",Vector3(x,1.6,-z),Vector3(.28,3.1,.28))
 asset(pasture,"farm_well",Vector2(-326,315),0,1.0,"PastureWell")
 asset(pasture,"handcart",Vector2(-333,291),1.1,1.0,"Cart")
 asset(pasture,"pasture_gate",Vector2(-302,301),.6,1.0,"Gate")
 var watch: Node3D = root.get_node("Places/WatchHill")
 var tower := asset(watch,"hill_watchpost",Vector2(59,-42),.08,1.0,"Watchpost")
 box(tower,"BuildingBody",Vector3(0,3.2,0),Vector3(5.65,6.0,4.95))
 asset(watch,"survey_waymarker",Vector2(28,-36),.6,1.0,"Waymarker")
 asset(watch,"rock_shelf",Vector2(72,-17),.8,1.0,"LookoutStone")
 var farm: Node3D = root.get_node("Places/OldFarm")
 var ruin := asset(farm,"ruined_homestead",Vector2(-234,101),.2,1.0,"OldHouse")
 box(ruin,"WestWallBody",Vector3(-3.85,1.0,0),Vector3(.45,1.8,6.0))
 box(ruin,"EastWallBody",Vector3(3.85,1.0,0),Vector3(.45,1.8,6.0))
 box(ruin,"BackWallBody",Vector3(0,1.3,-3.05),Vector3(8.0,2.3,.45))
 asset(farm,"handcart",Vector2(-221,116),1.4,1.0,"AbandonedCart")
 asset(farm,"pasture_gate",Vector2(-235,122),.2,1.0,"FallenGate")
 var hollow: Node3D = root.get_node("Places/HeatherHollow")
 asset(hollow,"rock_spire",Vector2(293,92),.3,1.0,"HollowStone")
 asset(hollow,"survey_waymarker",Vector2(249,84),.4,1.0,"ForestSign")
 var quarry: Node3D = root.get_node("Places/QuarryApproach")
 asset(quarry,"survey_waymarker",Vector2(148,-344),-.8,1.0,"QuarrySign")
 asset(quarry,"handcart",Vector2(169,-353),1.9,1.0,"StoneCart")
 var rng := RandomNumberGenerator.new()
 rng.seed = 684713
 var rock_count := 0
 # A few grouped outcrops decorate native cliffs; the terrain remains editable.
 for h in hills:
  for i in range(14):
   var angle: float = float(i)*TAU/14.0
   var skip := false
   for a in h.ramps:
    if absf(wrapf(angle-float(a),-PI,PI))<.30: skip=true
   if skip: continue
   var p := Vector2(h.x+cos(angle)*h.rx*.90,h.z+sin(angle)*h.rz*.90)
   if road_distance(p)<10: continue
   asset(details,"rock_shelf",p,angle,1.6,"CliffOutcrop")
   rock_count += 1
 for i in range(55):
  var p := Vector2(rng.randf_range(-440,440),rng.randf_range(-440,440))
  if road_distance(p)<12: continue
  var gx: float = absf(ground(p+Vector2(3,0))-ground(p-Vector2(3,0)))/6.0
  if gx>.8: continue
  var stone := asset(details,"hill_boulder",p,rng.randf_range(-PI,PI),1.0,"Boulder")
  box(stone,"RockBody",Vector3(0,.85,0),Vector3(1.8,1.6,1.45))
 # Short, irregular wall runs frame places, with deliberate gaps at paths.
 for spec in [[-355.0,325.0,14,0.0],[-320.0,275.0,9,0.0],[-248.0,120.0,7,0.0],[75.0,-72.0,7,0.0],[142.0,-325.0,8,.4]]:
  for i in range(spec[2]):
   var p := Vector2(spec[0]+i*4.0,spec[1])
   if road_distance(p)<8: continue
   var wall := asset(details,"fieldstone_wall",p,spec[3],1.0,"StoneWall")
   box(wall,"WallBody",Vector3(0,.52,0),Vector3(3.9,1.0,.52),false)
 scatter()
 return {"success":true,"outcrops":rock_count,"placements":root.find_children("Visual","MeshInstance3D",true,false).size(),"bodies":root.find_children("*","StaticBody3D",true,false).size()}
func scatter() -> void:
 var vegetation := Node3D.new()
 add(root,vegetation,"Vegetation")
 var rng := RandomNumberGenerator.new()
 rng.seed = 684712
 var buckets: Dictionary = {}
 for i in range(78000):
  var p := Vector2(rng.randf_range(-470,470),rng.randf_range(-470,470))
  if road_distance(p)<7.5: continue
  if p.distance_to(Vector2(59,-42))<10 or p.distance_to(Vector2(-234,101))<9: continue
  var y: float = ground(p)
  var sx: float = absf(ground(p+Vector2(3,0))-ground(p-Vector2(3,0)))/6
  var sz: float = absf(ground(p+Vector2(0,3))-ground(p-Vector2(0,3)))/6
  if is_nan(y) or maxf(sx,sz)>.6: continue
  var id: String = "drygrass_tuft" if i%5!=0 else "heather_patch" if sin(p.x/58.0+p.y/73.0)>.2 else "heather_dry"
  var cell := Vector2i(floori(p.x/64),floori(p.y/64))
  var key := id+"_"+str(cell.x)+"_"+str(cell.y)
  if not buckets.has(key): buckets[key]={"id":id,"points":[],"cell":cell}
  buckets[key].points.append(Vector3(p.x,y,p.y))
 for key in buckets:
  var bucket: Dictionary = buckets[key]
  var multimesh := MultiMesh.new()
  multimesh.transform_format = MultiMesh.TRANSFORM_3D
  multimesh.mesh = meshes[bucket.id]
  multimesh.instance_count = bucket.points.size()
  var node := MultiMeshInstance3D.new()
  add(vegetation,node,key)
  node.multimesh = multimesh
  node.material_override = painted
  node.visibility_range_end = 115
  node.visibility_range_end_margin = 18
  node.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
  node.position = Vector3(bucket.cell.x*64,0,bucket.cell.y*64)
  for i in range(bucket.points.size()):
   var basis := Basis(Vector3.UP,rng.randf_range(-PI,PI)).scaled(Vector3.ONE*rng.randf_range(.7,1.3))
   multimesh.set_instance_transform(i,Transform3D(basis,bucket.points[i]-node.position))


var access_paths: Array = []
func prepare_access() -> void:
 if not access_paths.is_empty(): return
 for h in hills:
  for angle in h.ramps:
   var axis := Vector2(cos(float(angle)),sin(float(angle)))
   var radius: float = 1.0/sqrt(pow(axis.x/h.rx,2)+pow(axis.y/h.rz,2))
   var center := Vector2(h.x,h.z)
   access_paths.append([center+axis*radius*.35,center+axis*radius*2.22])
func road_distance(p: Vector2) -> float:
 prepare_access()
 var value: float = minf(distance_to(p,route),distance_to(p,side_route))
 for path in access_paths:
  value = minf(value,distance_to(p,path))
 return value
func finish_access() -> Dictionary:
 root = get_parent() as Node3D
 prepare_access()
 # This method records entrances and replaces only this tool's vegetation.
 # Native height/control maps are saved by refine_native_data before binding.
 var probes: Array = []
 for h in hills:
  var angle: float = 0.0 if h.id in ["WatchHill","OldFarmHill","HeatherCrest"] else -PI/2.0
  var axis := Vector2(cos(angle),sin(angle))
  var radius: float = 1.0/sqrt(pow(axis.x/h.rx,2)+pow(axis.y/h.rz,2))
  var center := Vector2(h.x,h.z)
  var ramp_axis := Vector2(cos(float(h.ramps[0])),sin(float(h.ramps[0])))
  var rr: float = 1.0/sqrt(pow(ramp_axis.x/h.rx,2)+pow(ramp_axis.y/h.rz,2))
  var points: Dictionary = {"blocked_from":center+axis*radius*1.30,"blocked_to":center+axis*radius*.73,
   "ramp_bottom":center+ramp_axis*rr*2.24,"ramp_middle":center+ramp_axis*rr*1.15,"top":center}
  var entry: Dictionary = {"id":h.id}
  for key in points:
   var p: Vector2 = points[key]
   entry[key] = [p.x,ground(p),p.y]
  probes.append(entry)
 var layout: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(A+"layout.json"))
 layout["access_probes"] = probes
 layout["ramps"] = access_paths.map(func(path):return path.map(func(p):return [p.x,ground(p),p.y]))
 FileAccess.open(A+"layout.json",FileAccess.WRITE).store_string(JSON.stringify(layout,"\t"))
 var vegetation := root.get_node_or_null("Vegetation")
 if vegetation!=null:
  root.remove_child(vegetation)
  vegetation.free()
 scatter()
 return {"success":true,"hills":probes.size(),"ramps":access_paths.size()}


# Bake a new native data revision before binding it. Existing map resources and
# editor-owned Terrain3D buffers are never mutated while rendering.
func refine_native_data() -> Dictionary:
 root = get_parent() as Node3D
 var directory := T+"DataFinalPlayable"
 if DirAccess.dir_exists_absolute(ProjectSettings.globalize_path(directory)):
  return {"success":false,"error":"Refined data already exists; use Terrain3D brushes for later edits."}
 DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(directory))
 var dependencies: Array[Resource] = []
 for file in DirAccess.get_files_at(T+"Data"):
  if not file.ends_with(".res"): continue
  var region: Terrain3DRegion = load(T+"Data/"+file).duplicate(true)
  var encoded: String = file.trim_prefix("terrain3d").trim_suffix(".res")
  var loc := Vector2i(encoded.substr(0,3).replace("_","").to_int(),encoded.substr(3,3).replace("_","").to_int())
  region.set_location(loc)
  var heights := Image.create(256,256,false,Image.FORMAT_RF)
  var colors := Image.create(256,256,false,Image.FORMAT_RGBA8)
  var bits := PackedByteArray()
  bits.resize(256*256*4)
  for iz in range(256):
   for ix in range(256):
    var x: float = (loc.x*256+ix)*2.0
    var z: float = (loc.y*256+iz)*2.0
    var slope: float = maxf(absf(height_at(x+2,z)-height_at(x-2,z)),absf(height_at(x,z+2)-height_at(x,z-2)))/4.0
    var road: float = road_distance(Vector2(x,z))
    var dry: float = smoothstep(.1,.8,sin(x/57.0)*cos(z/71.0)+.4*sin((x+z)/29.0))
    var id: int = 2 if slope>1.05 else 1 if road<4.5 else 3 if dry>.7 else 0
    var color := Color(.40,.51,.27).lerp(Color(.48,.53,.30),dry)
    if id==2: color=Color(.46,.47,.43)
    if road<6.5 and slope<1.05: color=color.lerp(Color(.56,.46,.32),1.0-smoothstep(3.5,6.5,road))
    var wash: float = .022*sin(x/80.0)*cos(z/67.0)+.009*cos((x-z)/29.0)
    colors.set_pixel(ix,iz,Color(color.r+wash,color.g+wash,color.b+wash*.5,.65))
    heights.set_pixel(ix,iz,Color(height_at(x,z),0,0,1))
    var control: int = (id<<27)|2
    if absf(x)>480 or absf(z)>480: control|=4
    bits.encode_u32((iz*256+ix)*4,control)
  colors.generate_mipmaps()
  region.set_height_map(heights)
  region.set_color_map(colors)
  region.set_control_map(Image.create_from_data(256,256,false,Image.FORMAT_RF,bits))
  region.calc_height_range()
  region.set_modified(true)
  var path: String = directory+"/"+file
  var error: int = ResourceSaver.save(region,path,ResourceSaver.FLAG_COMPRESS)
  if error!=OK: return {"success":false,"error":error}
  region.take_over_path(path)
  dependencies.append(region)
 var terrain: Terrain3D = root.get_node("Geometry/Terrain")
 terrain.data_directory = directory
 terrain.set_meta("terrain_region_resources",dependencies)
 root.set_meta("surface_source_directory",directory)
 # Relocate our existing placement roots to the refined native heightfield.
 for n in root.find_children("*","Node3D",true,false):
  if n.has_meta("fox_asset_root"):
   var p: Vector3 = n.position
   n.position.y = ground(Vector2(p.x,p.z))
 for place in root.get_node("Places").get_children():
  var center: Marker3D = place.get_node("Center")
  center.position.y = ground(Vector2(center.position.x,center.position.z))
 import_models()
 for spec in [[root.get_node("Places/LowerPasture/PastureWell"),"farm_well"],[root.get_node("Places/LowerPasture/Cart"),"handcart"],[root.get_node("Places/OldFarm/AbandonedCart"),"handcart"],[root.get_node("Places/QuarryApproach/StoneCart"),"handcart"]]:
  spec[0].get_node("Visual").mesh = meshes[spec[1]]
 return finish_access()


func finish_details() -> Dictionary:
 root=get_parent() as Node3D
 import_models()
 var parent := root.get_node("DetailPlacements")
 if parent.has_node("AtmosphericDetails"): return {"success":false,"error":"Details are already authored."}
 var details := Node3D.new()
 add(parent,details,"AtmosphericDetails")
 for center in [Vector2(-330,304),Vector2(-230,105),Vector2(61,-45),Vector2(275,88),Vector2(155,-340)]:
  for i in range(14):
   var angle: float = i*TAU/14.0
   var p: Vector2 = center+Vector2(cos(angle),sin(angle))*(13.0+float(i%3)*3)
   if road_distance(p)<7: continue
   asset(details,"thorn_bush",p,angle,.8+float(i%3)*.2,"Thorn")
  for i in range(38):
   var p: Vector2 = center+Vector2(sin(i*2.4),cos(i*3.8))*(15.0+float(i%7)*3)
   if road_distance(p)<7 or p.distance_to(Vector2(59,-42))<9 or p.distance_to(Vector2(-234,101))<8: continue
   asset(details,"heather_patch",p,float(i),1.1,"HeatherDrift")
 for p in [Vector2(-319,312),Vector2(-231,132),Vector2(55,-48),Vector2(292,105),Vector2(146,-332)]:
  asset(details,"hill_boulder",p,.7,.7,"PlaceStone")
 var tower := root.get_node("Places/WatchHill/Watchpost")
 tower.position.y-=.16
 # Existing low walls remain independent editable pieces, with their own shapes.
 for n in root.find_children("Visual","MeshInstance3D",true,false):
  if n.get_parent().get_meta("asset_id","")=="handcart":
   box(n.get_parent(),"CartBody",Vector3(0,.75,0),Vector3(1.1,1.3,1.75))
 var well := root.get_node("Places/LowerPasture/PastureWell")
 box(well,"WellBody",Vector3(0,.55,0),Vector3(1.4,1.0,1.4))
 var terrain: Terrain3D = root.get_node("Geometry/Terrain")
 var layout: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(A+"layout.json"))
 var total: float=0.0
 var previous := Vector3.ZERO
 for i in range(layout.route.size()):
  var row: Array=layout.route[i]
  row[1]=ground(Vector2(row[0],row[2]))
  var point:=Vector3(row[0],row[1],row[2])
  if i>0: total+=previous.distance_to(point)
  previous=point
 for row in layout.side_route:
  row[1]=ground(Vector2(row[0],row[2]))
 layout.route_metres=total
 layout.walking_seconds=total/3.5
 FileAccess.open(A+"layout.json",FileAccess.WRITE).store_string(JSON.stringify(layout,"\t"))
 return {"success":true,"route_metres":total,"minutes":total/210.0}
func write_route_profile() -> Dictionary:
 root=get_parent() as Node3D
 var layout: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(A+"layout.json"))
 var total: float=0.0
 var points: Array=[]
 var previous:=Vector3.ZERO
 for p in route:
  var at:=Vector3(p.x,ground(p),p.y)
  assert(at.is_finite())
  points.append([at.x,at.y,at.z])
  if points.size()>1: total+=previous.distance_to(at)
  previous=at
 layout.route=points
 layout.side_route=side_route.map(func(p):return [p.x,ground(p),p.y])
 layout.route_metres=total
 layout.walking_seconds=total/3.5
 FileAccess.open(A+"layout.json",FileAccess.WRITE).store_string(JSON.stringify(layout,"\t"))
 return {"success":true,"route_metres":total,"minutes":total/210.0,"entry_height":ground(Vector2(-350,420)),"exit_height":ground(Vector2(-350,430)),"forest_arrival_height":ground(Vector2(430,150)),"forest_exit_height":ground(Vector2(440,150))}


func author_connections() -> Dictionary:
 root=get_parent() as Node3D
 if root.has_node("Connections"): return {"success":false,"error":"Connections already authored."}
 import_models()
 var parent:=Node3D.new()
 add(root,parent,"Connections")
 for spec in [["GardenExit",-350.0,430.0,"linvel_gardens"],["ForestExit",440.0,150.0,"linvel_beech"],["QuarryRoadEnd",250.0,-430.0,"future_quarries"]]:
  var marker:=Marker3D.new()
  add(parent,marker,spec[0])
  marker.position=Vector3(spec[1],ground(Vector2(spec[1],spec[2])),spec[2])
  marker.set_meta("destination_region",spec[3])
  asset(parent,"survey_waymarker",Vector2(spec[1]+5,spec[2]+3),.3,1.0,spec[0]+"Sign")
 return {"success":true,"markers":3}


func inspect_baked_height() -> Array:
 root=get_parent() as Node3D
 var terrain:Terrain3D=root.get_node("Geometry/Terrain")
 var faces:PackedVector3Array=terrain.bake_mesh(1,0).get_faces()
 var result:Array=[]
 for p in [Vector2(-230,185.9),Vector2(40,130),Vector2(40,-210),Vector2(-200,210)]:
  var y:float=NAN
  for i in range(0,faces.size(),3):
   var a:Vector3=faces[i]
   var b:Vector3=faces[i+1]
   var c:Vector3=faces[i+2]
   if p.x<minf(a.x,minf(b.x,c.x)) or p.x>maxf(a.x,maxf(b.x,c.x)) or p.y<minf(a.z,minf(b.z,c.z)) or p.y>maxf(a.z,maxf(b.z,c.z)):continue
   var d:float=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z)
   if absf(d)<.00001:continue
   var u:float=((b.z-c.z)*(p.x-c.x)+(c.x-b.x)*(p.y-c.z))/d
   var v:float=((c.z-a.z)*(p.x-c.x)+(a.x-c.x)*(p.y-c.z))/d
   if u>=0 and v>=0 and u+v<=1:
    y=u*a.y+v*b.y+(1-u-v)*c.y
    break
  result.append({"x":p.x,"z":p.y,"native":ground(p),"baked":y})
 return result



func record_access_edges() -> Dictionary:
 root = get_parent() as Node3D
 var probes: Array = []
 for h in hills:
  var center := Vector2(h.x,h.z)
  for index in range(16):
   var angle: float = TAU*index/16.0
   var entrance := false
   for ramp in h.ramps:
    if absf(angle_difference(angle,float(ramp)))<.65: entrance=true
   if entrance: continue
   var axis := Vector2(cos(angle),sin(angle))
   var radius: float = 1.0/sqrt(pow(axis.x/h.rx,2)+pow(axis.y/h.rz,2))
   var outside := center+axis*radius*1.30
   var inside := center+axis*radius*.63
   probes.append({"id":str(h.id)+"/"+str(index),"from":[outside.x,ground(outside),outside.y],"to":[inside.x,ground(inside),inside.y]})
 var layout: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(A+"layout.json"))
 layout["access_edges"]=probes
 FileAccess.open(A+"layout.json",FileAccess.WRITE).store_string(JSON.stringify(layout,"\t"))
 return {"success":true,"cliff_faces":probes.size()}

func finalize_art_and_routes() -> Dictionary:
 root=get_parent() as Node3D
 var terrain:Terrain3D=root.get_node("Geometry/Terrain")
 for spec in [[0,"HillMeadow"],[1,"CartRoad"],[2,"CliffStone"],[3,"DryGrass"]]:
  terrain.assets.set_texture(spec[0],tile(spec[0],spec[1]))
 ResourceSaver.save(terrain.assets,T+"fox_assets.tres")
 var material:ShaderMaterial=load(A+"painted_hills.tres")
 for file in DirAccess.get_files_at(A+"Meshes"):
  if not file.ends_with(".res"):continue
  var mesh:Mesh=load(A+"Meshes/"+file)
  for i in range(mesh.get_surface_count()):mesh.surface_set_material(i,material)
  ResourceSaver.save(mesh,A+"Meshes/"+file,ResourceSaver.FLAG_COMPRESS)
 for marker in root.get_node("Connections").get_children():
  if marker is Marker3D:marker.position.y=ground(Vector2(marker.position.x,marker.position.z))
 var removed:int=0
 for actor in root.get_node("DetailPlacements").get_children():
  if actor.get_meta("asset_id","") in ["hill_boulder","fieldstone_wall"] and road_distance(Vector2(actor.position.x,actor.position.z))<9:
   actor.get_parent().remove_child(actor)
   actor.free()
   removed+=1
 return {"success":true,"road_blockers_removed":removed}

func finish_painted_ground() -> Dictionary:
 root=get_parent() as Node3D
 var terrain:Terrain3D=root.get_node("Geometry/Terrain")
 var noise:=FastNoiseLite.new()
 noise.noise_type=FastNoiseLite.TYPE_SIMPLEX_SMOOTH
 noise.frequency=.016
 noise.fractal_octaves=4
 var texture:=NoiseTexture2D.new()
 texture.width=256
 texture.height=256
 texture.seamless=true
 texture.noise=noise
 ResourceSaver.save(texture,T+"Textures/HillMacro.tres")
 texture.take_over_path(T+"Textures/HillMacro.tres")
 terrain.material.set_shader_param("noise_texture",texture)
 terrain.material.set_shader_param("enable_macro_variation",true)
 terrain.material.set_shader_param("macro_variation1",Color(.78,.91,.72))
 terrain.material.set_shader_param("macro_variation2",Color(.91,.90,.83))
 terrain.material.set_shader_param("noise1_scale",.43)
 terrain.material.set_shader_param("noise2_scale",.87)
 ResourceSaver.save(terrain.material,T+"fox_material.tres")
 return {"success":true}

func save_preview() -> Dictionary:
 var scene := Node3D.new()
 scene.name = "LinvelFoxPreview"
 scene.set_script(load("res://Tests/Regions/LinvelFoxPreview.gd"))
 var area: Node3D = load("res://Scenes/Regions/Linvel/FoxHills.tscn").instantiate()
 scene.add_child(area)
 area.owner = scene
 var camera := Camera3D.new()
 camera.name = "PreviewCamera"
 camera.current = true
 scene.add_child(camera)
 camera.owner = scene
 var packed := PackedScene.new()
 packed.pack(scene)
 var error := ResourceSaver.save(packed,"res://Tests/Regions/LinvelFoxPreview.tscn")
 scene.free()
 return {"success":error==OK}
func inspect_access() -> Dictionary:
 root = get_parent() as Node3D
 var terrain: Terrain3D = root.get_node("Geometry/Terrain")
 var samples: Array = []
 for h in hills:
  var out := {"id":h.id,"ramps":h.ramps.size(),"top":terrain.data.get_height(Vector3(h.x,0,h.z)),"edge_rise":height_at(h.x+h.rx*.93,h.z)-height_at(h.x+h.rx*1.09,h.z)}
  samples.append(out)
 return {"samples":samples,"regions":terrain.data.get_region_count()}
