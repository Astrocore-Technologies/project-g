extends SceneTree
## Reads the working service scene. Preserves all manually edited rules/text in the input catalog.

func _initialize() -> void:
    call_deferred("_export")

func _export() -> void:
    var source := ""
    var destination := ""
    for argument in OS.get_cmdline_user_args():
        if argument.begins_with("--catalog="):
            source = argument.trim_prefix("--catalog=")
        if argument.begins_with("--output="):
            destination = argument.trim_prefix("--output=")
    if source.is_empty() or destination.is_empty():
        push_error("Explicit --catalog=<existing.json> and --output=<catalog.json> are required.")
        quit(1)
        return
    var definition = JSON.parse_string(FileAccess.get_file_as_string(source))
    if not definition is Dictionary:
        push_error("Invalid source service catalog.")
        quit(1)
        return
    var scene := load("res://Scenes/Regions/Linvel/Services/StartingCampServices.tscn") as PackedScene
    var service := scene.instantiate() as Node3D
    get_root().add_child(service)
    definition.regionId = service.get_meta("region_id")
    definition.revision = service.get_meta("placement_revision")
    definition.station = _point(service.get_node("Station").global_position)
    definition.giver = _point(service.get_node("Giver").global_position)
    definition.recipient = _point(service.get_node("Recipient").global_position)
    for node in service.get_children():
        if not node.has_meta("resource_id"):
            continue
        for resource in definition.resources:
            if resource.id == node.get_meta("resource_id"):
                resource.point = _point(node.global_position)
    var file := FileAccess.open(destination,FileAccess.WRITE)
    if file == null:
        push_error("Cannot write explicit service catalog destination.")
        quit(1)
        return
    file.store_string(JSON.stringify(definition,"  ")+"\n")
    file.close()
    service.free()
    print("CAMP_SERVICES_EXPORT_OK")
    quit()

func _point(position: Vector3) -> Dictionary:
    return {"x": snappedf(position.x,0.01),"height": snappedf(position.y,0.01),"z": snappedf(position.z,0.01)}
