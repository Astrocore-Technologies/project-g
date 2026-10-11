@tool
extends Node
class_name BeechCollisionTools

const ASSET_ROOT = "res://Assets/World/Linvel/Beech/"
var region: Node3D

func inspect(output: String) -> Dictionary:
	region = get_parent() as Node3D
	var visuals: Array = []
	for node in region.find_children("*", "Node3D", true, false):
		if node.has_meta("asset_id") and not node is MultiMeshInstance3D:
			visuals.append({"path":str(region.get_path_to(node)),"asset":node.get_meta("asset_id"),
				"position":vector(node.global_position),"yaw":node.global_rotation.y,"scale":vector(node.global_basis.get_scale())})
		elif node is MultiMeshInstance3D and str(node.get_meta("asset_id", "")).begins_with("beech"):
			for i in range(node.multimesh.instance_count):
				var t: Transform3D = node.global_transform * node.multimesh.get_instance_transform(i)
				visuals.append({"path":str(region.get_path_to(node)),"instance":i,"asset":node.get_meta("asset_id"),
					"position":vector(t.origin),"yaw":t.basis.get_euler().y,"scale":vector(t.basis.get_scale())})
	var bodies: Array = []
	for body in region.get_node("Geometry").get_children():
		if not body is StaticBody3D or body.name == "Ground":
			continue
		var collision: CollisionShape3D = body.get_node("Collision")
		var shape: BoxShape3D = collision.shape
		var closest: Dictionary = {}
		var distance := INF
		for v in visuals:
			var pos := Vector3(v.position[0],v.position[1],v.position[2])
			var d: float = Vector2(pos.x,pos.z).distance_to(Vector2(body.global_position.x,body.global_position.z))
			if d < distance:
				closest = v
				distance = d
		bodies.append({"path":str(region.get_path_to(body)),"position":vector(body.global_position),
			"size":vector(shape.size),"yaw":body.global_rotation.y,"closest_visual":closest,"distance_xz":distance})
	var mesh_node: MeshInstance3D = region.get_node("Geometry/Ground/beech_terrain").get_child(0)
	var arrays: Array = mesh_node.mesh.surface_get_arrays(0)
	var vertices: Array = []
	for v in arrays[Mesh.ARRAY_VERTEX]:
		vertices.append(vector(mesh_node.global_transform * v))
	var report := {"colliders":bodies,"visuals":visuals,"ground_vertices":vertices,"ground_indices":Array(arrays[Mesh.ARRAY_INDEX])}
	var file := FileAccess.open(output,FileAccess.WRITE)
	file.store_string(JSON.stringify(report))
	return {"colliders":bodies.size(),"visuals":visuals.size(),"report":output}

func vector(v: Vector3) -> Array:
	return [v.x,v.y,v.z]

func align() -> Dictionary:
	# Explicit editor repair. Bind each blocker to its visible part so a later
	# terrain-conformance pass can refresh placement without regenerating props.
	region = get_parent() as Node3D
	var candidates: Array = []
	var shelters: Array[Node3D] = []
	for node in region.find_children("*", "Node3D", true, false):
		var id: String = str(node.get_meta("asset_id", ""))
		if node is MultiMeshInstance3D and id.begins_with("beech"):
			for i in range(node.multimesh.instance_count):
				candidates.append({"node":node,"instance":i,"transform":node.global_transform*node.multimesh.get_instance_transform(i),"id":id})
		elif id in ["beech_mature_a","beech_mature_b","beech_young","moss_boulder_a"]:
			candidates.append({"node":node,"instance":-1,"transform":node.global_transform,"id":id})
		elif id == "woodcutters_shelter":
			shelters.append(node)
	var plans: Array = []
	for body in region.get_node("Geometry").get_children():
		if not body is StaticBody3D or body.name == "Ground":
			continue
		var collision: CollisionShape3D = body.get_node("Collision")
		var box: BoxShape3D = collision.shape
		if box.size.x > 20:
			continue # Deck and rails have their own authored dimensions.
		var source: Node3D
		var instance := -1
		var t: Transform3D
		var offset := Vector3.ZERO
		var size: Vector3
		var kind: String
		if body.has_meta("collision_visual"):
			source = region.get_node_or_null(body.get_meta("collision_visual"))
			instance = int(body.get_meta("collision_instance",-1))
			if source == null or instance >= 0 and (not source is MultiMeshInstance3D or instance >= source.multimesh.instance_count):
				return {"error":"Broken visual binding; no changes applied", "body":str(body.name)}
			t = source.global_transform*source.multimesh.get_instance_transform(instance) if instance >= 0 else source.global_transform
			kind = body.get_meta("collision_part")
			offset = body.get_meta("collision_local_offset")
		elif absf(box.size.x-0.4)<0.01 and absf(box.size.y-3.4)<0.01:
			var old_offset := Vector3.ZERO
			var distance := INF
			# The earlier composition pass renamed generated bodies and moved only
			# the first post. Match the remaining legacy coordinates explicitly.
			for pair in [[Vector2(-320,226),Vector2(-315,217)],[Vector2(-299,239),Vector2(-296,228)]]:
				var p: Vector2 = Vector2(body.global_position.x,body.global_position.z)-pair[0]
				if absf(absf(p.x)-3)<0.01 and absf(absf(p.y)-2)<0.01:
					for shelter in shelters:
						if Vector2(shelter.global_position.x,shelter.global_position.z).distance_to(pair[1])<0.01:
							source = shelter
							old_offset = Vector3(p.x,0,p.y)
							distance = 0
			for shelter in shelters:
				var d: float = Vector2(shelter.global_position.x,shelter.global_position.z).distance_to(Vector2(body.global_position.x,body.global_position.z))
				if d < distance:
					source = shelter
					distance = d
					old_offset = body.global_position-source.global_position
			if distance > 5:
				return {"error":"Orphan shelter collider; no changes applied", "body":str(body.name)}
			t = source.global_transform
			offset = Vector3(signf(old_offset.x)*3,1.725,signf(old_offset.z)*2)
			kind = "post"
		else:
			var distance := INF
			var found: Dictionary = {}
			for candidate in candidates:
				var p: Vector3 = candidate.transform.origin
				var d: float = Vector2(p.x,p.z).distance_to(Vector2(body.global_position.x,body.global_position.z))
				if d < distance:
					found = candidate
					distance = d
			if distance > 0.02:
				return {"error":"Orphan forest collider; no changes applied", "body":str(body.name)}
			source = found.node
			instance = found.instance
			t = found.transform
			kind = "trunk" if str(found.id).begins_with("beech") else "rock"
			offset = Vector3(0,4,0) if kind == "trunk" else Vector3(0,0.88,0)
		if kind == "rock":
			offset.y = 0.88
		if not t.basis.get_scale().is_equal_approx(Vector3.ONE*t.basis.get_scale().x) or absf(t.basis.get_euler().x)>0.001 or absf(t.basis.get_euler().z)>0.001:
			return {"error":"Nonuniform scale or tilted visual requires manual collision authoring", "body":str(body.name)}
		size = Vector3(0.9,8,0.9) if kind == "trunk" else Vector3(0.36,3.65,0.36) if kind == "post" else Vector3(2,1.76,1.6)
		plans.append({"body":body,"source":source,"instance":instance,"part":kind,"offset":offset,
			"transform":Transform3D(t.basis.orthonormalized(),t*offset),"size":size*t.basis.get_scale().x})
	var counts := {"trunk":0,"post":0,"rock":0}
	for plan in plans:
		var body: StaticBody3D = plan.body
		body.global_transform = plan.transform
		# Shapes were shared between some generated bodies. Preserve isolation.
		var box := BoxShape3D.new()
		box.size = plan.size
		body.get_node("Collision").shape = box
		body.set_meta("collision_visual",region.get_path_to(plan.source))
		body.set_meta("collision_instance",plan.instance)
		body.set_meta("collision_part",plan.part)
		body.set_meta("collision_local_offset",plan.offset)
		counts[plan.part] += 1
	# Layer 1 selects actual floors with RMB. Solid props use layer 3, so
	# foliage fading for the camera never turns them into invisible pick blockers.
	for body in region.get_node("Geometry").get_children():
		if body is StaticBody3D and body.is_in_group("surface_occluder"):
			body.collision_layer = 4
	region.set_meta("collision_alignment_version",1)
	return {"aligned":plans.size(),"parts":counts}
