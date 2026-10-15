@tool
extends "res://Tools/Linvel/BeechValleyAuthoring.gd"
# Explicit editor-only revision of the existing region authoring tool.
# Every build action refuses to replace saved sculpt/paint or placements.
const R := "res://Assets/World/Linvel/Beech/Revision2/"
const NATIVE := R+"Terrain/"
var paths: Array = [
 [[-435,18,330],[-330,24,260],[-220,32,210],[-135,28,149.25],[-108,28,129],[-81,28,108.75],[-50,30,70],[75,36,10],[185,48,-85],[285,43,-120],[390,50,-220],[475,55,-270]],
 [[-330,24,260],[-310,37,100],[-295,49,-105],[-190,72,-270],[-80,61,-195],[40,53,-155],[185,48,-85]],
 [[-330,24,260],[-225,20,360],[-75,17,340],[-45,17,310],[15,24,210],[65,29,120],[75,36,10]],
 [[-295,49,-105],[-350,59,-235],[-360,66,-420],[-445,58,-465]],
 [[185,48,-85],[160,55,-240],[160,59,-310],[230,63,-430],[320,64,-475]],
 [[185,48,-85],[230,50,-180],[285,56,-275],[325,59,-330],[310,61,-380],[230,63,-430]],
 [[-50,30,70],[-120,32,-10],[-165,35,-55]],
 [[285,43,-120],[335,39,-30],[335,37,50]],
 [[40,53,-155],[80,55,-215],[80,59,-285]],
 [[-330,24,260],[-350,21,320],[-305,18,350]],
 [[-87,28,117],[-30,27,157],[-45,23,207]],
 [[185,48,-85],[215,48,-30],[245,51,-12]],
 [[-295,49,-105],[-265,47,-65],[-230,45,-45]],
 [[-190,72,-270],[-230,73,-295]],
 [[-120,32,-10],[-170,34,25],[-205,35,50]]
]
var place_specs: Array = [
 {"id":"Woodcutters","label":"Двор лесных промыслов","p":Vector3(-330,24,260),"radius":31.0},
 {"id":"RavineBridge","label":"Буковый мост","p":Vector3(-108,28,129),"radius":10.0},
 {"id":"SunlitGlade","label":"Светлый буковый гребень","p":Vector3(185,48,-85),"radius":42.0},
 {"id":"CharcoalTerraces","label":"Углежогные террасы","p":Vector3(-295,49,-105),"radius":24.0},
 {"id":"KagerCamp","label":"Дальняя поляна","p":Vector3(325,59,-330),"radius":34.0}
]
var stream_path: Array = [[-70,-160],[-88,-70],[-105,70],[-110,125],[-86,230],[-45,310],[-15,390],[90,500]]

func native_height(x: float,z: float) -> float:
 var p := Vector2(x,z)
 # Broad landforms create a dry NW ridge, an eastern shoulder and a wet south.
 var h: float = 24.0+(350.0-z)*.035+x*.008
 h += 27.0*exp(-pow((x+235.0)/215.0,2)-pow((z+225.0)/235.0,2))
 h += 11.0*exp(-pow((x-220.0)/160.0,2)-pow((z+80.0)/220.0,2))
 h -= 9.0*exp(-pow((x+20.0)/210.0,2)-pow((z-315.0)/180.0,2))
 h += 1.1*sin(x/130.0)*cos(z/150.0)
 # An incised stream, not a repeated ring of identical plateau hills.
 var d: float = path_distance(p,stream_path)
 var channel: float = 1.0-smoothstep(3.0,15.0,d)
 if z < -145: channel *= smoothstep(-190.0,-145.0,z)
 var water: float = lerpf(29.0,12.5,clampf((z+160.0)/660.0,0.0,1.0))
 h = lerpf(h,water-.45,channel)
 # Two exposed rock shoulders: steep flanks, reachable only on authored paths.
 for shelf in [[-245.0,-290.0,68.0,45.0,12.0],[255.0,-25.0,48.0,27.0,9.0]]:
  var q: float = sqrt(pow((x-shelf[0])/shelf[2],2)+pow((z-shelf[1])/shelf[3],2))
  h += shelf[4]*(1.0-smoothstep(.89,1.05,q))
 var best: float = INF
 var road_y: float = h
 for path in paths:
  for i in range(path.size()-1):
   var a := Vector2(path[i][0],path[i][2])
   var b := Vector2(path[i+1][0],path[i+1][2])
   var delta := b-a
   var t: float = clampf((p-a).dot(delta)/delta.length_squared(),0.0,1.0)
   var dist: float = p.distance_to(a+delta*t)
   if dist<best:
    best=dist
    road_y=lerpf(path[i][1],path[i+1][1],t)
 # The stream continues beneath the bridge; its deck is separate geometry.
 var bridge := Vector2(-108,129)
 var bridge_axis := Vector2(48,-36).normalized()
 var along: float = (p-bridge).dot(bridge_axis)
 var across: float = absf((p-bridge).cross(bridge_axis))
 var under_bridge: bool = absf(along)<14.5 and across<11.0
 if not under_bridge:
  h=lerpf(h,road_y,1.0-smoothstep(5.5,19.0,best))
 if absf(along)<18.4 and across<3.5:
  h=minf(h,lerpf(26.5,27.82,smoothstep(13.0,18.4,absf(along))))
 for place in place_specs:
  if place.id=="RavineBridge": continue
  var distance: float = p.distance_to(Vector2(place.p.x,place.p.z))
  h=lerpf(h,place.p.y,1.0-smoothstep(place.radius*.6,place.radius+16,distance))
 return h

func path_distance(p: Vector2,path: Array) -> float:
 var best: float = INF
 for i in range(path.size()-1):
  var a := Vector2(path[i][0],path[i][1])
  var b := Vector2(path[i+1][0],path[i+1][1])
  var delta := b-a
  best=minf(best,p.distance_to(a+delta*clampf((p-a).dot(delta)/delta.length_squared(),0.0,1.0)))
 return best

func paths_distance(p: Vector2) -> float:
 var best: float = INF
 for path in paths:
  var flat: Array = []
  for v in path: flat.append([v[0],v[2]])
  best=minf(best,path_distance(p,flat))
 return best

func native_add(parent: Node,node: Node,label: String) -> Node:
 # Scatter presets become local editable nodes before their children are saved.
 if node is ProtonScatter: node.scene_file_path=""
 node.name=label
 parent.add_child(node,true)
 node.owner=root
 if node is ProtonScatter:
  for child in node.get_children():
   if child is ProtonScatterItem or child is ProtonScatterShape: child.owner=root
 return node

func native_tile(id: int,label: String) -> Terrain3DTextureAsset:
 var image := Image.create(256,256,false,Image.FORMAT_RGBA8)
 var normal := Image.create(256,256,false,Image.FORMAT_RGBA8)
 for z in range(256):
  for x in range(256):
   var wash: float = .97+.012*sin(TAU*(2*x+3*z)/256.0)+.008*sin(TAU*(7*x-5*z)/256.0)+.006*cos(TAU*(13*x+11*z)/256.0)
   image.set_pixel(x,z,Color(wash,wash,wash,.8))
   normal.set_pixel(x,z,Color(.5,.5,1,1))
 image.generate_mipmaps()
 normal.generate_mipmaps()
 var asset := Terrain3DTextureAsset.new()
 asset.id=id
 asset.name=label
 var albedo := ImageTexture.create_from_image(image)
 var normals := ImageTexture.create_from_image(normal)
 ResourceSaver.save(albedo,NATIVE+"Textures/"+label+"_albedo.res",ResourceSaver.FLAG_COMPRESS)
 albedo.take_over_path(NATIVE+"Textures/"+label+"_albedo.res")
 ResourceSaver.save(normals,NATIVE+"Textures/"+label+"_normal.res",ResourceSaver.FLAG_COMPRESS)
 normals.take_over_path(NATIVE+"Textures/"+label+"_normal.res")
 asset.albedo_texture=albedo
 asset.normal_texture=normals
 asset.uv_scale=.13
 asset.normal_depth=0
 asset.ao_strength=0
 return asset

func build_native_terrain() -> Dictionary:
 root=get_parent() as Node3D
 if root.has_node("Geometry") or DirAccess.dir_exists_absolute(ProjectSettings.globalize_path(NATIVE+"Data")):
  return {"success":false,"error":"Existing revision preserved. Sculpt and paint with Terrain3D brushes."}
 DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(NATIVE+"Data"))
 DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(NATIVE+"Textures"))
 root.add_to_group("surface_geometry",true)
 root.set_meta("region_id","linvel_beech")
 root.set_meta("beech_revision",2)
 root.set_meta("style","Stylized Hand-Painted Anime 3D")
 root.set_meta("surface_source_directory",NATIVE+"Data")
 root.set_meta("surface_cell_size",1.0)
 root.set_meta("surface_cell_height",.05)
 root.set_meta("surface_max_climb",.5)
 root.set_meta("surface_detail_distance",6.0)
 root.set_meta("surface_detail_error",2.0)
 root.set_meta("surface_terrain_lod",0)
 root.set_meta("surface_solid_terrain_los",true)
 root.set_meta("surface_steep_only_terrain_los",true)
 var geometry := Node3D.new()
 native_add(root,geometry,"Geometry")
 var terrain := Terrain3D.new()
 terrain.vertex_spacing=2.0
 terrain.mesh_size=48
 terrain.mesh_lods=6
 native_add(geometry,terrain,"Terrain")
 terrain.change_region_size(Terrain3D.SIZE_256)
 terrain.collision.mode=Terrain3DCollision.FULL_GAME
 terrain.collision.layer=1
 terrain.add_to_group("surface_terrain",true)
 var assets := Terrain3DAssets.new()
 for spec in [[0,"ForestFloor"],[1,"WoodlandTrack"],[2,"ExposedStone"],[3,"WetLeafLitter"]]:
  assets.set_texture(spec[0],native_tile(spec[0],spec[1]))
 terrain.assets=assets
 var material := Terrain3DMaterial.new()
 material.world_background=Terrain3DMaterial.NONE
 material.auto_shader=false
 material.dual_scaling=false
 material.shader_override=load("res://Assets/World/Linvel/Gardens/Terrain/painted_terrain.gdshader")
 material.shader_override_enabled=true
 material.set_shader_param("enable_macro_variation",false)
 terrain.material=material
 var heights := Image.create(512,512,false,Image.FORMAT_RF)
 var colors := Image.create(512,512,false,Image.FORMAT_RGBA8)
 var control := PackedByteArray()
 control.resize(512*512*4)
 for iz in range(512):
  for ix in range(512):
   var x: float = -512.0+ix*2.0
   var z: float = -512.0+iz*2.0
   var h: float = native_height(x,z)
   var slope: float = maxf(absf(native_height(x+2,z)-native_height(x-2,z)),absf(native_height(x,z+2)-native_height(x,z-2)))/4.0
   var road: float = paths_distance(Vector2(x,z))
   var wet: float = 1.0-smoothstep(6.0,28.0,path_distance(Vector2(x,z),stream_path))
   var id: int = 2 if slope>.90 else 1 if road<3.3 else 3 if wet>.6 else 0
   var color := Color(.38,.49,.27).lerp(Color(.46,.57,.31),smoothstep(35,60,h)*.6)
   color=color.lerp(Color(.25,.36,.26),wet*.5)
   if id==2: color=Color(.46,.46,.40)
   if slope<.90: color=color.lerp(Color(.57,.46,.31),1.0-smoothstep(2.6,5.4,road))
   var wash: float = .022*sin(x/72.0)*cos(z/91.0)+.011*sin((x-z)/33.0)
   colors.set_pixel(ix,iz,Color(color.r+wash,color.g+wash,color.b+wash*.6,.8))
   heights.set_pixel(ix,iz,Color(h,0,0,1))
   var bits: int = (id<<27)|2
   if absf(x)>500 or absf(z)>500: bits |= 4
   control.encode_u32((iz*512+ix)*4,bits)
 terrain.data.import_images([heights,Image.create_from_data(512,512,false,Image.FORMAT_RF,control),colors],Vector3(-512,0,-512),0,1)
 terrain.data.update_maps(Terrain3DRegion.TYPE_MAX,true,true)
 terrain.data.save_directory(NATIVE+"Data")
 terrain.data_directory=NATIVE+"Data"
 ResourceSaver.save(assets,NATIVE+"beech_assets.tres")
 assets.take_over_path(NATIVE+"beech_assets.tres")
 ResourceSaver.save(material,NATIVE+"beech_material.tres")
 material.take_over_path(NATIVE+"beech_material.tres")
 var dependencies: Array[Resource] = []
 for file in DirAccess.get_files_at(NATIVE+"Data"):
  if file.ends_with(".res"): dependencies.append(load(NATIVE+"Data/"+file))
 terrain.set_meta("terrain_region_resources",dependencies)
 var places := Node3D.new()
 native_add(root,places,"Places")
 for spec in place_specs:
  var place := Node3D.new()
  native_add(places,place,spec.id)
  place.position=spec.p
  place.set_meta("display_name",spec.label)
  native_add(place,Marker3D.new(),"Center")
 save_layout()
 make_lighting()
 return {"success":true,"regions":terrain.data.get_region_count(),"height_range":terrain.data.get_height_range(),"places":place_specs.size()}

func ground_at(p: Vector2) -> float:
 return root.get_node("Geometry/Terrain").data.get_height(Vector3(p.x,0,p.y))

func save_layout() -> void:
 var markers := Node3D.new()
 native_add(root,markers,"RouteMarkers")
 var sampled: Array = []
 var route_length: float = 0.0
 for route_index in range(paths.size()):
  var route_node := Node3D.new()
  native_add(markers,route_node,"Main" if route_index==0 else "Branch%02d"%route_index)
  var route_points: Array = []
  var path: Array = paths[route_index]
  for i in range(path.size()-1):
   var a := Vector3(path[i][0],path[i][1],path[i][2])
   var b := Vector3(path[i+1][0],path[i+1][1],path[i+1][2])
   var steps: int = ceili(a.distance_to(b)/2.0)
   for j in range(steps):
    var p := a.lerp(b,float(j)/steps)
    var axis := Vector2(48,-36).normalized()
    var delta := Vector2(p.x+108,p.z-129)
    p.y=28.10 if route_index==0 and absf(delta.dot(axis))<17.5 and absf(delta.cross(axis))<2.5 else ground_at(Vector2(p.x,p.z))
    route_points.append([p.x,p.y,p.z])
  var last: Array = path[-1]
  route_points.append([last[0],ground_at(Vector2(last[0],last[2])),last[2]])
  for i in range(path.size()):
   var marker := Marker3D.new()
   native_add(route_node,marker,"Point%02d"%i)
   marker.position=Vector3(path[i][0],path[i][1],path[i][2])
   if route_index==0: marker.add_to_group("linvel_main_route",true)
  sampled.append(route_points)
  if route_index==0:
   for i in range(route_points.size()-1):
    route_length+=Vector3(route_points[i][0],route_points[i][1],route_points[i][2]).distance_to(Vector3(route_points[i+1][0],route_points[i+1][1],route_points[i+1][2]))
 var spawn := Marker3D.new()
 native_add(markers,spawn,"PlayerSpawn")
 spawn.position=Vector3(-425,ground_at(Vector2(-425,330)),330)
 spawn.add_to_group("linvel_spawn",true)
 var gates := Node3D.new()
 native_add(root,gates,"RegionLinks")
 for spec in [["Gardens",-435,330,"linvel_gardens"],["FoxHills",-445,-465,"linvel_fox_hills"],["QuarryReserve",320,-475,""],["StoneGatesReserve",475,-270,""]]:
  var gate := Marker3D.new()
  native_add(gates,gate,spec[0])
  gate.position=Vector3(spec[1],ground_at(Vector2(spec[1],spec[2])),spec[2])
  gate.set_meta("destination",spec[3])
  gate.set_meta("reserved",spec[3].is_empty())
 root.set_meta("main_route_metres",route_length)
 var data := {"revision":2,"width":1000,"depth":1000,"backend":"Terrain3D","route":paths[0],"paths":paths,"route_samples":sampled,"places":place_specs.map(func(p): return {"id":p.id,"name":p.label,"position":[p.p.x,p.p.y,p.p.z],"radius":p.radius}),"route_metres":route_length,"walking_seconds":route_length/3.5,"spawn":[spawn.position.x,spawn.position.y,spawn.position.z]}
 FileAccess.open(R+"layout.json",FileAccess.WRITE).store_string(JSON.stringify(data,"\t"))

var revision_meshes: Dictionary = {}
var revision_specs: Dictionary = {}
var revision_material: ShaderMaterial
var paths_are_rounded := false

func prepare_revision_assets() -> Dictionary:
 root=get_parent() as Node3D
 if root.get_meta("beech_finished_terrain",false): round_route_corners()
 DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(R+"Meshes"))
 var catalog: Array = JSON.parse_string(FileAccess.get_file_as_string(ProjectSettings.globalize_path("res://../art-source/linvel/beech/revision2/catalog.json")))
 revision_material=ResourceLoader.load(R+"painted_forest.tres","ShaderMaterial",ResourceLoader.CACHE_MODE_IGNORE_DEEP)
 if revision_material==null: revision_material=ShaderMaterial.new()
 if revision_material.shader==null: revision_material.shader=load(R+"painted_vertex.gdshader")
 for parameter in ["bark_paint","wood_paint","stone_paint","cloth_paint","leaf_paint"]:
  if revision_material.get_shader_parameter(parameter)==null and FileAccess.file_exists(R+parameter+".res"):
   revision_material.set_shader_parameter(parameter,load(R+parameter+".res"))
 ResourceSaver.save(revision_material,R+"painted_forest.tres")
 revision_material.take_over_path(R+"painted_forest.tres")
 for spec in catalog:
  var packed: PackedScene=ResourceLoader.load(R+"Models/"+spec.id+".glb","PackedScene",ResourceLoader.CACHE_MODE_IGNORE_DEEP)
  if packed==null: return {"success":false,"error":"Asset import is incomplete: "+spec.id}
  var source: Node3D=packed.instantiate()
  var visual: MeshInstance3D=source.find_children("*","MeshInstance3D",true,false)[0]
  var mesh: Mesh=visual.mesh.duplicate(true)
  var path: String=R+"Meshes/"+spec.id+".res"
  ResourceSaver.save(mesh,path,ResourceSaver.FLAG_COMPRESS)
  mesh.take_over_path(path)
  revision_meshes[spec.id]=mesh
  revision_specs[spec.id]=spec
  source.free()
 for id in ["beech_stump","fallen_beech","trail_sign","fire_ring","fern_cluster","grass_tuft","hazel_shrub","meadow_flowers","moss_boulder_a"]:
  var path: String=ASSET_ROOT+"Meshes/"+id+".res"
  if FileAccess.file_exists(path): revision_meshes["old_"+id]=load(path)
 return {"success":true,"models":revision_meshes.size()}

func revision_box(parent: Node3D,label: String,center: Vector3,size: Vector3,walkable: bool=false,part: String="structure",opaque: bool=true) -> void:
 var body := StaticBody3D.new()
 native_add(parent,body,label)
 body.position=center
 body.collision_layer=1 if walkable else 4
 body.add_to_group("surface_geometry",true)
 if not walkable:
  body.set_meta("surface_nonwalkable_top",true)
  body.set_meta("collision_part",part)
  if opaque: body.add_to_group("surface_occluder",true)
 var shape := BoxShape3D.new()
 shape.size=size
 var collision := CollisionShape3D.new()
 collision.shape=shape
 native_add(body,collision,"Collision")

func revision_asset(parent: Node3D,id: String,p: Vector2,yaw: float=0.0,size: float=1.0,label: String="",collisions: bool=true,height_override: float=NAN) -> Node3D:
 var placement := Node3D.new()
 native_add(parent,placement,id if label.is_empty() else label)
 placement.global_position=Vector3(p.x,ground_at(p) if is_nan(height_override) else height_override,p.y)
 placement.rotation.y=yaw
 placement.scale=Vector3.ONE*size
 placement.set_meta("asset_id",id)
 placement.set_meta("beech_asset_root",true)
 var visual := MeshInstance3D.new()
 native_add(placement,visual,"Visual")
 visual.mesh=revision_meshes[id]
 visual.material_override=load(ASSET_ROOT+"painted_forest.tres") if id.begins_with("old_") else revision_material
 visual.visibility_range_end=240 if id.begins_with("beech_") else 180
 visual.visibility_range_end_margin=24
 if collisions and revision_specs.has(id):
  var index := 0
  for box in revision_specs[id].collision_boxes:
   var c: Array=box.center
   var s: Array=box.size
   revision_box(placement,"Body%02d"%index,Vector3(c[0],c[1],c[2]),Vector3(s[0],s[1],s[2]),box.get("walkable",false),box.get("part","structure"))
   index+=1
 return placement

func fill_places() -> Dictionary:
 root=get_parent() as Node3D
 if root.has_meta("beech_compositions_authored"): return {"success":false,"error":"Existing place compositions preserved."}
 var ready:=prepare_revision_assets()
 if not ready.get("success",false): return ready
 var yard: Node3D=root.get_node("Places/Woodcutters")
 revision_asset(yard,"working_forest_shelter",Vector2(-348,249),.18,1.0,"TimberWorkingBay")
 for p in [Vector2(-351,269),Vector2(-346,283),Vector2(-333,283)]:
  revision_asset(yard,"seasoned_timber",p,.15,1.0,"DryTimberOnSpacers")
 revision_asset(yard,"old_trail_sign",Vector2(-311,251),.7,1.0,"PromissoryWaySign",false)
 for p in [Vector2(-363,258),Vector2(-363,278),Vector2(-354,291)]:
  revision_asset(yard,"old_beech_stump",p,0,1.0,"FreshCutStump",false)
 var bridge: Node3D=root.get_node("Places/RavineBridge")
 revision_asset(bridge,"repaired_beech_bridge",Vector2(-108,129),atan2(36.0,48.0),1.0,"RepairedCrossing",true,28.0)
 revision_asset(bridge,"seasoned_timber",Vector2(-127,159),1.1,.65,"RemovedBridgeTimber")
 # Tall landmark trees frame approaches while keeping the deck and camera clear.
 revision_asset(bridge,"beech_canopy_0",Vector2(-139,124),.5,1.7,"WestBridgeBeech")
 revision_asset(bridge,"beech_canopy_2",Vector2(-63,127),1.5,1.55,"EastBridgeBeech")
 var sunny: Node3D=root.get_node("Places/SunlitGlade")
 revision_asset(sunny,"mossy_outcrop",Vector2(214,-102),.7,1.7,"SunlitStoneShoulder")
 revision_asset(sunny,"old_trail_sign",Vector2(166,-75),-.8,1.0,"NorthernForkSign",false)
 revision_asset(sunny,"kager_reused_shelter",Vector2(219,-37),.3,.65,"HerbalistRainCover")
 var kilns: Node3D=root.get_node("Places/CharcoalTerraces")
 revision_asset(kilns,"covered_charcoal_kiln",Vector2(-311,-105),.1,1.0,"WorkingKiln")
 revision_asset(kilns,"seasoned_timber",Vector2(-317,-88),1.1,.7,"KilnFuel")
 revision_asset(kilns,"kager_reused_shelter",Vector2(-276,-121),.2,.8,"CharcoalWorkersCover")
 var camp: Node3D=root.get_node("Places/KagerCamp")
 revision_asset(camp,"kager_reused_shelter",Vector2(344,-343),.4,1.1,"PatchedOldCover")
 revision_asset(camp,"seasoned_timber",Vector2(306,-343),.4,.75,"ScavengedStack")
 revision_asset(camp,"mossy_outcrop",Vector2(305,-310),-.8,.9,"ReachableSlingerCover")
 if revision_meshes.has("old_campfire_ring"):
  revision_asset(camp,"old_campfire_ring",Vector2(328,-341),0,1.3,"OldFireWithFreshUse",false)
 var finds := Node3D.new()
 native_add(root,finds,"Exploration")
 for spec in [["LowerLoading",-305,350,"Прежняя погрузка"],["RainCover",-45,207,"Навес у оврага"],["HerbTerrace",245,-12,"Травяная терраса"],["UnusedKiln",-230,-45,"Оставленная площадка"],["BentBeechView",-230,-295,"Обзор у бука"],["LowerCrossing",-45,310,"Низкая переправа"],["EmptyStand",-205,50,"Оставленная стоянка"],["AnimalTrack",80,-215,"Звериная тропа"]]:
  var place := Node3D.new()
  native_add(finds,place,spec[0])
  place.position=Vector3(spec[1],ground_at(Vector2(spec[1],spec[2])),spec[2])
  place.set_meta("display_name",spec[3])
  native_add(place,Marker3D.new(),"Center")
 revision_asset(finds.get_node("LowerLoading"),"seasoned_timber",Vector2(-317,359),1.4,.7,"OldLoadingSpacers")
 revision_asset(finds.get_node("LowerLoading"),"old_fallen_beech",Vector2(-297,360),1.1,.8,"DiscardedCutLog",false)
 revision_asset(finds.get_node("RainCover"),"kager_reused_shelter",Vector2(-49,216),1.0,.72,"RainShelter")
 revision_asset(finds.get_node("UnusedKiln"),"covered_charcoal_kiln",Vector2(-230,-45),.2,.63,"OldCharcoalRemains",false)
 revision_asset(finds.get_node("BentBeechView"),"beech_canopy_1",Vector2(-241,-300),.6,1.9,"BentRidgeBeech")
 revision_asset(finds.get_node("EmptyStand"),"kager_reused_shelter",Vector2(-204,61),1.4,.75,"EmptyOldShelter")
 make_encounter_reserves()
 make_water()
 root.set_meta("beech_compositions_authored",true)
 return {"success":true,"places":5,"discoveries":8,"bodies":root.find_children("*","StaticBody3D",true,false).size()}

func make_encounter_reserves() -> void:
 var parent := Node3D.new()
 native_add(root,parent,"EncounterMarkers")
 # Markers reserve physical space. Runtime AI/rewards remain server-owned.
 for spec in [["EdgeAnimal",-330,365,"territorial_animal"],["WoodlandWolf",-165,290,"wolf"],["SmallCamp",-165,-55,"kager_pair"],["GuardedStand",335,50,"kager_roles"],["FarAnimal",80,-285,"elite_wolf"],["FarCamp",325,-330,"regional_encounter"]]:
  var marker := Marker3D.new()
  native_add(parent,marker,spec[0])
  marker.position=Vector3(spec[1],ground_at(Vector2(spec[1],spec[2])),spec[2])
  marker.set_meta("placement_role",spec[3])
  marker.set_meta("reserved_only",true)
 var small: Node3D=root.get_node("EncounterMarkers/SmallCamp")
 var props := Node3D.new()
 native_add(root,props,"EncounterEnvironment")
 revision_asset(props,"kager_reused_shelter",Vector2(-175,-64),.2,.85,"SmallWorkingCamp")
 revision_asset(props,"mossy_outcrop",Vector2(-151,-47),1.1,.75,"SmallCampCover")
 revision_asset(props,"kager_reused_shelter",Vector2(352,54),1.2,.9,"GuardedCover")
 revision_asset(props,"mossy_outcrop",Vector2(322,61),.4,.85,"FlankCover")

func make_water() -> void:
 var geometry: Node3D=root.get_node("Geometry")
 var mesh := ImmediateMesh.new()
 var water := StandardMaterial3D.new()
 water.albedo_color=Color(.18,.36,.39)
 water.roughness=.42
 water.cull_mode=BaseMaterial3D.CULL_DISABLED
 mesh.surface_begin(Mesh.PRIMITIVE_TRIANGLES)
 for i in range(stream_path.size()-1):
  var a:=Vector2(stream_path[i][0],stream_path[i][1])
  var b:=Vector2(stream_path[i+1][0],stream_path[i+1][1])
  var axis: Vector2=(b-a).normalized()
  var right:=Vector2(-axis.y,axis.x)*1.45
  var steps: int=ceili(a.distance_to(b)/3)
  for j in range(steps):
   var c: Vector2=a.lerp(b,float(j)/steps)
   var d: Vector2=a.lerp(b,float(j+1)/steps)
   var y0: float=lerpf(29.0,12.5,clampf((c.y+160)/660.0,0.0,1.0))
   var y1: float=lerpf(29.0,12.5,clampf((d.y+160)/660.0,0.0,1.0))
   # At the ford water sits below the walkable road, preserving a dry foot line.
   y0=minf(y0,ground_at(c)+.10)
   y1=minf(y1,ground_at(d)+.10)
   for v in [Vector3(c.x-right.x,y0,c.y-right.y),Vector3(d.x-right.x,y1,d.y-right.y),Vector3(c.x+right.x,y0,c.y+right.y),Vector3(c.x+right.x,y0,c.y+right.y),Vector3(d.x-right.x,y1,d.y-right.y),Vector3(d.x+right.x,y1,d.y+right.y)]:
    mesh.surface_set_normal(Vector3.UP)
    mesh.surface_add_vertex(v)
 mesh.surface_end()
 var node := MeshInstance3D.new()
 native_add(geometry,node,"ForestStream")
 node.mesh=mesh
 node.material_override=water
 node.cast_shadow=GeometryInstance3D.SHADOW_CASTING_SETTING_OFF

func build_woodland_revision() -> Dictionary:
 root=get_parent() as Node3D
 if root.has_node("Woodland"): return {"success":false,"error":"Authored woodland preserved."}
 var ready:=prepare_revision_assets()
 if not ready.get("success",false): return ready
 var woodland := Node3D.new()
 native_add(root,woodland,"Woodland")
 # Designed woodland belts follow landforms, leaving glades and route loops clear.
 var belts: Array = [
  [Vector2(-445,175),Vector2(-385,110),Vector2(-275,130)],
  [Vector2(-360,75),Vector2(-240,15),Vector2(-225,-120)],
  [Vector2(-450,-220),Vector2(-365,-315),Vector2(-310,-420)],
  [Vector2(-135,-365),Vector2(-40,-400),Vector2(60,-350)],
  [Vector2(130,110),Vector2(245,170),Vector2(380,225)],
  [Vector2(285,115),Vector2(400,25),Vector2(440,-100)],
  [Vector2(385,-220),Vector2(440,-340),Vector2(370,-440)],
  [Vector2(-365,405),Vector2(-245,435),Vector2(-95,420)]
 ]
 var tree_positions: Array[Vector2] = []
 var generator := RandomNumberGenerator.new()
 generator.seed=684210
 var trees := 0
 for belt_index in range(belts.size()):
  var belt: Array=belts[belt_index]
  var node := Node3D.new()
  native_add(woodland,node,"WoodlandBelt%02d"%belt_index)
  for i in range(18):
   var segment: int=i%2
   var axis: Vector2=(belt[segment+1]-belt[segment]).normalized()
   var normal:=Vector2(-axis.y,axis.x)
   var p: Vector2=belt[segment].lerp(belt[segment+1],float(i/2)/8.0)+normal*generator.randf_range(-28,28)
   if paths_distance(p)<8 or absf(p.x)>475 or absf(p.y)>475: continue
   if path_distance(p,stream_path)<17: continue
   var blocked := false
   for place in place_specs:
    if p.distance_to(Vector2(place.p.x,place.p.z))<place.radius+14: blocked=true
   for other in tree_positions:
    if p.distance_to(other)<13: blocked=true
   if blocked: continue
   var slope: float=maxf(absf(ground_at(p+Vector2(3,0))-ground_at(p-Vector2(3,0))),absf(ground_at(p+Vector2(0,3))-ground_at(p-Vector2(0,3))))/6
   if slope>.45: continue
   var size: float=generator.randf_range(1.3,1.9)
   revision_asset(node,"beech_canopy_"+str(i%3),p,generator.randf_range(-PI,PI),size,"Beech%02d"%i)
   tree_positions.append(p)
   trees+=1
   if trees>=125: break
  if trees>=125: break
 var copse_positions: Array = [Vector2(-444,80),Vector2(-397,39),Vector2(-278,78),Vector2(-263,220),Vector2(-420,-65),Vector2(-360,-40),Vector2(-215,-155),Vector2(-230,-218),Vector2(-449,-318),Vector2(-285,-410),Vector2(-185,-400),Vector2(-60,-440),Vector2(10,-370),Vector2(109,-378),Vector2(218,-370),Vector2(390,-378),Vector2(433,-432),Vector2(465,-180),Vector2(409,-124),Vector2(465,30),Vector2(387,95),Vector2(308,200),Vector2(248,256),Vector2(170,170),Vector2(91,140),Vector2(-100,416),Vector2(-205,440),Vector2(-375,451),Vector2(-412,320),Vector2(378,332),Vector2(290,380),Vector2(180,376),Vector2(-48,36),Vector2(-189,113)]
 var copses := 0
 for p: Vector2 in copse_positions:
  if paths_distance(p)<22 or path_distance(p,stream_path)<20: continue
  var overlap := false
  for other in tree_positions:
   if p.distance_to(other)<20: overlap=true
  if overlap: continue
  revision_asset(woodland,"beech_copse_"+str(copses%2),p,generator.randf_range(-PI,PI),1.5,"WoodedOutcrop%02d"%copses)
  tree_positions.append(p)
  copses+=1
 root.set_meta("woodland_single_trees",trees+3)
 root.set_meta("woodland_copses",copses)
 fill_understory(tree_positions)
 return {"success":true,"individual_trees":trees+3,"wooded_outcrops":copses,"bodies":root.find_children("*","StaticBody3D",true,false).size()}

func fill_understory(centers: Array) -> void:
 var parent := Node3D.new()
 native_add(root,parent,"Understory")
 var generator:=RandomNumberGenerator.new()
 generator.seed=684211
 var buckets: Dictionary = {}
 # Deliberate forest floor and stream masks; no uniform scatter over the region.
 for i in range(14000):
  var center: Vector2=centers[i%centers.size()]
  var angle: float=generator.randf_range(-PI,PI)
  var p: Vector2=center+Vector2(cos(angle),sin(angle))*generator.randf_range(4,20)
  if paths_distance(p)<5 or absf(p.x)>490 or absf(p.y)>490: continue
  var slope: float=maxf(absf(ground_at(p+Vector2(2,0))-ground_at(p-Vector2(2,0))),absf(ground_at(p+Vector2(0,2))-ground_at(p-Vector2(0,2))))/4
  if slope>.45: continue
  var cell:=Vector2i(floori(p.x/64),floori(p.y/64))
  var id: String="woodland_fern" if i%5==0 else "sunlit_herb"
  var key:=id+"_"+str(cell.x)+"_"+str(cell.y)
  if not buckets.has(key): buckets[key]={"id":id,"cell":cell,"positions":[]}
  buckets[key].positions.append(Vector3(p.x,ground_at(p),p.y))
 for key in buckets:
  var bucket: Dictionary=buckets[key]
  var multimesh:=MultiMesh.new()
  multimesh.transform_format=MultiMesh.TRANSFORM_3D
  multimesh.mesh=revision_meshes[bucket.id]
  multimesh.instance_count=bucket.positions.size()
  var node:=MultiMeshInstance3D.new()
  native_add(parent,node,key)
  node.position=Vector3(bucket.cell.x*64,0,bucket.cell.y*64)
  node.multimesh=multimesh
  node.material_override=revision_material
  node.cast_shadow=GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
  node.visibility_range_end=95
  node.visibility_range_end_margin=18
  for i in range(bucket.positions.size()):
   var basis:=Basis(Vector3.UP,generator.randf_range(-PI,PI)).scaled(Vector3.ONE*generator.randf_range(.7,1.2))
   multimesh.set_instance_transform(i,Transform3D(basis,bucket.positions[i]-node.position))

func build_revision_preview() -> Dictionary:
 root=get_parent() as Node3D
 var preview := Node3D.new()
 preview.name="LinvelBeechPreview"
 preview.set_script(load("res://Tests/Regions/LinvelBeechPreview.gd"))
 var region: Node3D=root.duplicate()
 region.name="BeechValley"
 var tool: Node=region.get_node_or_null("Authoring")
 if tool!=null:
  region.remove_child(tool)
  tool.free()
 if region.has_node("HerbScatterAuthoring"):
  var source: Node=region.get_node("HerbScatterAuthoring")
  region.remove_child(source)
  source.free()
 preview.add_child(region)
 var camera:=Camera3D.new()
 camera.name="PreviewCamera"
 camera.current=true
 preview.add_child(camera)
 var figure: Node3D=load("res://Scenes/Actors/Primitives/PlayerFigure.tscn").instantiate()
 figure.name="PlayerFigure"
 preview.add_child(figure)
 preview_ownership(preview,preview)
 var packed:=PackedScene.new()
 var error: int=packed.pack(preview)
 var path: String="res://Tests/Regions/LinvelBeechRevisionPreview.tscn"
 if error==OK: error=ResourceSaver.save(packed,path)
 preview.free()
 return {"success":error==OK,"path":path,"error":error}

func preview_ownership(node: Node,owner_root: Node) -> void:
 for child in node.get_children():
  child.owner=owner_root
  preview_ownership(child,owner_root)

func inspect_revision() -> Dictionary:
 root=get_parent() as Node3D
 var terrain: Terrain3D=root.get_node("Geometry/Terrain")
 return {"editor_pid":OS.get_process_id(),"regions":terrain.data.get_region_count(),"height_range":terrain.data.get_height_range(),"nodes":root.find_children("*","",true,false).size(),"bodies":root.find_children("*","StaticBody3D",true,false).size(),"scene":root.scene_file_path}

func inspect_plant_imports() -> Dictionary:
 root=get_parent() as Node3D
 var meshes: Array=[]
 for id in ["sunlit_herb","woodland_fern"]:
  var model: Node3D=load(R+"Models/"+id+".glb").instantiate()
  var mesh: Mesh=model.find_children("*","MeshInstance3D",true,false)[0].mesh
  meshes.append({"id":id,"import_indices":mesh.surface_get_array_index_len(0),"native_indices":load(R+"Meshes/"+id+".res").surface_get_array_index_len(0)})
  model.free()
 return {"meshes":meshes}

func rescan_assets() -> Dictionary:
 EditorInterface.get_resource_filesystem().scan()
 return {"started":true}

func publish_revision() -> Dictionary:
 root=get_parent() as Node3D
 if not root.get_meta("beech_story_details_finished",false):
  return {"success":false,"error":"Complete and inspect compositions before publication."}
 # Pack the saved nodes directly. Duplicating Terrain3D's live editor subtree
 # also duplicates internal MultiMeshes and tries to set their format after
 # their instance count, producing engine errors unrelated to authored data.
 var removed: Array=[]
 for label in ["Authoring","HerbScatterAuthoring","GrassScatterAuthoring"]:
  if not root.has_node(label): continue
  var node: Node=root.get_node(label)
  removed.append([node,node.get_index()])
  root.remove_child(node)
 var packed: PackedScene=ResourceLoader.load(PUBLIC_PATH,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE)
 var error: int=packed.pack(root)
 if error==OK: error=ResourceSaver.save(packed,PUBLIC_PATH)
 for entry in removed:
  var node: Node=entry[0]
  root.add_child(node)
  root.move_child(node,mini(entry[1],root.get_child_count()-1))
  preview_ownership(node,root)
 return {"success":error==OK,"path":PUBLIC_PATH,"error":error}

func refine_camera_compositions() -> Dictionary:
 root=get_parent() as Node3D
 if root.has_meta("beech_camera_compositions_finished"):
  return {"success":false,"error":"Camera compositions already finished."}
 var ready:=prepare_revision_assets()
 if not ready.get("success",false): return ready
 var replaced := 0
 for n in root.get_node("TrailDetails").get_children():
  if n.get_meta("asset_id","")=="old_hazel_shrub":
   n.set_meta("asset_id","woodland_fern")
   n.get_node("Visual").mesh=revision_meshes.woodland_fern
   n.get_node("Visual").material_override=revision_material
   n.scale=Vector3.ONE*.85
   replaced+=1
 for n in root.get_node("Understory").get_children():
  if n.name.begins_with("woodland_fern_"): n.multimesh.mesh=revision_meshes.woodland_fern
 var parent := Node3D.new()
 native_add(root,parent,"LandmarkTrees")
 for spec in [[-355,228,1.55],[-319,237,1.45],[-364,283,1.55],[-301,283,1.4],[163,-111,1.5],[211,-63,1.6]]:
  revision_asset(parent,"beech_canopy_1",Vector2(spec[0],spec[1]),.5,spec[2],"LandmarkBeech")
 root.set_meta("beech_camera_compositions_finished",true)
 return {"success":true,"replaced_large_shrubs":replaced,"landmark_trees":6}

func compose_route_woodland() -> Dictionary:
 root=get_parent() as Node3D
 if root.has_meta("beech_route_woodland_finished"): return {"success":false,"error":"Route woodland already composed."}
 prepare_revision_assets()
 var trees: Array[Node3D]=[]
 for n in root.get_node("Woodland").find_children("*","Node3D",true,false):
  if n.get_meta("asset_id","").begins_with("beech_canopy_"): trees.append(n)
 var locations: Array[Vector2]=[]
 var sequence:=0
 for route_index in [0,1,2,3,4,5]:
  var path: Array=paths[route_index]
  for i in range(path.size()-1):
   var a:=Vector2(path[i][0],path[i][2])
   var b:=Vector2(path[i+1][0],path[i+1][2])
   var axis: Vector2=(b-a).normalized()
   var normal:=Vector2(-axis.y,axis.x)
   var steps: int=floori(a.distance_to(b)/19.0)
   for j in range(steps):
    for side in [-1,1]:
     var p: Vector2=a.lerp(b,(float(j)+.45)/steps)+normal*side*(9.5+float(sequence%3))
     sequence+=1
     var skip:=paths_distance(p)<7.5 or absf(p.x)>475 or absf(p.y)>475
     if p.distance_to(Vector2(-425,330))<20 or path_distance(p,stream_path)<8: skip=true
     for place in place_specs:
      var margin: float=17.0 if place.id=="Woodcutters" else 14.0 if place.id=="RavineBridge" else place.radius
      if p.distance_to(Vector2(place.p.x,place.p.z))<margin: skip=true
     for previous in locations:
      if p.distance_to(previous)<14: skip=true
     if skip: continue
     locations.append(p)
 if locations.size()<trees.size(): return {"success":false,"error":"Insufficient distinct woodland positions."}
 for i in range(trees.size()):
  var p: Vector2=locations[i]
  trees[i].global_position=Vector3(p.x,ground_at(p),p.y)
 root.set_meta("beech_route_woodland_finished",true)
 return {"success":true,"trees_composed":trees.size()}

func build_asset_prefabs() -> Dictionary:
 root=get_parent() as Node3D
 var directory: String=R+"Prefabs/"
 DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(directory))
 var written: Dictionary={}
 for visual in root.find_children("Visual","MeshInstance3D",true,false):
  var source: Node3D=visual.get_parent()
  var id: String=source.get_meta("asset_id","")
  if id.is_empty() or id.begins_with("old_") or written.has(id): continue
  var path: String=directory+id+".tscn"
  if FileAccess.file_exists(path):
   written[id]=true
   continue
  var copy: Node3D=source.duplicate()
  copy.name=id
  copy.transform=Transform3D.IDENTITY
  preview_ownership(copy,copy)
  var packed:=PackedScene.new()
  var error: int=packed.pack(copy)
  if error==OK: error=ResourceSaver.save(packed,path)
  copy.free()
  if error!=OK: return {"success":false,"error":error,"asset":id}
  written[id]=true
 return {"success":true,"prefabs":written.size(),"directory":directory}

func register_asset_placer_library() -> Dictionary:
 var library: AssetLibrary=AssetLibraryManager.get_asset_library()
 if library==null: return {"success":false,"notice":"Asset Placer library is not loaded; prefabs remain available in the FileSystem."}
 var added:=0
 for file in DirAccess.get_files_at(R+"Prefabs/"):
  if not file.ends_with(".tscn"): continue
  var path: String=R+"Prefabs/"+file
  if library.has_asset_path(path): continue
  if library.add_asset(AssetResource.new(path,"Буковая долина · "+file.trim_suffix(".tscn"))): added+=1
 return {"success":true,"added":added}

func start_herb_scatter() -> Dictionary:
 root=get_parent() as Node3D
 if root.has_node("HerbScatterAuthoring"): return {"success":false,"error":"Existing scatter masks preserved."}
 var scatter: Node3D=load("res://addons/proton_scatter/presets/scatter_default.tscn").instantiate()
 scatter.name="HerbScatterAuthoring"
 scatter.force_rebuild_on_load=false
 scatter.enable_updates_in_game=false
 scatter.global_seed=684212
 scatter.dbg_disable_thread=true
 scatter.position=Vector3(245,51,-12)
 scatter.get_node("ScatterItem").path=R+"Prefabs/sunlit_herb.tscn"
 scatter.get_node("ScatterItem").override_material=revision_material
 scatter.get_node("ScatterItem").override_cast_shadow=GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
 scatter.get_node("ScatterShape").shape.radius=5.5
 scatter.modifier_stack.stack[0].amount=48
 scatter.modifier_stack.stack[1].rotation=Vector3(0,360,0)
 scatter.modifier_stack.stack[3].ray_offset=100
 scatter.modifier_stack.stack[3].ray_length=200
 scatter.modifier_stack.stack[3].max_slope=30
 native_add(root,scatter,"HerbScatterAuthoring")
 scatter.rebuild(true)
 return {"success":true,"seed":684212,"scope":"sunlit herb terrace only"}

func bake_herb_scatter() -> Dictionary:
 root=get_parent() as Node3D
 var source: Node3D=root.get_node("HerbScatterAuthoring")
 var output: Node3D=source.get_node_or_null("ScatterOutput")
 if output==null or output.get_child_count()==0: return {"success":false,"error":"Scatter is still building."}
 var parent: Node3D=root.get_node("Exploration/HerbTerrace")
 if parent.has_node("HerbDrift"): return {"success":false,"error":"Existing baked plants preserved."}
 var copy: Node3D=output.duplicate()
 native_add(parent,copy,"HerbDrift")
 copy.global_transform=output.global_transform
 preview_ownership(copy,root)
 source.visible=false
 return {"success":true,"baked_children":copy.get_child_count(),"runtime_generator":false}

func write_native_height_samples() -> Dictionary:
 root=get_parent() as Node3D
 var terrain: Terrain3D=root.get_node("Geometry/Terrain")
 var heights: Array=[]
 for z in range(512):
  for x in range(512):
   var h: float=terrain.data.get_height(Vector3(-512+x*2,0,-512+z*2))
   heights.append(0.0 if is_nan(h) else h)
 FileAccess.open(ProjectSettings.globalize_path("res://../.artifacts/beech-revision2/native-height-samples.json"),FileAccess.WRITE).store_string(JSON.stringify(heights))
 return {"success":true,"samples":heights.size()}

func conform_native_surface(folder: String="DataFinal") -> Dictionary:
 root=get_parent() as Node3D
 var directory: String=NATIVE+folder
 if DirAccess.dir_exists_absolute(ProjectSettings.globalize_path(directory)):
  return {"success":false,"error":"Playable maps exist; preserve manual sculpt."}
 var values: Array=JSON.parse_string(FileAccess.get_file_as_string(ProjectSettings.globalize_path("res://../.artifacts/beech-revision2/matched-height-samples.json")))
 if values.size()!=512*512: return {"success":false,"error":"Invalid native sample grid."}
 DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(directory))
 var dependencies: Array[Resource]=[]
 for file in DirAccess.get_files_at(NATIVE+"DataFinished"):
  if not file.ends_with(".res"): continue
  var region: Terrain3DRegion=load(NATIVE+"DataFinished/"+file).duplicate(true)
  var encoded: String=file.trim_prefix("terrain3d").trim_suffix(".res")
  var loc:=Vector2i(encoded.substr(0,3).replace("_","").to_int(),encoded.substr(3,3).replace("_","").to_int())
  region.set_location(loc)
  var image:=Image.create(256,256,false,Image.FORMAT_RF)
  for z in range(256):
   for x in range(256):
    var ix: int=loc.x*256+x+256
    var iz: int=loc.y*256+z+256
    image.set_pixel(x,z,Color(values[iz*512+ix],0,0,1))
  region.set_height_map(image)
  region.calc_height_range()
  region.set_modified(true)
  var path: String=directory+"/"+file
  var error: int=ResourceSaver.save(region,path,ResourceSaver.FLAG_COMPRESS)
  if error!=OK: return {"success":false,"error":error}
  region.take_over_path(path)
  dependencies.append(region)
 var terrain: Terrain3D=root.get_node("Geometry/Terrain")
 terrain.data_directory=directory
 terrain.set_meta("terrain_region_resources",dependencies)
 root.set_meta("surface_source_directory",directory)
 for n in root.find_children("*","Node3D",true,false):
  if not n.has_meta("beech_asset_root") or n.name=="RepairedCrossing": continue
  var p: Vector3=n.global_position
  n.global_position=Vector3(p.x,ground_at(Vector2(p.x,p.z)),p.z)
 prepare_revision_assets()
 resample_finished_paths()
 return {"success":true,"regions":terrain.data.get_region_count(),"sculpt_preserved":NATIVE+"DataFinished"}

func configure_precise_bake(distance: float=36.0,error: float=0.25) -> Dictionary:
 root=get_parent() as Node3D
 var terrain: Terrain3D=root.get_node("Geometry/Terrain")
 terrain.data_directory=NATIVE+"DataFinished"
 var dependencies: Array[Resource]=[]
 for file in DirAccess.get_files_at(NATIVE+"DataFinished"):
  if file.ends_with(".res"): dependencies.append(load(NATIVE+"DataFinished/"+file))
 terrain.set_meta("terrain_region_resources",dependencies)
 root.set_meta("surface_source_directory",NATIVE+"DataFinished")
 root.set_meta("surface_detail_distance",distance)
 root.set_meta("surface_detail_error",error)
 root.set_meta("surface_snap_native_heights",true)
 for n in root.find_children("*","Node3D",true,false):
  if n.has_meta("beech_asset_root") and n.name!="RepairedCrossing":
   var p: Vector3=n.global_position
   n.global_position=Vector3(p.x,ground_at(Vector2(p.x,p.z)),p.z)
 prepare_revision_assets()
 resample_finished_paths()
 return {"success":true,"detail_distance":distance,"detail_error":error}

func sculpt_broad_planes() -> Dictionary:
 root=get_parent() as Node3D
 var directory: String=NATIVE+"DataSurvey"
 if DirAccess.dir_exists_absolute(ProjectSettings.globalize_path(directory)):
  return {"success":false,"error":"Survey maps exist; manual sculpt preserved."}
 var terrain: Terrain3D=root.get_node("Geometry/Terrain")
 terrain.data_directory=NATIVE+"DataFinished"
 var coarse: Dictionary={}
 for z in range(-512,513,32):
  for x in range(-512,513,32):
   var h: float=terrain.data.get_height(Vector3(clampf(x,-498,498),0,clampf(z,-498,498)))
   coarse[Vector2i(x,z)]=h
 DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(directory))
 var dependencies: Array[Resource]=[]
 for file in DirAccess.get_files_at(NATIVE+"DataFinished"):
  if not file.ends_with(".res"): continue
  var region: Terrain3DRegion=load(NATIVE+"DataFinished/"+file).duplicate(true)
  var loc: Vector2i=region.get_location()
  var old: Image=region.get_height_map()
  var heights:=Image.create(256,256,false,Image.FORMAT_RF)
  for iz in range(256):
   for ix in range(256):
    var x: int=(loc.x*256+ix)*2
    var z: int=(loc.y*256+iz)*2
    var gx: int=floori(float(x)/32)*32
    var gz: int=floori(float(z)/32)*32
    var u: float=float(x-gx)/32
    var v: float=float(z-gz)/32
    var a: float=coarse[Vector2i(gx,gz)]
    var b: float=coarse[Vector2i(gx+32,gz)]
    var c: float=coarse[Vector2i(gx,gz+32)]
    var d: float=coarse[Vector2i(gx+32,gz+32)]
    var h: float=a+(b-a)*u+(c-a)*v if u+v<=1 else d+(c-d)*(1-u)+(b-d)*(1-v)
    var p:=Vector2(x,z)
    var retain: float=1.0-smoothstep(16,34,path_distance(p,stream_path))
    for shelf in [[-245.0,-290.0,68.0,45.0],[255.0,-25.0,48.0,27.0]]:
     var q: float=sqrt(pow((x-shelf[0])/shelf[2],2)+pow((z-shelf[1])/shelf[3],2))
     retain=maxf(retain,1.0-smoothstep(1.1,1.65,q))
    h=lerpf(h,old.get_pixel(ix,iz).r,retain)
    heights.set_pixel(ix,iz,Color(h,0,0,1))
  region.set_height_map(heights)
  region.calc_height_range()
  region.set_modified(true)
  var path: String=directory+"/"+file
  var error: int=ResourceSaver.save(region,path,ResourceSaver.FLAG_COMPRESS)
  if error!=OK: return {"success":false,"error":error}
  region.take_over_path(path)
  dependencies.append(region)
 terrain.data_directory=directory
 terrain.set_meta("terrain_region_resources",dependencies)
 root.set_meta("surface_source_directory",directory)
 root.set_meta("surface_detail_distance",6.0)
 root.set_meta("surface_detail_error",0.25)
 for n in root.find_children("*","Node3D",true,false):
  if n.has_meta("beech_asset_root") and n.name!="RepairedCrossing":
   var p: Vector3=n.global_position
   n.global_position=Vector3(p.x,ground_at(Vector2(p.x,p.z)),p.z)
 var tree: Node3D=root.get_node("LandmarkTrees/LandmarkBeech3")
 tree.global_position=Vector3(-377,ground_at(Vector2(-377,267)),267)
 restore_route_definition()
 return {"success":true,"range":terrain.data.get_height_range()}

func restore_route_definition() -> Dictionary:
 var fresh: Node=load("res://Tools/Linvel/BeechValleyRevision.gd").new()
 paths=fresh.paths
 fresh.free()
 paths_are_rounded=false
 round_route_corners()
 resample_finished_paths()
 return {"success":true,"main_points":paths[0].size()}

func inspect_road_blockers() -> Dictionary:
 root=get_parent() as Node3D
 var boxes: Array=[]
 for c in root.find_children("*","CollisionShape3D",true,false):
  if c.shape is not BoxShape3D: continue
  var p: Vector3=c.global_position
  var size: Vector3=c.shape.size*c.global_basis.get_scale()
  if Vector2(p.x,p.z).distance_to(Vector2(-363,282))<18:
   boxes.append({"path":str(c.get_path()),"p":p,"size":size})
 return {"boxes":boxes}

func refresh_painted_assets() -> Dictionary:
 root=get_parent() as Node3D
 var ready:=prepare_revision_assets()
 if not ready.get("success",false): return ready
 var updated:=0
 for n in root.find_children("*","Node3D",true,false):
  var id: String=n.get_meta("asset_id","")
  if id.is_empty() or not n.has_node("Visual"): continue
  if id in ["old_fern_cluster","old_meadow_flowers"]:
   id="woodland_fern" if id=="old_fern_cluster" else "sunlit_herb"
   n.set_meta("asset_id",id)
   n.scale=Vector3.ONE*.85
  n.get_node("Visual").mesh=revision_meshes[id]
  n.get_node("Visual").material_override=load(ASSET_ROOT+"painted_forest.tres") if id.begins_with("old_") else revision_material
  updated+=1
 for n in root.get_node("Understory").get_children():
  var id: String="woodland_fern" if n.name.begins_with("woodland_fern") else "sunlit_herb"
  n.multimesh.mesh=revision_meshes[id]
  n.material_override=revision_material
 for n in root.get_node("Exploration/HerbTerrace").find_children("*","MeshInstance3D",true,false):
  n.mesh=revision_meshes.sunlit_herb
  n.material_override=revision_material
 var tree: Node3D=root.get_node("LandmarkTrees/LandmarkBeech3")
 tree.global_position=Vector3(-377,ground_at(Vector2(-377,267)),267)
 var timber: Node3D=root.get_node("Places/Woodcutters").find_children("*","Node3D",true,false).filter(func(n): return n.get_meta("asset_id","")=="seasoned_timber")[0]
 timber.global_position=Vector3(-359,ground_at(Vector2(-359,268)),268)
 for p in root.get_node("Places").get_children():
  if p.name=="RavineBridge": continue
  var center: Node3D=p.get_node("Center")
  var pos: Vector3=center.global_position
  center.global_position=Vector3(pos.x,ground_at(Vector2(pos.x,pos.z)),pos.z)
 restore_route_definition()
 return {"success":true,"updated_visuals":updated}

func polish_native_textures() -> Dictionary:
 root=get_parent() as Node3D
 if root.has_meta("beech_painted_tiles_finished"):
  return {"success":false,"error":"Saved texture painting preserved."}
 var terrain: Terrain3D=root.get_node("Geometry/Terrain")
 var labels: Array[String]=["ForestFloor","WoodlandTrack","ExposedStone","WetLeafLitter"]
 for id in range(4):
  var tile:=Image.create(256,256,false,Image.FORMAT_RGBA8)
  var rng:=RandomNumberGenerator.new()
  rng.seed=684300+id
  tile.fill(Color(.94,.97,.91,.8) if id==0 else Color(.91,.90,.87,.8) if id==3 else Color(.98,.97,.95,.8))
  # Broad brush washes and sparse physical marks, rather than high-frequency noise.
  for mark in range(230 if id in [0,3] else 110):
   var center:=Vector2(rng.randi_range(0,255),rng.randi_range(0,255))
   var radius:=Vector2(rng.randf_range(2,7),rng.randf_range(1,3)) if id in [0,3] else Vector2.ONE*rng.randf_range(1,4)
   var angle: float=rng.randf_range(-PI,PI)
   var color:=Color(1.04,.84,.60,.8) if id in [0,3] and mark%3==0 else Color(.80,.88,.76,.8) if id==0 else Color(.85,.84,.82,.8) if id==1 else Color(.82,.83,.79,.8) if id==2 else Color(.76,.78,.69,.8)
   for dz in range(-9,10):
    for dx in range(-9,10):
     var local:=Vector2(dx,dz).rotated(angle)
     var q: float=pow(local.x/radius.x,2)+pow(local.y/radius.y,2)
     if q>1: continue
     var ix: int=posmod(int(center.x)+dx,256)
     var iz: int=posmod(int(center.y)+dz,256)
     tile.set_pixel(ix,iz,tile.get_pixel(ix,iz).lerp(color,(1.0-smoothstep(.35,1.0,q))*.65))
  tile.generate_mipmaps()
  var texture:=ImageTexture.create_from_image(tile)
  var path: String=NATIVE+"Textures/"+labels[id]+"_albedo.res"
  ResourceSaver.save(texture,path,ResourceSaver.FLAG_COMPRESS)
  texture.take_over_path(path)
  terrain.assets.get_texture(id).albedo_texture=texture
 ResourceSaver.save(terrain.assets,NATIVE+"beech_assets.tres")
 root.set_meta("beech_painted_tiles_finished",true)
 return {"success":true,"textures":4,"resolution":256}

func round_route_corners() -> void:
 if paths_are_rounded: return
 for k in range(paths.size()):
  var path: Array=paths[k]
  var rounded: Array=[path[0]]
  for i in range(1,path.size()-1):
   if k==0 and i in [3,4,5,6]:
    rounded.append(path[i])
    continue
   var a:=Vector3(path[i-1][0],path[i-1][1],path[i-1][2])
   var b:=Vector3(path[i][0],path[i][1],path[i][2])
   var c:=Vector3(path[i+1][0],path[i+1][1],path[i+1][2])
   var before: Vector3=b.lerp(a,minf(.2,13.0/b.distance_to(a)))
   var after: Vector3=b.lerp(c,minf(.2,13.0/b.distance_to(c)))
   for p in [before,before*.25+b*.5+after*.25,after]: rounded.append([p.x,p.y,p.z])
  rounded.append(path[-1])
  paths[k]=rounded
 paths_are_rounded=true

func refine_native_terrain() -> Dictionary:
 root=get_parent() as Node3D
 var directory: String=NATIVE+"DataFinished"
 if DirAccess.dir_exists_absolute(ProjectSettings.globalize_path(directory)):
  return {"success":false,"error":"Finished native maps already exist; preserve manual sculpt and paint."}
 round_route_corners()
 DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(directory))
 var dependencies: Array[Resource]=[]
 for file in DirAccess.get_files_at(NATIVE+"Data"):
  if not file.ends_with(".res"): continue
  var region: Terrain3DRegion=load(NATIVE+"Data/"+file).duplicate(true)
  var encoded: String=file.trim_prefix("terrain3d").trim_suffix(".res")
  var loc:=Vector2i(encoded.substr(0,3).replace("_","").to_int(),encoded.substr(3,3).replace("_","").to_int())
  region.set_location(loc)
  var heights:=Image.create(256,256,false,Image.FORMAT_RF)
  var colors:=Image.create(256,256,false,Image.FORMAT_RGBA8)
  var bits:=PackedByteArray()
  bits.resize(256*256*4)
  for iz in range(256):
   for ix in range(256):
    var x: float=(loc.x*256+ix)*2.0
    var z: float=(loc.y*256+iz)*2.0
    heights.set_pixel(ix,iz,Color(native_height(x,z),0,0,1))
  for iz in range(256):
   for ix in range(256):
    var x: float=(loc.x*256+ix)*2.0
    var z: float=(loc.y*256+iz)*2.0
    var h: float=heights.get_pixel(ix,iz).r
    var sx: float=absf(heights.get_pixel(mini(ix+1,255),iz).r-heights.get_pixel(maxi(ix-1,0),iz).r)/4.0
    var sz: float=absf(heights.get_pixel(ix,mini(iz+1,255)).r-heights.get_pixel(ix,maxi(iz-1,0)).r)/4.0
    var slope: float=maxf(sx,sz)
    var road: float=paths_distance(Vector2(x,z))
    var wet: float=1.0-smoothstep(6.0,28.0,path_distance(Vector2(x,z),stream_path))
    var id: int=2 if slope>.9 else 1 if road<3.2 else 3 if wet>.6 else 0
    var color:=Color(.32,.43,.22).lerp(Color(.43,.52,.27),smoothstep(35,60,h)*.7)
    color=color.lerp(Color(.24,.32,.25),wet*.5)
    if id==2: color=Color(.46,.46,.40)
    if slope<.90: color=color.lerp(Color(.52,.42,.29),1.0-smoothstep(2.4,4.4,road))
    for spec in [[-311,-105,8.0],[-230,-45,5.0],[328,-341,4.0]]:
     var distance: float=Vector2(x,z).distance_to(Vector2(spec[0],spec[1]))
     color=color.lerp(Color(.15,.14,.11),1.0-smoothstep(spec[2]*.6,spec[2],distance))
    # Worn working ground remains a local composition rather than a giant brown field.
    var yard: float=Vector2(x,z).distance_to(Vector2(-337,258))
    color=color.lerp(Color(.48,.39,.27),(1.0-smoothstep(10,19,yard))*.65)
    var wash: float=.016*sin((x+z*.72)/43)+.009*sin((x*.37-z)/21)
    colors.set_pixel(ix,iz,Color(color.r+wash,color.g+wash,color.b+wash*.5,.8))
    var control: int=(id<<27)|2
    if absf(x)>500 or absf(z)>500: control|=4
    bits.encode_u32((iz*256+ix)*4,control)
  colors.generate_mipmaps()
  region.set_height_map(heights)
  region.set_color_map(colors)
  region.set_control_map(Image.create_from_data(256,256,false,Image.FORMAT_RF,bits))
  region.calc_height_range()
  region.set_modified(true)
  var path: String=directory+"/"+file
  var error: int=ResourceSaver.save(region,path,ResourceSaver.FLAG_COMPRESS)
  if error!=OK: return {"success":false,"error":error}
  region.take_over_path(path)
  dependencies.append(region)
 var terrain: Terrain3D=root.get_node("Geometry/Terrain")
 terrain.data_directory=directory
 terrain.set_meta("terrain_region_resources",dependencies)
 root.set_meta("surface_source_directory",directory)
 var assets:=Terrain3DAssets.new()
 for spec in [[0,"ForestFloor"],[1,"WoodlandTrack"],[2,"ExposedStone"],[3,"WetLeafLitter"]]: assets.set_texture(spec[0],native_tile(spec[0],spec[1]))
 terrain.assets=assets
 ResourceSaver.save(assets,NATIVE+"beech_assets.tres")
 assets.take_over_path(NATIVE+"beech_assets.tres")
 root.set_meta("beech_finished_terrain",true)
 return {"success":true,"regions":terrain.data.get_region_count(),"range":terrain.data.get_height_range()}

func finish_story_compositions() -> Dictionary:
 root=get_parent() as Node3D
 if root.has_meta("beech_story_details_finished"): return {"success":false,"error":"Finished story details preserved."}
 var ready:=prepare_revision_assets()
 if not ready.get("success",false): return ready
 var yard: Node3D=root.get_node("Places/Woodcutters")
 yard.get_node("TimberWorkingBay").global_position=Vector3(-342,ground_at(Vector2(-342,252)),252)
 revision_asset(yard,"split_board_deck",Vector2(-342,251),.18,1.0,"NewRaisedWorkDeck")
 revision_asset(yard,"forest_workbench",Vector2(-343,253),.18,1.0,"UsedWorkbench")
 revision_asset(yard,"worn_supply_bundle",Vector2(-340,247),.5,1.0,"WorkersSupplies")
 revision_asset(yard,"worn_supply_bundle",Vector2(-325,272),.3,.8,"ReadyForTransport")
 revision_asset(yard,"old_trail_sign",Vector2(-324,256),-.2,1.0,"YardRouteSign",false)
 revision_asset(root.get_node("Places/SunlitGlade"),"herb_drying_frame",Vector2(217,-42),.3,1.0,"HerbDryingRack")
 for p in [Vector2(243,-12),Vector2(246,-13),Vector2(244,-10),Vector2(247,-10)]:
  revision_asset(root.get_node("Exploration/HerbTerrace"),"sunlit_herb",p,.3,1.7,"SunlitPlant",false)
 for spec in [["Places/KagerCamp",328,-341],["EncounterEnvironment",-171,-57],["EncounterEnvironment",345,47],["Exploration/EmptyStand",-201,62]]:
  revision_asset(root.get_node(spec[0]),"forest_firepit",Vector2(spec[1],spec[2]),0,1.0,"UsedHearth",false)
 revision_asset(root.get_node("Places/KagerCamp"),"worn_supply_bundle",Vector2(344,-344),0,.9,"FoundSupplies")
 revision_asset(root.get_node("Places/CharcoalTerraces"),"forest_workbench",Vector2(-277,-124),.2,.75,"CharcoalSortingBench")
 revision_asset(root.get_node("Exploration/RainCover"),"worn_supply_bundle",Vector2(-47,215),1,.6,"DryBundle")
 var details := Node3D.new()
 native_add(root,details,"TrailDetails")
 # Roadside compositions are within the actual camera, with no blocking canopy bases.
 var details_count := 0
 for path in paths:
  for i in range(path.size()-1):
   var a:=Vector2(path[i][0],path[i][2])
   var b:=Vector2(path[i+1][0],path[i+1][2])
   var tangent: Vector2=(b-a).normalized()
   var normal:=Vector2(-tangent.y,tangent.x)
   var steps: int=maxi(1,floori(a.distance_to(b)/13.0))
   for j in range(steps):
    var center: Vector2=a.lerp(b,(float(j)+.3)/steps)
    for side in [-1,1]:
     var p: Vector2=center+normal*side*(4.8+float(j%3)*.6)
     if absf(p.x)>492 or absf(p.y)>492: continue
     if path_distance(p,stream_path)<6: continue
     for v in [Vector2(-108,129),Vector2(-342,252),Vector2(325,-330)]:
      if p.distance_to(v)<13: continue
     var id: String="old_hazel_shrub" if j%7==0 else "old_meadow_flowers" if j%3==0 else "old_fern_cluster"
     if not revision_meshes.has(id): continue
     revision_asset(details,id,p,float(j)*1.1,.75 if id=="old_hazel_shrub" else 1.0,"RoadsidePlant",false)
     details_count+=1
 for spec in [[-298,261,.3],[-333,329,1.0],[-240,-47,.6],[91,-205,1.2],[284,-269,.5]]:
  revision_asset(details,"old_fallen_beech",Vector2(spec[0],spec[1]),spec[2],.7,"WorkedOrFallenLog",false)
 # Keep an existing asset's X/Z and yaw while conforming it to the final maps.
 for n in root.find_children("*","Node3D",true,false):
  if not n.has_meta("beech_asset_root") or n.name=="RepairedCrossing": continue
  var p: Vector3=n.global_position
  n.global_position=Vector3(p.x,ground_at(Vector2(p.x,p.z)),p.z)
 root.set_meta("beech_story_details_finished",true)
 resample_finished_paths()
 return {"success":true,"roadside_details":details_count,"bodies":root.find_children("*","StaticBody3D",true,false).size()}

func resample_finished_paths() -> void:
 var data: Dictionary=JSON.parse_string(FileAccess.get_file_as_string(R+"layout.json"))
 var sampled: Array=[]
 var total: float=0
 var axis:=Vector2(48,-36).normalized()
 for k in range(paths.size()):
  var samples: Array=[]
  var path: Array=paths[k]
  for i in range(path.size()-1):
   var a:=Vector3(path[i][0],path[i][1],path[i][2])
   var b:=Vector3(path[i+1][0],path[i+1][1],path[i+1][2])
   var steps: int=ceili(a.distance_to(b)/2)
   for j in range(steps):
    var p: Vector3=a.lerp(b,float(j)/steps)
    var delta:=Vector2(p.x+108,p.z-129)
    p.y=28.1 if k==0 and absf(delta.dot(axis))<18 and absf(delta.cross(axis))<2.5 else ground_at(Vector2(p.x,p.z))
    samples.append([p.x,p.y,p.z])
  var last: Array=path[-1]
  samples.append([last[0],ground_at(Vector2(last[0],last[2])),last[2]])
  sampled.append(samples)
  if k==0:
   for i in range(samples.size()-1):
    total+=Vector3(samples[i][0],samples[i][1],samples[i][2]).distance_to(Vector3(samples[i+1][0],samples[i+1][1],samples[i+1][2]))
 data.paths=paths
 data.route=paths[0]
 data.route_samples=sampled
 var public_samples:=PackedVector3Array()
 for route in sampled:
  for p in route: public_samples.append(Vector3(p[0],p[1],p[2]))
 root.set_meta("surface_road_samples",public_samples)
 root.remove_meta("surface_road_profile")
 data.route_metres=total
 data.walking_seconds=total/3.5
 data.spawn=[-425,ground_at(Vector2(-425,330)),330]
 var links: Dictionary={}
 for point in [Vector2(-445,330),Vector2(-435,330),Vector2(-425,330),Vector2(-420,336),Vector2(-414,321),Vector2(-445,-465),Vector2(-432,-456),Vector2(248,50),Vector2(-323,228),Vector2(-302,241),Vector2(-296,241)]:
  links[str(int(point.x))+","+str(int(point.y))]=ground_at(point)
 data.heights_at_public_points=links
 FileAccess.open(R+"layout.json",FileAccess.WRITE).store_string(JSON.stringify(data,"\t"))
 root.set_meta("main_route_metres",total)















func finish_forest_composition() -> Dictionary:
 root=get_parent() as Node3D
 if root.has_meta("beech_forest_composition_finished"):
  return {"success":false,"error":"Saved forest compositions preserved."}
 restore_route_definition()
 var ready:=prepare_revision_assets()
 if not ready.get("success",false): return ready
 var removed:=0
 for n in root.get_node("TrailDetails").get_children():
  var id: String=n.get_meta("asset_id","")
  if id not in ["woodland_fern","sunlit_herb"]: continue
  var p:=Vector2(n.global_position.x,n.global_position.z)
  var working: bool=p.distance_to(Vector2(-330,260))<19 or p.distance_to(Vector2(325,-330))<17
  if paths_distance(p)<4.3 or working:
   n.free()
   removed+=1
 # Retain existing placement transforms; only remove foliage in used lanes/yards.
 var removed_instances:=0
 for node in root.get_node("Understory").get_children():
  var old: MultiMesh=node.multimesh
  var kept: Array[Transform3D]=[]
  for i in range(old.instance_count):
   var tr: Transform3D=old.get_instance_transform(i)
   var world: Vector3=node.to_global(tr.origin)
   var p:=Vector2(world.x,world.z)
   if paths_distance(p)<5.0 or p.distance_to(Vector2(-330,260))<19 or p.distance_to(Vector2(325,-330))<17:
    removed_instances+=1
    continue
   kept.append(tr)
  var saved:=MultiMesh.new()
  saved.transform_format=MultiMesh.TRANSFORM_3D
  saved.mesh=old.mesh
  saved.instance_count=kept.size()
  for i in range(kept.size()): saved.set_instance_transform(i,kept[i])
  node.multimesh=saved
  node.material_override=revision_material
 # Authored groups close views along outer forest curves and frame clearings.
 # Fixed candidates follow the dry ridge, ravine shoulders and eastern woods.
 var candidates: Array[Vector2]=[Vector2(-398,195),Vector2(-355,168),Vector2(-295,145),Vector2(-236,155),Vector2(-185,192),Vector2(-10,86),Vector2(35,76),Vector2(130,-35),Vector2(126,-133),Vector2(110,-223),Vector2(215,-221),Vector2(265,-330),Vector2(380,-260),Vector2(-267,-57),Vector2(-328,-150),Vector2(-308,-240),Vector2(-282,-305),Vector2(-405,-408),Vector2(-415,250),Vector2(-415,375),Vector2(-290,395),Vector2(-180,300),Vector2(-125,315),Vector2(15,295),Vector2(140,245),Vector2(150,110),Vector2(228,48),Vector2(310,-75),Vector2(405,-190),Vector2(410,-298),Vector2(373,-346),Vector2(225,-365),Vector2(102,-300),Vector2(12,-280),Vector2(-108,-270),Vector2(-270,-372),Vector2(-407,-203),Vector2(-442,-131),Vector2(-336,15),Vector2(-213,24)]
 var all_roots: Array=root.find_children("*","Node3D",true,false).filter(func(n): return n.has_meta("beech_asset_root"))
 var parent:=Node3D.new()
 native_add(root,parent,"ComposedForestEdges")
 var added:=0
 for p in candidates:
  if added>=18: break
  if paths_distance(p)<22 or path_distance(p,stream_path)<18: continue
  var slope: float=maxf(absf(ground_at(p+Vector2(6,0))-ground_at(p-Vector2(6,0))),absf(ground_at(p+Vector2(0,6))-ground_at(p-Vector2(0,6))))/12
  if slope>.28: continue
  var skip:=false
  for spec in place_specs:
   if p.distance_to(Vector2(spec.p.x,spec.p.z))<spec.radius+18: skip=true
  for n in all_roots:
   var pos: Vector3=n.global_position
   if p.distance_to(Vector2(pos.x,pos.z))<23: skip=true
  if skip: continue
  var group:=revision_asset(parent,"beech_copse_"+str(added%2),p,float(added)*.41,1.15,"WoodlandEdge%02d"%added)
  all_roots.append(group)
  added+=1
 root.set_meta("beech_forest_composition_finished",true)
 root.set_meta("beech_added_forest_edges",added)
 return {"success":true,"groups":added,"removed_roadside_plants":removed,"removed_understory":removed_instances,"bodies":root.find_children("*","StaticBody3D",true,false).size()}

func refine_neighbor_road_export() -> Dictionary:
 # Add only public precision metadata; preserve all saved neighbor geometry.
 var scene_path: String="res://Scenes/Regions/Linvel/FoxHills.tscn"
 var packed: PackedScene=ResourceLoader.load(scene_path,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE_DEEP)
 var copy: Node3D=packed.instantiate()
 var profile: Dictionary=JSON.parse_string(FileAccess.get_file_as_string("res://Assets/World/Linvel/FoxHills/layout.json"))
 var samples:=PackedVector3Array()
 for branch in profile.road_samples:
  for p in branch.samples: samples.append(Vector3(p[0],p[1],p[2]))
 copy.set_meta("surface_road_samples",samples)
 copy.set_meta("surface_snap_native_heights",true)
 var error: int=packed.pack(copy)
 if error==OK: error=ResourceSaver.save(packed,scene_path)
 copy.free()
 return {"success":error==OK,"samples":samples.size(),"error":error}


func apply_painted_ground_atlas() -> Dictionary:
 root=get_parent() as Node3D
 if root.has_meta("beech_ground_atlas_finished"):
  return {"success":false,"error":"Saved material choices preserved."}
 var terrain: Terrain3D=root.get_node("Geometry/Terrain")
 var source:=Image.load_from_file(ProjectSettings.globalize_path(NATIVE+"Textures/painted_ground_atlas.png"))
 if source==null: return {"success":false,"error":"Missing generated atlas source."}
 var width: int=source.get_width()/2
 var height: int=source.get_height()/2
 var labels: Array[String]=["ForestFloor","WoodlandTrack","ExposedStone","WetLeafLitter"]
 for id in range(4):
  var tile: Image=source.get_region(Rect2i((id%2)*width,(id/2)*height,width,height))
  tile.resize(512,512,Image.INTERPOLATE_LANCZOS)
  tile.generate_mipmaps()
  var texture:=ImageTexture.create_from_image(tile)
  var path: String=NATIVE+"Textures/"+labels[id]+"_painted.res"
  ResourceSaver.save(texture,path,ResourceSaver.FLAG_COMPRESS)
  texture.take_over_path(path)
  var asset: Terrain3DTextureAsset=terrain.assets.get_texture(id)
  asset.albedo_texture=texture
  asset.uv_scale=.33
  asset.normal_depth=0
 ResourceSaver.save(terrain.assets,NATIVE+"beech_assets.tres")
 terrain.material.shader_override=load(NATIVE+"painted_beech_terrain.gdshader")
 ResourceSaver.save(terrain.material,NATIVE+"beech_material.tres")
 root.set_meta("beech_ground_atlas_finished",true)
 return {"success":true,"tiles":4,"resolution":512,"source_dimensions":Vector2i(width*2,height*2)}




func enable_native_cliff_visibility() -> Dictionary:
 root=get_parent() as Node3D
 root.remove_meta("surface_visibility_columns")
 root.set_meta("surface_solid_terrain_los",false)
 root.set_meta("surface_native_cliff_faces",true)
 return {"success":true,"source":"Terrain3D LOD0 cliff faces"}

func ensure_region_preview_camera() -> Dictionary:
 root=get_parent() as Node3D
 var camera: Camera3D=root.get_node_or_null("RegionPreviewCamera")
 if camera==null:
  camera=Camera3D.new()
  native_add(root,camera,"RegionPreviewCamera")
 camera.add_to_group("region_preview_camera",true)
 var p:=Vector3(-334,ground_at(Vector2(-334,268)),268)
 camera.global_position=p+Vector3(0,24,24)
 camera.look_at(p)
 camera.fov=50
 camera.far=800
 camera.current=true
 camera.editor_description="Standalone F6 inspection camera. The network client removes it before loading this region."
 return {"success":true,"position":camera.position,"role":"standalone inspection only"}

func build_surface_paint_resources() -> Dictionary:
 root=get_parent() as Node3D
 if root.get_meta("beech_surface_paint_polished",false):
  return {"success":false,"error":"Existing painted material edits preserved."}
 var image:=Image.load_from_file(ProjectSettings.globalize_path(R+"painted_surface_atlas.png"))
 if image==null: return {"success":false,"error":"Missing painted source atlas."}
 var half:=Vector2i(image.get_width()/2,image.get_height()/2)
 var material:=ShaderMaterial.new()
 material.shader=load(R+"painted_vertex.gdshader")
 for spec in [["bark_paint",0,0],["wood_paint",1,0],["stone_paint",0,1],["cloth_paint",1,1]]:
  var tile: Image=image.get_region(Rect2i(Vector2i(spec[1],spec[2])*half,half))
  tile.resize(512,512,Image.INTERPOLATE_LANCZOS)
  tile.generate_mipmaps()
  var texture:=ImageTexture.create_from_image(tile)
  var path: String=R+spec[0]+".res"
  ResourceSaver.save(texture,path,ResourceSaver.FLAG_COMPRESS)
  texture.take_over_path(path)
  material.set_shader_parameter(spec[0],texture)
 ResourceSaver.save(material,R+"painted_forest.tres")
 material.take_over_path(R+"painted_forest.tres")
 revision_material=material
 root.set_meta("beech_surface_paint_polished",true)
 return {"success":true,"textures":4,"single_shared_material":true}

func refresh_polished_geometry() -> Dictionary:
 root=get_parent() as Node3D
 var ready:=prepare_revision_assets()
 if not ready.get("success",false): return ready
 var count:=0
 for asset in root.find_children("*","Node3D",true,false):
  if not asset.has_meta("beech_asset_root"): continue
  var id: String=asset.get_meta("asset_id","")
  if not revision_meshes.has(id): continue
  var visual: MeshInstance3D=asset.get_node("Visual")
  visual.mesh=revision_meshes[id]
  if not id.begins_with("old_"): visual.material_override=revision_material
  count+=1
 for node in root.get_node("Understory").get_children():
  var old: MultiMesh=node.multimesh
  var path: String=old.mesh.resource_path
  var id: String=path.get_file().get_basename()
  if revision_meshes.has(id): old.mesh=revision_meshes[id]
  node.material_override=revision_material
 return {"success":true,"updated_placements":count,"models":revision_specs.size()}

func fill_authored_forest_bands() -> Dictionary:
 root=get_parent() as Node3D
 if root.has_node("AuthoredForestBands"):
  return {"success":false,"error":"Saved forest placements preserved."}
 restore_route_definition()
 var ready:=prepare_revision_assets()
 if not ready.get("success",false): return ready
 var parent:=Node3D.new()
 native_add(root,parent,"AuthoredForestBands")
 # These six fixed ecological/compositional masks extend the approved woodland
 # belts. They do not choose roads, stories, clearings or encounter locations.
 var bands: Array=[
  ["YoungWesternEdge",Vector2(-355,160),Vector2(70,90),34,11.0],
  ["DryRidgeBeeches",Vector2(-365,-210),Vector2(85,110),36,12.0],
  ["CoolRavineWoods",Vector2(-160,10),Vector2(85,80),32,11.0],
  ["EasternInterior",Vector2(300,-130),Vector2(95,120),36,12.0],
  ["SouthernLeafBanks",Vector2(20,240),Vector2(90,80),34,11.0],
  ["NorthernForestFringe",Vector2(235,-380),Vector2(135,65),38,12.0]]
 var existing: Array=root.find_children("*","Node3D",true,false).filter(func(n): return n.has_meta("beech_asset_root"))
 var added:=0
 var results: Dictionary={}
 for bi in range(bands.size()):
  var band: Array=bands[bi]
  var center: Vector2=band[1]
  var radius: Vector2=band[2]
  var step: float=band[4]
  var candidates: Array[Vector2]=[]
  for iz in range(-int(radius.y/step),int(radius.y/step)+1):
   for ix in range(-int(radius.x/step),int(radius.x/step)+1):
    var p:=center+Vector2(ix*step+sin(iz*1.61+ix*.93+bi)*2.6,iz*step+cos(ix*1.47+iz*.62+bi)*2.2)
    var local: Vector2=(p-center)/radius
    if local.length_squared()>1: continue
    candidates.append(p)
  candidates.sort_custom(func(a,b): return a.distance_squared_to(center)<b.distance_squared_to(center))
  var group:=Node3D.new()
  native_add(parent,group,band[0])
  var placed:=0
  for p in candidates:
   if placed>=band[3]: break
   if paths_distance(p)<6.5 or path_distance(p,stream_path)<16: continue
   var skip:=false
   for place in place_specs:
    if p.distance_to(Vector2(place.p.x,place.p.z))<place.radius+6: skip=true
   for node in existing:
    var q: Vector3=node.global_position
    var exclusion: float=12.0 if str(node.get_meta("asset_id","")).begins_with("beech_copse") else 5.5
    if p.distance_to(Vector2(q.x,q.z))<exclusion: skip=true
   if skip: continue
   var slope: float=maxf(absf(ground_at(p+Vector2(2,0))-ground_at(p-Vector2(2,0))),absf(ground_at(p+Vector2(0,2))-ground_at(p-Vector2(0,2))))/4
   if slope>.42: continue
   var ids: Array=[4,0,3,1,5,2,6]
   var variant: int=ids[(added+bi*2)%ids.size()]
   var scale_factor: float=.86+.22*(sin(float(added)*2.399)*.5+.5)
   if bi==0: variant=4 if placed%3!=0 else 0
   if bi==1 and placed%3==0: variant=6
   var tree:=revision_asset(group,"beech_canopy_"+str(variant),p,float(added)*2.399,scale_factor,"Beech%03d"%added)
   existing.append(tree)
   added+=1
   placed+=1
  results[band[0]]=placed
 # Each tree keeps its own real collider. Its exact closed faces share the
 # existing bounded static face budget, without expanding the 256 box cap.
 var migrated:=0
 for body in root.find_children("*","StaticBody3D",true,false):
  if str(body.get_meta("collision_part",""))!="trunk": continue
  body.add_to_group("surface_exact_occluder",true)
  migrated+=1
 root.set_meta("beech_polished_forest_tree_count",added)
 root.set_meta("beech_exact_trunk_count",migrated)
 return {"success":true,"new_trees":added,"bands":results,"exact_trunks":migrated,"physical_bodies":root.find_children("*","StaticBody3D",true,false).size()}

func configure_forest_stream_surface() -> Dictionary:
 root=get_parent() as Node3D
 if root.get_meta("beech_stream_surface_polished",false):
  return {"success":false,"error":"Existing water material edits preserved."}
 var material:=ShaderMaterial.new()
 material.shader=load(R+"forest_stream.gdshader")
 ResourceSaver.save(material,R+"forest_stream.tres")
 material.take_over_path(R+"forest_stream.tres")
 root.get_node("Geometry/ForestStream").material_override=material
 root.set_meta("beech_stream_surface_polished",true)
 return {"success":true,"animated_stream":true,"extra_collisions":0}

func refine_main_approach_forests() -> Dictionary:
 root=get_parent() as Node3D
 if root.get_meta("beech_main_approach_forests_refined",false):
  return {"success":false,"error":"Saved approach forest edits preserved."}
 restore_route_definition()
 var moved:=0
 for spec in [["YoungWesternEdge",Vector2(-260,225),Vector2(80,65),34,11.0],["CoolRavineWoods",Vector2(-68,58),Vector2(75,70),32,11.0]]:
  var group: Node3D=root.get_node("AuthoredForestBands/"+spec[0])
  if group.get_child_count()!=spec[3]:
   return {"success":false,"error":"Forest band changed; preserve manual placements."}
  var moving: Array=group.get_children()
  var existing: Array=root.find_children("*","Node3D",true,false).filter(func(n): return n.has_meta("beech_asset_root") and not group.is_ancestor_of(n))
  var center: Vector2=spec[1]
  var radius: Vector2=spec[2]
  var step: float=spec[4]
  var candidates: Array[Vector2]=[]
  for iz in range(-int(radius.y/step),int(radius.y/step)+1):
   for ix in range(-int(radius.x/step),int(radius.x/step)+1):
    var p:=center+Vector2(ix*step+sin(iz*1.61+ix*.93)*2.6,iz*step+cos(ix*1.47+iz*.62)*2.2)
    if ((p-center)/radius).length_squared()<=1: candidates.append(p)
  candidates.sort_custom(func(a,b): return a.distance_squared_to(center)<b.distance_squared_to(center))
  var chosen: Array[Vector2]=[]
  for p in candidates:
   if chosen.size()>=moving.size(): break
   if paths_distance(p)<6.5 or path_distance(p,stream_path)<16: continue
   var skip:=false
   for place in place_specs:
    if p.distance_to(Vector2(place.p.x,place.p.z))<place.radius+6: skip=true
   for node in existing:
    var q: Vector3=node.global_position
    var distance: float=12.0 if str(node.get_meta("asset_id","")).begins_with("beech_copse") else 5.5
    if p.distance_to(Vector2(q.x,q.z))<distance: skip=true
   var slope: float=maxf(absf(ground_at(p+Vector2(2,0))-ground_at(p-Vector2(2,0))),absf(ground_at(p+Vector2(0,2))-ground_at(p-Vector2(0,2))))/4
   if skip or slope>.42: continue
   chosen.append(p)
  if chosen.size()!=moving.size(): return {"success":false,"error":"Cannot safely frame the main route; existing band retained."}
  for i in range(moving.size()):
   var p: Vector2=chosen[i]
   moving[i].global_position=Vector3(p.x,ground_at(p),p.y)
   moved+=1
 root.set_meta("beech_main_approach_forests_refined",true)
 return {"success":true,"moved_own_new_trees":moved,"road_clearance_m":6.5}

func build_leaf_paint_resource() -> Dictionary:
 root=get_parent() as Node3D
 if FileAccess.file_exists(R+"leaf_paint.res"):
  return {"success":false,"error":"Existing leaf paint preserved."}
 var image:=Image.load_from_file(ProjectSettings.globalize_path(R+"painted_leaf_tile.png"))
 if image==null: return {"success":false,"error":"Missing original leaf tile."}
 image.resize(512,512,Image.INTERPOLATE_LANCZOS)
 image.generate_mipmaps()
 var texture:=ImageTexture.create_from_image(image)
 var path: String=R+"leaf_paint.res"
 var error:=ResourceSaver.save(texture,path,ResourceSaver.FLAG_COMPRESS)
 if error!=OK: return {"success":false,"error":error}
 texture.take_over_path(path)
 var material: ShaderMaterial=load(R+"painted_forest.tres")
 material.set_shader_parameter("leaf_paint",texture)
 ResourceSaver.save(material,R+"painted_forest.tres")
 revision_material=material
 root.set_meta("beech_leaf_paint_polished",true)
 return {"success":true,"size":image.get_size(),"mipmaps":image.has_mipmaps(),"shared_material":true}

func restore_painted_material_bindings() -> Dictionary:
 root=get_parent() as Node3D
 var material: ShaderMaterial=ResourceLoader.load(R+"painted_forest.tres","ShaderMaterial",ResourceLoader.CACHE_MODE_IGNORE_DEEP)
 material.shader=ResourceLoader.load(R+"painted_vertex.gdshader","Shader",ResourceLoader.CACHE_MODE_IGNORE)
 var bindings: Dictionary={}
 for parameter in ["bark_paint","wood_paint","stone_paint","cloth_paint","leaf_paint"]:
  var styled: bool=root.get_meta("beech_anime_texture_pass",false)
  var style_files: Dictionary={"bark_paint":"SilverBark","leaf_paint":"LeafClusters","stone_paint":"GroundRock"}
  var path: String=R+"StylePass/"+style_files[parameter]+".res" if styled and style_files.has(parameter) else R+parameter+".res"
  var texture: Texture2D=load(path)
  material.set_shader_parameter(parameter,texture)
  bindings[parameter]=material.get_shader_parameter(parameter)!=null
 ResourceSaver.save(material,R+"painted_forest.tres")
 material.take_over_path(R+"painted_forest.tres")
 revision_material=material
 return {"success":true,"bindings":bindings,"uniforms":material.shader.get_shader_uniform_list()}

func persist_forest_stream_geometry() -> Dictionary:
 root=get_parent() as Node3D
 var node: MeshInstance3D=root.get_node("Geometry/ForestStream")
 if node.mesh is ArrayMesh or node.mesh.get_surface_count()>0:
  return {"success":false,"error":"Existing authored water geometry preserved."}
 var terrain: Terrain3D=root.get_node("Geometry/Terrain")
 var builder:=SurfaceTool.new()
 builder.begin(Mesh.PRIMITIVE_TRIANGLES)
 var triangles:=0
 for i in range(stream_path.size()-1):
  var a:=Vector2(stream_path[i][0],stream_path[i][1])
  var b:=Vector2(stream_path[i+1][0],stream_path[i+1][1])
  var axis: Vector2=(b-a).normalized()
  var right:=Vector2(-axis.y,axis.x)*1.45
  var steps: int=ceili(a.distance_to(b)/3)
  for j in range(steps):
   var c: Vector2=a.lerp(b,float(j)/steps)
   var d: Vector2=a.lerp(b,float(j+1)/steps)
   var y0: float=lerpf(29.0,12.5,clampf((c.y+160)/660.0,0.0,1.0))
   var y1: float=lerpf(29.0,12.5,clampf((d.y+160)/660.0,0.0,1.0))
   # Keep the authored shallow ford's walking surface above the water.
   if c.distance_to(Vector2(-45,310))<8: y0=minf(y0,terrain.data.get_height(Vector3(c.x,0,c.y))-.08)
   if d.distance_to(Vector2(-45,310))<8: y1=minf(y1,terrain.data.get_height(Vector3(d.x,0,d.y))-.08)
   for v in [Vector3(c.x-right.x,y0,c.y-right.y),Vector3(d.x-right.x,y1,d.y-right.y),Vector3(c.x+right.x,y0,c.y+right.y),Vector3(c.x+right.x,y0,c.y+right.y),Vector3(d.x-right.x,y1,d.y-right.y),Vector3(d.x+right.x,y1,d.y+right.y)]:
    builder.set_normal(Vector3.UP)
    builder.add_vertex(v)
   triangles+=2
 var mesh:=builder.commit()
 var path: String=R+"Meshes/forest_stream.res"
 var error:=ResourceSaver.save(mesh,path,ResourceSaver.FLAG_COMPRESS)
 if error!=OK: return {"success":false,"error":error}
 mesh.take_over_path(path)
 node.mesh=mesh
 root.set_meta("beech_stream_mesh_persisted",true)
 return {"success":true,"triangles":triangles,"bounds":mesh.get_aabb(),"collisions_added":0}

func compose_low_forest_regrowth() -> Dictionary:
 root=get_parent() as Node3D
 if root.has_node("AuthoredForestRegrowth"):
  return {"success":false,"error":"Saved regrowth placements preserved."}
 var ready:=prepare_revision_assets()
 if not ready.get("success",false): return ready
 restore_route_definition()
 var parent:=Node3D.new()
 native_add(root,parent,"AuthoredForestRegrowth")
 var count:=0
 # Low flexible regrowth belongs around the six saved woodland bands. The
 # authored road/stream/working-place margins take priority over vegetation.
 for tree in root.get_node("AuthoredForestBands").get_children():
  for trunk in tree.get_children():
   if not trunk.has_meta("beech_asset_root"): continue
   var base: Vector2=Vector2(trunk.position.x,trunk.position.z)
   for side in range(2):
    var angle: float=float(count)*2.399+side*2.1
    var p:=base+Vector2(cos(angle),sin(angle))*(3.0+side*.7)
    if paths_distance(p)<6.5 or path_distance(p,stream_path)<6: continue
    var blocked:=false
    for place in place_specs:
     if p.distance_to(Vector2(place.p.x,place.p.z))<place.radius+4: blocked=true
    var slope: float=maxf(absf(ground_at(p+Vector2(1,0))-ground_at(p-Vector2(1,0))),absf(ground_at(p+Vector2(0,1))-ground_at(p-Vector2(0,1))))/2
    if blocked or slope>.45: continue
    var bush:=revision_asset(parent,"beech_understory_bush",p,angle,.85+float(count%4)*.12,"Regrowth%03d"%count,false)
    bush.get_node("Visual").visibility_range_end=110
    bush.get_node("Visual").visibility_range_end_margin=20
    count+=1
 root.set_meta("beech_low_regrowth_composed",true)
 return {"success":true,"bushes":count,"road_clearance_m":6.5,"collisions_added":0}

func refine_saved_stream_edges() -> Dictionary:
 root=get_parent() as Node3D
 if root.get_meta("beech_stream_edges_refined",false):
  return {"success":false,"error":"Saved water edge edits preserved."}
 var node: MeshInstance3D=root.get_node("Geometry/ForestStream")
 if not node.mesh is ArrayMesh: return {"success":false,"error":"Persist the stream mesh first."}
 var arrays: Array=node.mesh.surface_get_arrays(0)
 var vertices: PackedVector3Array=arrays[Mesh.ARRAY_VERTEX]
 var original:=vertices.duplicate()
 var uv:=PackedVector2Array()
 uv.resize(vertices.size())
 for i in range(vertices.size()):
  var start: int=(i/6)*6
  var corner: int=i%6
  var centre: Vector3=(original[start]+original[start+2])*.5 if corner in [0,2,3] else (original[start+1]+original[start+5])*.5
  var radial: Vector3=original[i]-centre
  var width: float=.94+.1*sin(centre.z*.09)+.07*sin(centre.x*.13)
  var shift:=Vector3(radial.z,0,-radial.x).normalized()*.06*sin(centre.z*.71+centre.x*.37)
  vertices[i]=centre+radial*width+shift
  uv[i]=Vector2(1.0 if corner in [2,3,5] else 0.0,centre.z*.11)
 arrays[Mesh.ARRAY_VERTEX]=vertices
 arrays[Mesh.ARRAY_TEX_UV]=uv
 var mesh:=ArrayMesh.new()
 mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES,arrays)
 var path: String=R+"Meshes/forest_stream.res"
 var error:=ResourceSaver.save(mesh,path,ResourceSaver.FLAG_COMPRESS)
 if error!=OK: return {"success":false,"error":error}
 mesh.take_over_path(path)
 node.mesh=mesh
 root.set_meta("beech_stream_edges_refined",true)
 return {"success":true,"vertices":vertices.size(),"collisions_added":0}

func apply_hand_painted_style_pass() -> Dictionary:
 root=get_parent() as Node3D
 if root.get_meta("beech_anime_texture_pass",false):
  return {"success":false,"error":"Saved style pass edits preserved."}
 var directory: String=R+"StylePass/"
 var ground:=Image.load_from_file(ProjectSettings.globalize_path(directory+"ground_atlas.png"))
 var objects:=Image.load_from_file(ProjectSettings.globalize_path(directory+"tree_water_atlas.png"))
 if ground==null or objects==null: return {"success":false,"error":"Missing original style atlas."}
 var images: Array[Image]=[ground,objects]
 var labels: Array[String]=["GroundGrass","GroundPath","GroundRock","DampSoil","LeafClusters","SilverBark","WaterColour","WaterFoam"]
 var tiles: Array[Texture2D]=[]
 for i in range(labels.size()):
  var source: Image=images[i/4]
  var half:=Vector2i(source.get_width()/2,source.get_height()/2)
  var cell:=i%4
  var tile: Image=source.get_region(Rect2i(Vector2i(cell%2,cell/2)*half,half))
  tile.resize(512,512,Image.INTERPOLATE_LANCZOS)
  tile.generate_mipmaps()
  var texture:=ImageTexture.create_from_image(tile)
  var path: String=directory+labels[i]+".res"
  var error:=ResourceSaver.save(texture,path,ResourceSaver.FLAG_COMPRESS)
  if error!=OK: return {"success":false,"error":error}
  texture.take_over_path(path)
  tiles.append(texture)
 var terrain: Terrain3D=root.get_node("Geometry/Terrain")
 var scales: Array[float]=[.18,.20,.14,.20]
 for i in range(4):
  var asset: Terrain3DTextureAsset=terrain.assets.get_texture(i)
  asset.albedo_texture=tiles[i]
  asset.uv_scale=scales[i]
  asset.normal_depth=0
  asset.ao_strength=0
 ResourceSaver.save(terrain.assets,NATIVE+"beech_assets.tres")
 var material: ShaderMaterial=ResourceLoader.load(R+"painted_forest.tres","ShaderMaterial",ResourceLoader.CACHE_MODE_IGNORE_DEEP)
 material.shader=ResourceLoader.load(R+"painted_vertex.gdshader","Shader",ResourceLoader.CACHE_MODE_IGNORE)
 material.set_shader_parameter("focus_world",Vector3.ONE*-10000)
 material.set_shader_parameter("bark_paint",tiles[5])
 material.set_shader_parameter("leaf_paint",tiles[4])
 material.set_shader_parameter("stone_paint",tiles[2])
 material.set_shader_parameter("wood_paint",load(R+"wood_paint.res"))
 material.set_shader_parameter("cloth_paint",load(R+"cloth_paint.res"))
 ResourceSaver.save(material,R+"painted_forest.tres")
 material.take_over_path(R+"painted_forest.tres")
 revision_material=material
 var assigned:=0
 for visual in root.find_children("*","GeometryInstance3D",true,false):
  if visual.material_override is ShaderMaterial and visual.material_override.shader.resource_path==R+"painted_vertex.gdshader":
   visual.material_override=material
   assigned+=1
 var stream:=ShaderMaterial.new()
 stream.shader=ResourceLoader.load(R+"forest_stream.gdshader","Shader",ResourceLoader.CACHE_MODE_IGNORE)
 stream.set_shader_parameter("water_paint",tiles[6])
 stream.set_shader_parameter("foam_paint",tiles[7])
 ResourceSaver.save(stream,R+"forest_stream.tres")
 stream.take_over_path(R+"forest_stream.tres")
 root.get_node("Geometry/ForestStream").material_override=stream
 root.set_meta("beech_anime_texture_pass",true)
 return {"success":true,"textures":tiles.size(),"updated_visuals":assigned,"ground_grass_drawn":false,"height_or_collision_changes":0}

func inspect_style_surfaces() -> Dictionary:
 root=get_parent() as Node3D
 var terrain: Terrain3D=root.get_node("Geometry/Terrain")
 var tiles: Array=[]
 for i in range(4):
  var texture: Texture2D=terrain.assets.get_texture(i).albedo_texture
  var image:=texture.get_image()
  tiles.append({"id":i,"path":texture.resource_path,"format":image.get_format(),"first_pixel":image.get_pixel(0,0),"size":image.get_size()})
 return {"tiles":tiles,"shader":terrain.material.shader_override.resource_path,"flat_normals":terrain.material.get_shader_param("flat_terrain_normals")}

func pack_neutral_terrain_height_channels() -> Dictionary:
 root=get_parent() as Node3D
 if root.get_meta("beech_style_channels_packed",false):
  return {"success":false,"error":"Saved texture channel edits preserved."}
 var terrain: Terrain3D=root.get_node("Geometry/Terrain")
 var packed:=0
 for i in range(4):
  var asset: Terrain3DTextureAsset=terrain.assets.get_texture(i)
  var path: String=asset.albedo_texture.resource_path
  var image: Image=asset.albedo_texture.get_image()
  image.clear_mipmaps()
  image.convert(Image.FORMAT_RGBA8)
  # Alpha is Terrain3D's blend-height channel, not surface transparency.
  # A neutral constant keeps painterly colour free of artificial bump relief.
  for y in range(image.get_height()):
   for x in range(image.get_width()):
    var colour: Color=image.get_pixel(x,y)
    colour.a=.5
    image.set_pixel(x,y,colour)
  image.generate_mipmaps()
  var texture:=ImageTexture.create_from_image(image)
  ResourceSaver.save(texture,path,ResourceSaver.FLAG_COMPRESS)
  texture.take_over_path(path)
  asset.albedo_texture=texture
  packed+=1
 ResourceSaver.save(terrain.assets,NATIVE+"beech_assets.tres")
 root.set_meta("beech_style_channels_packed",true)
 return {"success":true,"textures":packed,"format":"RGBA8","height_alpha":.5}

