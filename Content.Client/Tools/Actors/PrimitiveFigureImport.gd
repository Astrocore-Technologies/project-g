@tool
class_name PrimitiveFigureImport
extends Node

## One-time import preparation. Existing editable resources are preserved unless explicitly rebuilt.
func prepare(rebuild: bool = false) -> Dictionary:
    var folder := "res://Scenes/Actors/Primitives/"
    var target := folder + "RigidFigureModel.tscn"
    if ResourceLoader.exists(target) and not rebuild:
        return {"status": "preserved", "path": target}
    var imported: PackedScene = load("res://Assets/Characters/Primitives/primitive_figure.glb")
    var model: Node3D = imported.instantiate()
    model.name = "RigidFigureModel"
    model.scene_file_path = ""
    var players := model.find_children("*", "AnimationPlayer", true, false)
    if players.size() != 1:
        model.free()
        return {"error": "Expected one imported AnimationPlayer"}
    var player: AnimationPlayer = players[0]
    var library: AnimationLibrary = player.get_animation_library("").duplicate(true)
    var loops := ["idle","combat_idle","walk","walk_sword","run","run_sword","cast_hold","block_hold","stun_loop","sit_idle","dead_back","dead_front","down_hold","airborne"]
    for clip in library.get_animation_list():
        library.get_animation(clip).loop_mode = Animation.LOOP_LINEAR if clip in loops else Animation.LOOP_NONE
    var library_path := folder + "primitive_animations.tres"
    var error := ResourceSaver.save(library, library_path)
    if error != OK:
        model.free()
        return {"error": error}
    player.remove_animation_library("")
    player.add_animation_library("", load(library_path))
    for child in model.find_children("*", "", true, false):
        child.owner = model
    var packed := PackedScene.new()
    error = packed.pack(model)
    if error == OK:
        error = ResourceSaver.save(packed, target)
    var result := {"error": error, "clips": library.get_animation_list(), "path": target, "library": library_path}
    model.free()
    return result
