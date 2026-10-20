@tool
extends Node3D
## Native scene editing convenience; nothing runs automatically or in gameplay.
## Building scenes remain ordinary instances. Only repeated decor is batched.
@export_tool_button("Обновить отображение декора", "Reload") var refresh_action: Callable = refresh_from_sources

func refresh_from_sources() -> void:
	if not Engine.is_editor_hint():
		return
	var district := get_parent()
	var updated := 0
	for child in get_children():
		if not child is MultiMeshInstance3D:
			continue
		var paths: PackedStringArray = child.get_meta("source_mesh_paths",PackedStringArray())
		if paths.size()!=child.multimesh.instance_count:
			push_warning("Changed instance count: author a new batch for this asset first.")
			continue
		for i in paths.size():
			var source := district.get_node_or_null(NodePath(paths[i])) as MeshInstance3D
			if source==null:
				push_warning("Missing decor source: "+paths[i])
				continue
			var tf := source.transform
			var parent := source.get_parent()
			while parent!=district:
				if parent is Node3D:
					tf = parent.transform*tf
				parent = parent.get_parent()
			child.multimesh.set_instance_transform(i,tf)
			updated += 1
	print("Linvel: updated ",updated," decor transforms; save the district scene.")
