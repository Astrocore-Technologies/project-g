@tool
extends Node
class_name GardenValleyAuthoring

const A := "res://Assets/World/Linvel/Gardens/"
var root: Node3D
var profile: Dictionary
var meshes: Dictionary = {}
var materials: Dictionary = {}
var rng := RandomNumberGenerator.new()
var counts := {"trees":0,"plants":0,"walls":0,"colliders":0}

func add(parent: Node, child: Node, label: String) -> Node:
	child.name = label
	parent.add_child(child,true)
	child.owner = root
	return child

func first_mesh(node: Node) -> MeshInstance3D:
	if node is MeshInstance3D:
		return node
	for child in node.get_children():
		var found := first_mesh(child)
		if found != null:
			return found
	return null

func ground_height(x: float,z: float) -> float:
	if is_instance_valid(root) and root.has_node("Geometry/Terrain"):
		return (root.get_node("Geometry/Terrain") as Terrain3D).data.get_height(Vector3(x,0,z))
	var rows: Array = profile.heights
	var gx: float = clampf((x+480)/4.0,0,239.999)
	var gz: float = clampf((z+400)/4.0,0,199.999)
	var ix := int(gx)
	var iz := int(gz)
	return lerpf(lerpf(rows[iz][ix],rows[iz][ix+1],gx-ix),lerpf(rows[iz+1][ix],rows[iz+1][ix+1],gx-ix),gz-iz)

func road_distance(x: float,z: float) -> float:
	var best := 10000.0
	var paths: Array = [profile.route]
	paths.append_array(profile.side_paths)
	for path in paths:
		for i in range(path.size()-1):
			var a := Vector2(path[i][0],path[i][1])
			var b := Vector2(path[i+1][0],path[i+1][1])
			var d := b-a
			best = minf(best,Vector2(x,z).distance_to(a+d*clampf((Vector2(x,z)-a).dot(d)/d.length_squared(),0,1)))
	return best

func river_x(z: float) -> float:
	return -45+60*sin(z/120)

func surface_clearance(p: Vector2) -> bool:
	if road_distance(p.x,p.y)<9 or absf(p.x-river_x(p.y))<23:
		return false
	for site in profile.places:
		if p.distance_to(Vector2(site.x,site.z))<float(site.radius)+9:
			return false
	return true

func painted_material() -> ShaderMaterial:
	var sh := Shader.new()
	sh.code = """shader_type spatial;
render_mode specular_disabled;
uniform sampler2D painted_atlas : source_color, filter_linear_mipmap;
uniform vec3 focus_world = vec3(-10000.0);
varying vec3 world_position;
void vertex() { world_position=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz; }
void fragment() {
 vec3 ray=focus_world-CAMERA_POSITION_WORLD;
 float t=dot(world_position-CAMERA_POSITION_WORLD,ray)/max(dot(ray,ray),1.0);
 float d=length(world_position-(CAMERA_POSITION_WORLD+ray*t));
 if(t>0.12 && t<0.95 && d<1.8 && mod(floor(FRAGCOORD.x)+floor(FRAGCOORD.y)*2.0,4.0)>0.5) discard;
 ALBEDO=texture(painted_atlas,UV).rgb; ROUGHNESS=1.0;
}
void light() {float n=dot(NORMAL,LIGHT); float tone=mix(0.42,1.0,smoothstep(-0.05,0.65,n)); DIFFUSE_LIGHT+=ALBEDO*LIGHT_COLOR*ATTENUATION*tone/PI;}
"""
	var m := ShaderMaterial.new()
	m.shader = sh
	var sample: Node3D = load(A+"orchard_apple.glb").instantiate()
	var source: BaseMaterial3D = first_mesh(sample).mesh.surface_get_material(0)
	m.set_shader_parameter("painted_atlas",source.albedo_texture)
	sample.free()
	ResourceSaver.save(m,A+"painted_gardens.tres")
	m.take_over_path(A+"painted_gardens.tres")
	return m

func prepare_assets() -> void:
	materials.painted = painted_material()
	var sh := Shader.new()
	sh.code = """shader_type spatial;
render_mode specular_disabled;
uniform sampler2D brush : source_color, filter_linear_mipmap;
void fragment(){ALBEDO=texture(brush,vec2(UV.x,1.0-UV.y)).rgb;ROUGHNESS=1.0;}
void light(){float shade=mix(0.45,1.0,smoothstep(0.05,0.7,dot(NORMAL,LIGHT)));DIFFUSE_LIGHT+=ALBEDO*LIGHT_COLOR*ATTENUATION*shade/PI;}
"""
	var gm := ShaderMaterial.new()
	gm.shader = sh
	var sample: Node3D = load(A+"garden_terrain.glb").instantiate()
	gm.set_shader_parameter("brush",(first_mesh(sample).mesh.surface_get_material(0) as BaseMaterial3D).albedo_texture)
	sample.free()
	ResourceSaver.save(gm,A+"painted_ground.tres")
	gm.take_over_path(A+"painted_ground.tres")
	materials.ground = gm
	var ws := Shader.new()
	ws.code = """shader_type spatial;
render_mode unshaded;
varying vec3 p;
void vertex(){p=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;}
void fragment(){float w=sin(p.z*1.15+TIME*1.1)*sin(p.x*.8-TIME*.75);ALBEDO=mix(vec3(.075,.24,.26),vec3(.17,.43,.44),w*.25+.5);ROUGHNESS=.9;}
"""
	var wm := ShaderMaterial.new()
	wm.shader = ws
	ResourceSaver.save(wm,A+"stream_water.tres")
	wm.take_over_path(A+"stream_water.tres")
	materials.water = wm
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(A+"Meshes"))
	for id in ["farm_cottage","stream_watermill","raised_granary","farm_shed","orchard_apple","orchard_blossom","orchard_pear","fieldstone_wall","pasture_fence","farm_well","garden_trellis","spring_crop_patch","meadow_patch","hedgerow_shrub","garden_boulder_a","garden_boulder_b","farm_bridge","garden_terrain","garden_stream"]:
		var packed: PackedScene = load(A+id+".glb")
		var source: Node3D = packed.instantiate()
		var mesh: ArrayMesh = first_mesh(source).mesh.duplicate()
		var material: Material = materials.ground if id == "garden_terrain" else materials.water if id == "garden_stream" else materials.painted
		for s in range(mesh.get_surface_count()):
			mesh.surface_set_material(s,material)
		var path: String = A+"Meshes/"+id+".res"
		ResourceSaver.save(mesh,path)
		var uid := ResourceLoader.get_resource_uid(path)
		if uid == ResourceUID.INVALID_ID:
			uid = ResourceUID.create_id()
			ResourceSaver.set_uid(path,uid)
		ResourceUID.add_id(uid,path) if not ResourceUID.has_id(uid) else ResourceUID.set_id(uid,path)
		mesh.take_over_path(path)
		meshes[id] = mesh
		source.free()

func asset(parent: Node,id: String,p: Vector3,yaw: float = 0.0,size: float = 1.0,label: String = "") -> MeshInstance3D:
	var placement := Node3D.new()
	add(parent,placement,label if not label.is_empty() else id)
	placement.position = p
	placement.rotation.y = yaw
	placement.set_meta("garden_asset_root",true)
	placement.set_meta("asset_id",id)
	var mi := MeshInstance3D.new()
	add(placement,mi,"Visual")
	mi.mesh = meshes[id]
	mi.set_meta("asset_id",id)
	mi.scale = Vector3.ONE*size
	mi.material_override = materials.painted
	mi.visibility_range_end = 230 if id in ["farm_cottage","stream_watermill","raised_granary"] else 145
	mi.visibility_range_end_margin = 20
	return mi

func box_for(visual: MeshInstance3D,local_offset: Vector3,dimensions: Vector3,part: String) -> StaticBody3D:
	var body := StaticBody3D.new()
	add(visual.get_parent(),body,part.capitalize()+"Body")
	body.add_to_group("surface_geometry",true)
	body.position = visual.transform*local_offset
	body.basis = visual.basis.orthonormalized()
	body.collision_layer = 4
	body.add_to_group("surface_occluder",true)
	body.set_meta("surface_nonwalkable_top",true)
	body.set_meta("collision_visual",body.get_path_to(visual))
	body.set_meta("collision_reference_version",2)
	body.set_meta("collision_local_offset",local_offset)
	body.set_meta("collision_local_size",dimensions)
	body.set_meta("collision_part",part)
	var shape := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = dimensions*visual.scale
	shape.shape = box
	add(body,shape,"Collision")
	counts.colliders += 1
	return body

func building(parent: Node,id: String,x: float,z: float,yaw: float = 0,label: String = "") -> MeshInstance3D:
	var visual := asset(parent,id,Vector3(x,ground_height(x,z),z),yaw,1,label)
	var size := Vector3(7.6,6,6.2) if id == "farm_cottage" else Vector3(9.2,6,7.4) if id == "stream_watermill" else Vector3(5.6,5.7,4.6)
	box_for(visual,Vector3(0,size.y/2,0),size,"building")
	if id in ["farm_cottage","stream_watermill"]:
		var depth := 7.4 if id == "stream_watermill" else 6.2
		var width := 9.2 if id == "stream_watermill" else 7.6
		for sign in [-1,1]:
			box_for(visual,Vector3(sign*width*.34,1.65,depth/2+2.59),Vector3(.22,3.16,.22),"post")
	return visual

func tree(parent: Node,id: String,x: float,z: float,size: float = 1.0,solid: bool = false) -> void:
	var visual := asset(parent,id,Vector3(x,ground_height(x,z),z),rng.randf()*TAU,size)
	if solid:
		box_for(visual,Vector3(0,2.0,0),Vector3(.65,4.3,.65),"trunk")
	counts.trees += 1

func batch(parent: Node,id: String,transforms: Array,label: String,distance: float) -> void:
	if transforms.is_empty():
		return
	var node := MultiMeshInstance3D.new()
	add(parent,node,label)
	var mm := MultiMesh.new()
	mm.transform_format = MultiMesh.TRANSFORM_3D
	mm.mesh = meshes[id]
	mm.instance_count = transforms.size()
	for i in range(transforms.size()):
		mm.set_instance_transform(i,transforms[i])
	node.multimesh = mm
	node.material_override = materials.painted
	node.visibility_range_end = distance
	node.visibility_range_end_margin = 15
	node.set_meta("asset_id",id)

func terrain() -> void:
	var geo := Node3D.new()
	add(root,geo,"Geometry")
	geo.add_to_group("surface_geometry",true)
	var ground := StaticBody3D.new()
	add(geo,ground,"Ground")
	ground.add_to_group("surface_terrain",true)
	var mi := asset(ground,"garden_terrain",Vector3.ZERO)
	mi.material_override = materials.ground
	mi.visibility_range_end = 0
	var shape := CollisionShape3D.new()
	shape.shape = mi.mesh.create_trimesh_shape()
	add(ground,shape,"Collision")
	var water := asset(root,"garden_stream",Vector3.ZERO)
	water.material_override = materials.water
	water.visibility_range_end = 0
	var deck := StaticBody3D.new()
	add(geo,deck,"MillBridgeDeck")
	deck.position = Vector3(river_x(60),7.89,60)
	var collision := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = Vector3(28.4,.22,3.6)
	collision.shape = box
	add(deck,collision,"Collision")
	var bridge := asset(root,"farm_bridge",Vector3(river_x(60),7.85,60))
	deck.reparent(bridge.get_parent(),true)
	deck.owner = root
	deck.set_meta("collision_visual",deck.get_path_to(bridge))
	deck.set_meta("collision_reference_version",2)
	deck.set_meta("collision_local_offset",Vector3(0,.04,0))
	deck.set_meta("collision_local_size",box.size)
	deck.set_meta("collision_part","deck")
	deck.add_to_group("surface_geometry",true)
	for side in [-1,1]:
		box_for(bridge,Vector3(0,.78,side*1.6),Vector3(28,1.5,.2),"rail")

func lighting() -> void:
	var we := WorldEnvironment.new()
	add(root,we,"GardenAtmosphere")
	var env := Environment.new()
	var sky := Sky.new()
	var sky_mat := ProceduralSkyMaterial.new()
	sky_mat.sky_top_color = Color(.35,.59,.76)
	sky_mat.sky_horizon_color = Color(.79,.84,.76)
	sky_mat.ground_horizon_color = Color(.65,.73,.52)
	sky.sky_material = sky_mat
	env.background_mode = Environment.BG_SKY
	env.sky = sky
	env.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	env.ambient_light_color = Color(.72,.79,.70)
	env.ambient_light_energy = .65
	env.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	env.fog_enabled = true
	env.fog_light_color = Color(.72,.81,.73)
	env.fog_density = .0009
	we.environment = env
	var sun := DirectionalLight3D.new()
	add(root,sun,"SpringSun")
	sun.rotation_degrees = Vector3(-48,-38,0)
	sun.light_color = Color(1,.94,.80)
	sun.light_energy = 1.12
	sun.shadow_enabled = true
	sun.directional_shadow_max_distance = 100
	sun.directional_shadow_mode = DirectionalLight3D.SHADOW_PARALLEL_2_SPLITS

func places() -> void:
	var p := Node3D.new()
	add(root,p,"Places")
	for site in profile.places:
		var node := Node3D.new()
		add(p,node,site.id)
		node.set_meta("display_name",site.name)
		var marker := Marker3D.new()
		add(node,marker,"Center")
		marker.position = Vector3(site.x,ground_height(site.x,site.z),site.z)
		marker.add_to_group("linvel_landmark",true)
	building(p.get_node("WestFarms"),"farm_cottage",-420,252,-.2,"WaysideHouse")
	building(p.get_node("TerracedFarm"),"farm_cottage",-267,197,.2,"OrchardHouse")
	building(p.get_node("TerracedFarm"),"raised_granary",-284,194,.2)
	building(p.get_node("MillHamlet"),"stream_watermill",-43,82,0,"Watermill")
	building(p.get_node("MillHamlet"),"farm_cottage",-78,97,.2,"MillersNeighbour")
	building(p.get_node("SunnyOrchard"),"farm_cottage",207,-47,-.3,"GardenHouse")
	building(p.get_node("HillPasture"),"raised_granary",279,220,.3,"PastureBarn")
	for site_id in ["WestFarms","TerracedFarm","SunnyOrchard"]:
		var site: Dictionary
		for s in profile.places:
			if s.id == site_id:
				site = s
		var group: Node3D = p.get_node(site_id)
		var x: float = site.x+17
		var z: float = site.z+12
		var shed := asset(group,"farm_shed",Vector3(x,ground_height(x,z),z),.3)
		for a in [-2.6,2.6]:
			for b in [-1.8,1.8]:
				box_for(shed,Vector3(a,1.7,-b),Vector3(.28,3.5,.28),"post")
		asset(group,"farm_well",Vector3(site.x-11,ground_height(site.x-11,site.z-6),site.z-6),0)
		asset(group,"garden_trellis",Vector3(site.x-9,ground_height(site.x-9,site.z+14),site.z+14),.2)
		for k in range(3):
			tree(group,"orchard_blossom",site.x-18+k*10,site.z+20,1.0,true)
	var signs := Node3D.new()
	add(root,signs,"RoadsideDetails")
	for pt in [Vector2(-330,210),Vector2(-190,115),Vector2(80,38),Vector2(320,-165)]:
		var x: float = pt.x+11
		var z: float = pt.y
		var rock := asset(signs,"garden_boulder_a",Vector3(x,ground_height(x,z),z),rng.randf()*TAU,1.1)
		box_for(rock,Vector3(0,.7,0),Vector3(2.8,1.6,1.8),"rock")

func fields() -> void:
	var parent := Node3D.new()
	add(root,parent,"FieldsAndOrchards")
	for i in range(profile.fields.size()):
		var f: Dictionary = profile.fields[i]
		var group := Node3D.new()
		add(parent,group,"Field_%02d" % i)
		group.set_meta("field_width",f.w)
		group.set_meta("field_depth",f.d)
		var co := cos(float(f.yaw))
		var si := sin(float(f.yaw))
		# Cell-local saved instances, not per-frame generation.
		var transforms: Array = []
		for ix in range(int(f.w/4)):
			for iz in range(int(f.d/5)):
				var a: float = (ix+.5)*4-f.w/2
				var b: float = (iz+.5)*5-f.d/2
				var x: float = f.x+a*co-b*si
				var z: float = f.z+a*si+b*co
				if road_distance(x,z)>9:
					var basis := Basis(Vector3.UP,-float(f.yaw))
					transforms.append(Transform3D(basis,Vector3(x,ground_height(x,z),z)))
		batch(group,"spring_crop_patch",transforms,"SpringRows",85)
		counts.plants += transforms.size()
		# Low walls follow the slope, with gaps for the path and field entrances.
		for edge in [-1,1]:
			for j in range(int(f.w/4.0)):
				var a: float = -f.w/2+2+j*4
				var b: float = edge*(f.d/2+3)
				var x: float = f.x+a*co-b*si
				var z: float = f.z+a*si+b*co
				if road_distance(x,z)>10 and absf(x-river_x(z))>25:
					var wall := asset(group,"fieldstone_wall",Vector3(x,ground_height(x,z)-.22,z),-float(f.yaw))
					wall.rotation.z = atan((ground_height(x+2*co,z+2*si)-ground_height(x-2*co,z-2*si))/4)
					counts.walls += 1
	var orchards: Array = [[150,-135,7,5],[310,60,6,5],[-340,310,7,4]]
	for patch in orchards:
		for ix in range(patch[2]):
			for iz in range(patch[3]):
				var x: float = patch[0]+ix*13+rng.randf_range(-1,1)
				var z: float = patch[1]+iz*13+rng.randf_range(-1,1)
				if surface_clearance(Vector2(x,z)):
					tree(parent,["orchard_apple","orchard_blossom","orchard_pear"][(ix+iz)%3],x,z,rng.randf_range(.85,1.15),true)

func landscape() -> void:
	var parent := Node3D.new()
	add(root,parent,"HedgerowsAndMeadows")
	for cx in range(24):
		for cz in range(20):
			var plants: Array = []
			var bushes: Array = []
			var rocks: Array = []
			for k in range(20):
				var x: float = -480+cx*40+rng.randf()*40
				var z: float = -400+cz*40+rng.randf()*40
				if not surface_clearance(Vector2(x,z)):
					continue
				var s := rng.randf_range(.8,1.35)
				var basis := Basis(Vector3.UP,rng.randf()*TAU).scaled(Vector3.ONE*s)
				var tr := Transform3D(basis,Vector3(x,ground_height(x,z),z))
				plants.append(tr)
				if k<2:
					bushes.append(tr)
				if k==3 and rng.randf()<.3:
					rocks.append(tr)
			batch(parent,"meadow_patch",plants,"Meadow_%02d_%02d" % [cx,cz],70)
			batch(parent,"hedgerow_shrub",bushes,"Hedge_%02d_%02d" % [cx,cz],125)
			batch(parent,"garden_boulder_b",rocks,"Stone_%02d_%02d" % [cx,cz],160)
			counts.plants += plants.size()+bushes.size()
	# Sparse lanes of trees guide players; open views remain between crowns.
	for x in [-385,-350,-315,-185,-145,95,135,245,350,385]:
		var z: float = 185 if x<0 else -float(x)*.42+15
		if absf(x-river_x(z))>28:
			tree(parent,"orchard_blossom" if int(x)%2 else "orchard_pear",x,z,1.2,true)
	# Northern pasture fences: visible without impeding the main road.
	var pasture := Node3D.new()
	add(root,pasture,"UpperPastureFence")
	for i in range(20):
		var x: float = 205+i*5
		for z in [170.0,245.0]:
			asset(pasture,"pasture_fence",Vector3(x,ground_height(x,z),z))

func markers() -> void:
	var route := Node3D.new()
	add(root,route,"RouteMarkers")
	for i in range(profile.route.size()):
		var p: Array = profile.route[i]
		var marker := Marker3D.new()
		add(route,marker,"Main_%02d" % i)
		marker.position = Vector3(p[0],8.0 if absf(p[0]-river_x(p[1]))<14 else ground_height(p[0],p[1]),p[1])
		marker.add_to_group("linvel_main_route",true)
	var spawn := Marker3D.new()
	add(route,spawn,"PlayerSpawn")
	spawn.position = Vector3(-420,14,230)
	spawn.add_to_group("linvel_spawn",true)

func build() -> Dictionary:
	root = get_parent() as Node3D
	if root.has_node("Geometry"):
		return {"error":"Already authored. Manual edits are preserved; build only into a new scene."}
	profile = JSON.parse_string(FileAccess.get_file_as_string(A+"layout.json"))
	rng.seed = 1684
	root.set_meta("region_id","linvel_gardens")
	root.set_meta("surface_cell_size",.5)
	root.set_meta("surface_cell_height",.05)
	root.set_meta("surface_detail_distance",12.0)
	root.set_meta("surface_detail_error",5.0)
	root.set_meta("main_route_metres",profile.route_metres)
	root.set_meta("style","Stylized Hand-Painted Anime 3D")
	prepare_assets()
	terrain()
	lighting()
	places()
	fields()
	landscape()
	markers()
	finish_collisions()
	return counts

func finish_collisions() -> Dictionary:
	# Explicit editor action. Low field walls block walking, while their waist
	# height does not create a new combat LOS rule. No runtime rebuild occurs.
	root = get_parent() as Node3D
	if not root.has_meta("garden_wall_collision_version"):
		for wall in root.get_node("FieldsAndOrchards").find_children("*","MeshInstance3D",true,false):
			if wall.get_meta("asset_id","") == "fieldstone_wall":
				var body: StaticBody3D = box_for(wall,Vector3(0,.55,0),Vector3(4.08,1.08,.6),"wall")
				body.remove_from_group("surface_occluder")
		root.set_meta("garden_wall_collision_version",1)
	var aligned := 0
	for body in root.find_children("*","StaticBody3D",true,false):
		if body.has_meta("collision_visual"):
			var visual: MeshInstance3D = body.get_node(body.get_meta("collision_visual")) if body.get_meta("collision_reference_version",1)==2 else root.get_node(body.get_meta("collision_visual"))
			if not body.has_meta("collision_local_size"):
				body.set_meta("collision_local_size",body.get_node("Collision").shape.size/visual.scale)
			var relative: Transform3D = (body.get_parent() as Node3D).global_transform.affine_inverse()*visual.global_transform
			body.transform = Transform3D(relative.basis.orthonormalized(),relative*body.get_meta("collision_local_offset"))
			body.get_node("Collision").shape.size = body.get_meta("collision_local_size")*relative.basis.get_scale().abs()
			aligned += 1
	return {"aligned":aligned}

func merge_source(node: Node,transform: Transform3D,st: SurfaceTool) -> void:
	var next_transform: Transform3D = transform*(node.transform if node is Node3D else Transform3D.IDENTITY)
	if node is MeshInstance3D:
		for i in range(node.mesh.get_surface_count()):
			st.append_from(node.mesh,i,next_transform)
	for child in node.get_children():
		merge_source(child,next_transform,st)

func load_household_props() -> void:
	for id in ["handcart","public_bench","cargo_crate","cargo_barrel","cargo_sacks","flower_planter","market_fruit","street_lantern"]:
		var source: Node3D = load("res://Assets/World/Linvel/"+id+".glb").instantiate()
		var st := SurfaceTool.new()
		st.begin(Mesh.PRIMITIVE_TRIANGLES)
		merge_source(source,Transform3D.IDENTITY,st)
		var mesh := st.commit()
		mesh.surface_set_material(0,materials.painted)
		var path: String = A+"Meshes/"+id+".res"
		ResourceSaver.save(mesh,path)
		mesh.take_over_path(path)
		meshes[id] = mesh
		source.free()

func polish() -> Dictionary:
	root = get_parent() as Node3D
	if root.has_meta("garden_polish_version"):
		return {"already_polished":true}
	profile = JSON.parse_string(FileAccess.get_file_as_string(A+"layout.json"))
	materials.painted = load(A+"painted_gardens.tres")
	for id in ["meadow_patch","hedgerow_shrub","fieldstone_wall"]:
		meshes[id] = load(A+"Meshes/"+id+".res")
	load_household_props()
	rng.seed = 2684
	var group := Node3D.new()
	add(root,group,"HouseholdDetails")
	for p in profile.places:
		if p.id == "ForestApproach":
			continue
		var x: float = p.x
		var z: float = p.z
		var farm := Node3D.new()
		add(group,farm,p.id+"Details")
		for spec in [["handcart",9,8,.7],["public_bench",5,-7,.2],["cargo_crate",12,11,0],["cargo_barrel",12,9,0],["cargo_sacks",12,13,.3],["flower_planter",7,-9,0],["flower_planter",2,-9,0],["street_lantern",8,-8,0]]:
			var px: float = x+spec[1]
			var pz: float = z+spec[2]
			var v := asset(farm,spec[0],Vector3(px,ground_height(px,pz),pz),float(spec[3]))
			if spec[0] == "handcart":
				box_for(v,Vector3(0,.65,0),Vector3(1.5,1.1,2.4),"cart")
			elif spec[0] == "cargo_crate":
				box_for(v,Vector3(0,.4,0),Vector3(.9,.75,.7),"crate")
		var flowers: Array = []
		var hedges: Array = []
		for k in range(160):
			var angle := rng.randf()*TAU
			var radius := rng.randf_range(10,31)
			var px: float = x+cos(angle)*radius
			var pz: float = z+sin(angle)*radius
			if road_distance(px,pz)>6 and absf(px-river_x(pz))>15:
				var basis := Basis(Vector3.UP,angle).scaled(Vector3.ONE*rng.randf_range(.75,1.3))
				flowers.append(Transform3D(basis,Vector3(px,ground_height(px,pz),pz)))
				if k%16 == 0:
					hedges.append(Transform3D(Basis(Vector3.UP,angle),Vector3(px,ground_height(px,pz),pz)))
		batch(farm,"meadow_patch",flowers,"YardFlowers",85)
		batch(farm,"hedgerow_shrub",hedges,"GardenBorders",125)
	# The preview focuses on useful village spaces instead of empty central circles.
	root.get_node("Places/MillHamlet/Center").position = Vector3(-48,ground_height(-48,95),95)
	root.get_node("Places/TerracedFarm/Center").position = Vector3(-261,ground_height(-261,213),213)
	root.get_node("Places/SunnyOrchard/Center").position = Vector3(212,ground_height(212,-33),-33)
	root.set_meta("garden_polish_version",1)
	return {"household_props":40,"sites":5}

func final_detail_pass() -> Dictionary:
	root = get_parent() as Node3D
	profile = JSON.parse_string(FileAccess.get_file_as_string(A+"layout.json"))
	materials.painted = load(A+"painted_gardens.tres")
	for id in ["stream_watermill","irrigation_trough","vegetable_bed"]:
		var source: Node3D = ResourceLoader.load(A+id+".glb","PackedScene",ResourceLoader.CACHE_MODE_IGNORE_DEEP).instantiate()
		var mesh: ArrayMesh = first_mesh(source).mesh.duplicate()
		mesh.surface_set_material(0,materials.painted)
		var path: String = A+"Meshes/"+id+".res"
		ResourceSaver.save(mesh,path)
		mesh.take_over_path(path)
		meshes[id] = mesh
		source.free()
	var mill_root: Node3D = root.get_node("Places/MillHamlet/Watermill")
	var mill: MeshInstance3D = mill_root as MeshInstance3D if mill_root is MeshInstance3D else mill_root.get_node("Visual") as MeshInstance3D
	mill.mesh = meshes.stream_watermill
	mill_root.position.x = river_x(82)-13.5
	mill_root.position.y = 7.85
	root.get_node("Places/MillHamlet/Center").position = Vector3(mill_root.position.x-3,7.85,98)
	if not root.has_meta("garden_drainage_version"):
		var group := Node3D.new()
		add(root,group,"DrainageAndKitchenGardens")
		for spec in [[-402,270],[-278,215],[190,-35],[-82,110]]:
			for k in range(3):
				var x: float = spec[0]+k*4
				var z: float = spec[1]
				asset(group,"vegetable_bed",Vector3(x,ground_height(x,z),z),.1)
		for spec in [[-335,150],[-155,-45],[110,150],[280,-5]]:
			for k in range(4):
				var x: float = spec[0]+k*6
				var z: float = spec[1]
				asset(group,"irrigation_trough",Vector3(x,ground_height(x,z)-.12,z),0)
		root.set_meta("garden_drainage_version",1)
	# Persist resource UIDs in their own native headers before scene save.
	for file in DirAccess.get_files_at(A+"Meshes"):
		if file.ends_with(".res"):
			var path := A+"Meshes/"+file
			var uid := ResourceLoader.get_resource_uid(path)
			if uid == ResourceUID.INVALID_ID:
				uid = ResourceUID.create_id()
			ResourceSaver.set_uid(path,uid)
			ResourceUID.add_id(uid,path) if not ResourceUID.has_id(uid) else ResourceUID.set_id(uid,path)
	finish_collisions()
	return {"models":19,"kitchen_beds":12,"drains":16,"mill_wheel_bottom":4.05}

func refresh_models_and_heights() -> Dictionary:
	# Explicit offline replacement of shared model resources. Coordinates and
	# hand-authored transforms are retained; only terrain attachment is updated.
	root = get_parent() as Node3D
	profile = JSON.parse_string(FileAccess.get_file_as_string(A+"layout.json"))
	materials.painted = load(A+"painted_gardens.tres")
	materials.ground = load(A+"painted_ground.tres")
	materials.water = load(A+"stream_water.tres")
	for id in ["farm_cottage","stream_watermill","raised_granary","farm_shed","orchard_apple","orchard_blossom","orchard_pear","fieldstone_wall","pasture_fence","farm_well","garden_trellis","spring_crop_patch","meadow_patch","hedgerow_shrub","garden_boulder_a","garden_boulder_b","farm_bridge","garden_terrain","garden_stream","irrigation_trough","vegetable_bed"]:
		var source: Node3D = ResourceLoader.load(A+id+".glb","PackedScene",ResourceLoader.CACHE_MODE_IGNORE_DEEP).instantiate()
		var mesh: ArrayMesh = first_mesh(source).mesh.duplicate()
		var mat: Material = materials.ground if id=="garden_terrain" else materials.water if id=="garden_stream" else materials.painted
		for i in range(mesh.get_surface_count()):
			mesh.surface_set_material(i,mat)
		var path: String = A+"Meshes/"+id+".res"
		var uid := ResourceLoader.get_resource_uid(path)
		ResourceSaver.save(mesh,path)
		if uid == ResourceUID.INVALID_ID:
			uid = ResourceUID.create_id()
		ResourceSaver.set_uid(path,uid)
		ResourceUID.add_id(uid,path) if not ResourceUID.has_id(uid) else ResourceUID.set_id(uid,path)
		mesh.take_over_path(path)
		meshes[id] = mesh
		source.free()
	var updated := 0
	for node in root.find_children("*","GeometryInstance3D",true,false):
		var id: String = str(node.get_meta("asset_id",""))
		if node is MultiMeshInstance3D:
			if meshes.has(id):
				node.multimesh.mesh = meshes[id]
			for i in range(node.multimesh.instance_count):
				var tr: Transform3D = node.multimesh.get_instance_transform(i)
				tr.origin.y = ground_height(tr.origin.x,tr.origin.z)
				node.multimesh.set_instance_transform(i,tr)
		elif node is MeshInstance3D:
			if meshes.has(id):
				node.mesh = meshes[id]
			if id not in ["garden_terrain","garden_stream","farm_bridge","stream_watermill"]:
				var embed := -.22 if id=="fieldstone_wall" else -.12 if id=="irrigation_trough" else 0.0
				var placement: Node3D = node.get_parent() if node.get_parent().get_meta("garden_asset_root",false) else node
				var world := placement.global_position
				world.y = ground_height(world.x,world.z)+embed
				placement.global_position = world
		updated += 1
	if root.has_node("Geometry/Ground/Collision"):
		root.get_node("Geometry/Ground/Collision").shape = meshes.garden_terrain.create_trimesh_shape()
	for marker in root.get_node("RouteMarkers").get_children():
		if marker is Marker3D:
			marker.position.y = 8.0 if absf(marker.position.x-river_x(marker.position.z))<14 else ground_height(marker.position.x,marker.position.z)
	root.set_meta("garden_terrain_pass",2)
	if not root.has_node("Geometry/Terrain"):
		root.set_meta("surface_detail_error",5.0)
	finish_collisions()
	return {"updated_nodes":updated,"model_resources":meshes.size()}

func add_forest_edge() -> Dictionary:
	root = get_parent() as Node3D
	if root.has_node("ForestEdge"):
		return {"existing":true}
	profile = JSON.parse_string(FileAccess.get_file_as_string(A+"layout.json"))
	var group := Node3D.new()
	add(root,group,"ForestEdge")
	rng.seed = 4684
	var forest_mat: Material = load("res://Assets/World/Linvel/Beech/painted_forest.tres")
	for cz in range(8):
		var node := MultiMeshInstance3D.new()
		add(group,node,"BeechBoundary_%02d" % cz)
		var mm := MultiMesh.new()
		mm.transform_format = MultiMesh.TRANSFORM_3D
		mm.mesh = load("res://Assets/World/Linvel/Beech/Meshes/beech_mature_b.res")
		mm.instance_count = 8
		for i in range(8):
			var x := rng.randf_range(450,477)
			var z := -370+cz*40+rng.randf()*40
			if absf(z+225)<18:
				z += 30
			var basis := Basis(Vector3.UP,rng.randf()*TAU).scaled(Vector3.ONE*rng.randf_range(.8,1.2))
			mm.set_instance_transform(i,Transform3D(basis,Vector3(x,ground_height(x,z),z)))
		node.multimesh = mm
		node.material_override = forest_mat
		node.visibility_range_end = 220
		node.visibility_range_end_margin = 25
	return {"background_beeches":64}

