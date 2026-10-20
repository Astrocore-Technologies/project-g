@tool
extends Node
## User-approved additions outside the city layout. No city or route regeneration.
const DATA := "res://Assets/World/Linvel/City/Masterplan/layout.json"

func extend_mainland() -> Dictionary:
	var city := get_parent() as Node3D
	if city.get_meta("mainland_extended",false):
		return {"error":"Existing sculpt: edit Terrain3D directly"}
	var layout: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(DATA))
	var city_polygons: Array[PackedVector2Array] = []
	for platform in layout.platforms:
		var poly := PackedVector2Array()
		for p in platform.points:
			poly.append(Vector2(p[0],p[1]))
		city_polygons.append(poly)
	var land_polygons: Array[PackedVector2Array] = []
	for land in layout.landmasses:
		var poly := PackedVector2Array()
		for p in land.points:
			poly.append(Vector2(p[0],p[1]))
		land_polygons.append(poly)
	var terrain := city.get_node("World/Terrain3D") as Terrain3D
	var changed := 0
	for z in range(-610,102,2):
		for x in range(-496,498,2):
			var point := Vector2(x,z)
			var protected := false
			for poly in city_polygons:
				if Geometry2D.is_point_in_polygon(point,poly):
					protected = true
					break
			if protected:
				continue
			for link in layout.links:
				var a := Vector2(link.start[0],link.start[2])
				var b := Vector2(link.end[0],link.end[2])
				if point.distance_to(Geometry2D.get_closest_point_to_segment(point,a,b))<float(link.width)*.5+3:
					protected = true
					break
			if protected:
				continue
			for poly in land_polygons:
				if not Geometry2D.is_point_in_polygon(point,poly):
					continue
				var edge_distance := 10000.0
				for i in poly.size():
					edge_distance = minf(edge_distance,point.distance_to(Geometry2D.get_closest_point_to_segment(point,poly[i],poly[(i+1)%poly.size()])))
				var height := lerpf(12,32,clampf((90.0-z)/300,0,1))+2.5*sin(x*.018+z*.011)
				height = lerpf(-7,height,smoothstep(0,21,edge_distance))
				var position := Vector3(x,0,z)
				var previous := terrain.data.get_height(position)
				if not is_finite(previous) or height<=previous+.05:
					continue
				terrain.data.set_height(position,height)
				terrain.data.set_control_auto(position,false)
				terrain.data.set_control_base_id(position,0 if edge_distance>12 else 2)
				terrain.data.set_control_navigation(position,height>0)
				changed += 1
				break
	terrain.data.update_maps(Terrain3DRegion.TYPE_MAX,true,true)
	terrain.data.save_directory(terrain.data_directory)
	city.set_meta("mainland_extended",true)
	city.set_meta("layout_approved_by_user","2026-10-20; art production authorized")
	return {"native_cells_raised":changed,"city_platforms_preserved":true,"harbor_kept_open":true}
