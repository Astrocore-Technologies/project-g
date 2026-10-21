extends SceneTree
## Explicit offline authoring entry point; edits only saved coast water resources.
func _initialize() -> void:
	_build.call_deferred()

func _build() -> void:
	var helper: Node = load("res://Tools/Linvel/LinvelWaterAuthoring.gd").new()
	root.add_child(helper)
	helper.load_shore()
	var result: Dictionary = helper.build_water()
	print("WATER_BUILD "+JSON.stringify(result))
	helper.free()
	quit(0)
