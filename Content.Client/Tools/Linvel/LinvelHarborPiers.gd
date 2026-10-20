@tool
extends "res://Tools/Linvel/LinvelHarborFinish.gd"
## Resizes the reviewed harbor's existing modular pier assets, not source models.

func large_pier(parent: Node3D, x: float, start: float, count: int, label: String) -> void:
	var group := own(parent,Node3D.new(),label) as Node3D
	group.set_meta("deck_width_m",6.0)
	group.set_meta("length_from_quay_m",count*12.0+8.0)
	for index in range(count):
		var z := start+6.0+index*12.0
		var model := add_asset(group,"dock_straight",Vector3(x,SEA,z),0,"Deck%d" % index)
		model.scale = Vector3(2.4,1,2)
		# Collision is a sibling, with physical dimensions and scale one.
		solid(group,"WalkableDeck%d" % index,Vector3(x,QUAY-0.09,z),Vector3(6,0.18,12.03),0,true)
	var end_z := start+count*12.0+4.0
	var landing := add_asset(group,"dock_landing",Vector3(x,SEA,end_z),0,"Landing")
	landing.scale = Vector3(2.4,1,2)
	solid(group,"WalkableLanding",Vector3(x,QUAY-0.09,end_z),Vector3(12,0.18,8.03),0,true)
	for index in range(count+1):
		for sign in [-1,1]:
			add_asset(group,"harbor_bollard",Vector3(x+sign*2.35,QUAY,start+3+index*10),0,"Bollard%d_%d" % [index,sign])
	for sign in [-1,1]:
		add_asset(group,"mooring_rope",Vector3(x+sign*4.5,QUAY,end_z-1),0,"LandingLine%d" % sign)

func finish_large_piers() -> Dictionary:
	city = get_parent() as Node3D
	if city.scene_file_path != "res://Scenes/Regions/Linvel/LinvelCity.tscn":
		return {"error":"Open LinvelCity before the pier revision."}
	part = ResourceLoader.load(HARBOR_PART,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE).instantiate()
	if part.has_meta("large_piers_v3"):
		part.free()
		return {"error":"Already revised; preserve manual edits."}
	painted = load(CITY+"Materials/linvel_painted.tres")
	var old := part.get_node("FishingPiers")
	part.remove_child(old)
	old.free()
	var piers := own(part,Node3D.new(),"FishingPiers") as Node3D
	large_pier(piers,-62,64.2,4,"WestPier")
	large_pier(piers,-28,67,3,"EastPier")
	var boats := part.get_node("MooredBoats")
	boats.get_node("LocalFishingSailboat").position = Vector3(-66.6,SEA,84)
	boats.get_node("EastRowboat").position = Vector3(-23.5,SEA,84)
	boats.get_node("WestRowboat").position = Vector3(-57.5,SEA,101)
	var surface := part.get_node("Bay/ShelteredBayWater") as MeshInstance3D
	var sea_mesh := surface.mesh.duplicate() as PlaneMesh
	sea_mesh.size = Vector2(220,156)
	surface.mesh = sea_mesh
	surface.position.z = 124
	var terrain := city.get_node("World/Terrain3D") as Terrain3D
	var dredged := 0
	for z in range(68,128):
		for x in range(-84,-11):
			var p := Vector3(x,0,z)
			if terrain.data.get_control_hole(p) or terrain.data.get_height(p)>SEA:
				continue
			var near_shore := clampf((z-shore_z(x)-2.0)/8.0,0.0,1.0)
			terrain.data.set_height(p,lerpf(3.5,0.0,near_shore))
			dredged += 1
	terrain.data.update_maps(Terrain3DRegion.TYPE_HEIGHT,true,true)
	terrain.data.save_directory(terrain.data_directory)
	var paving: ShaderMaterial = load(CITY+"Materials/RigidPaving.tres")
	paving.set_shader_parameter("tiles_per_metre",0.25)
	ResourceSaver.save(paving,CITY+"Materials/RigidPaving.tres")
	part.set_meta("large_piers_v3",true)
	var packed := PackedScene.new()
	packed.pack(part)
	var error := ResourceSaver.save(packed,HARBOR_PART)
	part.free()
	if error!=OK:
		return {"error":error}
	old = city.get_node("World/CityParts/HarborQuarter")
	old.get_parent().remove_child(old)
	old.free()
	var saved: PackedScene = ResourceLoader.load(HARBOR_PART,"PackedScene",ResourceLoader.CACHE_MODE_IGNORE)
	var instance := saved.instantiate()
	city.get_node("World/CityParts").add_child(instance)
	instance.owner = city
	return {"main_pier_metres":Vector2(56,6),"east_pier_metres":Vector2(44,6),"landing_metres":Vector2(12,8),"pier_center_spacing_m":34,"berth_depth_m":5.74,"dredged_cells":dredged}
