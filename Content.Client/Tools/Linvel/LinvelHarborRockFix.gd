@tool
extends Node
## Relocate former shoreline boulders exposed by the enlarged lighthouse plaza.

func fix_lighthouse_rocks() -> Dictionary:
	var city := get_parent()
	if city.scene_file_path != "res://Scenes/Regions/Linvel/LinvelCity.tscn":
		return {"error":"Open LinvelCity."}
	const PATH := "res://Scenes/Regions/Linvel/Parts/HarborQuarter.tscn"
	var part: Node3D = ResourceLoader.load(PATH,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	var positions := [Vector3(-118,4.3,72),Vector3(-115,4.2,78),Vector3(-121,4.5,66)]
	var index := 0
	for child in part.get_node("HarborDetails").get_children():
		if child is Node3D and str(child.get_meta("linvel_asset_id","")).begins_with("coastal_boulder") and child.position.x>-110 and child.position.x<-90:
			child.position = positions[index]
			index += 1
			if index==positions.size():
				break
	var packed := PackedScene.new()
	packed.pack(part)
	var error := ResourceSaver.save(packed,PATH)
	part.free()
	if error!=OK:
		return {"error":error}
	var old := city.get_node("World/CityParts/HarborQuarter")
	old.get_parent().remove_child(old)
	old.free()
	var saved: PackedScene = ResourceLoader.load(PATH,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE)
	var instance := saved.instantiate()
	city.get_node("World/CityParts").add_child(instance)
	instance.owner = city
	return {"boulders_moved_to_coast":index,"plaza_geometry_changed":false}
