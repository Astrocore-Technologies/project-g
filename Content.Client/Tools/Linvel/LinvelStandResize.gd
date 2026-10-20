@tool
extends Node
## Edits only the existing saved stand's simple collision and placement.
## Matching mesh resize is baked into Blender geometry; node scale stays one.

func refresh_stand_import() -> Dictionary:
	EditorInterface.get_resource_filesystem().reimport_files(PackedStringArray(["res://Assets/World/Linvel/arena_covered_stand.glb"]))
	return {"requested_import":"arena_covered_stand.glb"}

func apply_large_stand() -> Dictionary:
	var part := get_parent()
	if part.scene_file_path != "res://Scenes/Regions/Linvel/Parts/TrainingQuarter.tscn":
		return {"error":"Open TrainingQuarter before resizing its stand."}
	var stand := part.get_node("TrainingArena/CoveredStand") as Node3D
	if stand.get_meta("stand_revision", "") == "large-v2":
		return {"error":"Already resized; preserve manual edits."}
	var sx := 14.0/9.039999961853027
	var sz := 7.0/5.159999847412109
	var count := 0
	for body in stand.get_children():
		if not body is StaticBody3D:
			continue
		body.position.x *= sx
		body.position.z *= sz
		for child in body.get_children():
			if not child is CollisionShape3D:
				continue
			var shape: Shape3D = child.shape.duplicate()
			if shape is BoxShape3D:
				shape.size.x *= sx
				shape.size.z *= sz
			elif shape is ConvexPolygonShape3D:
				var points: PackedVector3Array = shape.points
				for index in range(points.size()):
					points[index].x *= sx
					points[index].z *= sz
				shape.points = points
			child.shape = shape
			count += 1
	stand.position.z = -46.5
	stand.set_meta("stand_revision","large-v2")
	stand.set_meta("dimensions_metres",Vector3(14,4.58,7))
	stand.set_meta("source_library","art-source/linvel/arena/arena_covered_stand-large.blend")
	return {"colliders_resized":count,"node_scale":stand.scale,"dimensions":Vector3(14,4.58,7),"center":stand.position}
