@tool
extends "res://Tools/Linvel/LinvelMasterplanAuthoring.gd"
## Targeted corrections found by initial route/ground checks. Not a generator.

func finish_connections() -> Dictionary:
	var current := get_parent() as Node3D
	if current.get_meta("masterplan_revision",0)!=1 or current.get_meta("connections_reviewed",false):
		return {"error":"Wrong revision or correction already applied"}
	layout = JSON.parse_string(FileAccess.get_file_as_string(BASE+"layout.json"))
	for q in layout.districts:
		part = ResourceLoader.load(q.scene,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
		districts[q.id] = part
		if q.id in ["ResidentialTerraces","SanctuaryQuarter"]:
			for street in layout.streets:
				if street.id not in ["LowerWestLane","EastNeighborhood"] or street.part!=q.id:
					continue
				var old := part.get_node(street.id)
				part.remove_child(old)
				old.free()
				build_street(street)
		if q.id=="CraftsQuarter":
			var old := part.get_node("WestCartAscent")
			part.remove_child(old)
			old.free()
			for link in layout.links:
				if link.id=="WestCartAscent":
					build_link(link)
		if q.id=="GateQuarter":
			box(part,"GateApproachFloor",Vector3(54,31.85,-154),Vector3(64,.3,28),material("GateFloor",Color("dbcfac")))
			for info in [["LandwardWall_-49",Vector3(-10,34,-150),Vector3(72,4,2.4)],["LandwardWall_117",Vector3(117,38,-146),Vector3(60,4,2.4)]]:
				var node := part.get_node(info[0]) as MeshInstance3D
				node.position = info[1]
				var mesh := BoxMesh.new()
				mesh.size = info[2]
				node.mesh = mesh
				var body := part.get_node(str(info[0])+"Collision") as StaticBody3D
				body.position = node.position
				var shape := BoxShape3D.new()
				shape.size = info[2]
				body.get_node("Shape").shape = shape
			for b in layout.buildings:
				if b.part==q.id and b.role=="wall":
					part.get_node(b.id).position = vec(b.position)
					part.get_node(b.id+"Collision").position = vec(b.position)
		var packed := PackedScene.new()
		packed.pack(part)
		var error := ResourceSaver.save(packed,q.scene)
		part.free()
		if error!=OK:
			return {"error":error,"district":q.id}
	current.set_meta("connections_reviewed",true)
	return {"corrected":"Two lanes, cart-ramp endpoint, grounded gate walls and five tower positions"}
