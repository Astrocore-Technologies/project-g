@tool
extends Node
class_name GardenTerrainTools

func editor_diagnostics() -> Dictionary:
	root = get_parent() as Node3D
	var terrain: Terrain3D = root.get_node("Geometry/Terrain")
	var renderer := RenderingServer
	var camera: Camera3D = EditorInterface.get_editor_viewport_3d(0).get_camera_3d()
	return {"driver":renderer.call("get_current_rendering_driver_name") if renderer.has_method("get_current_rendering_driver_name") else "unknown", "method":renderer.call("get_current_rendering_method") if renderer.has_method("get_current_rendering_method") else "unknown", "terrain_regions":terrain.data.get_region_count(), "height_range":terrain.data.get_height_range(), "data_directory":terrain.data_directory, "editor_camera":camera.global_position, "terrain_camera":terrain.get_camera().get_path() if is_instance_valid(terrain.get_camera()) else "none"}

const A := "res://Assets/World/Linvel/Gardens/"
const T := A+"Terrain/"
var root: Node3D
var profile: Dictionary

func sample_height(x: float,z: float) -> float:
	var gx := clampf((x+480.0)/4.0,0.0,239.999)
	var gz := clampf((z+400.0)/4.0,0.0,199.999)
	var ix := int(gx)
	var iz := int(gz)
	var rows: Array = profile.heights
	return lerpf(lerpf(rows[iz][ix],rows[iz][ix+1],gx-ix),lerpf(rows[iz+1][ix],rows[iz+1][ix+1],gx-ix),gz-iz)

func native_texture(id: int,label: String) -> Terrain3DTextureAsset:
	# Own painted tile, low-frequency strokes and restrained material detail.
	var image := Image.create(256,256,true,Image.FORMAT_RGBA8)
	var normal := Image.create(256,256,true,Image.FORMAT_RGBA8)
	for z in range(256):
		for x in range(256):
			var broad := sin(float(x)/21.0)*cos(float(z)/29.0)*.035
			var detail := sin(x*.37+sin(z*.15))*sin(z*.27)*.015
			var furrow := (sin(x*.12)*.045) if id==3 else 0.0
			var value := .95+broad+detail+furrow
			var base: Color = [Color(.70,.85,.64),Color(.90,.82,.70),Color(.85,.86,.75),Color(.78,.79,.63)][id]
			image.set_pixel(x,z,Color(base.r*value,base.g*value,base.b*value,.5))
			normal.set_pixel(x,z,Color(.5,.5,1.0,1.0))
	image.generate_mipmaps()
	normal.generate_mipmaps()
	var asset := Terrain3DTextureAsset.new()
	asset.id = id
	asset.name = label
	var albedo := ImageTexture.create_from_image(image)
	ResourceSaver.save(albedo,T+"Textures/"+label+"_albedo.res",ResourceSaver.FLAG_COMPRESS)
	albedo.take_over_path(T+"Textures/"+label+"_albedo.res")
	asset.albedo_texture = albedo
	var normals := ImageTexture.create_from_image(normal)
	ResourceSaver.save(normals,T+"Textures/"+label+"_normal.res",ResourceSaver.FLAG_COMPRESS)
	normals.take_over_path(T+"Textures/"+label+"_normal.res")
	asset.normal_texture = normals
	asset.uv_scale = .2 if id==3 else .1
	asset.normal_depth = 0.0
	asset.ao_strength = 0.0
	asset.roughness = 0.0
	ResourceSaver.save(asset,T+"Textures/"+label+".res",ResourceSaver.FLAG_COMPRESS)
	return asset

func convert() -> Dictionary:
	root = get_parent() as Node3D
	if root.has_node("Geometry/Terrain"):
		return {"existing":true,"notice":"Native terrain already present; its sculpt and paint are never reimported automatically."}
	profile = JSON.parse_string(FileAccess.get_file_as_string(A+"layout.json"))
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(T+"Data"))
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(T+"Textures"))
	# Native Terrain3D storage, four editable 512m tiles at 2m vertex spacing.
	var terrain := Terrain3D.new()
	terrain.name = "Terrain"
	terrain.vertex_spacing = 2.0
	terrain.mesh_size = 48
	terrain.mesh_lods = 6
	root.get_node("Geometry").add_child(terrain)
	# Configure the native collision resource after it enters the scene tree.
	terrain.collision.mode = Terrain3DCollision.FULL_GAME
	terrain.collision.layer = 1
	terrain.owner = root
	terrain.change_region_size(Terrain3D.SIZE_256)
	terrain.add_to_group("surface_terrain",true)
	var assets := Terrain3DAssets.new()
	for spec in [[0,"Meadow"],[1,"Road"],[2,"StonyBank"],[3,"CultivatedSoil"]]:
		assets.set_texture(spec[0],native_texture(spec[0],spec[1]))
	terrain.assets = assets
	var material := Terrain3DMaterial.new()
	material.world_background = Terrain3DMaterial.NONE
	material.auto_shader = false
	material.dual_scaling = false
	material.shader_override = load(T+"painted_terrain.gdshader")
	material.shader_override_enabled = true
	material.set_shader_param("enable_macro_variation",false)
	material.set_shader_param("blend_sharpness",.35)
	terrain.material = material
	var source := Image.load_from_file(ProjectSettings.globalize_path(A+"garden_terrain_garden_ground.png"))
	var heightmap := Image.create(512,512,false,Image.FORMAT_RF)
	var colormap := Image.create(512,512,false,Image.FORMAT_RGBA8)
	for iz in range(512):
		for ix in range(512):
			var x: float = -512.0+ix*2.0
			var z: float = -512.0+iz*2.0
			heightmap.set_pixel(ix,iz,Color(sample_height(x,z),0,0,1))
			var sx := clampi(roundi((x+480)/960.0*(source.get_width()-1)),0,source.get_width()-1)
			var sz := clampi(roundi((z+400)/800.0*(source.get_height()-1)),0,source.get_height()-1)
			var color := source.get_pixel(sx,sz)
			color.a = .5
			colormap.set_pixel(ix,iz,color)
	var images: Array[Image] = [heightmap,null,colormap]
	terrain.data.import_images(images,Vector3(-512,0,-512),0.0,1.0)
	for iz in range(512):
		for ix in range(512):
			var x: float = -512.0+ix*2.0
			var z: float = -512.0+iz*2.0
			var p := Vector3(x,0,z)
			terrain.data.set_control_auto(p,false)
			terrain.data.set_control_navigation(p,true)
			if absf(x)>480 or absf(z)>400:
				terrain.data.set_control_hole(p,true)
				continue
			var color := colormap.get_pixel(ix,iz)
			var river := absf(x-(-45+60*sin(z/120)))
			var texture_id := 2 if river<16 else 1 if color.r>color.g*1.08 else 0
			for field in profile.fields:
				var a: float = (x-field.x)*cos(field.yaw)+(z-field.z)*sin(field.yaw)
				var b: float = -(x-field.x)*sin(field.yaw)+(z-field.z)*cos(field.yaw)
				if absf(a)<field.w*.5 and absf(b)<field.d*.5:
					texture_id = 3
			terrain.data.set_control_base_id(p,texture_id)
	terrain.data.update_maps(Terrain3DRegion.TYPE_MAX,true,true)
	terrain.data.save_directory(T+"Data")
	terrain.data_directory = T+"Data"
	ResourceSaver.save(assets,T+"garden_assets.tres")
	assets.take_over_path(T+"garden_assets.tres")
	ResourceSaver.save(material,T+"garden_material.tres")
	material.take_over_path(T+"garden_material.tres")
	# Explicit resource references make terrain files visible to Godot's dependency
	# scanner and game exports, alongside Terrain3D's native directory loading.
	var dependencies: Array[Resource] = []
	for file in DirAccess.get_files_at(T+"Data"):
		if file.ends_with(".res"):
			dependencies.append(load(T+"Data/"+file))
	terrain.set_meta("terrain_region_resources",dependencies)
	# The old ground is removed only after the native data has been saved.
	var old: Node = root.get_node("Geometry/Ground")
	old.get_parent().remove_child(old)
	old.free()
	root.set_meta("surface_source_directory",T+"Data")
	root.set_meta("garden_terrain_backend","Terrain3D")
	root.set_meta("surface_detail_distance",16.0)
	root.set_meta("surface_detail_error",8.0)
	return {"terrain_regions":terrain.data.get_region_count(),"spacing":terrain.vertex_spacing,"range":terrain.data.get_height_range(),"old_ground_removed":true}

func validate() -> Dictionary:
	root = get_parent() as Node3D
	var terrain: Terrain3D = root.get_node("Geometry/Terrain")
	var error := 0.0
	profile = JSON.parse_string(FileAccess.get_file_as_string(A+"layout.json"))
	for p in profile.route:
		var x: float = p[0]
		var z: float = p[1]
		error = maxf(error,absf(terrain.data.get_height(Vector3(x,0,z))-sample_height(x,z)))
	return {"regions":terrain.data.get_region_count(),"route_height_error":error,"old_ground_present":root.has_node("Geometry/Ground")}

func finish_native_paint() -> Dictionary:
	root = get_parent() as Node3D
	var terrain: Terrain3D = root.get_node("Geometry/Terrain")
	if not root.has_meta("garden_native_paint_version"):
		var assets := Terrain3DAssets.new()
		for spec in [[0,"Meadow"],[1,"Road"],[2,"StonyBank"],[3,"CultivatedSoil"]]:
			assets.set_texture(spec[0],native_texture(spec[0],spec[1]))
		terrain.assets = assets
		ResourceSaver.save(assets,T+"garden_assets.tres")
		assets.take_over_path(T+"garden_assets.tres")
		var source := Image.load_from_file(ProjectSettings.globalize_path(A+"garden_terrain_garden_ground.png"))
		var colors: Array[Color] = [Color(.70,.85,.64),Color(.90,.82,.70),Color(.85,.86,.75),Color(.78,.79,.63)]
		for iz in range(512):
			for ix in range(512):
				var p := Vector3(-512.0+ix*2.0,0,-512.0+iz*2.0)
				var sx := clampi(roundi((p.x+480)/960.0*(source.get_width()-1)),0,source.get_width()-1)
				var sz := clampi(roundi((p.z+400)/800.0*(source.get_height()-1)),0,source.get_height()-1)
				var original := source.get_pixel(sx,sz).srgb_to_linear()
				var base := colors[terrain.data.get_control_base_id(p)].srgb_to_linear()
				var tint := Color(original.r/base.r,original.g/base.g,original.b/base.b,.5).linear_to_srgb()
				tint.a = .5
				terrain.data.set_color(p,tint)
		terrain.data.update_maps(Terrain3DRegion.TYPE_COLOR,true,true)
		terrain.data.save_directory(T+"Data")
		root.set_meta("garden_native_paint_version",1)
	# Persist every resource's UID before the scene references it.
	for directory in [T,T+"Data/",T+"Textures/"]:
		for file in DirAccess.get_files_at(directory):
			if not file.ends_with(".res") and not file.ends_with(".tres"):
				continue
			var path: String = directory+file
			var uid := ResourceLoader.get_resource_uid(path)
			if uid == ResourceUID.INVALID_ID:
				uid = ResourceUID.create_id()
			ResourceSaver.set_uid(path,uid)
			ResourceUID.add_id(uid,path) if not ResourceUID.has_id(uid) else ResourceUID.set_id(uid,path)
	ResourceSaver.save(terrain.assets,T+"garden_assets.tres")
	ResourceSaver.save(terrain.material,T+"garden_material.tres")
	return {"paint_layers":4,"native_height_data_preserved":true}
func native_settings() -> Dictionary:
	root = get_parent() as Node3D
	var terrain: Terrain3D = root.get_node("Geometry/Terrain")
	# Set the stored Terrain3DCollision object, not deprecated compatibility aliases.
	terrain.collision.mode = Terrain3DCollision.FULL_GAME
	terrain.collision.layer = 1
	var properties := []
	for p in terrain.get_property_list():
		if "collision" in p.name:
			properties.append({"name":p.name,"usage":p.usage})
	var ids := {}
	for suffix in ["albedo","normal"]:
		var path: String = T+"Textures/CultivatedSoil_"+suffix+".res"
		var uid := ResourceLoader.get_resource_uid(path)
		ids[suffix] = {"uid":ResourceUID.id_to_text(uid)}
	return {"collision_mode":terrain.collision.mode,"properties":properties,"texture_ids":ids}