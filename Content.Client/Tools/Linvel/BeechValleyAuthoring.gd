@tool
extends Node
class_name BeechValleyAuthoring

const ASSET_ROOT = "res://Assets/World/Linvel/Beech/"
const PUBLIC_PATH = "res://Scenes/Regions/Linvel/BeechValley.tscn"
var root: Node3D
var profile: Dictionary
var rng := RandomNumberGenerator.new()
var models: Dictionary = {}
var shaders: Dictionary = {}
var obstacles := 0
var tree_count := 0
var detail_count := 0

func attach(parent: Node, node: Node, node_name: String) -> Node:
	node.name = node_name
	parent.add_child(node,true)
	node.owner = root
	return node

func mesh_in(node: Node) -> MeshInstance3D:
	if node is MeshInstance3D:
		return node
	for child in node.get_children():
		var found: MeshInstance3D = mesh_in(child)
		if found != null:
			return found
	return null

func model(id: String) -> PackedScene:
	if not models.has(id):
		models[id] = ResourceLoader.load(ASSET_ROOT + id + ".glb", "PackedScene", ResourceLoader.CACHE_MODE_IGNORE_DEEP)
	return models[id]

func height(x: float, z: float) -> float:
	var rows: Array = profile.heights
	var spacing: float = profile.spacing
	var gx: float = clampf((x + 480.0) / spacing, 0, rows[0].size()-1.001)
	var gz: float = clampf((z + 400.0) / spacing, 0, rows.size()-1.001)
	var ix: int = int(gx)
	var iz: int = int(gz)
	return lerpf(lerpf(rows[iz][ix], rows[iz][ix+1], gx-ix), lerpf(rows[iz+1][ix], rows[iz+1][ix+1], gx-ix), gz-iz)

func road_distance(x: float, z: float) -> float:
	var best := 10000.0
	var paths: Array = [profile.route]
	paths.append_array(profile.side_paths)
	for path in paths:
		for i in range(path.size()-1):
			var a := Vector2(path[i][0], path[i][1])
			var b := Vector2(path[i+1][0], path[i+1][1])
			var p := Vector2(x, z)
			var d := b-a
			best = minf(best, p.distance_to(a+d*clampf((p-a).dot(d)/d.length_squared(), 0, 1)))
	return best

func clearance(x: float, z: float) -> float:
	var best: float = road_distance(x,z)-10
	for place in profile.places:
		best = minf(best, Vector2(x,z).distance_to(Vector2(place.x,place.z))-float(place.radius)-8)
	return best

func river_x(z: float) -> float:
	return 80+35*sin((z-80)/120)

func asset(parent: Node, id: String, at: Vector3, yaw: float = 0, size: float = 1, label: String = "") -> Node3D:
	var instance: Node3D = model(id).instantiate()
	attach(parent, instance, label if not label.is_empty() else id)
	instance.position = at
	instance.rotation.y = yaw
	instance.scale = Vector3.ONE*size
	var mi: MeshInstance3D = mesh_in(instance)
	if mi != null and id != "beech_terrain" and id != "beech_stream":
		mi.material_override = shaders.painted
		mi.visibility_range_end = 185 if id.begins_with("beech") else 125
		mi.visibility_range_end_margin = 18
	return instance

func collider(name: String, pos: Vector3, size: Vector3, yaw: float = 0, opaque: bool = true) -> StaticBody3D:
	var body := StaticBody3D.new()
	attach(root.get_node("Geometry"), body, name)
	body.position = pos
	body.rotation.y = yaw
	var collision := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = size
	collision.shape = box
	attach(body, collision, "Collision")
	if opaque:
		body.add_to_group("surface_occluder", true)
		obstacles += 1
	return body

func make_materials() -> void:
	var sample: Node3D = ResourceLoader.load(ASSET_ROOT+"beech_mature_a.glb","PackedScene",ResourceLoader.CACHE_MODE_IGNORE_DEEP).instantiate()
	var mi: MeshInstance3D = mesh_in(sample)
	var original: BaseMaterial3D = mi.mesh.surface_get_material(0)
	var shader := Shader.new()
	shader.code = """shader_type spatial;
render_mode specular_disabled;
uniform sampler2D painted_atlas : source_color, filter_linear_mipmap;
uniform vec3 focus_world = vec3(-10000.0);
varying vec3 world_position;
void vertex() { world_position = (MODEL_MATRIX * vec4(VERTEX,1.0)).xyz; }
void fragment() {
    vec3 ray = focus_world-CAMERA_POSITION_WORLD;
    float t = dot(world_position-CAMERA_POSITION_WORLD,ray)/max(dot(ray,ray),1.0);
    float distance_to_ray = length(world_position-(CAMERA_POSITION_WORLD+ray*t));
    if (t > 0.12 && t < 0.95 && distance_to_ray < 1.8 && mod(floor(FRAGCOORD.x)+floor(FRAGCOORD.y)*2.0,4.0)>0.5) discard;
    ALBEDO = texture(painted_atlas, UV).rgb; ROUGHNESS = 1.0;
}
void light() { float n = dot(NORMAL, LIGHT); float band = mix(0.48, 1.0, smoothstep(0.1, 0.5, n)); DIFFUSE_LIGHT += ALBEDO * LIGHT_COLOR * ATTENUATION * band / PI; }
"""
	var material := ShaderMaterial.new()
	material.shader = shader
	material.set_shader_parameter("painted_atlas", original.albedo_texture)
	shaders.painted = material
	ResourceSaver.save(material, ASSET_ROOT+"painted_forest.tres")
	material.take_over_path(ASSET_ROOT+"painted_forest.tres")
	sample.free()
	var ground_sample: Node3D = ResourceLoader.load(ASSET_ROOT+"beech_terrain.glb","PackedScene",ResourceLoader.CACHE_MODE_IGNORE_DEEP).instantiate()
	var ground_mesh: MeshInstance3D = mesh_in(ground_sample)
	var ground_original: BaseMaterial3D = ground_mesh.mesh.surface_get_material(0)
	var gs := Shader.new()
	gs.code = """shader_type spatial;
render_mode specular_disabled;
uniform sampler2D brush_texture : source_color, repeat_enable, filter_linear_mipmap;
void fragment() { ALBEDO = texture(brush_texture, vec2(UV.x, 1.0-UV.y)).rgb; ROUGHNESS = 1.0; }
void light() { float n = dot(NORMAL, LIGHT); float band = mix(0.55, 1.0, smoothstep(0.1, 0.6, n)); DIFFUSE_LIGHT += ALBEDO * LIGHT_COLOR * ATTENUATION * band / PI; }
"""
	var gm := ShaderMaterial.new()
	gm.shader = gs
	gm.set_shader_parameter("brush_texture", ground_original.albedo_texture)
	shaders.ground = gm
	ResourceSaver.save(gm, ASSET_ROOT+"painted_ground.tres")
	gm.take_over_path(ASSET_ROOT+"painted_ground.tres")
	ground_sample.free()
	var ws := Shader.new()
	ws.code = """shader_type spatial;
render_mode unshaded;
varying vec3 world_position;
void vertex() { world_position = (MODEL_MATRIX * vec4(VERTEX,1.0)).xyz; }
void fragment() { float ripple = sin(world_position.x*1.8+world_position.z*0.65+TIME*1.1)*sin(world_position.z*2.1-TIME*0.8); ALBEDO = mix(vec3(0.025,0.12,0.19),vec3(0.08,0.31,0.37),ripple*0.35+0.5); ROUGHNESS = 0.8; }
"""
	var wm := ShaderMaterial.new()
	wm.shader = ws
	shaders.water = wm
	ResourceSaver.save(wm, ASSET_ROOT+"stream_water.tres")
	wm.take_over_path(ASSET_ROOT+"stream_water.tres")

func make_ground() -> void:
	var geometry := Node3D.new()
	attach(root, geometry, "Geometry")
	geometry.add_to_group("surface_geometry", true)
	var ground := StaticBody3D.new()
	attach(geometry, ground, "Ground")
	ground.add_to_group("surface_terrain", true)
	var terrain: Node3D = asset(ground, "beech_terrain", Vector3.ZERO)
	var mi: MeshInstance3D = mesh_in(terrain)
	mi.material_override = shaders.ground
	var collision := CollisionShape3D.new()
	collision.shape = mi.mesh.create_trimesh_shape()
	attach(ground, collision, "Collision")
	var water: Node3D = asset(root, "beech_stream", Vector3.ZERO)
	mesh_in(water).material_override = shaders.water
	var crossing := collider("BridgeDeck", Vector3(80,6.08,80), Vector3(40.6,0.24,5.6), 0, false)
	var bridge := asset(crossing, "wooden_footbridge", Vector3(0,-0.08,0), 0, 1, "BridgeVisual")
	bridge.scale.x = 1.7
	for z in [77.35,82.65]:
		collider("BridgeRail", Vector3(80,6.65,z),Vector3(40.6,1.2,0.25))

func make_lighting() -> void:
	var environment_node := WorldEnvironment.new()
	attach(root, environment_node, "ForestAtmosphere")
	var environment := Environment.new()
	var sky := Sky.new()
	var sky_material := ProceduralSkyMaterial.new()
	sky_material.sky_top_color = Color(0.30,0.53,0.70)
	sky_material.sky_horizon_color = Color(0.75,0.82,0.74)
	sky_material.ground_bottom_color = Color(0.25,0.33,0.25)
	sky_material.ground_horizon_color = Color(0.69,0.76,0.68)
	sky.sky_material = sky_material
	environment.background_mode = Environment.BG_SKY
	environment.sky = sky
	environment.ambient_light_source = Environment.AMBIENT_SOURCE_COLOR
	environment.ambient_light_color = Color(0.72,0.79,0.67)
	environment.ambient_light_energy = 0.62
	environment.tonemap_mode = Environment.TONE_MAPPER_FILMIC
	environment.fog_enabled = true
	environment.fog_light_color = Color(0.66,0.76,0.68)
	environment.fog_density = 0.0011
	environment_node.environment = environment
	var sun := DirectionalLight3D.new()
	attach(root, sun, "SpringSun")
	sun.rotation_degrees = Vector3(-53,-32,0)
	sun.light_color = Color(1.0,0.92,0.75)
	sun.light_energy = 1.15
	sun.shadow_enabled = true
	sun.directional_shadow_max_distance = 95
	sun.directional_shadow_mode = DirectionalLight3D.SHADOW_PARALLEL_2_SPLITS

func make_places() -> void:
	var places := Node3D.new()
	attach(root, places, "Places")
	for p in profile.places:
		var group := Node3D.new()
		attach(places, group, p.id)
		var x: float = p.x
		var z: float = p.z
		var y: float = height(x,z)
		group.set_meta("display_name",p.name)
		group.set_meta("clearing_radius",p.radius)
		var marker := Marker3D.new()
		attach(group,marker,"Center")
		marker.position = Vector3(x,6.2 if p.id == "RavineBridge" else y,z)
		marker.add_to_group("linvel_landmark",true)
	var yard: Node3D = places.get_node("Woodcutters")
	asset(yard,"woodcutters_shelter",Vector3(-320,10,226),0.2)
	asset(yard,"woodcutters_shelter",Vector3(-299,10,239),-0.1)
	for i in range(5):
		asset(yard,"timber_stack",Vector3(-331+i*5,10,233),0.1)
	for i in range(6):
		var x: float = -321+i*6
		asset(yard,"beech_stump",Vector3(x,height(x,201),201),rng.randf()*TAU)
	asset(yard,"charcoal_mound",Vector3(-280,10,236))
	asset(yard,"fallen_beech",Vector3(-284,10,207),0.6)
	var edge: Node3D = places.get_node("ManagedEdge")
	asset(edge,"woodcutters_shelter",Vector3(-432,7,345),0.5)
	asset(edge,"trail_sign",Vector3(-419,7,331),-0.4)
	var camp: Node3D = places.get_node("KagerCamp")
	for offset in [Vector2(-12,8),Vector2(12,10),Vector2(0,-14)]:
		asset(camp,"camp_tent",Vector3(340+offset.x,20,-290+offset.y),rng.randf()*TAU,1.3)
	asset(camp,"fire_ring",Vector3(340,20,-290),0,1.25)
	asset(camp,"timber_stack",Vector3(324,20,-280),0.3)
	asset(camp,"fallen_beech",Vector3(340,20,-273),0)
	asset(camp,"beech_stump",Vector3(348,20,-275))
	var glade: Node3D = places.get_node("SunlitGlade")
	asset(glade,"moss_boulder_a",Vector3(251,15,36),0.3,2.4)
	asset(glade,"moss_boulder_b",Vector3(220,15,72),1.1,2)
	asset(glade,"fallen_beech",Vector3(261,15,56),0.6)
	for point in [Vector2(-335,140),Vector2(-70,125),Vector2(155,140),Vector2(195,-20),Vector2(265,-210)]:
		asset(places,"trail_sign",Vector3(point.x+6,height(point.x+6,point.y),point.y),rng.randf()*TAU)
	# Side paths end at working or exploration sites, as described in the lore.
	asset(places,"charcoal_mound",Vector3(-410,height(-410,95),95),0,1.4,"OldCharcoalGround")
	asset(places,"woodcutters_shelter",Vector3(-400,height(-400,104),104),0.2,1,"RainShelter")
	asset(places,"moss_boulder_a",Vector3(375,height(375,80),80),0,3,"HerbalistsOutcrop")
	asset(places,"beech_dead",Vector3(150,height(150,-280),-280),0,1.4,"OldWatchTree")
	# Solid colliders match the visible structural parts rather than blocking an entire shelter.
	for center in [Vector2(-320,226),Vector2(-299,239),Vector2(-432,345),Vector2(-400,104)]:
		for offset in [Vector2(-3,-2),Vector2(3,-2),Vector2(-3,2),Vector2(3,2)]:
			var p: Vector2 = center+offset
			collider("ShelterPost",Vector3(p.x,height(p.x,p.y)+1.7,p.y),Vector3(0.4,3.4,0.4))

func add_multimesh(parent: Node, id: String, transforms: Array, name: String, distance: float) -> void:
	if transforms.is_empty():
		return
	var sample: Node3D = model(id).instantiate()
	var source: MeshInstance3D = mesh_in(sample)
	var instance := MultiMeshInstance3D.new()
	attach(parent,instance,name)
	var multi := MultiMesh.new()
	multi.transform_format = MultiMesh.TRANSFORM_3D
	multi.mesh = source.mesh
	multi.instance_count = transforms.size()
	for i in range(transforms.size()):
		multi.set_instance_transform(i,transforms[i])
	instance.multimesh = multi
	instance.material_override = shaders.painted
	instance.visibility_range_end = distance
	instance.visibility_range_end_margin = 16
	instance.set_meta("asset_id",id)
	sample.free()

func make_woodland() -> void:
	var woodland := Node3D.new()
	attach(root,woodland,"Woodland")
	var detail := Node3D.new()
	attach(root,detail,"Undergrowth")
	for cz in range(20):
		for cx in range(24):
			var origin := Vector3(-480+cx*40,0,-400+cz*40)
			var cell := Node3D.new()
			attach(woodland,cell,"Cell_%02d_%02d" % [cx,cz])
			cell.position = origin
			var buckets: Dictionary = {"beech_mature_a":[],"beech_mature_b":[],"beech_young":[]}
			var low: Dictionary = {"hazel_shrub":[],"fern_cluster":[],"grass_tuft":[],"meadow_flowers":[]}
			for i in range(14):
				var x: float = origin.x+rng.randf_range(3,37)
				var z: float = origin.z+rng.randf_range(3,37)
				if clearance(x,z)<2 or absf(x-river_x(z))<20:
					continue
				var id: String = ["beech_mature_a","beech_mature_b","beech_young"][i%3]
				var size: float = rng.randf_range(0.95,1.65)
				var t := Transform3D(Basis(Vector3.UP,rng.randf()*TAU).scaled(Vector3.ONE*size),Vector3(x,height(x,z),z)-origin)
				buckets[id].append(t)
				tree_count += 1
				# Collide the closest trunks; background canopies are batched and cosmetic.
				if road_distance(x,z)<22 and obstacles<190:
					collider("BeechTrunk",Vector3(x,height(x,z)+4*size,z),Vector3(0.9*size,8*size,0.9*size))
			for id in buckets:
				add_multimesh(cell,id,buckets[id],id,170)
			var low_cell := Node3D.new()
			attach(detail,low_cell,"Cell_%02d_%02d" % [cx,cz])
			low_cell.position = origin
			for i in range(22):
				var x: float = origin.x+rng.randf_range(0,40)
				var z: float = origin.z+rng.randf_range(0,40)
				if road_distance(x,z)<6 or absf(x-river_x(z))<12:
					continue
				var id: String = ["hazel_shrub","fern_cluster","grass_tuft","meadow_flowers"][i%4]
				var size: float = rng.randf_range(0.7,1.7)
				low[id].append(Transform3D(Basis(Vector3.UP,rng.randf()*TAU).scaled(Vector3.ONE*size),Vector3(x,height(x,z),z)-origin))
				detail_count += 1
			for id in low:
				add_multimesh(low_cell,id,low[id],id,65 if id != "hazel_shrub" else 100)
	# Hero trees remain individual scene instances for convenient manual editing.
	var heroes := Node3D.new()
	attach(root,heroes,"HeroTreesAndRocks")
	for i in range(75):
		var index: int = i % (profile.route.size()-1)
		var a := Vector2(profile.route[index][0],profile.route[index][1])
		var b := Vector2(profile.route[index+1][0],profile.route[index+1][1])
		var center: Vector2 = a.lerp(b,rng.randf())
		var side: Vector2 = (b-a).normalized().orthogonal()
		var p: Vector2 = center+side*(14.0 if i%2==0 else -14.0)
		if clearance(p.x,p.y)<1 or absf(p.x-river_x(p.y))<18:
			continue
		asset(heroes,"moss_boulder_a" if i%3==0 else "beech_mature_b",Vector3(p.x,height(p.x,p.y),p.y),rng.randf()*TAU,1.25)
		if obstacles<235:
			collider("HeroObstacle",Vector3(p.x,height(p.x,p.y)+1.1,p.y),Vector3(2.5,2.2,2.0))
	for i in range(34):
		var z: float = -375+i*22
		for side in [-1,1]:
			var x: float = river_x(z)+side*rng.randf_range(12,20)
			asset(heroes,"moss_boulder_b",Vector3(x,height(x,z),z),rng.randf()*TAU,rng.randf_range(1.6,3.0))
	for i in range(12):
		var x: float = -420+i*75
		asset(heroes,"forest_cliff",Vector3(x,height(x,-375)-3,-383),rng.randf_range(-0.2,0.2),2.2)

func make_route_markers() -> void:
	var routes := Node3D.new()
	attach(root,routes,"RouteMarkers")
	for i in range(profile.route.size()):
		var p: Array = profile.route[i]
		var marker := Marker3D.new()
		attach(routes,marker,"Main_%02d" % i)
		marker.position = Vector3(p[0],6.2 if i in [7,8] else height(p[0],p[1]),p[1])
		marker.set_meta("route_order",i)
		marker.add_to_group("linvel_main_route",true)
	var spawn := Marker3D.new()
	attach(routes,spawn,"PlayerSpawn")
	spawn.position = Vector3(-425,7,330)
	spawn.add_to_group("linvel_spawn",true)

func build() -> Dictionary:
	root = get_parent() as Node3D
	if root.has_node("Geometry"):
		return {"error":"Scene already authored; manual edits are preserved. Use a new scene for another generation."}
	profile = JSON.parse_string(FileAccess.get_file_as_string(ASSET_ROOT+"layout.json"))
	rng.seed = 684
	root.set_meta("region_id","linvel_beech")
	root.set_meta("surface_cell_size",0.5)
	root.set_meta("surface_cell_height",0.05)
	root.set_meta("surface_detail_distance",24.0)
	root.set_meta("surface_detail_error",4.0)
	root.set_meta("main_route_metres",profile.route_metres)
	root.set_meta("style","Stylized Hand-Painted Anime 3D")
	make_materials()
	make_ground()
	make_lighting()
	make_places()
	make_woodland()
	make_route_markers()
	repair()
	conform_to_ground()
	polish_clearings()
	return {"trees":tree_count,"undergrowth":detail_count,"opaque_colliders":obstacles,"route_metres":profile.route_metres,"nodes":root.find_children("*","",true,false).size()}

func preview_asset() -> Dictionary:
	root = get_parent() as Node3D
	var tree: Node3D = model("beech_mature_a").instantiate()
	attach(root,tree,"BeechTree")
	return {"tree_children":tree.get_child_count()}

func own_children(node: Node) -> void:
	for child in node.get_children():
		child.owner = root
		own_children(child)

func editable_models() -> void:
	# Convert imported wrappers to native editable nodes. Imported instance
	# children otherwise omit our material overrides when PackedScene is saved.
	for old in root.find_children("*","Node3D",true,false):
		if not old.scene_file_path.ends_with(".glb"):
			continue
		var id: String = old.scene_file_path.get_file().get_basename()
		var parent: Node = old.get_parent()
		var name_before: String = old.name
		var native := Node3D.new()
		attach(parent,native,name_before+"_editable")
		native.transform = old.transform
		native.set_meta("asset_id",id)
		for child in old.get_children():
			old.remove_child(child)
			native.add_child(child)
			child.owner = root
			own_children(child)
		parent.remove_child(old)
		old.free()
		native.name = name_before

func repair() -> Dictionary:
	root = get_parent() as Node3D
	profile = JSON.parse_string(FileAccess.get_file_as_string(ASSET_ROOT+"layout.json"))
	make_materials()
	editable_models()
	var count := 0
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(ASSET_ROOT+"Meshes"))
	var meshes: Dictionary = {}
	for id in ["beech_mature_a","beech_mature_b","beech_young","beech_dead","hazel_shrub","fern_cluster","grass_tuft","meadow_flowers","moss_boulder_a","moss_boulder_b","forest_cliff","fallen_beech","beech_stump","timber_stack","woodcutters_shelter","wooden_footbridge","trail_sign","camp_tent","fire_ring","charcoal_mound","beech_terrain","beech_stream"]:
		var packed: PackedScene = ResourceLoader.load(ASSET_ROOT+id+".glb","PackedScene",ResourceLoader.CACHE_MODE_IGNORE_DEEP)
		var sample: Node3D = packed.instantiate()
		var mesh: ArrayMesh = mesh_in(sample).mesh.duplicate()
		for surface in range(mesh.get_surface_count()):
			mesh.surface_set_material(surface,shaders.ground if id == "beech_terrain" else shaders.water if id == "beech_stream" else shaders.painted)
		var path: String = ASSET_ROOT+"Meshes/"+id+".res"
		ResourceSaver.save(mesh,path)
		mesh.take_over_path(path)
		var uid := ResourceLoader.get_resource_uid(path)
		if uid != ResourceUID.INVALID_ID:
			if ResourceUID.has_id(uid):
				ResourceUID.set_id(uid,path)
			else:
				ResourceUID.add_id(uid,path)
		meshes[id] = mesh
		sample.free()
	for node in root.find_children("*","",true,false):
		if str(node.name).begins_with("@"):
			node.name = node.get_class()+"_%04d" % count
			count += 1
		if node is MultiMeshInstance3D:
			node.multimesh.mesh = meshes[node.get_meta("asset_id")]
			node.material_override = shaders.painted
		elif node is MeshInstance3D:
			var parent: Node = node.get_parent()
			if parent.has_meta("asset_id"):
				node.mesh = meshes[parent.get_meta("asset_id")]
			if node.get_parent().name == "beech_terrain":
				node.material_override = shaders.ground
			elif node.get_parent().name == "beech_stream":
				node.material_override = shaders.water
			else:
				node.material_override = shaders.painted
	root.set_meta("surface_cell_height",0.05)
	root.get_node("Places/RavineBridge/Center").position.y = 6.2
	var geometry: Node3D = root.get_node("Geometry")
	for node in geometry.get_children():
		if node is StaticBody3D and node.name != "Ground":
			var box: BoxShape3D = node.get_node("Collision").shape
			if box.size.y == 3.5:
				geometry.remove_child(node)
				node.free()
			elif box.size.x > 20 and box.size.y < 2:
				box.size = Vector3(40.6,box.size.y,box.size.z)
	var bridge: Node3D = geometry.get_node("BridgeDeck/BridgeVisual")
	bridge.scale.x = 1.7
	geometry.get_node("Ground/Collision").shape = meshes.beech_terrain.create_trimesh_shape()
	for node in root.get_node("HeroTreesAndRocks").get_children():
		if node.has_meta("asset_id") and str(node.get_meta("asset_id")).begins_with("moss_boulder") and absf(node.position.x-river_x(node.position.z))<26:
			node.position.y = height(node.position.x,node.position.z)
	return {"normalized_names":count,"shared_meshes":meshes.size()}

func conform_to_ground() -> Dictionary:
	# Explicit offline action after editing terrain; runtime never rebuilds the scene.
	root = get_parent() as Node3D
	profile = JSON.parse_string(FileAccess.get_file_as_string(ASSET_ROOT+"layout.json"))
	var count := 0
	for node in root.find_children("*","",true,false):
		if node is MultiMeshInstance3D:
			for i in range(node.multimesh.instance_count):
				var transform: Transform3D = node.multimesh.get_instance_transform(i)
				var world: Vector3 = node.global_transform*transform.origin
				transform.origin.y = height(world.x,world.z)-node.global_position.y
				node.multimesh.set_instance_transform(i,transform)
		elif node is Node3D and node.has_meta("asset_id") and not node is MultiMeshInstance3D:
			var id: String = node.get_meta("asset_id")
			if id not in ["beech_terrain","beech_stream","wooden_footbridge"]:
				node.position.y = height(node.position.x,node.position.z)-3 if id == "forest_cliff" else height(node.position.x,node.position.z)
				count += 1
	for body in root.get_node("Geometry").get_children():
		if body is StaticBody3D and body.name != "Ground":
			var box: BoxShape3D = body.get_node("Collision").shape
			if box.size.x < 20:
				body.position.y = height(body.position.x,body.position.z)+box.size.y/2
	return {"conformed_models":count}

func polish_clearings() -> Dictionary:
	# Explicit one-time composition pass; repeated calls preserve manual edits.
	root = get_parent() as Node3D
	if root.has_meta("clearing_composition_version"):
		return {"status":"already_composed"}
	profile = JSON.parse_string(FileAccess.get_file_as_string(ASSET_ROOT+"layout.json"))
	var counts: Dictionary = {}
	var destinations := {
		"woodcutters_shelter":[Vector2(-315,217),Vector2(-296,228)],
		"timber_stack":[Vector2(-312,230),Vector2(-309,230),Vector2(-306,230),Vector2(-303,230),Vector2(-300,230)],
		"beech_stump":[Vector2(-315,212),Vector2(-311,212),Vector2(-307,212),Vector2(-303,212),Vector2(-299,212),Vector2(-295,212)],
		"charcoal_mound":[Vector2(-290,224)],"fallen_beech":[Vector2(-291,214)]}
	for node in root.get_node("Places/Woodcutters").get_children():
		if not node.has_meta("asset_id"):
			continue
		var id: String = node.get_meta("asset_id")
		var index: int = counts.get(id,0)
		if destinations.has(id) and index < destinations[id].size():
			var point: Vector2 = destinations[id][index]
			node.position = Vector3(point.x,height(point.x,point.y),point.y)
			counts[id] = index+1
	# Move the existing structural colliders with their visible shelter posts.
	for body in root.get_node("Geometry").get_children():
		if str(body.name).begins_with("ShelterPost"):
			for pair in [[Vector2(-320,226),Vector2(-315,217)],[Vector2(-299,239),Vector2(-296,228)]]:
				var p := Vector2(body.position.x,body.position.z)
				if p.distance_to(pair[0]) < 5:
					p += pair[1]-pair[0]
					body.position = Vector3(p.x,height(p.x,p.y)+1.7,p.y)
	counts.clear()
	var camp_positions := {"camp_tent":[Vector2(332,-284),Vector2(349,-283),Vector2(338,-299)],
		"fire_ring":[Vector2(343,-292)],"timber_stack":[Vector2(347,-287)],
		"fallen_beech":[Vector2(340,-278)],"beech_stump":[Vector2(348,-280)]}
	for node in root.get_node("Places/KagerCamp").get_children():
		if not node.has_meta("asset_id"):
			continue
		var id: String = node.get_meta("asset_id")
		var index: int = counts.get(id,0)
		if camp_positions.has(id) and index < camp_positions[id].size():
			var p: Vector2 = camp_positions[id][index]
			node.position = Vector3(p.x,height(p.x,p.y),p.y)
			counts[id] = index+1
	var details := Node3D.new()
	attach(root,details,"ClearingDetails")
	var painted: ShaderMaterial = load(ASSET_ROOT+"painted_forest.tres")
	rng.seed = 6842026
	for center in [Vector2(-305,220),Vector2(340,-290),Vector2(240,50),Vector2(-425,330)]:
		for i in range(36):
			var angle: float = rng.randf()*TAU
			var p: Vector2 = center+Vector2(cos(angle),sin(angle))*rng.randf_range(7,16)
			if road_distance(p.x,p.y)<3:
				continue
			var id: String = ["grass_tuft","fern_cluster","meadow_flowers"][i%3]
			var wrapper := Node3D.new()
			attach(details,wrapper,id)
			wrapper.set_meta("asset_id",id)
			wrapper.position = Vector3(p.x,height(p.x,p.y),p.y)
			wrapper.rotation.y = angle
			wrapper.scale = Vector3.ONE*rng.randf_range(0.8,1.5)
			var mesh := MeshInstance3D.new()
			attach(wrapper,mesh,"Mesh")
			mesh.mesh = load(ASSET_ROOT+"Meshes/"+id+".res")
			mesh.material_override = painted
			mesh.visibility_range_end = 80
			mesh.visibility_range_end_margin = 12
	root.set_meta("clearing_composition_version",1)
	return {"detail_clusters":details.get_child_count()}

func refresh_stream_material() -> bool:
	var material: ShaderMaterial = load(ASSET_ROOT+"stream_water.tres")
	material.shader.code = """shader_type spatial;
render_mode unshaded;
varying vec3 world_position;
void vertex() { world_position = (MODEL_MATRIX * vec4(VERTEX,1.0)).xyz; }
void fragment() { float ripple = sin(world_position.x*1.8+world_position.z*0.65+TIME*1.1)*sin(world_position.z*2.1-TIME*0.8); ALBEDO = mix(vec3(0.025,0.12,0.19),vec3(0.08,0.31,0.37),ripple*0.35+0.5); ROUGHNESS = 0.8; }
"""
	return ResourceSaver.save(material,ASSET_ROOT+"stream_water.tres") == OK
