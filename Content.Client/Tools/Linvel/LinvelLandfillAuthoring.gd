@tool
extends Node
## Bounded native Terrain3D correction. Does not regenerate districts or assets.
## The saved terrain data remains editable with Terrain3D's normal tools.
const DATA := "res://Assets/World/Linvel/City/Masterplan/layout.json"
const PLAN := "res://Assets/World/Linvel/City/landfill-plan.json"
var layout: Dictionary
var plan: Dictionary
var boundary := PackedVector2Array()
var platforms: Array[PackedVector2Array] = []
var route_segments: Array[Dictionary] = []

func prepare() -> void:
	layout = JSON.parse_string(FileAccess.get_file_as_string(DATA))
	plan = JSON.parse_string(FileAccess.get_file_as_string(PLAN))
	boundary.clear()
	platforms.clear()
	route_segments.clear()
	for p in plan.land_boundary_xz:
		boundary.append(Vector2(p[0],p[1]))
	for spec in layout.platforms:
		var polygon := PackedVector2Array()
		for p in spec.points:
			polygon.append(Vector2(p[0],p[1]))
		platforms.append(polygon)
	for spec in layout.streets+layout.links:
		if spec.get("kind","") in ["pier","breakwater","bridge"]:
			continue
		var points: Array = spec.get("points",[spec.get("start"),spec.get("end")])
		for i in range(points.size()-1):
			route_segments.append({"a":Vector2(points[i][0],points[i][2]),"b":Vector2(points[i+1][0],points[i+1][2]),"ay":float(points[i][1]),"by":float(points[i+1][1]),"radius":float(spec.width)*.5+.6})

func edge_distance(point: Vector2, polygon: PackedVector2Array) -> float:
	var distance := INF
	for i in polygon.size():
		distance = minf(distance,point.distance_to(Geometry2D.get_closest_point_to_segment(point,polygon[i],polygon[(i+1)%polygon.size()])))
	return distance

func platform_index(p: Vector2) -> int:
	for i in platforms.size():
		if Geometry2D.is_point_in_polygon(p,platforms[i]):
			return i
	return -1

func target_height(p: Vector2) -> float:
	# Keep fill below nearby lower terraces; stone stays on rigid scene geometry.
	var target := clampf(7+(105-p.y)*.085,7,32)
	for i in platforms.size():
		var d := edge_distance(p,platforms[i])
		if d<10:
			target = minf(target,float(layout.platforms[i].height)-.5)
	var route_height := INF
	for road in route_segments:
		var delta: Vector2 = road.b-road.a
		var t := clampf((p-road.a).dot(delta)/delta.length_squared(),0,1)
		if p.distance_to(road.a+delta*t)<road.radius:
			route_height = minf(route_height,lerpf(road.ay,road.by,t)-.28)
	if is_finite(route_height):
		target = route_height
	return maxf(plan.minimum_interior_ground_m,target)

func inspect_fill(include_platforms := false) -> Dictionary:
	prepare()
	var city := get_parent() as Node3D
	var terrain := city.get_node("World/Terrain3D") as Terrain3D
	var water := 0
	var holes := 0
	var minimum := INF
	var samples := 0
	for z in range(-280,137,4):
		for x in range(-250,285,4):
			var p := Vector2(x,z)
			if not Geometry2D.is_point_in_polygon(p,boundary) or edge_distance(p,boundary)<18:
				continue
			if not include_platforms and platform_index(p)>=0:
				continue
			var h := terrain.data.get_height(Vector3(x,0,z))
			samples += 1
			if not is_finite(h):
				holes += 1
			elif h<=1:
				water += 1
			if is_finite(h):
				minimum = minf(minimum,h)
	return {"scene":city.scene_file_path,"terrain_directory":terrain.data_directory,"samples":samples,"submerged_samples":water,"unexpected_holes":holes,"minimum_ground_m":minimum,"ok":water==0 and holes==0}

func close_urban_underlay() -> Dictionary:
	# Art01 used holes under terrace floors. Their seams exposed the global sea.
	# A continuous, low land foundation closes those holes without a high relief
	# that could morph through the rigid floors at distant Terrain3D LODs.
	prepare()
	var city := get_parent() as Node3D
	if city.scene_file_path!="res://Scenes/Regions/Linvel/Art01/LinvelCity_Art01.tscn":
		return {"error":"This correction applies only to Art01's former terrain holes"}
	if city.get_meta("dry_urban_underlay",false):
		return {"error":"Already applied; preserve manual terrain edits"}
	var terrain := city.get_node("World/Terrain3D") as Terrain3D
	var changed := 0
	for z in range(-290,145,2):
		for x in range(-260,291,2):
			var point := Vector2(x,z)
			if not Geometry2D.is_point_in_polygon(point,boundary) or platform_index(point)<0:
				continue
			var position := Vector3(x,0,z)
			terrain.data.set_height(position,3)
			terrain.data.set_control_hole(position,false)
			terrain.data.set_control_auto(position,false)
			terrain.data.set_control_base_id(position,0)
			terrain.data.set_control_navigation(position,false)
			changed += 1
	terrain.data.update_maps(Terrain3DRegion.TYPE_MAX,true,true)
	terrain.data.save_directory(terrain.data_directory)
	city.set_meta("dry_urban_underlay",true)
	var report := {"native_floor_cells":changed,"all_city_ground":inspect_fill(true),"rigid_district_geometry_unchanged":true}
	var path := ProjectSettings.globalize_path("res://../.artifacts/linvel-landfill/art01-underlay-report.json").simplify_path()
	var file := FileAccess.open(path,FileAccess.WRITE)
	file.store_string(JSON.stringify(report,"\t"))
	return report

func fill_landward_gaps() -> Dictionary:
	prepare()
	var city := get_parent() as Node3D
	if city.scene_file_path not in plan.source_scenes:
		return {"error":"Only the two approved Linvel scenes are accepted"}
	if city.get_meta("dry_city_landfill_revision",0)>=int(plan.revision):
		return {"error":"Already applied: preserve subsequent manual Terrain3D edits"}
	var terrain := city.get_node("World/Terrain3D") as Terrain3D
	var before := inspect_fill()
	var changed := 0
	var dry_cells := 0
	for z in range(-290,145,2):
		for x in range(-260,291,2):
			var point := Vector2(x,z)
			if not Geometry2D.is_point_in_polygon(point,boundary):
				continue
			# Floors, their underlying height maps and existing holes stay intact.
			if platform_index(point)>=0:
				continue
			var position := Vector3(x,0,z)
			var previous := terrain.data.get_height(position)
			if not is_finite(previous):
				previous = -8
			var target := target_height(point)
			var fade := smoothstep(0,float(plan.shore_blend_m),edge_distance(point,boundary))
			var desired := lerpf(previous,maxf(previous,target),fade)
			if desired<=previous+.02:
				continue
			terrain.data.set_control_hole(position,false)
			terrain.data.set_height(position,desired)
			terrain.data.set_control_auto(position,false)
			terrain.data.set_control_base_id(position,0)
			terrain.data.set_control_navigation(position,desired>0)
			changed += 1
			if previous<=0 and desired>0:
				dry_cells += 1
	terrain.data.update_maps(Terrain3DRegion.TYPE_MAX,true,true)
	terrain.data.save_directory(terrain.data_directory)
	city.set_meta("dry_city_landfill_revision",plan.revision)
	var after := inspect_fill()
	var report := {"before":before,"after":after,"native_cells_raised":changed,"submerged_cells_filled":dry_cells,"district_scenes_unchanged":true,"harbor_outside_boundary_untouched":true}
	var folder := ProjectSettings.globalize_path("res://../.artifacts/linvel-landfill").simplify_path()
	DirAccess.make_dir_recursive_absolute(folder)
	var suffix := "art01" if city.get_meta("whole_city_art_revision",0)>0 else "masterplan"
	var file := FileAccess.open(folder+"/"+suffix+"-report.json",FileAccess.WRITE)
	file.store_string(JSON.stringify(report,"\t"))
	return report
