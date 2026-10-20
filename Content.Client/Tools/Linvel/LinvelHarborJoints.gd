@tool
extends "res://Tools/Linvel/LinvelHarborPiers.gd"
## Cut clear pier portals through the curved quay; no coplanar stone on wood.

func wall_span(parent: Node3D, a: Vector2, b: Vector2, label: String, top_height: float) -> void:
	var delta := b-a
	if delta.length()<0.05:
		return
	var midpoint := (a+b)*0.5
	var yaw := -atan2(delta.y,delta.x)
	var model := add_asset(parent,"harbor_seawall",Vector3(midpoint.x,top_height-1.415,midpoint.y),rad_to_deg(yaw),label)
	model.scale.x = delta.length()/4.0
	solid(parent,label+"Collision",Vector3(midpoint.x,top_height-0.14,midpoint.y),Vector3(delta.length()+0.04,0.28,0.88),yaw,true)

func portal_front(x: float) -> float:
	if x>=-65.25 and x<=-58.75:
		return 64.2
	if x>=-31.25 and x<=-24.75:
		return 67.0
	return shore_z(x)

func finish_pier_portals() -> Dictionary:
	city = get_parent() as Node3D
	if city.scene_file_path != "res://Scenes/Regions/Linvel/LinvelCity.tscn":
		return {"error":"Open LinvelCity before the portal correction."}
	part = ResourceLoader.load(HARBOR_PART,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	if part.has_meta("pier_portals_v4"):
		part.free()
		return {"error":"Already corrected; preserve manual edits."}
	painted = load(CITY+"Materials/linvel_painted.tres")
	var quay := part.get_node("QuayAndAccess") as Node3D
	for child in quay.get_children():
		if str(child.name).begins_with("QuayWall") or str(child.name).begins_with("QuayCap"):
			quay.remove_child(child)
			child.free()
	var segment := 0
	for interval in [Vector2(-88,-65.25),Vector2(-58.75,-31.25),Vector2(-24.75,-16)]:
		var count := ceili((interval.y-interval.x)/4.0)
		for index in range(count):
			var x1 := lerpf(interval.x,interval.y,float(index)/count)
			var x2 := lerpf(interval.x,interval.y,float(index+1)/count)
			wall_span(quay,Vector2(x1,shore_z(x1)),Vector2(x2,shore_z(x2)),"QuayWall%02d" % segment,QUAY)
			segment += 1
	for portal in [[-65.25,-58.75,64.2],[-31.25,-24.75,67.0]]:
		for x in [portal[0],portal[1]]:
			wall_span(quay,Vector2(x,shore_z(x)),Vector2(x,portal[2]),"PortalReturn%d" % segment,QUAY-0.08)
			segment += 1
	var floors := part.get_node("RigidPaving") as Node3D
	for child in floors.get_children():
		if str(child.name).begins_with("LowerQuay"):
			floors.remove_child(child)
			child.free()
	var material: Material = load(CITY+"Materials/RigidPaving.tres")
	var breaks: Array[float] = [-94,-88,-84,-80,-76,-72,-68,-65.25,-58.75,-55,-51,-47,-43,-39,-35,-31.25,-24.75,-20,-16,-15]
	for index in range(breaks.size()-1):
		var x1 := breaks[index]
		var x2 := breaks[index+1]
		# Choose each interval's front independently: discontinuities become hard portals.
		var middle := (x1+x2)*0.5
		var is_portal := middle>-65.25 and middle<-58.75 or middle>-31.25 and middle<-24.75
		var z1 := portal_front(middle) if is_portal else shore_z(x1)
		var z2 := portal_front(middle) if is_portal else shore_z(x2)
		add_flat_quad(floors,"LowerQuay%02d" % index,[Vector3(x1,6.985,52.2),Vector3(x1,6.985,z1),Vector3(x2,6.985,z2),Vector3(x2,6.985,52.2)],material)
	for entry in [Vector2(-62,64.2),Vector2(-28,67)]:
		solid(floors,"PierThreshold%s" % entry.x,Vector3(entry.x,6.885,entry.y-0.5),Vector3(6.5,0.2,1.0),0,true)
	# Keep each tree in a flat planting bed rather than paving over its roots.
	var grass := ShaderMaterial.new()
	grass.shader = load(CITY+"Materials/RigidPaving.gdshader")
	grass.set_shader_parameter("paving_texture",load(ASSETS+"Beech/Revision2/StylePass/GroundGrass.res"))
	grass.set_shader_parameter("tiles_per_metre",0.08)
	ResourceSaver.save(grass,CITY+"Materials/FlatPlanting.tres")
	grass.take_over_path(CITY+"Materials/FlatPlanting.tres")
	for p in [Vector2(-48,37),Vector2(-79,35)]:
		add_flat_quad(floors,"TreeBed%s" % p.x,[Vector3(p.x-2,8.51,p.y-2),Vector3(p.x-2,8.51,p.y+2),Vector3(p.x+2,8.51,p.y+2),Vector3(p.x+2,8.51,p.y-2)],grass)
	part.set_meta("pier_portals_v4",true)
	var packed := PackedScene.new()
	packed.pack(part)
	var error := ResourceSaver.save(packed,HARBOR_PART)
	part.free()
	if error!=OK:
		return {"error":error}
	var old := city.get_node("World/CityParts/HarborQuarter")
	old.get_parent().remove_child(old)
	old.free()
	var saved: PackedScene = ResourceLoader.load(HARBOR_PART,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE)
	var instance := saved.instantiate()
	city.get_node("World/CityParts").add_child(instance)
	instance.owner = city
	return {"pier_portals":2,"stone_above_timber_removed":true,"threshold_collision":true,"planting_beds":2}

func close_threshold_seams() -> Dictionary:
	city = get_parent() as Node3D
	if city.scene_file_path != "res://Scenes/Regions/Linvel/LinvelCity.tscn":
		return {"error":"Open LinvelCity."}
	part = ResourceLoader.load(HARBOR_PART,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	if part.has_meta("threshold_seams_closed"):
		part.free()
		return {"error":"Already closed."}
	var changed := 0
	for visual in part.get_node("RigidPaving").get_children():
		if not visual is MeshInstance3D or not str(visual.name).begins_with("LowerQuay"):
			continue
		var arrays: Array = visual.mesh.surface_get_arrays(0)
		var vertices: PackedVector3Array = arrays[Mesh.ARRAY_VERTEX]
		var modified := false
		for index in range(vertices.size()):
			var p := vertices[index]
			if absf(p.z-64.2)<0.001 and p.x>=-65.26 and p.x<=-58.74 or absf(p.z-67.0)<0.001 and p.x>=-31.26 and p.x<=-24.74:
				vertices[index].z += 0.03
				modified = true
		if modified:
			arrays[Mesh.ARRAY_VERTEX] = vertices
			var mesh := ArrayMesh.new()
			mesh.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES,arrays)
			visual.mesh = mesh
			changed += 1
	part.set_meta("threshold_seams_closed",true)
	var packed := PackedScene.new()
	packed.pack(part)
	ResourceSaver.save(packed,HARBOR_PART)
	part.free()
	var old := city.get_node("World/CityParts/HarborQuarter")
	old.get_parent().remove_child(old)
	old.free()
	var saved: PackedScene = ResourceLoader.load(HARBOR_PART,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE)
	var instance := saved.instantiate()
	city.get_node("World/CityParts").add_child(instance)
	instance.owner = city
	return {"threshold_surfaces":changed,"seams_closed_metres":0.03}
