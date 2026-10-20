@tool
extends Node
## Bounded initial review fix. Saved native scenes remain the authoring sources.
func finish_review() -> Dictionary:
	var city := get_parent() as Node3D
	if city.get_meta("masterplan_revision",0)!=1 or city.get_meta("final_blockout_review",false):
		return {"error":"Wrong revision or correction already applied"}
	var path := "res://Scenes/Regions/Linvel/Masterplan/Parts/ResidentialTerraces.tscn"
	var part: Node3D = ResourceLoader.load(path,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	for label in ["WestHome_08","WestHome_08Collision"]:
		part.get_node(label).position.z = -66
	var packed := PackedScene.new()
	packed.pack(part)
	var error := ResourceSaver.save(packed,path)
	part.free()
	if error!=OK:
		return {"error":error}
	var terrain := city.get_node("World/Terrain3D") as Terrain3D
	var painted := 0
	for z in range(-168,240,2):
		for x in range(-232,200,2):
			var p := Vector3(x,0,z)
			if terrain.data.get_height(p)>0:
				terrain.data.set_control_base_id(p,2)
				painted += 1
	terrain.data.update_maps(Terrain3DRegion.TYPE_CONTROL,true,true)
	terrain.data.save_directory(terrain.data_directory)
	city.set_meta("final_blockout_review",true)
	return {"house_grounded":true,"rock_underlay_cells":painted}
