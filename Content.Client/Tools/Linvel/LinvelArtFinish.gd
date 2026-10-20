@tool
extends "res://Tools/Linvel/LinvelCityArtAuthoring.gd"
## Visual correction of the isolated Art01 copy. Original blockout/forest assets
## and original terrain data are read only. All output stays in Art01/Production.
const BEECH := "res://Assets/World/Linvel/Beech/Revision2/VisualStyle/"
var trees_replaced := 0
var batch_sources := 0
var batch_draws := 0
var terrain_report := {}

func load_layout() -> void:
	city = get_parent() as Node3D
	layout = JSON.parse_string(FileAccess.get_file_as_string(DATA))
	polygons.clear()
	for p in layout.platforms:
		var polygon := PackedVector2Array()
		for a in p.points:
			polygon.append(Vector2(a[0],a[1]))
		polygons.append(polygon)
	paving = load(BASE+"Limestone.tres")
	painted = load("res://Assets/World/Linvel/City/Materials/linvel_painted.tres")

func prepare_beeches() -> void:
	# Reuse the EXACT native forest meshes and their painted foliage materials.
	# Local volume normals, vertex colours and wind are preserved by flatten().
	for i in 7:
		prefab("city_beech_%d"%i,BEECH+"Prefabs/beech_canopy_%d.tscn"%i,null)

func replace_trees(node: Node) -> void:
	for child in node.get_children():
		var key: String = child.get_meta("original_asset","")
		if key in ["orchard_tree","blossoming_tree","cypress_tree"]:
			var tf: Transform3D = child.transform
			var label := str(child.name)
			drop(child)
			var tree := place(node,"city_beech_%d"%(trees_replaced%7),label,tf.origin)
			tree.transform = tf.scaled_local(Vector3.ONE*.48)
			tree.set_meta("matches_beech_forest_visual_style",true)
			# Keep a small, explicit solid trunk. The crown stays non-blocking.
			collision_box(tree,"Trunk",Vector3(0,3.8,0),Vector3(.9,7.6,.9))
			trees_replaced += 1
		else:
			replace_trees(child)

func emit_triangle(st: SurfaceTool, a: Vector3, b: Vector3, c: Vector3) -> void:
	st.add_vertex(a)
	st.add_vertex(c)
	st.add_vertex(b)

func emit_quad(st: SurfaceTool, a: Vector3, b: Vector3, c: Vector3, d: Vector3) -> void:
	emit_triangle(st,a,b,c)
	emit_triangle(st,a,c,d)

func portal_wall_height(p: Vector2, h: float) -> float:
	for link in layout.links:
		if link.kind not in ["stairs","ramp"]:
			continue
		var a := Vector2(link.start[0],link.start[2])
		var b := Vector2(link.end[0],link.end[2])
		var t := (p-a).dot(b-a)/(b-a).length_squared()
		if t>=-.02 and t<=1.02 and p.distance_to(a+(b-a)*clampf(t,0,1))<float(link.width)*.5+.15:
			h = minf(h,lerpf(link.start[1],link.end[1],clampf(t,0,1))-.3)
	return h

func finish_walls() -> void:
	for i in layout.platforms.size():
		var platform: Dictionary = layout.platforms[i]
		var root := part.get_node_or_null(platform.id) as Node3D
		if root==null:
			continue
		root.get_node("RetainingMass").visible = false
		drop(root.get_node_or_null("ContinuousStoneRetainingWalls"))
		var st := SurfaceTool.new()
		st.begin(Mesh.PRIMITIVE_TRIANGLES)
		var poly := polygons[i]
		var h := float(platform.height)
		var outer := PackedVector2Array()
		for vertex in poly.size():
			var previous := poly[(vertex-1+poly.size())%poly.size()]
			var current := poly[vertex]
			var following := poly[(vertex+1)%poly.size()]
			var n1 := Vector2((current-previous).y,-(current-previous).x).normalized()
			var n2 := Vector2((following-current).y,-(following-current).x).normalized()
			if Geometry2D.is_point_in_polygon((previous+current)*.5+n1*.1,poly):
				n1 = -n1
			if Geometry2D.is_point_in_polygon((current+following)*.5+n2*.1,poly):
				n2 = -n2
			var miter := (n1+n2).normalized()
			outer.append(current+miter*.8/maxf(.25,miter.dot(n2)))
		for edge in poly.size():
			var a := poly[edge]
			var b := poly[(edge+1)%poly.size()]
			var normal := Vector2((b-a).y,-(b-a).x).normalized()
			if Geometry2D.is_point_in_polygon((a+b)*.5+normal*.1,poly):
				normal = -normal
			var n := maxi(1,ceili(a.distance_to(b)/2.0))
			for j in n:
				var aa := a.lerp(b,float(j)/n)
				var bb := a.lerp(b,float(j+1)/n)
				var mid := (aa+bb)*.5
				if top_height(mid+normal*.2)>h+.1:
					continue
				var outer_a := outer[edge].lerp(outer[(edge+1)%outer.size()],float(j)/n)
				var outer_b := outer[edge].lerp(outer[(edge+1)%outer.size()],float(j+1)/n)
				var ha := portal_wall_height(aa,h)
				var hb := portal_wall_height(bb,h)
				emit_quad(st,Vector3(outer_a.x,-8,outer_a.y),Vector3(outer_a.x,ha,outer_a.y),Vector3(outer_b.x,hb,outer_b.y),Vector3(outer_b.x,-8,outer_b.y))
				emit_quad(st,Vector3(aa.x,ha-.015,aa.y),Vector3(outer_a.x,ha-.015,outer_a.y),Vector3(outer_b.x,hb-.015,outer_b.y),Vector3(bb.x,hb-.015,bb.y))
		st.generate_normals()
		st.index()
		var wall := own(root,MeshInstance3D.new(),"ContinuousStoneRetainingWalls") as MeshInstance3D
		wall.mesh = st.commit()
		wall.material_override = paving

func author_native_landscape() -> Dictionary:
	var terrain := city.get_node("World/Terrain3D") as Terrain3D
	var original := terrain.data
	var maps := Image.create(512,512,false,Image.FORMAT_RF)
	var peaks := [[-460,-520,175,120,90],[-260,-600,235,105,150],[-65,-480,145,90,100],[90,-640,240,125,135],[270,-535,180,110,95],[470,-650,220,130,140]]
	var noise := FastNoiseLite.new()
	noise.seed = 684
	noise.frequency = .018
	var mountain_cells := 0
	for iz in 512:
		for ix in 512:
			var p := Vector2(ix*2-512,iz*2-768)
			var h := original.get_height(Vector3(p.x,0,p.y))
			if p.y<-300:
				var ridge := 0.0
				var warp := noise.get_noise_2d(p.x,p.y)*22
				for peak in peaks:
					var d := Vector2((p.x-peak[0]+warp)/peak[3],(p.y-peak[1]-warp)/peak[4])
					ridge = maxf(ridge,float(peak[2])*exp(-d.length_squared()*.85))
				ridge += maxf(0,noise.get_noise_2d(p.x*2,p.y*2)*24)
				var t := smoothstep(-300,-380,p.y)
				var desired := lerpf(h,maxf(26,ridge),t)
				if desired>h:
					h = desired
					mountain_cells += 1
			if top_height(p)>0:
				h = -8.0
			maps.set_pixel(ix,iz,Color(h,0,0,1))
	var fresh := Terrain3D.new()
	add_child(fresh)
	fresh.visible = false
	fresh.change_region_size(Terrain3D.SIZE_128)
	fresh.vertex_spacing = 2
	fresh.assets = terrain.assets.duplicate(true)
	fresh.material = terrain.material.duplicate(true)
	var grass := fresh.assets.get_texture(0)
	grass.albedo_texture = prepared_grass()
	grass.uv_scale = .045
	fresh.assets.get_texture(2).uv_scale = .025
	fresh.data.import_images([maps,null,null],Vector3(-512,0,-768),0,1)
	var city_holes := 0
	for iz in 512:
		for ix in 512:
			var p := Vector3(ix*2-512,0,iz*2-768)
			var h := maps.get_pixel(ix,iz).r
			var in_city := top_height(Vector2(p.x,p.z))>0
			fresh.data.set_control_auto(p,false)
			fresh.data.set_control_base_id(p,2 if in_city or (p.z<-320 and h>65) else 0)
			fresh.data.set_control_navigation(p,h>0 and not in_city)
			if in_city:
				# Built terraces use their own rigid floors and vertical walls.
				# Hide the terrain under them to avoid LOD peaks piercing masonry.
				fresh.data.set_control_hole(p,true)
				city_holes += 1
	fresh.data.update_maps(Terrain3DRegion.TYPE_MAX,true,true)
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(BASE+"TerrainData"))
	fresh.data.save_directory(BASE+"TerrainData")
	ResourceSaver.save(fresh.assets,BASE+"TerrainAssets.tres")
	fresh.assets.take_over_path(BASE+"TerrainAssets.tres")
	ResourceSaver.save(fresh.material,BASE+"TerrainMaterial.tres")
	fresh.material.take_over_path(BASE+"TerrainMaterial.tres")
	terrain.assets = fresh.assets
	terrain.material = fresh.material
	terrain.data_directory = BASE+"TerrainData"
	var dependencies: Array[Resource] = []
	for file in DirAccess.get_files_at(BASE+"TerrainData"):
		if file.ends_with(".res"):
			dependencies.append(ResourceLoader.load(BASE+"TerrainData/"+file,"",ResourceLoader.CACHE_MODE_IGNORE))
	terrain.set_meta("terrain_region_resources",dependencies)
	terrain.set_meta("art_native_landscape",true)
	remove_child(fresh)
	fresh.free()
	return {"native_regions":terrain.data.get_region_count(),"mountain_cells":mountain_cells,"built_terrace_holes":city_holes,"original_data_preserved":true}

func prepared_grass() -> Texture2D:
	# Terrain3D texture arrays require identical size, format and mipmaps.
	var image := Image.load_from_file(ProjectSettings.globalize_path(BASE+"Textures/grass_paint.png"))
	image.resize(512,512,Image.INTERPOLATE_LANCZOS)
	image.convert(Image.FORMAT_RGBA8)
	image.generate_mipmaps()
	var texture := ImageTexture.create_from_image(image)
	assert(ResourceSaver.save(texture,BASE+"Textures/grass_paint.res",ResourceSaver.FLAG_COMPRESS)==OK)
	texture.take_over_path(BASE+"Textures/grass_paint.res")
	return texture

func repair_grass_import() -> Dictionary:
	var root := get_parent() as Node3D
	assert(root.scene_file_path==REVIEW+"LinvelCity_Art01.tscn")
	var terrain := root.get_node("World/Terrain3D") as Terrain3D
	terrain.assets.get_texture(0).albedo_texture = prepared_grass()
	assert(ResourceSaver.save(terrain.assets,BASE+"TerrainAssets.tres")==OK)
	return {"size":512,"format":"RGBA8","mipmaps":true}

func flatten_hidden_terrain() -> Dictionary:
	load_layout()
	assert(city.scene_file_path==REVIEW+"LinvelCity_Art01.tscn")
	var terrain := city.get_node("World/Terrain3D") as Terrain3D
	assert(terrain.data_directory==BASE+"TerrainData")
	var lowered := 0
	for z in range(-240,188,2):
		for x in range(-214,188,2):
			if top_height(Vector2(x,z))>0:
				terrain.data.set_height(Vector3(x,0,z),-8)
				lowered += 1
	terrain.data.update_maps(Terrain3DRegion.TYPE_HEIGHT,true,true)
	terrain.data.save_directory(terrain.data_directory)
	terrain.assets.get_texture(2).uv_scale = .025
	ResourceSaver.save(terrain.assets,BASE+"TerrainAssets.tres")
	return {"hidden_underlay_cells_lowered":lowered,"reason":"Prevent height morphing from pulling hidden terrace heights into neighbouring terrain LODs","original_terrain_untouched":true}

func repair_street_supports_and_walls() -> Dictionary:
	load_layout()
	assert(city.scene_file_path==REVIEW+"LinvelCity_Art01.tscn")
	for q in layout.districts:
		var path: String = REVIEW+"Parts/"+q.id+".tscn"
		part = ResourceLoader.load(path,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
		finish_walls()
		if not part.has_node("StreetSupportFloors"):
			var floors := own(part,Node3D.new(),"StreetSupportFloors") as Node3D
			for road in layout.streets:
				if road.part!=q.id:
					continue
				for i in range(road.points.size()-1):
					var a := vec(road.points[i])
					var b := vec(road.points[i+1])
					var body := own(floors,StaticBody3D.new(),"%s_%02d"%[road.id,i]) as StaticBody3D
					body.position = (a+b)*.5-Vector3(0,.125,0)
					body.rotation.y = atan2((b-a).x,(b-a).z)
					body.collision_layer = 1
					body.collision_mask = 0
					body.add_to_group("surface_geometry",true)
					var shape := BoxShape3D.new()
					shape.size = Vector3(road.width,.25,a.distance_to(b)+.2)
					var collision := own(body,CollisionShape3D.new(),"Shape") as CollisionShape3D
					collision.shape = shape
		var saved := PackedScene.new()
		assert(saved.pack(part)==OK)
		assert(ResourceSaver.save(saved,path)==OK)
		part.free()
	var parent := city.get_node("World/CityParts")
	for q in layout.districts:
		drop(parent.get_node(q.id))
		var packed: PackedScene = ResourceLoader.load(REVIEW+"Parts/"+q.id+".tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE)
		var node := packed.instantiate()
		parent.add_child(node)
		node.owner = city
	city.get_node("World/Lighting/SpringAtmosphere").environment.fog_density = .00018
	return {"rigid_street_supports":true,"continuous_mitered_walls":true,"original_blockout_untouched":true}

func soften_ridge() -> void:
	var ridge := part.get_node_or_null("DistantMountainRidge")
	if ridge!=null:
		ridge.visible = false
	# Native Terrain3D now supplies the whole ridge instead of enlarged prop rocks.

func relative_transform(node: Node3D) -> Transform3D:
	var result := node.transform
	var parent := node.get_parent()
	while parent!=part:
		if parent is Node3D:
			result = parent.transform*result
		parent = parent.get_parent()
	return result

func gather_batches(node: Node, groups: Dictionary) -> void:
	var key: String = node.get_meta("original_asset","")
	if key in ["stone_balustrade","street_lantern","flower_planter","orchard_fence","harbor_bollard","dock_straight"] or key.begins_with("city_beech_"):
		for child in node.get_children():
			if child is MeshInstance3D:
				var signature := mesh_signature(child.mesh)
				if not groups.has(signature):
					groups[signature] = {"mesh":child.mesh,"material":child.material_override,"transforms":[],"paths":PackedStringArray(),"key":key}
				groups[signature].transforms.append(relative_transform(child))
				groups[signature].paths.append(str(part.get_path_to(child)))
		node.visible = false
		node.set_meta("drawn_by_art_multimesh",true)
		batch_sources += 1
		return
	for child in node.get_children():
		gather_batches(child,groups)

func mesh_signature(mesh: Mesh) -> String:
	# Resource IDs change after native scene serialization. Geometry stays stable.
	return mesh.resource_name+"_"+str(mesh.get_aabb())+"_"+str(mesh.surface_get_array_len(0))+"_"+str(mesh.surface_get_array_index_len(0))

func bake_decor_batches() -> void:
	var groups := {}
	gather_batches(part,groups)
	var group := own(part,Node3D.new(),"ArtDecorBatches") as Node3D
	group.set_script(load("res://Tools/Linvel/LinvelDecorBatchEditor.gd"))
	group.set_meta("authoring","Source placements retained; hidden visuals are drawn here. Rebuild only this district after moving a source.")
	var i := 0
	for data in groups.values():
		var multi := MultiMesh.new()
		multi.transform_format = MultiMesh.TRANSFORM_3D
		multi.mesh = data.mesh
		multi.instance_count = data.transforms.size()
		for j in data.transforms.size():
			multi.set_instance_transform(j,data.transforms[j])
		var visible := own(group,MultiMeshInstance3D.new(),"%s_%03d"%[data.key,i]) as MultiMeshInstance3D
		visible.multimesh = multi
		visible.material_override = data.material
		visible.set_meta("source_mesh_paths",data.paths)
		visible.extra_cull_margin = 1
		batch_draws += 1
		i += 1

func wire_decor_editing() -> Dictionary:
	load_layout()
	assert(city.scene_file_path==REVIEW+"LinvelCity_Art01.tscn")
	var wired := 0
	for q in layout.districts:
		var path: String = REVIEW+"Parts/"+q.id+".tscn"
		part = ResourceLoader.load(path,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
		var groups := {}
		gather_batches(part,groups)
		var batches := part.get_node("ArtDecorBatches")
		batches.set_script(load("res://Tools/Linvel/LinvelDecorBatchEditor.gd"))
		for child in batches.get_children():
			var signature := mesh_signature(child.multimesh.mesh)
			assert(groups.has(signature))
			child.set_meta("source_mesh_paths",groups[signature].paths)
			wired += 1
		var saved := PackedScene.new()
		assert(saved.pack(part)==OK)
		assert(ResourceSaver.save(saved,path)==OK)
		part.free()
	var parent := city.get_node("World/CityParts")
	for q in layout.districts:
		drop(parent.get_node(q.id))
		var packed: PackedScene = ResourceLoader.load(REVIEW+"Parts/"+q.id+".tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE)
		var node := packed.instantiate()
		parent.add_child(node)
		node.owner = city
	return {"editable_render_batches":wired,"native_source_placements_preserved":true}

func finish_copy() -> Dictionary:
	load_layout()
	if city.scene_file_path!=REVIEW+"LinvelCity_Art01.tscn" or city.get_meta("art_visual_finish",false):
		return {"error":"Only the uncorrected Art01 review copy is accepted"}
	prepare_beeches()
	terrain_report = author_native_landscape()
	for q in layout.districts:
		var path: String = REVIEW+"Parts/"+q.id+".tscn"
		part = ResourceLoader.load(path,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
		replace_trees(part)
		finish_walls()
		if q.id=="CoastAndBackdrop":
			soften_ridge()
		if q.id=="CityCenter":
			part.get_node("NavigatorsFountain").scale = Vector3.ONE*1.8
		bake_decor_batches()
		var saved := PackedScene.new()
		assert(saved.pack(part)==OK)
		assert(ResourceSaver.save(saved,path)==OK)
		part.free()
	var parent := city.get_node("World/CityParts")
	for q in layout.districts:
		drop(parent.get_node(q.id))
		var packed: PackedScene = ResourceLoader.load(REVIEW+"Parts/"+q.id+".tscn","PackedScene",ResourceLoader.CACHE_MODE_IGNORE)
		var root := packed.instantiate()
		parent.add_child(root)
		root.owner = city
	var environment := city.get_node("World/Lighting/SpringAtmosphere").environment.duplicate(true) as Environment
	environment.fog_enabled = true
	environment.fog_light_color = Color("a7c9cc")
	environment.fog_density = .0007
	city.get_node("World/Lighting/SpringAtmosphere").environment = environment
	city.set_meta("art_visual_finish",true)
	city.set_meta("tree_style","Exact Beech Valley VisualStyle native meshes and materials")
	return {"trees_replaced":trees_replaced,"instanced_decor_sources":batch_sources,"batch_meshes":batch_draws,"terrain":terrain_report,"save_after_removing_helper":true}
