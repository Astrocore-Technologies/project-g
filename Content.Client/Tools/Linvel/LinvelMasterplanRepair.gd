@tool
extends "res://Tools/Linvel/LinvelMasterplanAuthoring.gd"
## One-time corrections to revision 01 made during its initial verification.
## Refuses to run a second time; never rebuilds existing districts.

func fix_meshes(node: Node) -> int:
	if node.scene_file_path.ends_with(".glb"):
		return 0
	var changed := 0
	if node is MeshInstance3D and node.mesh is ArrayMesh:
		var old: ArrayMesh = node.mesh
		var fixed := ArrayMesh.new()
		for surface in old.get_surface_count():
			var arrays := old.surface_get_arrays(surface)
			var indices: PackedInt32Array = arrays[Mesh.ARRAY_INDEX]
			for i in range(0,indices.size(),3):
				var saved := indices[i+1]
				indices[i+1] = indices[i+2]
				indices[i+2] = saved
			arrays[Mesh.ARRAY_INDEX] = indices
			var normals: PackedVector3Array = arrays[Mesh.ARRAY_NORMAL]
			for i in normals.size():
				normals[i] = -normals[i]
			arrays[Mesh.ARRAY_NORMAL] = normals
			fixed.add_surface_from_arrays(Mesh.PRIMITIVE_TRIANGLES,arrays)
			fixed.surface_set_material(surface,node.material_override)
		node.mesh = fixed
		var collider := node.get_parent().get_node_or_null(str(node.name)+"Collision/Shape")
		if collider is CollisionShape3D:
			collider.shape = fixed.create_trimesh_shape()
		changed += 1
	for child in node.get_children():
		changed += fix_meshes(child)
	return changed

func repair() -> Dictionary:
	var current := get_parent() as Node3D
	if current.get_meta("masterplan_revision",0)!=1 or current.get_meta("blockout_surface_fix",false):
		return {"error":"Revision mismatch or correction already applied"}
	layout = JSON.parse_string(FileAccess.get_file_as_string(BASE+"layout.json"))
	for p in layout.platforms:
		var poly := PackedVector2Array()
		for a in p.points:
			poly.append(Vector2(a[0],a[1]))
		polygons.append(poly)
	var changes := 0
	for q in layout.districts:
		part = ResourceLoader.load(q.scene,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
		changes += fix_meshes(part)
		if q.id=="CityCenter":
			var old := part.get_node("CivicProcessional")
			part.remove_child(old)
			old.free()
			for street in layout.streets:
				if street.id=="CivicProcessional":
					districts.CityCenter = part
					build_street(street)
		if q.id=="ResidentialTerraces":
			# Close a narrow paper-plan seam at the tavern courtyard entrance.
			box(part,"TavernEntryBridge",Vector3(-115,11.85,80),Vector3(10,.3,9),material("TavernBridge",Color("d7c69f")))
		var saved := PackedScene.new()
		var error := saved.pack(part)
		if error==OK:
			error = ResourceSaver.save(saved,q.scene)
		part.free()
		if error!=OK:
			return {"error":"District repair save failed","district":q.id,"code":error}
	root = current
	part = current
	var old_terrain := current.get_node("World/Terrain3D")
	old_terrain.get_parent().remove_child(old_terrain)
	old_terrain.free()
	var report := build_terrain(current.get_node("World"))
	current.set_meta("blockout_surface_fix",true)
	return {"meshes_repaired":changes,"terrain":report,"note":"Save main scene after removing helper, then reload saved districts."}

func reload_districts() -> Dictionary:
	var current := get_parent() as Node3D
	var data: Dictionary = JSON.parse_string(FileAccess.get_file_as_string(BASE+"layout.json"))
	var parent := current.get_node("World/CityParts")
	for q in data.districts:
		var old := parent.get_node(q.id)
		parent.remove_child(old)
		old.free()
		var saved: PackedScene = ResourceLoader.load(q.scene,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE)
		var instance := saved.instantiate()
		parent.add_child(instance)
		instance.owner = current
	return {"districts_reloaded":data.districts.size()}
