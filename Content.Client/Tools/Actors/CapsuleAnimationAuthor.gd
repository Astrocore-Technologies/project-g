@tool
extends Node
# Native Animation resources stay editable. Re-running this tool preserves authored keys.
const LIBRARY_PATH := "res://Scenes/Actors/Primitives/CapsuleAnimations.tres"
const CLIPS := ["idle","combat_idle","idle_look","walk","walk_sword","run","run_sword","dash","basic_left","basic_right","thrust","sweep","rend","breaker","pommel","hamstring","riposte","whirl","breath","finisher","cast_start","cast_hold","cast_release","cast_self","block_enter","block_hold","block_exit","block_hit","parry","parry_success","hit_front","hit_back","hit_left","hit_right","stun_loop","airborne","knockdown","down_hold","recover","death_back","dead_back","death_front","dead_front","wave","nod","no","bow","point","cheer","clap","sit_down","sit_idle","stand_up","talk","pickup","gather","craft","interact","equip","unequip","npc_windup","npc_strike","boss_windup","boss_slam","dummy_hit"]
const LOOPS := ["idle","combat_idle","walk","walk_sword","run","run_sword","stun_loop","cast_hold","sit_idle","block_hold","dead_back","dead_front","down_hold","airborne"]
func prepare() -> Dictionary:
	var library: AnimationLibrary
	var created := not ResourceLoader.exists(LIBRARY_PATH)
	if created:
		library = AnimationLibrary.new()
		for clip in CLIPS:
			var animation := Animation.new()
			animation.length = duration(clip)
			if clip in LOOPS: animation.loop_mode = Animation.LOOP_LINEAR
			var paths := ["Center/Pose:position","Center/Pose:rotation","Center/Pose/Weapon:position","Center/Pose/Weapon:rotation"]
			for path in paths:
				var track := animation.add_track(Animation.TYPE_VALUE)
				animation.track_set_path(track, NodePath(path))
				animation.value_track_set_update_mode(track, Animation.UPDATE_CONTINUOUS)
			for sample in range(33):
				var u := float(sample)/32.0
				var pose := values(clip, u)
				for track in range(4):
					animation.track_insert_key(track, u*animation.length, pose[track])
			library.add_animation(clip, animation)
		var error := ResourceSaver.save(library, LIBRARY_PATH)
		if error != OK: return {"error": error}
		library.take_over_path(LIBRARY_PATH)
	else:
		library = load(LIBRARY_PATH)
	var player: AnimationPlayer = get_parent().get_node("AnimationPlayer")
	if player.has_animation_library(""): player.remove_animation_library("")
	player.add_animation_library("", library)
	return {"clips": library.get_animation_list().size(), "created": created}
func duration(clip: String) -> float:
	if clip in ["walk","walk_sword"]: return 1.0
	if clip in ["run","run_sword"]: return .65
	if clip in ["idle","combat_idle","idle_look"]: return 3.0
	if clip.begins_with("death"): return 1.1
	if clip in ["sit_down","stand_up","recover"]: return .8
	if clip in ["wave","cheer","clap","talk","gather","craft"]: return 2.0
	if clip in ["equip","unequip","block_enter","block_exit","parry","block_hit","parry_success"]: return .3
	if clip.begins_with("hit") or clip=="dummy_hit": return .4
	return 1.0
func values(clip: String, u: float) -> Array:
	var pos := Vector3.ZERO
	var rot := Vector3.ZERO
	var weapon_pos := Vector3(-.43,.12,.08)
	var weapon_rot := Vector3.ZERO
	var cycle := sin(u*TAU)
	var pulse := sin(u*PI)
	var swing := smoothstep(0.0,.35,u) if u<=.35 else 1.0-smoothstep(.35,1.0,u)
	match clip:
		"idle","idle_look","combat_idle":
			pos.y = cycle*.012
			rot.z = cycle*.018
			if clip=="idle_look": rot.y = sin(u*TAU)*.28
			if clip=="combat_idle": rot.x = -.08; weapon_rot.z = -.3
		"walk","walk_sword","run","run_sword":
			var running := clip.begins_with("run")
			pos.y = abs(cycle)*(.065 if running else .032)
			rot.x = -.14 if running else -.065
			rot.z = cycle*(.045 if running else .025)
			weapon_rot.x = cycle*.13
		"dash":
			rot.x = -.42*pulse; pos.y = -.12*pulse
		"block_enter","block_hold","block_exit","block_hit","parry","parry_success":
			var weight := 1.0
			if clip=="block_enter": weight=smoothstep(0,1,u)
			if clip=="block_exit": weight=1.0-smoothstep(0,1,u)
			rot = Vector3(-.18,0,-.12)*weight
			weapon_pos += Vector3(.12,.18,.3)*weight
			weapon_rot = Vector3(-.55,0,-.7)*weight
			if clip=="block_hit": rot.x += .12*pulse
			if clip.begins_with("parry"): weapon_rot.y = pulse*.8
		"hit_front","hit_back","hit_left","hit_right","dummy_hit":
			var force := pulse*(1.0-u)*.35
			rot.x = force*(-1.0 if clip=="hit_back" else 1.0)
			if clip=="hit_left" or clip=="hit_right": rot = Vector3(0,0,force*(1.0 if clip=="hit_left" else -1.0))
		"stun_loop":
			rot = Vector3(.12,0,cycle*.08); pos.y=-.08
		"airborne":
			rot.x = -.45; rot.z=cycle*.04
		"knockdown","down_hold","death_back","dead_back","death_front","dead_front","recover":
			var weight := 1.0
			if clip.begins_with("death") or clip=="knockdown": weight=smoothstep(0,.85,u)
			if clip=="recover": weight=1.0-smoothstep(0,1,u)
			rot.x = PI/2*weight*(-1.0 if clip.ends_with("front") else 1.0)
			pos.y = -.55*weight
			weapon_rot.z = -.8*weight
		"sit_down","sit_idle","stand_up":
			var weight := 1.0
			if clip=="sit_down": weight=smoothstep(0,1,u)
			if clip=="stand_up": weight=1.0-smoothstep(0,1,u)
			pos.y=-.38*weight; rot.x=.12*weight
		"wave":
			rot.z=sin(u*TAU*3)*.12*pulse; rot.y=sin(u*TAU)*.1
		"nod","talk":
			rot.x=sin(u*TAU*(2 if clip=="nod" else 3))*.1*pulse
		"no":
			rot.y=sin(u*TAU*3)*.3*pulse
		"bow","pickup","gather","interact":
			rot.x = .4*pulse
			pos.y = -.12*pulse
		"point":
			rot.x=-.18*pulse; rot.z=-.1*pulse
		"cheer","clap":
			pos.y=abs(sin(u*TAU*2))*.12*pulse; rot.z=sin(u*TAU*2)*.1*pulse
		"craft":
			rot.x=.18*pulse; rot.y=sin(u*TAU*3)*.12*pulse
		"equip","unequip":
			rot.z=.09*pulse; weapon_rot.z=-.5*pulse
		"cast_start","cast_hold","cast_release","cast_self","breath":
			var weight := pulse
			if clip=="cast_start": weight=smoothstep(0,1,u)
			if clip=="cast_hold": weight=.9+cycle*.1
			rot.x=-.2*weight; pos.y=.025*weight
			if clip=="cast_release": rot.x=.18*pulse
		_:
			# The contact pose is at normalized .35; only public timelines choose this sample.
			var side := -1.0 if clip in ["basic_left","sweep","hamstring","riposte"] else 1.0
			rot.y = side*.4*swing
			rot.x = .13*swing
			weapon_pos.z += .4*swing
			weapon_rot = Vector3(-.75*swing,side*.7*swing,-side*1.4*swing)
			if clip=="thrust": weapon_rot=Vector3(PI/2*swing,0,0); weapon_pos.z+=.2*swing
			if clip in ["boss_slam","boss_windup","breaker","finisher"]: rot.x=.35*swing; weapon_rot.x=-2.0*swing
			if clip=="whirl": rot.y=TAU*smoothstep(0,1,u); weapon_rot.z=-1.4*pulse
	return [pos,rot,weapon_pos,weapon_rot]
