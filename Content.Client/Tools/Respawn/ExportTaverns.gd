extends SceneTree
## Exports only the catalog from editable service scenes. Never rewrites scenes or manual map work.

const SOURCES := ["res://Scenes/Regions/Linvel/Services/StartingTavern.tscn"]

func _initialize() -> void:
	call_deferred("_export")

func _export() -> void:
	var destination := ""
	for argument in OS.get_cmdline_user_args():
		if argument.begins_with("--output="):
			destination = argument.trim_prefix("--output=")
	if destination.is_empty():
		push_error("Explicit --output=<catalog.json> is required.")
		quit(1)
		return
	var taverns: Array[Dictionary] = []
	for source in SOURCES:
		var scene := load(source) as PackedScene
		if scene == null:
			quit(1)
			return
		var service := scene.instantiate() as Node3D
		get_root().add_child(service)
		var npc := service.get_node("Npc") as Node3D
		var spawn := service.get_node("Respawn") as Node3D
		taverns.append({"id": service.get_meta("tavern_id"), "networkId": service.get_meta("network_id"),
			"revision": service.get_meta("placement_revision"), "regionId": service.get_meta("region_id"),
			"name": service.get_meta("public_name"), "npc": _point(npc.global_position),
			"spawn": _point(spawn.global_position), "interactionRange": service.get_meta("interaction_range")})
		service.free()
	var file := FileAccess.open(destination, FileAccess.WRITE)
	if file == null:
		push_error("Cannot write the explicit tavern catalog destination.")
		quit(1)
		return
	file.store_string(JSON.stringify({"version": 1, "startingTavern": "linvel_starting_tavern", "taverns": taverns}, "  ")+"\n")
	file.close()
	print("TAVERN_EXPORT_OK: ", taverns.size())
	quit()

func _point(position: Vector3) -> Dictionary:
	# Source precision is centimetres; do not turn float32 authoring noise into a new placement revision.
	return {"x": snappedf(position.x, 0.01), "height": snappedf(position.y, 0.01), "z": snappedf(position.z, 0.01)}
