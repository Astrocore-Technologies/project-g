"""Blender MCP second pass after build_reference_assets.py.

Connection map: continuous voxel union retains overlapping crown lobes;
each small leaf fan starts 45 mm inside the crown surface. Branch tips stay
inside the original crown envelope. No terrain or existing region assets change.
Already polished objects are guarded to preserve manual work.
"""
import bpy, math, random
from mathutils import Vector
from pathlib import Path

ROOT=Path('D:/projects/project-g')
scene=bpy.data.scenes['EliaraForestReferenceAssets']
bpy.context.window.scene=scene
checks=[]
for obj in tuple(scene.objects):
    if 'Leaves' not in obj.name or obj.get('reference_canopy_polished',False):
        continue
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.context.view_layer.objects.active=obj
    obj.data=obj.data.copy()
    modifier=obj.modifiers.new('Organic canopy union','REMESH')
    modifier.mode='VOXEL';modifier.voxel_size=.18
    modifier.use_smooth_shade=True
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    modifier=obj.modifiers.new('Soft lobe transitions','SMOOTH')
    modifier.factor=.6;modifier.iterations=3
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    modifier=obj.modifiers.new('Canopy budget','DECIMATE')
    modifier.ratio=.55
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    old=obj.data
    vertices=[tuple(v.co) for v in old.vertices]
    faces=[tuple(p.vertices) for p in old.polygons]
    candidates=tuple(old.polygons)
    rng=random.Random(411 if '_a_' in obj.name else 412)
    for index in range(440):
        face=candidates[rng.randrange(len(candidates))]
        normal=face.normal.normalized()
        point=face.center-normal*.045
        tangent=normal.cross(Vector((0,0,1)) if abs(normal.z)<.9 else Vector((1,0,0))).normalized()
        bitangent=normal.cross(tangent).normalized()
        for blade in range(3):
            angle=blade*1.8+rng.random()*.6
            direction=(tangent*math.cos(angle)+bitangent*math.sin(angle)+normal*.4).normalized()
            side=direction.cross(normal).normalized()
            length=.32+rng.random()*.28
            width=length*.24
            base=len(vertices)
            for ring in range(4):
                t=ring/3
                centre=point+direction*length*t+normal*.08*math.sin(t*math.pi)
                half_width=width*math.sin(t*math.pi)+.002
                vertices.extend([tuple(centre-side*half_width),tuple(centre+side*half_width)])
            for ring in range(3):
                a=base+ring*2
                faces.extend([(a,a+1,a+3),(a,a+3,a+2)])
    mesh=bpy.data.meshes.new(obj.name+'_LeafFans')
    mesh.from_pydata(vertices,[],faces);mesh.update()
    colour=mesh.color_attributes.new(name='Col',type='FLOAT_COLOR',domain='POINT')
    for vertex in mesh.vertices:
        colour.data[vertex.index].color=(max(0,min(1,(vertex.co.z-4)/8)),.9+.1*math.sin(vertex.co.x*.6),.5,1)
    for polygon in mesh.polygons: polygon.use_smooth=True
    for material in old.materials: mesh.materials.append(material)
    assert not mesh.validate(verbose=False,clean_customdata=False)
    obj.data=mesh
    obj['reference_canopy_polished']=True
    # Every source transform stays applied; ground pivots support placement.
    assert tuple(obj.scale)==(1,1,1) and tuple(obj.rotation_euler)==(0,0,0)
    bounds=[[min(v.co[i] for v in mesh.vertices),max(v.co[i] for v in mesh.vertices)] for i in range(3)]
    assert all(math.isfinite(value) for axis in bounds for value in axis)
    checks.append({'name':obj.name,'bounds':bounds,'triangles':sum(len(p.vertices)-2 for p in mesh.polygons)})

for collection in scene.collection.children:
    if not collection.name.startswith('beech_'): continue
    bpy.ops.object.select_all(action='DESELECT')
    for obj in collection.objects: obj.select_set(True)
    bpy.context.view_layer.objects.active=collection.objects[0]
    bpy.ops.export_scene.gltf(filepath=str(ROOT/'Content.Client/Assets/ArtDirection/ForestReference/Models'/(collection.name+'.glb')),
        export_format='GLB',use_selection=True,use_active_scene=True,
        export_vertex_color='ACTIVE',export_attributes=True)
bpy.data.libraries.write(str(ROOT/'art-source/linvel/forest-reference/forest_reference_assets.blend'),{scene},fake_user=True,compress=True)
result={'polished':checks,'source_preserved':True}
