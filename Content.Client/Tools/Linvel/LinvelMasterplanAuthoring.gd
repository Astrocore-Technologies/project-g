@tool
extends Node
## Explicit editor authoring only. Saved district scenes and native Terrain3D are
## the working sources; no generator is attached to the finished city.
const BASE := "res://Assets/World/Linvel/City/Masterplan/"
const PARTS := "res://Scenes/Regions/Linvel/Masterplan/Parts/"
const MAIN := "res://Scenes/Regions/Linvel/LinvelCity.tscn"
const LEGACY := "res://Scenes/Regions/Linvel/Legacy/LinvelCity_Review03.tscn"
var layout: Dictionary
var polygons: Array[PackedVector2Array] = []
var root: Node3D
var part: Node3D
var districts: Dictionary = {}
var materials: Dictionary = {}
var building_count := 0

func own(parent: Node, child: Node, label: String) -> Node:
	child.name = label
	parent.add_child(child)
	child.owner = part
	return child

func vec(a: Array) -> Vector3:
	return Vector3(a[0],a[1],a[2])

func material(key: String, color: Color) -> StandardMaterial3D:
	if materials.has(key):
		return materials[key]
	var m := StandardMaterial3D.new()
	m.resource_name = "Blockout_" + key
	m.albedo_color = color
	m.roughness = 1.0
	m.diffuse_mode = BaseMaterial3D.DIFFUSE_TOON
	materials[key] = m
	return m

func body_mesh(parent: Node3D, label: String, mesh: Mesh, mat: Material, position := Vector3.ZERO, solid := false, layer := 1) -> MeshInstance3D:
	var mi := own(parent,MeshInstance3D.new(),label) as MeshInstance3D
	mi.mesh = mesh
	for surface in mesh.get_surface_count():
		if mesh.surface_get_material(surface)==null:
			mesh.surface_set_material(surface,mat)
	mi.material_override = mat
	mi.position = position
	if solid:
		var body := own(parent,StaticBody3D.new(),label+"Collision") as StaticBody3D
		body.position = position
		body.collision_layer = layer
		body.collision_mask = 0
		body.add_to_group("surface_geometry",true)
		var cs := own(body,CollisionShape3D.new(),"Shape") as CollisionShape3D
		cs.shape = mesh.create_trimesh_shape()
	return mi

func box(parent: Node3D, label: String, pos: Vector3, size: Vector3, mat: Material, solid := true, layer := 1) -> MeshInstance3D:
	var mesh := BoxMesh.new()
	mesh.size = size
	var mi := body_mesh(parent,label,mesh,mat,pos)
	if solid:
		var body := own(parent,StaticBody3D.new(),label+"Collision") as StaticBody3D
		body.position = pos
		body.collision_layer = layer
		body.collision_mask = 0
		body.add_to_group("surface_geometry",true)
		var cs := own(body,CollisionShape3D.new(),"Shape") as CollisionShape3D
		var shape := BoxShape3D.new()
		shape.size = size
		cs.shape = shape
	return mi

func tri(st: SurfaceTool, a: Vector3, b: Vector3, c: Vector3) -> void:
	st.add_vertex(a)
	st.add_vertex(c)
	st.add_vertex(b)

func quad(st: SurfaceTool, a: Vector3, b: Vector3, c: Vector3, d: Vector3) -> void:
	tri(st,a,b,c)
	tri(st,a,c,d)

func finish_mesh(st: SurfaceTool) -> ArrayMesh:
	st.generate_normals()
	st.index()
	return st.commit()

func top_height(p: Vector2) -> float:
	var h := -8.0
	for i in polygons.size():
		if Geometry2D.is_point_in_polygon(p,polygons[i]):
			h = maxf(h,float(layout.platforms[i].height))
	return h

func link_projection(p: Vector2, link: Dictionary) -> Vector2:
	var a := Vector2(link.start[0],link.start[2])
	var b := Vector2(link.end[0],link.end[2])
	var v := b-a
	var t := (p-a).dot(v)/v.length_squared()
	return Vector2(t,p.distance_to(a+v*clampf(t,0,1)))

func portal_cut(p: Vector2, h: float) -> bool:
	for link in layout.links:
		if link.kind not in ["ramp","stairs"]:
			continue
		var q := link_projection(p,link)
		if q.x>=-0.03 and q.x<=1.03 and q.y<float(link.width)*0.5+0.15:
			var y := lerpf(link.start[1],link.end[1],clampf(q.x,0,1))
			if y<h-0.001:
				return true
	return false

func build_platform(p: Dictionary, polygon: PackedVector2Array) -> void:
	part = districts[p.part]
	var node := own(part,Node3D.new(),p.id) as Node3D
	node.set_meta("terrace_height_m",p.height)
	var h := float(p.height)
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var cuts: Array[PackedVector2Array] = []
	var index := int(layout.platforms.find(p))
	for i in polygons.size():
		var other_h := float(layout.platforms[i].height)
		if other_h>h+0.1 or (is_equal_approx(other_h,h) and i>index):
			cuts.append(polygons[i])
	for link in layout.links:
		if link.kind not in ["ramp","stairs"]:
			continue
		var sy := float(link.start[1])
		var ey := float(link.end[1])
		if minf(sy,ey)>=h:
			continue
		var t0 := -.01
		var t1 := 1.01
		if sy>ey:
			t0 = maxf(t0,(sy-h)/(sy-ey))
		elif ey>sy:
			t1 = minf(t1,(h-sy)/(ey-sy))
		var start := Vector2(link.start[0],link.start[2])
		var end := Vector2(link.end[0],link.end[2])
		var aa := start.lerp(end,t0)
		var bb := start.lerp(end,t1)
		var r := Vector2((end-start).y,-(end-start).x).normalized()*(float(link.width)*.5+.08)
		cuts.append(PackedVector2Array([aa-r,bb-r,bb+r,aa+r]))
	var bounds := Rect2(polygon[0],Vector2.ZERO)
	for v in polygon:
		bounds = bounds.expand(v)
	for z in range(floori(bounds.position.y/2)*2,ceili(bounds.end.y/2)*2,2):
		for x in range(floori(bounds.position.x/2)*2,ceili(bounds.end.x/2)*2,2):
			var cell := PackedVector2Array([Vector2(x,z),Vector2(x+2,z),Vector2(x+2,z+2),Vector2(x,z+2)])
			var pieces := Geometry2D.intersect_polygons(cell,polygon)
			for cut in cuts:
				var next: Array[PackedVector2Array] = []
				for piece in pieces:
					next.append_array(Geometry2D.clip_polygons(piece,cut))
				pieces = next
				if pieces.is_empty():
					break
			for piece in pieces:
				var indices := Geometry2D.triangulate_polygon(piece)
				for j in range(0,indices.size(),3):
					var a := piece[indices[j]]
					var b := piece[indices[j+1]]
					var c := piece[indices[j+2]]
					if (b-a).cross(c-a)>0:
						var swap := b
						b = c
						c = swap
					tri(st,Vector3(a.x,h,a.y),Vector3(b.x,h,b.y),Vector3(c.x,h,c.y))
	var floor_mesh := finish_mesh(st)
	body_mesh(node,"FlatTerrace",floor_mesh,material(p.id,Color(p.color)),Vector3.ZERO,true)
	var side := SurfaceTool.new()
	side.begin(Mesh.PRIMITIVE_TRIANGLES)
	for i in polygon.size():
		var a := polygon[i]
		var b := polygon[(i+1)%polygon.size()]
		var n := ceili(a.distance_to(b)/1.5)
		for j in n:
			var aa := a.lerp(b,float(j)/n)
			var bb := a.lerp(b,float(j+1)/n)
			if portal_cut((aa+bb)*0.5,h):
				continue
			# Side mass stops beneath lower terraces; no hidden wall across a route.
			var low := maxf(1.5,top_height((aa+bb)*0.5+Vector2((b-a).y,-(b-a).x).normalized()*2))
			low = minf(low,h-0.45)
			quad(side,Vector3(aa.x,low,aa.y),Vector3(aa.x,h,aa.y),Vector3(bb.x,h,bb.y),Vector3(bb.x,low,bb.y))
	var wall_material := material("Limestone",Color("a39e85"))
	wall_material.cull_mode = BaseMaterial3D.CULL_DISABLED
	body_mesh(node,"RetainingMass",finish_mesh(side),wall_material)

func build_link(q: Dictionary) -> void:
	part = districts[q.part]
	var parent := own(part,Node3D.new(),q.id) as Node3D
	parent.set_meta("route_width_m",q.width)
	parent.set_meta("route_kind",q.kind)
	var a := vec(q.start)
	var b := vec(q.end)
	var r := Vector3(b.z-a.z,0,a.x-b.x).normalized()*float(q.width)*0.5
	var mat := material("Route",Color("e0ceaa"))
	if q.kind in ["pier","bridge"]:
		mat = material("Timber",Color("a77a4c"))
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	var count := maxi(1,ceili(absf(b.y-a.y)/0.18)) if q.kind=="stairs" else 1
	for i in count:
		var c := a.lerp(b,float(i)/count)
		var d := a.lerp(b,float(i+1)/count)
		if q.kind=="stairs":
			var step_height := maxf(c.y,d.y)
			var c0 := c
			var d0 := d
			c.y = step_height
			d.y = step_height
			quad(st,c-r,d-r,d+r,c+r)
			if b.y<a.y:
				quad(st,d-r,d0-r,d0+r,d+r)
			else:
				quad(st,c0-r,c-r,c+r,c0+r)
		else:
			quad(st,c-r,d-r,d+r,c+r)
	quad(st,a-r-Vector3(0,.6,0),b-r-Vector3(0,.6,0),b-r,a-r)
	quad(st,a+r,b+r,b+r-Vector3(0,.6,0),a+r-Vector3(0,.6,0))
	body_mesh(parent,"VisibleSurface",finish_mesh(st),mat)
	# Continuous planar collision for steps; actual widths, never inherited scale.
	var collision_st := SurfaceTool.new()
	collision_st.begin(Mesh.PRIMITIVE_TRIANGLES)
	quad(collision_st,a-r,b-r,b+r,a+r)
	var ramp := finish_mesh(collision_st)
	var hidden := body_mesh(parent,"WalkableSlope",ramp,mat,Vector3.ZERO,true)
	hidden.visible = false
	if q.kind in ["pier","bridge","breakwater"]:
		var n := maxi(1,ceili(a.distance_to(b)/10))
		for i in range(n+1):
			var c := a.lerp(b,float(i)/n)
			for sign_value in [-1,1]:
				var center: Vector3 = c+r*float(sign_value)*0.9
				box(parent,"Support%d_%d"%[i,sign_value],Vector3(center.x,(c.y-5)*.5,center.z),Vector3(.7,c.y+5,.7),mat,false)

func build_street(q: Dictionary) -> void:
	part = districts[q.part]
	var st := SurfaceTool.new()
	st.begin(Mesh.PRIMITIVE_TRIANGLES)
	for i in range(q.points.size()-1):
		var a := vec(q.points[i])+Vector3(0,.045,0)
		var b := vec(q.points[i+1])+Vector3(0,.045,0)
		var r := Vector3(b.z-a.z,0,a.x-b.x).normalized()*float(q.width)*.5
		quad(st,a-r,b-r,b+r,a+r)
	var road := body_mesh(part,q.id,finish_mesh(st),material("Street",Color("eee0bd")))
	road.set_meta("route_width_m",q.width)

func build_building(b: Dictionary) -> void:
	part = districts[b.part]
	var packed: PackedScene = load(BASE+"Kit/block_"+b.kit+".glb")
	var instance := packed.instantiate() as Node3D
	own(part,instance,b.id)
	instance.position = vec(b.position)
	instance.rotation_degrees.y = float(b.yaw)
	instance.scale = Vector3.ONE*float(b.scale)
	instance.set_meta("blockout_role",b.role)
	instance.set_meta("prototype_dimensions_m",vec(b.dimensions))
	instance.set_meta("not_final_art",true)
	if b.role!="ship_clearance":
		building_count += 1
		var body := own(part,StaticBody3D.new(),b.id+"Collision") as StaticBody3D
		body.position = instance.position
		body.rotation = instance.rotation
		body.collision_layer = 4
		body.collision_mask = 0
		body.add_to_group("surface_geometry",true)
		body.add_to_group("surface_occluder",true)
		body.set_meta("surface_nonwalkable_top",true)
		var shape := BoxShape3D.new()
		shape.size = Vector3(b.dimensions[0],b.dimensions[2]*.72,b.dimensions[1])
		var cs := own(body,CollisionShape3D.new(),"Shape") as CollisionShape3D
		cs.shape = shape
		cs.position.y = shape.size.y*.5

func cylinder(parent: Node3D, label: String, p: Vector3, radius: float, h: float, mat: Material, solid := false) -> MeshInstance3D:
	var mesh := CylinderMesh.new()
	mesh.top_radius = radius
	mesh.bottom_radius = radius
	mesh.height = h
	mesh.radial_segments = 32
	return body_mesh(parent,label,mesh,mat,p+Vector3(0,h*.5,0),solid)

func special_spaces() -> void:
	part = districts.CityCenter
	cylinder(part,"FountainEnvelope",Vector3(0,23,10),3.3,.9,material("Fountain",Color("86b8ba")),true)
	for x in [-24,24]:
		for z in [-5,9,23]:
			box(part,"MarketEnvelope_%d_%d"%[x,z],Vector3(x,24.3,z),Vector3(4,2.6,4),material("Market",Color("b47a53")),true,4)
	part = districts.TrainingQuarter
	var arena := cylinder(part,"ArenaFloor",Vector3(133,36.05,-109),1,.12,material("Sand",Color("d9b56e")))
	arena.scale = Vector3(31,1,24)
	# Fence sectors leave generous west and south portals.
	for i in 40:
		var angle := float(i)*TAU/40
		if absf(angle-PI)<.22 or absf(angle-PI*.5)<.22:
			continue
		var p := Vector3(133+31*cos(angle),36.7,-109+24*sin(angle))
		box(part,"ArenaRail_%02d"%i,p,Vector3(.55,1.4,2.7),material("Timber",Color("a77a4c")),false).rotation.y = -angle
	for i in 3:
		box(part,"StandTier_%d"%i,Vector3(133,36.4+i*.55,-135.5-i*1.5),Vector3(29,.8+i*1.1,1.8),material("Stand",Color("b7a47f")))
	for i in 3:
		var p := Vector3(121+i*12,36,-98)
		cylinder(part,"TrainingDummyEnvelope_%d"%(i+1),p,.42,1.8,material("Dummy",Color("8c6a46")),true)
	box(part,"DornCourt",Vector3(137,36.045,-64),Vector3(39,.08,19),material("Sand",Color("d9b56e")),false)
	box(part,"DornShelterEnvelope",Vector3(164,39,-65),Vector3(12,6,13),material("Plaster",Color("d7c69f")),true,4)
	part = districts.GateQuarter
	# Landward wall only, with a full 20m opening around the road.
	for entry in [[-49.0,134.0,32.0],[117.0,104.0,36.0]]:
		box(part,"LandwardWall_%d"%int(entry[0]),Vector3(entry[0],entry[2]+2,-155),Vector3(entry[1],4,2.4),material("Limestone",Color("a39e85")),true,4)
	part = districts.CoastAndBackdrop
	var sea := PlaneMesh.new()
	sea.size = Vector2(2400,2400)
	var water := material("Sea",Color("388ca5"))
	water.roughness = .8
	body_mesh(part,"OpenSea",sea,water,Vector3(0,-.05,0))
	# Low frequency masses establish the valley scale without final mountain art.
	for i in 19:
		var mesh := CylinderMesh.new()
		mesh.top_radius = 1
		mesh.bottom_radius = 82+float(i%4)*14
		mesh.height = 110+float((i*7)%9)*17
		mesh.radial_segments = 7
		var p := Vector3(-700+i*79,mesh.height*.5-5,-440-float(i%3)*110)
		var hill := body_mesh(part,"MountainMass_%02d"%i,mesh,material("Mountain%d"%(i%3),Color("72989b").lerp(Color("a9bdc0"),float(i%3)*.17)),p)
		hill.scale.z = .75
	part = districts.GardensQuarter
	for i in 5:
		box(part,"OrchardPlot_%d"%i,Vector3(-26+i*25,32.06,-218+float(i%2)*34),Vector3(18,.1,21),material("Garden",Color("a0ae68")),false)
	# Broad canopy envelopes are compositional masses, not a decorative asset pass.
	var groves := [[-167,18,-24],[-153,18,31],[-143,29,-111],[-82,29,-109],[-56,32,-132],[10,32,-154],[154,36,-140],[172,36,-65],[158,20,18],[137,20,65],[-126,12,61],[-49,12,85],[59,12,95],[79,4,127]]
	for i in groves.size():
		var a: Array = groves[i]
		var target := districts.ResidentialTerraces as Node3D
		part = target
		var mesh := SphereMesh.new()
		mesh.radius = 5.5
		mesh.height = 10
		mesh.radial_segments = 10
		mesh.rings = 4
		body_mesh(target,"CanopyEnvelope_%02d"%i,mesh,material("Canopy",Color("6f9251")),Vector3(a[0],a[1]+6,a[2]))

func terrain_height(p: Vector2) -> float:
	var h := top_height(p)-.28
	if h < -7:
		for i in polygons.size():
			var distance := 10000.0
			var poly := polygons[i]
			for j in poly.size():
				distance = minf(distance,p.distance_to(Geometry2D.get_closest_point_to_segment(p,poly[j],poly[(j+1)%poly.size()])))
			h = maxf(h,float(layout.platforms[i].height)-1.0-distance*2.8)
	if p.y < -170:
		var coast := 170+(-p.y-170)*.55-absf(p.x)
		if coast>0:
			var mainland := 25+7*exp(-pow((p.y+190)/70,2))+13*pow(sin(p.x*.012+p.y*.008),2)*smoothstep(200,420,-p.y)
			mainland = lerpf(-8,mainland,clampf(coast/28,0,1))
			h = maxf(h,mainland)
			if top_height(p)>0:
				h = top_height(p)-.28
	for q in layout.links:
		if q.kind not in ["stairs","ramp"]:
			continue
		var projection := link_projection(p,q)
		if projection.x>=-.03 and projection.x<=1.03 and projection.y<float(q.width)*.5+2:
			h = minf(h,lerpf(q.start[1],q.end[1],clampf(projection.x,0,1))-.3)
	return maxf(-8,h)

func build_terrain(world: Node3D) -> Dictionary:
	part = root
	var terrain := own(world,Terrain3D.new(),"Terrain3D") as Terrain3D
	terrain.vertex_spacing = 2.0
	terrain.change_region_size(Terrain3D.SIZE_128)
	terrain.mesh_size = 48
	terrain.mesh_lods = 6
	terrain.collision.mode = Terrain3DCollision.FULL_GAME
	terrain.collision.layer = 1
	terrain.add_to_group("surface_terrain",true)
	var assets: Terrain3DAssets = load("res://Assets/World/Linvel/City/Terrain/CenterAssets.tres").duplicate(true)
	terrain.assets = assets
	var mat: Terrain3DMaterial = load("res://Assets/World/Linvel/City/Terrain/CenterMaterial.tres").duplicate(true)
	terrain.material = mat
	var heights := Image.create(512,512,false,Image.FORMAT_RF)
	for iz in 512:
		for ix in 512:
			var p := Vector2(ix*2-512,iz*2-768)
			heights.set_pixel(ix,iz,Color(terrain_height(p),0,0,1))
	var maps: Array[Image] = [heights,null,null]
	terrain.data.import_images(maps,Vector3(-512,0,-768),0,1)
	for iz in 512:
		for ix in 512:
			var p := Vector3(ix*2-512,0,iz*2-768)
			var h := heights.get_pixel(ix,iz).r
			terrain.data.set_control_auto(p,false)
			terrain.data.set_control_base_id(p,2 if h>1 and p.z>-170 and top_height(Vector2(p.x,p.z))<0 else 0)
			terrain.data.set_control_navigation(p,h>0)
	terrain.data.update_maps(Terrain3DRegion.TYPE_MAX,true,true)
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(BASE+"TerrainData"))
	terrain.data.save_directory(BASE+"TerrainData")
	terrain.data_directory = BASE+"TerrainData"
	ResourceSaver.save(assets,BASE+"TerrainAssets.tres")
	assets.take_over_path(BASE+"TerrainAssets.tres")
	ResourceSaver.save(mat,BASE+"TerrainMaterial.tres")
	mat.take_over_path(BASE+"TerrainMaterial.tres")
	var dependencies: Array[Resource] = []
	for file in DirAccess.get_files_at(BASE+"TerrainData"):
		if file.ends_with(".res"):
			dependencies.append(load((BASE+"TerrainData/")+file))
	terrain.set_meta("terrain_region_resources",dependencies)
	terrain.set_meta("masterplan_01",true)
	return {"regions":terrain.data.get_region_count(),"vertex_spacing":2,"directory":terrain.data_directory}

func build_city() -> Dictionary:
	var current := get_parent() as Node3D
	if current.scene_file_path!=MAIN:
		return {"error":"Open LinvelCity before authoring"}
	if current.get_meta("masterplan_revision",0)!=0 or ResourceLoader.exists(LEGACY):
		return {"error":"Existing authored revision: refuse regeneration; edit native scenes instead"}
	layout = JSON.parse_string(FileAccess.get_file_as_string(BASE+"layout.json"))
	if not layout.paper_clearance_conflicts.is_empty():
		return {"error":"Resolve paper route clearance before build","conflicts":layout.paper_clearance_conflicts}
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(PARTS))
	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(LEGACY.get_base_dir()))
	var previous_owner := owner
	owner = null
	var legacy := PackedScene.new()
	var error := legacy.pack(current)
	owner = previous_owner
	if error!=OK:
		return {"error":"Legacy pack failed","code":error}
	error = ResourceSaver.save(legacy,LEGACY)
	if error!=OK:
		return {"error":"Legacy save failed","code":error}
	root = Node3D.new()
	root.name = "LinvelCity"
	root.set_meta("masterplan_revision",1)
	root.set_meta("linvel_city_stage","whole-city-blockout-awaiting-review")
	root.set_meta("art_direction",layout.art_direction)
	root.set_meta("human_playtest_approved",false)
	root.set_meta("server_integration",false)
	root.set_meta("legacy_scene",LEGACY)
	root.set_meta("paper_plan","art-source/linvel/masterplan/linvel-paper-plan.png")
	add_child(root)
	part = root
	var world := own(root,Node3D.new(),"World") as Node3D
	var parts := own(world,Node3D.new(),"CityParts") as Node3D
	for p in layout.platforms:
		var poly := PackedVector2Array()
		for a in p.points:
			poly.append(Vector2(a[0],a[1]))
		polygons.append(poly)
	for q in layout.districts:
		var district := Node3D.new()
		district.name = q.id
		district.set_meta("review_stage","Untextured blockout, not final art")
		parts.add_child(district)
		districts[q.id] = district
	for i in layout.platforms.size():
		build_platform(layout.platforms[i],polygons[i])
	for q in layout.links:
		build_link(q)
	for q in layout.streets:
		build_street(q)
	for b in layout.buildings:
		build_building(b)
	special_spaces()
	for a in layout.anchors:
		part = districts[a.part]
		var marker := own(part,Marker3D.new(),"Anchor_"+a.id) as Marker3D
		marker.position = vec(a.position)
		marker.set_meta("public_label",a.label)
		marker.set_meta("review_only_not_server_binding",true)
	for q in layout.districts:
		var district := districts[q.id] as Node3D
		var packed := PackedScene.new()
		error = packed.pack(district)
		if error!=OK:
			return {"error":"District packing failed","district":q.id}
		error = ResourceSaver.save(packed,q.scene)
		if error!=OK:
			return {"error":"District save failed","district":q.id}
		parts.remove_child(district)
		district.free()
		var saved: PackedScene = ResourceLoader.load(q.scene,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE)
		var instance := saved.instantiate()
		assert(instance.scene_file_path==q.scene)
		parts.add_child(instance)
		instance.owner = root
	part = root
	var lights := current.get_node("World/Lighting").duplicate()
	world.add_child(lights)
	set_owners(lights,root)
	var environment: Environment = lights.get_node("SpringAtmosphere").environment.duplicate(true)
	environment.ambient_light_energy = .48
	environment.tonemap_mode = Environment.TONE_MAPPER_LINEAR
	lights.get_node("SpringAtmosphere").environment = environment
	var sun := lights.get_node("SpringSun") as DirectionalLight3D
	sun.light_energy = .95
	sun.directional_shadow_max_distance = 600
	var terrain_report := build_terrain(world)
	var packed := PackedScene.new()
	error = packed.pack(root)
	if error==OK:
		error = ResourceSaver.save(packed,MAIN)
	root.queue_free()
	return {"saved":error==OK,"buildings":building_count,"districts":layout.districts.size(),"terrain":terrain_report,"legacy":LEGACY,"main":MAIN}

func set_owners(node: Node, target: Node) -> void:
	node.owner = target
	for child in node.get_children():
		set_owners(child,target)

func reload_saved_city() -> void:
	EditorInterface.reload_scene_from_path(MAIN)
