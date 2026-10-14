@tool
extends "res://Tools/Linvel/BeechValleyRevision.gd"
## Explicit visual rollout on the existing saved scene; never runs on load.
const STYLE := R+"VisualStyle/"
const REF := "res://Assets/ArtDirection/ForestReference/"
@export_tool_button("Опубликовать долину") var publish_style := publish_revision
@export_tool_button("Перепроецировать сохранённую траву") var project_grass := reproject_style_grass
var grass_areas := [
 Vector3(-360,-350,175),Vector3(-105,-400,175),Vector3(165,-375,185),
 Vector3(380,-265,175),Vector3(-410,-105,170),Vector3(-145,-160,190),
 Vector3(95,-90,180),Vector3(355,35,190),Vector3(-365,155,185),
 Vector3(-100,215,180),Vector3(150,230,190),Vector3(380,325,180),
 Vector3(-330,405,180),Vector3(-60,450,160),Vector3(180,445,160)]
var grass_exclusions: Array[AABB] = []

func style_save(resource: Resource,path: String) -> void:
 DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(path.get_base_dir()))
 assert(ResourceSaver.save(resource,path,ResourceSaver.FLAG_COMPRESS)==OK)
 resource.take_over_path(path)

func style_material(label: String,shader_name: String,parameters: Dictionary) -> ShaderMaterial:
 var material := ShaderMaterial.new()
 material.shader=load(REF+"Shaders/"+shader_name+".gdshader")
 for key in parameters: material.set_shader_parameter(key,parameters[key])
 style_save(material,STYLE+"Materials/"+label+".tres")
 return material

func inspect_visual_rollout(stage: String="before") -> Dictionary:
 root=get_parent()
 var terrain: Terrain3D=root.get_node("Geometry/Terrain")
 var assets: Dictionary={}
 var bodies: Array=[]
 var placements: Array=[]
 var triangles:=0
 for node in root.find_children("*","",true,false):
  if node.has_meta("asset_id"):
   var id: String=node.get_meta("asset_id")
   assets[id]=assets.get(id,0)+1
   placements.append([str(root.get_path_to(node)),id,str(node.transform)])
  if node is StaticBody3D:
   var shapes: Array=[]
   for c in node.get_children():
    if c is CollisionShape3D:
     shapes.append([str(c.name),str(c.transform),c.disabled,c.shape.get_class(),str(c.shape.size) if c.shape is BoxShape3D else str(c.shape.get_rid())])
   bodies.append([str(root.get_path_to(node)),str(node.transform),node.collision_layer,shapes,node.get_groups()])
  if node is MeshInstance3D and node.mesh!=null:
   triangles+=node.mesh.get_faces().size()/3
 var report: Dictionary={"assets":assets,"placements":placements,"bodies":bodies,"terrain_directory":terrain.data_directory,"height_range":str(terrain.data.get_height_range()),"mesh_triangles":triangles,"grass_tufts":root.get_meta("style_grass_tufts",0)}
 var file:=FileAccess.open(ProjectSettings.globalize_path("res://../.artifacts/beech-style-rollout/"+stage+".json"),FileAccess.WRITE)
 file.store_string(JSON.stringify(report,"  "))
 return {"assets":assets,"bodies":bodies.size(),"placements":placements.size(),"height_range":str(terrain.data.get_height_range()),"grass_tufts":report.grass_tufts}

func apply_reference_visual_style() -> Dictionary:
 root=get_parent()
 if root.get_meta("beech_reference_style",false): return {"success":false,"reason":"Existing visual edits preserved"}
 var bark:=style_material("SilverBark","painted_surface",{"surface_paint":load(R+"StylePass/SilverBark.res"),"surface_tint":Color(.73,.76,.68),"paint_strength":.65})
 var props:=style_material("PaintedProps","painted_props",{"bark_paint":load(R+"StylePass/SilverBark.res"),"wood_paint":load(R+"wood_paint.res"),"stone_paint":load(R+"StylePass/GroundRock.res"),"cloth_paint":load(R+"cloth_paint.res")})
 var low:=style_material("LowVegetation","beech_foliage",{"leaf_paint":load(R+"StylePass/LeafClusters.res"),"crown_floor":0.0,"crown_height":2.0,"crown_centre":Vector3(0,.8,0),"volume_normals":.20,"wind_strength":.035,"leaf_tint":Color(.46,.65,.29),"base_tint":Color(.18,.32,.095)})
 style_material("ForestGrass","forest_grass",{"wind_strength":.10})
 var water:=style_material("ForestWater","forest_water",{"water_paint":load(R+"StylePass/WaterColour.res"),"foam_paint":load(R+"StylePass/WaterFoam.res"),"minimum_opacity":.62,"opacity_depth":.18,"depth_colour_distance":.65,"shore_width":.10})
 var styled: Dictionary={}
 for id in ["beech_canopy_0","beech_canopy_1","beech_canopy_2","beech_canopy_3","beech_canopy_4","beech_canopy_5","beech_canopy_6","beech_copse_0","beech_copse_1"]:
  var model: Node3D=load(STYLE+"Models/"+id+".glb").instantiate()
  var pair: Dictionary={}
  for visual in model.find_children("*","MeshInstance3D",true,false):
   var label: String="Leaves" if "Leaves" in str(visual.name) else ("UpperBranches" if "UpperBranches" in str(visual.name) else "Bark")
   var mesh: Mesh=visual.mesh.duplicate(true)
   style_save(mesh,STYLE+"Meshes/"+id+"_"+label+".res")
   pair[label]=mesh
  assert(pair.has("Leaves") and pair.has("Bark"),id+": incomplete import")
  var box: AABB=pair.Leaves.get_aabb()
  var material:=style_material(id+"Leaves","beech_foliage",{"leaf_paint":load(R+"StylePass/LeafClusters.res"),"crown_floor":box.position.y,"crown_height":box.end.y,"crown_centre":box.get_center(),"leaf_tint":Color(.46,.65,.29),"base_tint":Color(.18,.32,.095),"paint_strength":.55,"wind_strength":.10})
  pair.material=material
  styled[id]=pair
  model.free()
 var tree_count:=0
 for node in root.find_children("*","Node3D",true,false):
  var id: String=node.get_meta("asset_id","")
  if not styled.has(id) or not node.has_node("Visual"): continue
  var visual: MeshInstance3D=node.get_node("Visual")
  visual.mesh=styled[id].Bark
  visual.material_override=props if id.begins_with("beech_copse") else bark
  var foliage:=MeshInstance3D.new()
  foliage.mesh=styled[id].Leaves
  foliage.material_override=styled[id].material
  foliage.visibility_range_end=visual.visibility_range_end
  foliage.visibility_range_end_margin=visual.visibility_range_end_margin
  foliage.extra_cull_margin=.25
  foliage.add_to_group("forest_crown",true)
  native_add(node,foliage,"Foliage")
  tree_count+=1
 for visual in root.find_children("*","GeometryInstance3D",true,false):
  if visual.is_in_group("forest_crown") or visual.name=="ForestStream": continue
  var old: ShaderMaterial=visual.material_override as ShaderMaterial
  if old==null or old.shader==null or old.shader.resource_path!=R+"painted_vertex.gdshader": continue
  var parent_id: String=visual.get_parent().get_meta("asset_id","")
  var green: bool=parent_id=="beech_understory_bush" or str(visual.name).begins_with("woodland_fern") or str(visual.name).begins_with("sunlit_herb")
  visual.material_override=low if green else props
 var terrain: Terrain3D=root.get_node("Geometry/Terrain")
 var mat: Terrain3DMaterial=terrain.material.duplicate(true)
 mat.shader_override=load(REF+"Shaders/painted_terrain.gdshader")
 mat.shader_override_enabled=true
 mat.set_shader_param("enable_macro_variation",false)
 mat.set_shader_param("flat_terrain_normals",false)
 mat.set_shader_param("colour_map_strength",.22)
 style_save(mat,STYLE+"TerrainMaterial.tres")
 terrain.material=mat
 var assets: Terrain3DAssets=terrain.assets.duplicate(true)
 for i in range(4):
  var tile: Terrain3DTextureAsset=assets.get_texture(i)
  tile.uv_scale=.16 if i==0 else .20
  tile.normal_depth=0.0
  tile.ao_strength=0.0
 style_save(assets,STYLE+"TerrainAssets.tres")
 terrain.assets=assets
 root.get_node("Geometry/ForestStream").material_override=water
 var environment: Environment=root.get_node("ForestAtmosphere").environment.duplicate(true)
 environment.ambient_light_source=Environment.AMBIENT_SOURCE_COLOR
 environment.ambient_light_color=Color(.67,.78,.79)
 environment.ambient_light_energy=.52
 environment.tonemap_mode=Environment.TONE_MAPPER_FILMIC
 environment.fog_enabled=true
 environment.fog_light_color=Color(.72,.81,.72)
 environment.fog_density=.0013
 var sky:=Sky.new()
 var sky_material:=ProceduralSkyMaterial.new()
 sky_material.sky_top_color=Color(.40,.61,.73)
 sky_material.sky_horizon_color=Color(.76,.84,.78)
 sky_material.ground_bottom_color=Color(.30,.39,.26)
 sky_material.ground_horizon_color=Color(.76,.84,.78)
 sky.sky_material=sky_material
 environment.sky=sky
 environment.background_mode=Environment.BG_SKY
 style_save(environment,STYLE+"ForestEnvironment.tres")
 root.get_node("ForestAtmosphere").environment=environment
 var sun: DirectionalLight3D=root.get_node("SpringSun")
 sun.rotation_degrees=Vector3(-52,-32,0)
 sun.light_color=Color(1,.94,.81)
 sun.light_energy=1.15
 sun.shadow_enabled=true
 sun.directional_shadow_max_distance=130
 sun.shadow_blur=1.5
 root.set_meta("beech_reference_style",true)
 return {"success":true,"trees_and_copses":tree_count,"native_height_control_colour_maps":"preserved","collisions_changed":0}

func create_style_grass_area(index: int) -> Dictionary:
 root=get_parent()
 if not root.has_node("GrassScatterAuthoring"): native_add(root,Node3D.new(),"GrassScatterAuthoring")
 var parent: Node3D=root.get_node("GrassScatterAuthoring")
 var label: String="ForestGrassArea%02d"%index
 if parent.has_node(label): return {"success":false,"reason":"Saved scatter preserved"}
 var area: Vector3=grass_areas[index]
 var scatter: ProtonScatter=load("res://addons/proton_scatter/presets/scatter_default.tscn").instantiate()
 scatter.force_rebuild_on_load=false
 scatter.enable_updates_in_game=false
 scatter.dbg_disable_thread=true
 scatter.global_seed=684140+index
 scatter.position=Vector3(area.x,0,area.y)
 scatter.get_node("ScatterItem").path=REF+"Prefabs/GrassTuft.tscn"
 scatter.get_node("ScatterItem").override_material=load(STYLE+"Materials/ForestGrass.tres")
 scatter.get_node("ScatterItem").override_cast_shadow=GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
 scatter.get_node("ScatterShape").shape.radius=area.z
 scatter.modifier_stack.stack[0].amount=120000 if index in [2,6,10] else 90000
 scatter.modifier_stack.stack[1].position=Vector3.ZERO
 scatter.modifier_stack.stack[1].rotation=Vector3(0,360,0)
 scatter.modifier_stack.stack[2].enabled=false
 scatter.modifier_stack.stack[3].enabled=false
 native_add(parent,scatter,label)
 scatter.rebuild(true)
 return {"success":true,"index":index,"requested":scatter.modifier_stack.stack[0].amount}

func cache_style_grass_exclusions() -> void:
 grass_exclusions.clear()
 for body in root.find_children("*","StaticBody3D",true,false):
  if body.get_meta("collision_part","")=="trunk": continue
  for shape in body.get_children():
   if shape is CollisionShape3D and shape.shape is BoxShape3D:
    var box: AABB=shape.global_transform*AABB(-shape.shape.size*.5,shape.shape.size)
    grass_exclusions.append(box.grow(.75))

func refine_initial_grass_density(index: int) -> Dictionary:
 # Used during this rollout only. Once handed off, existing bakes stay guarded.
 root=get_parent()
 if root.get_meta("beech_style_handed_off",false): return {"success":false,"reason":"Saved edits preserved"}
 var scatter: ProtonScatter=root.get_node("GrassScatterAuthoring/ForestGrassArea%02d"%index)
 var removed:=0
 for chunk in root.get_node("ForestGrass").get_children():
  if not str(chunk.name).begins_with("Area%02d_"%index): continue
  removed+=chunk.multimesh.instance_count
  chunk.get_parent().remove_child(chunk)
  chunk.queue_free()
 root.set_meta("style_grass_tufts",root.get_meta("style_grass_tufts",0)-removed)
 scatter.set_meta("visual_baked",false)
 scatter.modifier_stack.stack[0].amount=120000 if index in [2,6,10] else 90000
 scatter.rebuild(true)
 return {"success":true,"index":index,"removed":removed,"requested":scatter.modifier_stack.stack[0].amount}

func bake_style_grass_area(index: int) -> Dictionary:
 root=get_parent()
 var area: Vector3=grass_areas[index]
 var scatter: ProtonScatter=root.get_node("GrassScatterAuthoring/ForestGrassArea%02d"%index)
 if scatter.get_meta("visual_baked",false): return {"success":false,"reason":"Saved grass edits preserved"}
 var output: Node=scatter.get_node_or_null("ScatterOutput")
 if output==null: return {"success":false,"reason":"Scatter build not complete"}
 var terrain: Terrain3D=root.get_node("Geometry/Terrain")
 cache_style_grass_exclusions()
 if not root.has_node("ForestGrass"): native_add(root,Node3D.new(),"ForestGrass")
 var group: Node3D=root.get_node("ForestGrass")
 var chunks: Dictionary={}
 var count:=0
 var mesh: Mesh=load(REF+"Meshes/GrassTuft.res")
 for visual in output.find_children("*","MultiMeshInstance3D",true,false):
  for i in range(visual.multimesh.instance_count):
   var transform: Transform3D=visual.global_transform*visual.multimesh.get_instance_transform(i)
   var p: Vector3=transform.origin
   if absf(p.x)>494 or absf(p.z)>494: continue
   var duplicate:=false
   for earlier in range(index):
    var a: Vector3=grass_areas[earlier]
    if Vector2(p.x-a.x,p.z-a.y).length()<a.z: duplicate=true;break
   if duplicate: continue
   var h: float=terrain.data.get_height(p)
   if is_nan(h): continue
   var normal: Vector3=terrain.data.get_normal(p)
   if normal.y<.85: continue
   var texture: Vector3=terrain.data.get_texture_id(p)
   if (texture.x==1 and texture.z<.75) or (texture.y==1 and texture.z>.18): continue
   if (texture.x==2 and texture.z<.75) or (texture.y==2 and texture.z>.25): continue
   if path_distance(Vector2(p.x,p.z),stream_path)<3.2: continue
   var blocked:=false
   for place in place_specs:
    if Vector2(p.x-place.p.x,p.z-place.p.z).length()<place.radius*.85: blocked=true;break
   if blocked: continue
   p.y=h
   for box in grass_exclusions:
    if p.x>box.position.x and p.x<box.end.x and p.z>box.position.z and p.z<box.end.z: blocked=true;break
   if blocked: continue
   # Ecological density varies by native moisture, slope and ground cover.
   var wash: float=.5+.5*sin(p.x*.041+sin(p.z*.036)*2.0)
   var threshold: float=.45 if texture.x==3 else .78
   if wash>threshold: continue
   transform.origin=p
   var key:=Vector2i(floori(p.x/32.0),floori(p.z/32.0))
   if not chunks.has(key): chunks[key]=[]
   chunks[key].append(transform)
   count+=1
 for key in chunks:
  var list: Array=chunks[key]
  var origin:=Vector3(key.x*32,0,key.y*32)
  var multi:=MultiMesh.new()
  multi.transform_format=MultiMesh.TRANSFORM_3D
  multi.mesh=mesh
  multi.instance_count=list.size()
  for i in range(list.size()):
   var t: Transform3D=list[i]
   t.origin-=origin
   multi.set_instance_transform(i,t)
  var label: String="Area%02d_%d_%d"%[index,key.x,key.y]
  style_save(multi,STYLE+"Grass/"+label+".res")
  var node:=MultiMeshInstance3D.new()
  node.multimesh=multi
  node.material_override=load(STYLE+"Materials/ForestGrass.tres")
  node.cast_shadow=GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
  node.visibility_range_end=105
  node.visibility_range_end_margin=8
  node.position=origin
  node.add_to_group("forest_grass",true)
  native_add(group,node,label)
 scatter.visible=false
 scatter.set_meta("visual_baked",true)
 root.set_meta("style_grass_tufts",root.get_meta("style_grass_tufts",0)+count)
 return {"success":true,"area":index,"tufts":count,"chunks":chunks.size(),"collision_bodies_added":0}

func reproject_style_grass() -> Dictionary:
 root=get_parent()
 if not root.has_node("ForestGrass"): return {"success":false}
 var terrain: Terrain3D=root.get_node("Geometry/Terrain")
 var count:=0
 for node in root.get_node("ForestGrass").get_children():
  for i in range(node.multimesh.instance_count):
   var t: Transform3D=node.multimesh.get_instance_transform(i)
   var p: Vector3=node.global_transform*t.origin
   t.origin.y=terrain.data.get_height(p)-node.global_position.y
   node.multimesh.set_instance_transform(i,t)
   count+=1
  ResourceSaver.save(node.multimesh,node.multimesh.resource_path,ResourceSaver.FLAG_COMPRESS)
 return {"success":true,"reprojected_tufts":count}

func refresh_style_shader_bindings() -> Dictionary:
 # Explicit native refresh avoids stale Shader resources in an already-open
 # editor. Region copies keep the approved reference's own material cache safe.
 root=get_parent()
 var leaf:=Shader.new()
 leaf.code=FileAccess.get_file_as_string(REF+"Shaders/beech_foliage.gdshader")
 style_save(leaf,STYLE+"Shaders/beech_foliage.gdshader")
 var water:=Shader.new()
 water.code=FileAccess.get_file_as_string(REF+"Shaders/forest_water.gdshader")
 style_save(water,STYLE+"Shaders/forest_water.gdshader")
 var ground:=Shader.new()
 ground.code=FileAccess.get_file_as_string(REF+"Shaders/painted_terrain.gdshader")
 style_save(ground,STYLE+"Shaders/painted_terrain.gdshader")
 var saved: Dictionary={}
 for visual in root.find_children("*","GeometryInstance3D",true,false):
  var material: ShaderMaterial=visual.material_override as ShaderMaterial
  if material==null or not material.resource_path.begins_with(STYLE): continue
  if visual.is_in_group("forest_crown"):
   material.shader=leaf
   var box: AABB=visual.mesh.get_aabb()
   material.set_shader_parameter("crown_floor",box.position.y)
   material.set_shader_parameter("crown_height",box.end.y)
   material.set_shader_parameter("crown_centre",box.get_center())
   material.set_shader_parameter("leaf_paint",load(R+"StylePass/LeafClusters.res"))
   material.set_shader_parameter("leaf_tint",Color(.46,.65,.29))
   material.set_shader_parameter("base_tint",Color(.18,.32,.095))
   material.set_shader_parameter("paint_strength",.55)
   material.set_shader_parameter("wind_strength",.10)
  elif material.resource_path.ends_with("LowVegetation.tres"):
   material.shader=leaf
   material.set_shader_parameter("leaf_paint",load(R+"StylePass/LeafClusters.res"))
   material.set_shader_parameter("crown_floor",0.0)
   material.set_shader_parameter("crown_height",2.0)
   material.set_shader_parameter("crown_centre",Vector3(0,.8,0))
   material.set_shader_parameter("volume_normals",.2)
   material.set_shader_parameter("wind_strength",.035)
   material.set_shader_parameter("leaf_tint",Color(.46,.65,.29))
   material.set_shader_parameter("base_tint",Color(.18,.32,.095))
  elif material.resource_path.ends_with("ForestWater.tres"):
   material.shader=water
   var parameters: Dictionary={"water_paint":load(R+"StylePass/WaterColour.res"),"foam_paint":load(R+"StylePass/WaterFoam.res"),"minimum_opacity":.62,"opacity_depth":.18,"depth_colour_distance":.65,"shore_width":.10,"shore_fade_distance":.8}
   for param in parameters:
    material.set_shader_parameter(param,parameters[param])
  saved[material.resource_path]=material
 for path in saved: ResourceSaver.save(saved[path],path)
 var terrain: Terrain3D=root.get_node("Geometry/Terrain")
 terrain.material.shader_override=ground
 terrain.material.set_shader_param("colour_map_strength",.22)
 terrain.material.set_shader_param("enable_macro_variation",false)
 terrain.material.set_shader_param("flat_terrain_normals",false)
 ResourceSaver.save(terrain.material,STYLE+"TerrainMaterial.tres")
 return {"success":true,"materials":saved.size(),"colour_map_strength":terrain.material.get_shader_param("colour_map_strength")}

func inspect_open_style_scenes() -> Dictionary:
 return {"open":EditorInterface.get_open_scenes(),"reload":EditorInterface.has_method("reload_scene_from_path"),"close":EditorInterface.has_method("close_scene")}

func reload_style_preview_tabs() -> Dictionary:
 # These are generated public views; the editable authoring scene remains open.
 for path in [PUBLIC_PATH,"res://Tests/Regions/LinvelBeechPreview.tscn"]:
  if path in EditorInterface.get_open_scenes(): EditorInterface.reload_scene_from_path(path)
 return {"success":true}

func apply_upper_branch_visibility() -> Dictionary:
 root=get_parent()
 var assets: Dictionary={}
 for id in ["beech_canopy_0","beech_canopy_1","beech_canopy_2","beech_canopy_3","beech_canopy_4","beech_canopy_5","beech_canopy_6","beech_copse_0","beech_copse_1"]:
  var model: Node3D=ResourceLoader.load(STYLE+"Models/"+id+".glb","PackedScene",ResourceLoader.CACHE_MODE_IGNORE_DEEP).instantiate()
  var pair: Dictionary={}
  for part in model.find_children("*","MeshInstance3D",true,false):
   if "Leaves" in str(part.name): continue
   var label: String="UpperBranches" if "UpperBranches" in str(part.name) else "Bark"
   var mesh: Mesh=part.mesh.duplicate(true)
   style_save(mesh,STYLE+"Meshes/"+id+"_"+label+".res")
   pair[label]=mesh
  assert(pair.has("Bark") and pair.has("UpperBranches"),id)
  assets[id]=pair
  model.free()
 var count:=0
 for node in root.find_children("*","Node3D",true,false):
  var id: String=node.get_meta("asset_id","")
  if not assets.has(id): continue
  var lower: MeshInstance3D=node.get_node("Visual")
  lower.mesh=assets[id].Bark
  var upper: MeshInstance3D=node.get_node_or_null("UpperBranches")
  if upper==null:
   upper=MeshInstance3D.new()
   native_add(node,upper,"UpperBranches")
  upper.mesh=assets[id].UpperBranches
  upper.material_override=lower.material_override
  upper.visibility_range_end=lower.visibility_range_end
  upper.visibility_range_end_margin=lower.visibility_range_end_margin
  upper.add_to_group("forest_upper_branches",true)
  count+=1
 return {"success":true,"split_trees":count,"physics_changes":0}

func refine_style_stream_geometry(polish_requested: bool=false) -> Dictionary:
 root=get_parent()
 if root.get_meta("beech_style_stream_grade",false) and not polish_requested: return {"success":false,"reason":"Existing water edits preserved"}
 var terrain: Terrain3D=root.get_node("Geometry/Terrain")
 var curve:=Curve2D.new()
 curve.bake_interval=1.0
 for i in range(stream_path.size()):
  var p:=Vector2(stream_path[i][0],stream_path[i][1])
  var previous:=Vector2(stream_path[maxi(i-1,0)][0],stream_path[maxi(i-1,0)][1])
  var next:=Vector2(stream_path[mini(i+1,stream_path.size()-1)][0],stream_path[mini(i+1,stream_path.size()-1)][1])
  var tangent: Vector2=(next-previous).normalized()
  curve.add_point(p,-tangent*p.distance_to(previous)/3.0,tangent*p.distance_to(next)/3.0)
 var stations: PackedVector2Array=curve.get_baked_points()
 var levels: Array[float]=[]
 for p in stations:
  var nominal: float=lerpf(29.0,12.5,clampf((p.y+160)/660.0,0.0,1.0))
  var floor_y: float=terrain.data.get_height(Vector3(p.x,0,p.y))
  # Conform to saved rock lips beneath the bridge, without editing Terrain3D.
  # A downstream-to-upstream envelope makes the visible flow descend; the
  # authored ford remains a shallow walkable crossing at the same elevation.
  levels.append(maxf(nominal,floor_y+(.035 if p.distance_to(Vector2(-45,310))<8 else .22)))
 for i in range(levels.size()-2,-1,-1):
  levels[i]=maxf(levels[i],levels[i+1]+stations[i].distance_to(stations[i+1])*.0005)
 # Limit visual descent across saved rock lips without editing native ground.
 for i in range(1,levels.size()):
  levels[i]=maxf(levels[i],levels[i-1]-stations[i].distance_to(stations[i-1])*.055)
 var st:=SurfaceTool.new()
 st.begin(Mesh.PRIMITIVE_TRIANGLES)
 var rows: Array=[]
 for i in range(stations.size()):
  var p: Vector2=stations[i]
  var tangent: Vector2=(stations[mini(i+1,stations.size()-1)]-stations[maxi(i-1,0)]).normalized()
  var across:=Vector2(-tangent.y,tangent.x)
  var row: Array[Vector3]=[]
  for j in range(17):
   var q: Vector2=p+across*(float(j)-8.0)
   var height: float=levels[i]
   row.append(Vector3(q.x,height,q.y))
  rows.append(row)
 for i in range(rows.size()-1):
  for j in range(16):
   for p in [rows[i][j],rows[i+1][j],rows[i][j+1],rows[i][j+1],rows[i+1][j],rows[i+1][j+1]]:
    st.set_normal(Vector3.UP)
    st.set_uv(Vector2(p.x,p.z)*.1)
    st.add_vertex(p)
 var mesh:=st.commit()
 style_save(mesh,STYLE+"Meshes/ForestStream.res")
 root.get_node("Geometry/ForestStream").mesh=mesh
 root.set_meta("beech_style_stream_continuous",true)
 root.set_meta("beech_style_stream_grade",true)
 return {"success":true,"stations":stations.size(),"triangles":mesh.get_faces().size()/3,"native_height_changes":0,"collision_changes":0}

func refine_style_bark_mapping() -> Dictionary:
 root=get_parent()
 var material: ShaderMaterial=load(STYLE+"Materials/SilverBark.tres")
 material.shader=load(STYLE+"Shaders/painted_bark.gdshader")
 material.set_shader_parameter("surface_paint",load(R+"StylePass/SilverBark.res"))
 material.set_shader_parameter("surface_tint",Color(.73,.76,.68))
 material.set_shader_parameter("paint_strength",.65)
 style_save(material,STYLE+"Materials/SilverBark.tres")
 return {"success":true,"projection":"triplanar; continuous across branch joints"}

func save_style_asset_prefabs() -> Dictionary:
 root=get_parent()
 var done: Dictionary={}
 var library: AssetLibrary=AssetLibraryManager.get_asset_library()
 for node in root.find_children("*","Node3D",true,false):
  var id: String=node.get_meta("asset_id","")
  if id.is_empty() or done.has(id) or not node.has_node("Visual"): continue
  var path: String=STYLE+"Prefabs/"+id+".tscn"
  if not FileAccess.file_exists(path):
   var copy: Node3D=node.duplicate()
   copy.scene_file_path=""
   copy.transform=Transform3D.IDENTITY
   preview_ownership(copy,copy)
   var packed:=PackedScene.new()
   assert(packed.pack(copy)==OK)
   style_save(packed,path)
   copy.free()
  if library!=null and not library.has_asset_path(path): library.add_asset(AssetResource.new(path,"Элиара / Буковая долина / "+id))
  done[id]=true
 return {"success":true,"prefabs":done.size(),"plugin":"Asset Placer","existing_prefab_edits":"preserved"}










