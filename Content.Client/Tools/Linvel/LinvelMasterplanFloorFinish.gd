@tool
extends "res://Tools/Linvel/LinvelMasterplanAuthoring.gd"
## Replace only first-review terrace surfaces with exact polygon boundaries.

func finish_floors() -> Dictionary:
	var current := get_parent() as Node3D
	if current.get_meta("masterplan_revision",0)!=1 or current.get_meta("connected_civic_terrace_v3",false):
		return {"error":"Wrong revision or correction already applied"}
	layout = JSON.parse_string(FileAccess.get_file_as_string(BASE+"layout.json"))
	for p in layout.platforms:
		var poly := PackedVector2Array()
		for a in p.points:
			poly.append(Vector2(a[0],a[1]))
		polygons.append(poly)
	for q in layout.districts:
		part = ResourceLoader.load(q.scene,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
		districts[q.id] = part
		for i in layout.platforms.size():
			var p: Dictionary = layout.platforms[i]
			if p.part!=q.id:
				continue
			var old := part.get_node_or_null(p.id)
			if old!=null:
				part.remove_child(old)
				old.free()
			build_platform(p,polygons[i])
		if q.id=="GateQuarter" and part.has_node("GateApproachFloor"):
			for label in ["GateApproachFloor","GateApproachFloorCollision"]:
				var old := part.get_node(label)
				part.remove_child(old)
				old.free()
		if q.id=="ResidentialTerraces":
			part.get_node("TavernEntryBridge").position.y += .02
			part.get_node("TavernEntryBridgeCollision").position.y += .02
		if q.id=="CityCenter":
			part.get_node("CivicBuilding_04").position = Vector3(34,23,-25)
			part.get_node("CivicBuilding_04Collision").position = Vector3(34,23,-25)
		for street in layout.streets:
			if street.part==q.id and street.id in ["CivicUpperConnector","ArenaWestEntry"] and not part.has_node(street.id):
				build_street(street)
		var saved := PackedScene.new()
		saved.pack(part)
		var error := ResourceSaver.save(saved,q.scene)
		part.free()
		if error!=OK:
			return {"error":error,"district":q.id}
	current.set_meta("exact_blockout_floors",true)
	current.set_meta("continuous_route_landings_v2",true)
	current.set_meta("connected_civic_terrace_v3",true)
	return {"platforms":layout.platforms.size(),"exact_boundary_clipping":true}
