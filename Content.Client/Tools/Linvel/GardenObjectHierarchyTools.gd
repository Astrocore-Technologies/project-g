@tool
extends Node
class_name GardenObjectHierarchyTools

var root: Node3D

func model_nodes() -> Array[MeshInstance3D]:
    var result: Array[MeshInstance3D] = []
    for node in root.find_children("*", "MeshInstance3D", true, false):
        if node.has_meta("asset_id") and node.get_meta("asset_id") not in ["garden_terrain", "garden_stream"]:
            result.append(node)
    return result

func resolve_visual(body: StaticBody3D) -> MeshInstance3D:
    var path: NodePath = body.get_meta("collision_visual", NodePath(""))
    if path.is_empty():
        return null
    var visual: Node = body.get_node_or_null(path) if body.get_meta("collision_reference_version", 1) == 2 else root.get_node_or_null(path)
    return visual as MeshInstance3D

func migrate() -> Dictionary:
    root = get_parent() as Node3D
    if root.get_meta("garden_object_hierarchy_version",0) >= 1:
        return {"success":true,"already_converted":true,"audit":audit()}
    var links: Array[Dictionary] = []
    for body in root.find_children("*", "StaticBody3D", true, false):
        if body.is_in_group("surface_terrain"):
            continue
        var visual: MeshInstance3D = resolve_visual(body)
        if visual == null and body.name == "MillBridgeDeck":
            var bridge: Node = root.get_node("farm_bridge")
            visual = bridge as MeshInstance3D if bridge is MeshInstance3D else bridge.get_node("Visual") as MeshInstance3D
        var shape_node: CollisionShape3D = body.get_node_or_null("Collision")
        if visual == null or shape_node == null or not shape_node.shape is BoxShape3D:
            return {"success":false, "error":"Unresolved collider: "+str(root.get_path_to(body))}
        var offset: Vector3 = body.get_meta("collision_local_offset", visual.global_transform.affine_inverse()*body.global_position)
        var dimensions: Vector3 = body.get_meta("collision_local_size", shape_node.shape.size/visual.scale.abs())
        links.append({"body":body, "visual":visual, "offset":offset, "dimensions":dimensions})
    var wrapped := 0
    for visual in model_nodes():
        if visual.get_parent().get_meta("garden_asset_root", false):
            continue
        var parent: Node = visual.get_parent()
        var index := visual.get_index()
        var label := visual.name
        var original := visual.transform
        var world := visual.global_transform
        var placement := Node3D.new()
        placement.name = str(label)+"_Placement"
        parent.add_child(placement, true)
        parent.move_child(placement, index)
        placement.owner = root
        placement.transform = Transform3D(original.basis.orthonormalized(), original.origin)
        placement.set_meta("garden_asset_root", true)
        placement.set_meta("asset_id", visual.get_meta("asset_id"))
        visual.reparent(placement, false)
        visual.name = "Visual"
        placement.name = label
        visual.owner = root
        visual.transform = placement.transform.affine_inverse()*original
        if not visual.global_transform.is_equal_approx(world):
            return {"success":false, "error":"Visual transform changed: "+str(root.get_path_to(visual))}
        wrapped += 1
    for link in links:
        var body: StaticBody3D = link.body
        var visual: MeshInstance3D = link.visual
        var placement: Node3D = visual.get_parent()
        body.reparent(placement, false)
        body.name = str(body.get_meta("collision_part", "deck")).capitalize()+"Body"
        body.owner = root
        body.transform = Transform3D(visual.basis.orthonormalized(), visual.transform*link.offset)
        var shape_node: CollisionShape3D = body.get_node("Collision")
        var box: BoxShape3D = shape_node.shape.duplicate()
        box.size = link.dimensions*visual.scale.abs()
        shape_node.shape = box
        shape_node.owner = root
        body.set_meta("collision_visual", body.get_path_to(visual))
        body.set_meta("collision_reference_version", 2)
        body.set_meta("collision_local_offset", link.offset)
        body.set_meta("collision_local_size", link.dimensions)
        body.set_meta("collision_part", body.get_meta("collision_part", "deck"))
        body.add_to_group("surface_geometry", true)
    root.set_meta("garden_object_hierarchy_version", 1)
    return {"success":true, "wrapped_models":wrapped, "attached_colliders":links.size(), "audit":audit()}

func footprint(visual: MeshInstance3D, margin: float = 0.0) -> Rect2:
    var bounds: AABB = visual.global_transform*visual.get_aabb()
    return Rect2(Vector2(bounds.position.x,bounds.position.z),Vector2(bounds.size.x,bounds.size.z)).grow(margin)

func road_distance(point: Vector2, profile: Dictionary) -> float:
    var best := INF
    var paths: Array = [profile.route]
    paths.append_array(profile.side_paths)
    for path in paths:
        for i in range(path.size()-1):
            var a := Vector2(path[i][0],path[i][1])
            var b := Vector2(path[i+1][0],path[i+1][1])
            var delta := b-a
            best = minf(best, point.distance_to(a+delta*clampf((point-a).dot(delta)/delta.length_squared(),0,1)))
    return best

func crown_conflicts() -> Array[Dictionary]:
    var buildings: Array[MeshInstance3D] = []
    var trees: Array[MeshInstance3D] = []
    for visual in model_nodes():
        var id: String = visual.get_meta("asset_id")
        if id in ["farm_cottage", "stream_watermill", "raised_granary", "farm_shed", "vegetable_bed"]:
            buildings.append(visual)
        elif id.begins_with("orchard_"):
            trees.append(visual)
    var conflicts: Array[Dictionary] = []
    for tree in trees:
        for house in buildings:
            if footprint(tree).intersects(footprint(house, 1.5)):
                conflicts.append({"tree":str(root.get_path_to(tree)), "building":str(root.get_path_to(house))})
    return conflicts

func repair_crowns() -> Dictionary:
    root = get_parent() as Node3D
    var terrain: Terrain3D = root.get_node("Geometry/Terrain")
    var profile: Dictionary = JSON.parse_string(FileAccess.get_file_as_string("res://Assets/World/Linvel/Gardens/layout.json"))
    var buildings: Array[MeshInstance3D] = []
    var trees: Array[MeshInstance3D] = []
    for visual in model_nodes():
        var id: String = visual.get_meta("asset_id")
        if id in ["farm_cottage","stream_watermill","raised_granary","farm_shed","vegetable_bed"]:
            buildings.append(visual)
        elif id.begins_with("orchard_"):
            trees.append(visual)
    var moved: Array[Dictionary] = []
    for tree in trees:
        var bounds := footprint(tree)
        var blocked := false
        for house in buildings:
            if bounds.intersects(footprint(house, 1.5)):
                blocked = true
                break
        if not blocked:
            continue
        var placement: Node3D = tree.get_parent() if tree.get_parent().get_meta("garden_asset_root",false) else tree
        var original := placement.global_position
        var chosen := original
        var found := false
        # Prefer a nearby garden position; never put the trunk on a road.
        for radius in [12.0,16.0,20.0,24.0,28.0,32.0,40.0]:
            for step in range(16):
                var angle: float = PI/2.0+float(step)*TAU/16.0
                var offset: Vector2 = Vector2(cos(angle),sin(angle))*float(radius)
                var point := Vector2(original.x,original.z)+offset
                if absf(point.x)>465 or absf(point.y)>385 or road_distance(point,profile)<7.0:
                    continue
                var candidate := Rect2(bounds.position+offset,bounds.size)
                var clear := true
                for house in buildings:
                    if candidate.intersects(footprint(house,1.5)):
                        clear = false
                        break
                if not clear:
                    continue
                for other in trees:
                    if other != tree and candidate.intersects(footprint(other,.35)):
                        clear = false
                        break
                if not clear:
                    continue
                var height := terrain.data.get_height(Vector3(point.x,0,point.y))
                if is_nan(height) or is_inf(height):
                    continue
                chosen = Vector3(point.x,height,point.y)
                found = true
                break
            if found:
                break
        if not found:
            return {"success":false, "error":"No clear garden position: "+str(root.get_path_to(tree)), "moved":moved}
        placement.global_position = chosen
        moved.append({"object":str(root.get_path_to(placement)), "from":original,"to":chosen})
    return {"success":true, "moved":moved, "remaining_conflicts":crown_conflicts()}

func audit() -> Dictionary:
    root = get_parent() as Node3D
    var detached: Array[String] = []
    var malformed: Array[String] = []
    var total := 0
    for body in root.find_children("*","StaticBody3D",true,false):
        if body.is_in_group("surface_terrain"):
            continue
        total += 1
        var visual: MeshInstance3D = resolve_visual(body)
        if visual == null or body.get_parent()!=visual.get_parent() or not body.get_parent().get_meta("garden_asset_root",false):
            detached.append(str(root.get_path_to(body)))
            continue
        var expected: Vector3 = visual.global_transform*body.get_meta("collision_local_offset")
        if body.global_position.distance_to(expected)>.001 or not body.is_in_group("surface_geometry"):
            malformed.append(str(root.get_path_to(body)))
    return {"colliders":total,"detached":detached,"misaligned":malformed,"crown_conflicts":crown_conflicts()}

func verify_transform_follow() -> Dictionary:
    root = get_parent() as Node3D
    var placement: Node3D = root.get_node("Places/SunnyOrchard/GardenHouse")
    var original := placement.transform
    var relative: Dictionary = {}
    for child in placement.get_children():
        if child is StaticBody3D:
            relative[child] = placement.global_transform.affine_inverse()*child.global_transform
    placement.position += Vector3(3,0,5)
    placement.rotation.y += .35
    placement.scale *= 1.25
    var followed := true
    for body in relative:
        if not body.global_transform.is_equal_approx(placement.global_transform*relative[body]):
            followed = false
    placement.transform = original
    return {"success":followed,"checked_bodies":relative.size(),"transform_restored":placement.transform.is_equal_approx(original)}
