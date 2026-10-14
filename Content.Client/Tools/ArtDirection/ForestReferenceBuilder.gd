@tool
extends Node
## Explicit, guarded construction of the isolated art reference. No build-on-load.
## Terrain brushes, saved object transforms and Proton Scatter remain the editors.

const A := "res://Assets/ArtDirection/ForestReference/"
const B := "res://Assets/World/Linvel/Beech/Revision2/"
const SCENE := "res://Tests/ArtDirection/EliaraForestReference.tscn"
const AUTHORING := "res://Tools/ArtDirection/EliaraForestReference.tscn"
var root: Node3D
var materials: Dictionary = {}
@export_tool_button("Опубликовать эталон и превью") var publish_button := save_reference
@export_tool_button("Обновить высоту травы по Terrain3D") var grass_height_button := reproject_saved_grass

func add(parent: Node, node: Node, label: String) -> Node:
	node.name = label
	parent.add_child(node)
	node.owner = root
	return node

func save_resource(resource: Resource, path: String) -> void:
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(path.get_base_dir()))
	assert(ResourceSaver.save(resource,path,ResourceSaver.FLAG_COMPRESS)==OK)
	resource.take_over_path(path)

func material(label: String, shader: String, params: Dictionary) -> ShaderMaterial:
	var m := ShaderMaterial.new()
	m.shader = load(A+"Shaders/"+shader+".gdshader")
	for key in params: m.set_shader_parameter(key,params[key])
	save_resource(m,A+"Materials/"+label+".tres")
	materials[label] = m
	return m

func prepare_materials() -> void:
	material("BeechLeaves","beech_foliage",{"leaf_paint":load(B+"StylePass/LeafClusters.res"),"leaf_tint":Color(.46,.65,.29),"base_tint":Color(.18,.32,.095),"paint_strength":.55})
	material("SilverBark","painted_surface",{"surface_paint":load(B+"StylePass/SilverBark.res"),"surface_tint":Color(.73,.76,.68),"paint_strength":.65})
	material("ForestGrass","forest_grass",{})
	material("ForestWater","forest_water",{"water_paint":load(B+"StylePass/WaterColour.res"),"foam_paint":load(B+"StylePass/WaterFoam.res")})
	material("PaintedProps","painted_props",{"bark_paint":load(B+"StylePass/SilverBark.res"),"wood_paint":load(B+"wood_paint.res"),"stone_paint":load(B+"StylePass/GroundRock.res"),"cloth_paint":load(B+"cloth_paint.res")})
	material("MossStone","painted_surface",{"surface_paint":load(B+"StylePass/GroundRock.res"),"surface_tint":Color(.67,.73,.64),"paint_strength":.55})

func stream_x(z: float) -> float:
	return -7.0+sin(z*.072)*6.0

func water_y(z: float) -> float:
	return 2.0+z*.026

func path_z(x: float) -> float:
	return 5.0+sin(x*.073)*3.4

func ground(x: float, z: float) -> float:
	var hills := 6.8*exp(-((x-22)*(x-22)/260.0+(z+19)*(z+19)/300.0))
	hills += 4.8*exp(-((x+27)*(x+27)/260.0+(z-25)*(z-25)/250.0))
	var h := 3.0+z*.035+hills+.28*sin(x*.1)*cos(z*.08)
	var distance := absf(x-stream_x(z))
	var bank := smoothstep(1.65,4.6,distance)
	return lerpf(water_y(z)-.70,h,bank)

func create_terrain() -> void:
	root = get_parent()
	var terrain := Terrain3D.new()
	terrain.vertex_spacing = 1.0
	terrain.mesh_size = 32
	terrain.mesh_lods = 4
	add(root,terrain,"Terrain")
	terrain.change_region_size(Terrain3D.SIZE_64)
	terrain.collision.mode = Terrain3DCollision.FULL_GAME
	terrain.collision.layer = 1
	var assets := Terrain3DAssets.new()
	var source: Terrain3DAssets = load(B+"Terrain/beech_assets.tres")
	for i in range(4):
		var texture: Terrain3DTextureAsset = source.get_texture(i).duplicate(true)
		texture.uv_scale = .16 if i==0 else .20
		assets.set_texture(i,texture)
	terrain.assets = assets
	var mat := Terrain3DMaterial.new()
	mat.world_background = Terrain3DMaterial.NONE
	mat.auto_shader = false
	mat.dual_scaling = false
	mat.shader_override = load(A+"Shaders/painted_terrain.gdshader")
	mat.shader_override_enabled = true
	mat.set_shader_param("enable_macro_variation",false)
	terrain.material = mat
	var height := Image.create(128,128,false,Image.FORMAT_RF)
	var colour := Image.create(128,128,false,Image.FORMAT_RGBA8)
	var control := PackedByteArray()
	control.resize(128*128*4)
	for iz in range(128):
		for ix in range(128):
			var x := float(ix-64)
			var z := float(iz-64)
			var trail := absf(z-path_z(x))
			var river := absf(x-stream_x(z))
			var wet := 1.0-smoothstep(2.0,5.0,river)
			var blend := 1.0-smoothstep(1.9,3.3,trail)
			var base := 3 if wet>.6 else 0
			var bits: int = (base<<27)|(1<<22)|(roundi(blend*255)<<14)|2
			if absf(x)>40 or absf(z)>40: bits |= 4
			control.encode_u32((iz*128+ix)*4,bits)
			height.set_pixel(ix,iz,Color(ground(x,z),0,0,1))
			var c := Color(.82,.89,.69).lerp(Color(.75,.80,.68),wet*.45)
			c = c.lerp(Color(.92,.84,.71),blend)
			var wash := .025*sin(x*.17+z*.07)
			colour.set_pixel(ix,iz,Color(c.r+wash,c.g+wash,c.b+wash,.5))
	terrain.data.import_images([height,Image.create_from_data(128,128,false,Image.FORMAT_RF,control),colour],Vector3(-64,0,-64),0,1)
	terrain.data.update_maps(Terrain3DRegion.TYPE_MAX,true,true)
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(A+"TerrainData"))
	terrain.data.save_directory(A+"TerrainData")
	terrain.data_directory = A+"TerrainData"
	save_resource(assets,A+"TerrainAssets.tres")
	save_resource(mat,A+"TerrainMaterial.tres")

func mesh_from_glb(id: String) -> Mesh:
	var model: Node = load(A+"Models/"+id+".glb").instantiate()
	var mesh: Mesh = model.find_children("*","MeshInstance3D",true,false)[0].mesh
	model.free()
	return mesh

func create_trees() -> void:
	var group := Node3D.new()
	add(root,group,"BeechGroups")
	# Designed belts frame the trail, stream and hillside. The front camera
	# corridor stays open; trunks never occupy the walking line.
	var positions := [Vector2(-31,-30),Vector2(-22,-28),Vector2(-12,-31),Vector2(0,-28),Vector2(11,-29),Vector2(24,-31),Vector2(34,-24),Vector2(-33,-17),Vector2(-24,-14),Vector2(-14,-19),Vector2(0,-16),Vector2(12,-14),Vector2(26,-18),Vector2(33,-10),Vector2(-32,-4),Vector2(-20,-3),Vector2(7,-3),Vector2(21,-3),Vector2(33,0),Vector2(-33,18),Vector2(-23,19),Vector2(-12,20),Vector2(12,18),Vector2(24,19),Vector2(34,14),Vector2(-33,32),Vector2(-19,31),Vector2(-4,33),Vector2(12,32),Vector2(27,31),Vector2(35,29)]
	for i in range(positions.size()):
		var p: Vector2 = positions[i]
		if absf(p.x-stream_x(p.y))<5.5:
			p.x = stream_x(p.y)+(7.0 if p.x>=stream_x(p.y) else -7.0)
		var model: Node3D = load(A+"Models/beech_reference_"+("a" if i%2==0 else "b")+".glb").instantiate()
		# Unpack imported nodes before changing their ownership. Otherwise a
		# PackedScene retains inherited meshes and serialises duplicate children.
		model.scene_file_path = ""
		add(group,model,"Beech%02d"%i)
		model.position = Vector3(p.x,ground(p.x,p.y),p.y)
		model.rotation.y = float(i)*2.399
		var scale_factor := .84+.20*(.5+.5*sin(i*1.72))
		model.scale = Vector3.ONE*scale_factor
		model.set_meta("asset_id","beech_reference_"+("a" if i%2==0 else "b"))
		for visual in model.find_children("*","MeshInstance3D",true,false):
			visual.owner = root
			visual.material_override = materials.BeechLeaves if "Leaves" in str(visual.name) else materials.SilverBark
		var body := StaticBody3D.new()
		body.collision_layer = 4
		add(model,body,"TrunkCollision")
		var collision := CollisionShape3D.new()
		var shape := CylinderShape3D.new()
		shape.radius = .52
		shape.height = 4.5
		collision.shape = shape
		collision.position.y = 2.2
		add(body,collision,"Shape")

func create_water() -> void:
	root = get_parent()
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	for iz in range(160):
		var z0 := -40.0+iz*.5
		var z1 := z0+.5
		var points := [Vector3(stream_x(z0)-3.7,water_y(z0),z0),Vector3(stream_x(z0)+3.7,water_y(z0),z0),Vector3(stream_x(z1)-3.7,water_y(z1),z1),Vector3(stream_x(z1)+3.7,water_y(z1),z1)]
		for j in [0,2,1,1,2,3]:
			st.set_normal(Vector3.UP)
			st.set_uv(Vector2(float(j%2),float(iz+int(j/2))/160.0))
			st.add_vertex(points[j])
	var mesh := st.commit()
	save_resource(mesh,A+"Meshes/StreamSurface.res")
	var water: MeshInstance3D = root.get_node_or_null("Stream")
	if water==null:
		water = MeshInstance3D.new()
		add(root,water,"Stream")
	water.mesh = mesh
	water.material_override = load(A+"Materials/ForestWater.tres")
	water.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF

func old_prop(parent: Node, id: String, p: Vector3, rotation_y: float, label: String) -> MeshInstance3D:
	var node := MeshInstance3D.new()
	node.mesh = load(B+"Meshes/"+id+".res")
	node.material_override = materials.PaintedProps
	node.position = p
	node.rotation.y = rotation_y
	add(parent,node,label)
	return node

func create_places() -> void:
	var group := Node3D.new()
	add(root,group,"TrailAndWorkplace")
	var bx := stream_x(4.0)
	var bridge := old_prop(group,"repaired_beech_bridge",Vector3(bx,3.3,4),0,"TimberBridge")
	var bridge_bounds := bridge.mesh.get_aabb()
	bridge.scale = Vector3(10.0/bridge_bounds.size.x,.70,3.0/bridge_bounds.size.z)
	bridge.set_meta("environment_story","Maintained crossing used by the nearby forestry shelter")
	var shelter := old_prop(group,"working_forest_shelter",Vector3(22,ground(22,10),10),PI,"ForestryShelter")
	old_prop(group,"seasoned_timber",Vector3(25,ground(25,15),15),.3,"SeasonedTimber")
	old_prop(group,"forest_workbench",Vector3(19,ground(19,10),10),-.3,"WorkBench")
	old_prop(group,"worn_supply_bundle",Vector3(23,ground(23,12),12),0,"SupplyBundle")
	for i in range(10):
		var p := Vector2(stream_x(-30+i*7)+(-3.8 if i%2==0 else 3.6),-30+i*7)
		if i==5: p = Vector2(stream_x(13)+3.8,13)
		var stone := old_prop(group,"mossy_outcrop",Vector3(p.x,ground(p.x,p.y)-.14,p.y),float(i)*2.1,"BankStone%02d"%i)
		stone.scale = Vector3.ONE*(.34+.14*(i%3))
		stone.material_override = materials.MossStone
	var surface := StaticBody3D.new()
	add(bridge,surface,"BridgeCollision")
	surface.collision_layer = 1
	var shape := BoxShape3D.new()
	var aabb := bridge.mesh.get_aabb()
	shape.size = Vector3(aabb.size.x,.18,aabb.size.z)
	var collision := CollisionShape3D.new()
	collision.shape = shape
	collision.position = Vector3(aabb.get_center().x,.06,aabb.get_center().z)
	add(surface,collision,"Deck")
	# The shelter collider follows the asset when it is moved in the editor.
	var body := StaticBody3D.new()
	body.collision_layer = 4
	add(shelter,body,"ShelterCollision")
	var box := BoxShape3D.new()
	box.size = Vector3(4,2.4,2.4)
	var cs := CollisionShape3D.new()
	cs.shape = box
	cs.position.y = 1.2
	add(body,cs,"Shape")

func create_atmosphere() -> void:
	var camera := Camera3D.new()
	camera.fov = 50
	camera.far = 250
	camera.position = Vector3(2,27,35)
	camera.rotation_degrees.x = -45
	camera.current = true
	add(root,camera,"GameCamera")
	var sun := DirectionalLight3D.new()
	sun.rotation_degrees = Vector3(-52,-32,0)
	sun.light_color = Color(1,.94,.81)
	sun.light_energy = 1.15
	sun.shadow_enabled = true
	sun.directional_shadow_max_distance = 130
	sun.shadow_blur = 1.5
	add(root,sun,"SpringSun")
	var env := Environment.new()
	env.background_mode = Environment.BG_SKY
	var sky := Sky.new()
	var sky_material := ProceduralSkyMaterial.new()
	sky_material.sky_top_color = Color(.40,.61,.73)
	sky_material.sky_horizon_color = Color(.76,.84,.78)
	sky_material.ground_bottom_color = Color(.30,.39,.26)
	sky_material.ground_horizon_color = Color(.76,.84,.78)
	sky.sky_material = sky_material
	env.sky = sky
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(.67,.78,.79)
	env.ambient_light_energy = .52
	env.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	env.fog_enabled = true
	env.fog_light_color = Color(.72,.81,.72)
	env.fog_density = .0025
	var atmosphere := WorldEnvironment.new()
	atmosphere.environment = env
	add(root,atmosphere,"Atmosphere")
	var figure := MeshInstance3D.new()
	var capsule := CapsuleMesh.new()
	capsule.radius = .34
	capsule.height = 1.8
	figure.mesh = capsule
	var m := StandardMaterial3D.new()
	m.albedo_color = Color(.28,.47,.63)
	m.roughness = .9
	figure.material_override = m
	figure.position = Vector3(2,ground(2,7)+.9,7)
	add(root,figure,"PlayerScale")
	root.set_script(load("res://Tests/ArtDirection/ForestReferencePreview.gd"))

func build_reference() -> Dictionary:
	root = get_parent()
	if root.has_node("Terrain"):
		return {"success":false,"reason":"Existing reference preserved. Edit Terrain3D and saved nodes directly."}
	root.set_meta("art_reference","Eliara forest / draft for visual approval")
	root.set_meta("authored_extent_metres",Vector2(80,80))
	prepare_materials()
	create_terrain()
	create_trees()
	create_water()
	create_places()
	create_atmosphere()
	return {"success":true,"trees":31,"extent":"80 x 80 m","terrain":"Editable native Terrain3D","materials":materials.keys(),"bridge_bounds":root.get_node("TrailAndWorkplace/TimberBridge").mesh.get_aabb()}

func start_grass_authoring() -> Dictionary:
	root = get_parent()
	if root.has_node("GrassAuthoring"): return {"success":false,"reason":"Existing scatter preserved"}
	var mesh := mesh_from_glb("grass_reference")
	save_resource(mesh,A+"Meshes/GrassTuft.res")
	var source := MeshInstance3D.new()
	source.name = "GrassTuft"
	source.mesh = mesh
	source.material_override = load(A+"Materials/ForestGrass.tres")
	var packed := PackedScene.new()
	assert(packed.pack(source)==OK)
	save_resource(packed,A+"Prefabs/GrassTuft.tscn")
	source.free()
	var parent := Node3D.new()
	add(root,parent,"GrassAuthoring")
	var areas := [Vector2(-25,-23),Vector2(12,-22),Vector2(28,-15),Vector2(-27,24),Vector2(8,25),Vector2(29,26)]
	for i in range(areas.size()):
		var scatter: ProtonScatter = load("res://addons/proton_scatter/presets/scatter_default.tscn").instantiate()
		scatter.force_rebuild_on_load = false
		scatter.enable_updates_in_game = false
		scatter.dbg_disable_thread = true
		scatter.global_seed = 684140+i
		scatter.position = Vector3(areas[i].x,0,areas[i].y)
		scatter.get_node("ScatterItem").path = A+"Prefabs/GrassTuft.tscn"
		scatter.get_node("ScatterItem").override_material = load(A+"Materials/ForestGrass.tres")
		scatter.get_node("ScatterItem").override_cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
		scatter.get_node("ScatterShape").shape.radius = 13.0
		scatter.modifier_stack.stack[0].amount = 1450
		scatter.modifier_stack.stack[1].rotation = Vector3(0,360,0)
		scatter.modifier_stack.stack[3].enabled = false
		add(parent,scatter,"GrassArea%d"%i)
		scatter.rebuild(true)
	return {"success":true,"areas":6,"requested_tufts":8700,"plugin":"Proton Scatter","next":"bake_grass after scatter build completes"}

func bake_grass() -> Dictionary:
	root = get_parent()
	if root.has_node("Grass"): return {"success":false,"reason":"Existing baked edits preserved"}
	var group := Node3D.new()
	add(root,group,"Grass")
	var count := 0
	for scatter in root.get_node("GrassAuthoring").get_children():
		var output := scatter.get_node_or_null("ScatterOutput")
		if output==null: return {"success":false,"error":"Scatter output not ready"}
		var chunk_index := 0
		for visual in output.find_children("*","MultiMeshInstance3D",true,false):
			var original: MultiMesh = visual.multimesh
			var transforms: Array[Transform3D] = []
			for i in range(original.instance_count):
				var t: Transform3D = visual.global_transform*original.get_instance_transform(i)
				var p := t.origin
				if absf(p.x)>39 or absf(p.z)>39 or absf(p.z-path_z(p.x))<3.05 or absf(p.x-stream_x(p.z))<3.0: continue
				if Vector2(p.x-22,p.z-11).length()<5.2: continue
				# Explicit ecology mask and native height projection are saved into
				# the bake. Low plants never gain physics bodies or block the road.
				p.y = root.get_node("Terrain").data.get_height(p)
				t.origin = p
				transforms.append(t)
			var multi := MultiMesh.new()
			multi.transform_format = MultiMesh.TRANSFORM_3D
			multi.mesh = original.mesh
			multi.instance_count = transforms.size()
			for i in range(transforms.size()): multi.set_instance_transform(i,transforms[i])
			save_resource(multi,A+"Grass/"+str(scatter.name)+"_%02d.res"%chunk_index)
			var node := MultiMeshInstance3D.new()
			node.multimesh = multi
			node.material_override = load(A+"Materials/ForestGrass.tres")
			node.cast_shadow = GeometryInstance3D.SHADOW_CASTING_SETTING_OFF
			add(group,node,str(scatter.name)+"_%02d"%chunk_index)
			count += transforms.size()
			chunk_index += 1
		scatter.visible = false
	root.set_meta("grass_tuft_count",count)
	return {"success":true,"tufts":count,"draw_groups":group.get_child_count(),"collisions":0}

func save_reference() -> Dictionary:
	root = get_parent()
	var packed := PackedScene.new()
	var error := packed.pack(root)
	if error==OK: error = ResourceSaver.save(packed,AUTHORING)
	if error!=OK: return {"success":false,"error":error}
	# Same private-authoring / pure-published pattern as the region tools.
	# Keep the exact editor nodes alive, including manual scatter mask edits.
	var detached: Array[Node] = []
	for label in ["Authoring","GrassAuthoring"]:
		var node := root.get_node_or_null(label)
		if node!=null:
			root.remove_child(node)
			detached.append(node)
	packed = PackedScene.new()
	error = packed.pack(root)
	if error==OK: error = ResourceSaver.save(packed,SCENE)
	for node in detached:
		root.add_child(node)
		node.owner = root
		for child in node.find_children("*","Node",true,false): child.owner = root
	return {"success":error==OK,"path":SCENE,"authoring":AUTHORING,"error":error}

func repair_grass_resource_paths() -> Dictionary:
	root = get_parent()
	var i := 0
	for node in root.get_node("Grass").get_children():
		node.name = "GrassChunk%02d"%i
		save_resource(node.multimesh,A+"Grass/Chunk%02d.res"%i)
		i += 1
	return {"success":true,"unique_chunks":i}

func repair_tree_instances() -> Dictionary:
	root = get_parent()
	var removed := 0
	for tree in root.get_node("BeechGroups").get_children():
		tree.scene_file_path = ""
		for child in tree.get_children():
			if child is MeshInstance3D and str(child.name).ends_with("2"):
				tree.remove_child(child)
				child.free()
				removed += 1
		for visual in tree.find_children("*","MeshInstance3D",true,false):
			visual.owner = root
			visual.material_override = load(A+"Materials/"+("BeechLeaves" if "Leaves" in str(visual.name) else "SilverBark")+".tres")
	var leaf: ShaderMaterial = load(A+"Materials/BeechLeaves.tres")
	leaf.set_shader_parameter("leaf_tint",Color(.46,.65,.29))
	leaf.set_shader_parameter("paint_strength",.55)
	leaf.set_shader_parameter("base_tint",Color(.18,.32,.095))
	save_resource(leaf,A+"Materials/BeechLeaves.tres")
	return {"success":true,"removed_duplicate_meshes":removed,"trees":31}

func refresh_reference_meshes() -> Dictionary:
	root = get_parent()
	EditorInterface.get_resource_filesystem().reimport_files(PackedStringArray([A+"Models/beech_reference_a.glb",A+"Models/beech_reference_b.glb"]))
	var changed := 0
	for variant in ["a","b"]:
		var imported: Node3D = ResourceLoader.load(A+"Models/beech_reference_"+variant+".glb","PackedScene",ResourceLoader.CACHE_MODE_IGNORE_DEEP).instantiate()
		for tree in root.get_node("BeechGroups").get_children():
			var id := "beech_reference_a" if "beech_reference_a" in str(tree.get_child(0).name) else "beech_reference_b"
			tree.set_meta("asset_id",id)
			if id!="beech_reference_"+variant: continue
			for visual in tree.find_children("*","MeshInstance3D",true,false):
				for source in imported.find_children("*","MeshInstance3D",true,false):
					if str(visual.name)==str(source.name):
						visual.mesh = source.mesh
						changed += 1
		imported.free()
	var leaf: ShaderMaterial = load(A+"Materials/BeechLeaves.tres")
	leaf.set_shader_parameter("leaf_paint",load(B+"StylePass/LeafClusters.res"))
	save_resource(leaf,A+"Materials/BeechLeaves.tres")
	var water: ShaderMaterial = load(A+"Materials/ForestWater.tres")
	water.set_shader_parameter("water_paint",load(B+"StylePass/WaterColour.res"))
	water.set_shader_parameter("foam_paint",load(B+"StylePass/WaterFoam.res"))
	save_resource(water,A+"Materials/ForestWater.tres")
	return {"success":true,"updated_meshes":changed}

func refine_stream_clearance() -> Dictionary:
	root = get_parent()
	var moved := 0
	for tree in root.get_node("BeechGroups").get_children():
		var p: Vector3 = tree.position
		var distance := p.x-stream_x(p.z)
		if absf(distance)<5.5:
			p.x = stream_x(p.z)+(7.0 if distance>=0 else -7.0)
			p.y = root.get_node("Terrain").data.get_height(p)
			tree.position = p
			moved += 1
	var stone: Node3D = root.get_node("TrailAndWorkplace/BankStone05")
	stone.position = Vector3(stream_x(13)+3.8,ground(stream_x(13)+3.8,13)-.14,13)
	return {"success":true,"trees_moved_out_of_stream":moved,"bridge_approach_clear":true}

func externalize_asset_meshes() -> Dictionary:
	root = get_parent()
	var saved: Dictionary = {}
	for tree in root.get_node("BeechGroups").get_children():
		for visual in tree.find_children("*","MeshInstance3D",true,false):
			var label := str(visual.name)
			if not saved.has(label):
				save_resource(visual.mesh,A+"Meshes/"+label+".res")
				saved[label] = visual.mesh
			else: visual.mesh = saved[label]
	return {"success":true,"shared_meshes":saved.size()}

func save_asset_prefabs() -> Dictionary:
	root = get_parent()
	var written := 0
	for index in [0,1]:
		var source: Node3D = root.get_node("BeechGroups").get_child(index)
		var tree: Node3D = source.duplicate()
		tree.name = "BeechReference"+("A" if index==0 else "B")
		tree.transform = Transform3D.IDENTITY
		for child in tree.find_children("*","Node",true,false): child.owner=tree
		var packed := PackedScene.new()
		assert(packed.pack(tree)==OK)
		save_resource(packed,A+"Prefabs/"+str(tree.name)+".tscn")
		tree.free()
		written += 1
	var library: AssetLibrary = AssetLibraryManager.get_asset_library()
	var registered := 0
	if library!=null:
		for file in DirAccess.get_files_at(A+"Prefabs"):
			if not file.ends_with(".tscn"): continue
			var path := A+"Prefabs/"+file
			if library.has_asset_path(path): continue
			if library.add_asset(AssetResource.new(path,"Элиара · эталон · "+file.trim_suffix(".tscn"))): registered+=1
	return {"success":true,"trees":written,"registered_in_asset_placer":registered,"library_available":library!=null}

func reproject_saved_grass() -> Dictionary:
	root = get_parent()
	var updated := 0
	for node in root.get_node("Grass").get_children():
		var multi: MultiMesh = node.multimesh
		for i in range(multi.instance_count):
			var t := multi.get_instance_transform(i)
			var world: Vector3 = node.global_transform*t.origin
			var height: float = root.get_node("Terrain").data.get_height(world)
			if is_nan(height): continue
			world.y = height
			t.origin = node.global_transform.affine_inverse()*world
			multi.set_instance_transform(i,t)
			updated += 1
		save_resource(multi,multi.resource_path)
	return {"success":true,"updated":updated,"horizontal_edits_preserved":true}

func save_lighting_presets() -> Dictionary:
	var settings := [
		["Day","Солнечный день",Vector3(-52,-32,0),Color(1,.94,.81),1.15,Color(.67,.78,.79),.52,Color(.72,.81,.72)],
		["Diffuse","Рассеянный свет",Vector3(-62,-32,0),Color(.82,.89,.95),.75,Color(.61,.73,.79),.58,Color(.62,.73,.73)],
		["Evening","Тёплый вечер",Vector3(-23,-68,0),Color(1,.73,.44),.85,Color(.52,.61,.77),.43,Color(.70,.68,.65)]]
	for spec in settings:
		var path: String = A+"Lighting/"+spec[0]+".tres"
		if FileAccess.file_exists(path): continue
		var preset := Resource.new()
		preset.set_script(load(A+"ForestLightingPreset.gd"))
		preset.set("display_name",spec[1])
		preset.set("sun_rotation_degrees",spec[2])
		preset.set("sun_colour",spec[3])
		preset.set("sun_energy",spec[4])
		preset.set("ambient_colour",spec[5])
		preset.set("ambient_energy",spec[6])
		preset.set("fog_colour",spec[7])
		save_resource(preset,path)
	return {"success":true,"presets":3}










