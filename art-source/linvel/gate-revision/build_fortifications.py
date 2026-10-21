"""Execute through Blender MCP. Original meshes; existing scenes untouched.
Connection map (metres): wall body -2.5..4.5 meets coping 4.46..4.82;
merlons 4.78..5.6 overlap coping .04; plinth -2.6...45 overlaps body.
Arch piers 0..6.1 meet spring at 6 with .1 overlap; adjoining wedge faces
share exact vertices; top spandrels share outer wedge edges; coping 9.56..9.9.
No directional cylinders. Cube size=2, transforms applied immediately.
"""
import bpy, math, json
from pathlib import Path
ROOT=Path('D:/projects/project-g/art-source/linvel/gate-revision')
OUT=Path('D:/projects/project-g/Content.Client/Assets/World/Linvel/City/Production/Models')
assert not bpy.data.scenes.get('Linvel_GateRevision'), 'Already authored; use editable source'
scene=bpy.data.scenes.new('Linvel_GateRevision')
bpy.context.window.scene=scene
scene.unit_settings.system='METRIC'
stone=bpy.data.materials.new('Linvel_WarmLimestone')
stone.diffuse_color=(.72,.66,.49,1)
stone.use_nodes=True
stone.node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.72,.66,.49,1)
stone.node_tree.nodes.get('Principled BSDF').inputs['Roughness'].default_value=.94
audit=[]
def bounds(o):
    bpy.context.view_layer.update()
    vs=[o.matrix_world@v.co for v in o.data.vertices]
    return [[min(v[i] for v in vs),max(v[i] for v in vs)] for i in range(3)]
def box(name,loc,size):
    bpy.ops.mesh.primitive_cube_add(size=2,location=loc)
    o=bpy.context.object;o.name=name;o.scale=tuple(v/2 for v in size)
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
    o.data.materials.append(stone)
    audit.append({'part':name,'bounds':bounds(o)})
    return o
def mesh(name,verts,faces):
    m=bpy.data.meshes.new(name);m.from_pydata(verts,[],faces);m.update()
    o=bpy.data.objects.new(name,m);scene.collection.objects.link(o);m.materials.append(stone)
    audit.append({'part':name,'bounds':bounds(o)});return o
def overlap(a,b,axis=2,minimum=.005):
    aa=bounds(a);bb=bounds(b);v=min(aa[axis][1],bb[axis][1])-max(aa[axis][0],bb[axis][0])
    assert v>=minimum,(a.name,b.name,v)
    audit.append({'joint':[a.name,b.name],'overlap_m':v})
def export(key,objects):
    bpy.ops.object.select_all(action='DESELECT')
    for o in objects:o.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.object.join();o=bpy.context.object;o.name=key
    bevel=o.modifiers.new('SoftPaintedEdges','BEVEL');bevel.width=.035;bevel.segments=1
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
    o.data.calc_loop_triangles()
    bpy.ops.export_scene.gltf(filepath=str(OUT/(key+'.glb')),use_selection=True,use_active_scene=True,export_format='GLB')
    return {'key':key,'bounds_blender_m':bounds(o),'triangles':len(o.data.loop_triangles),'transforms_clean':tuple(o.scale)==(1,1,1) and tuple(o.rotation_euler)==(0,0,0),'own_original_geometry':True}
parts=[]
body=box('CurtainBody',(0,0,1),(8,2.0,7));parts.append(body)
plinth=box('BuriedPlinth',(0,0,-1.05),(8,2.4,3.1));parts.append(plinth);overlap(body,plinth)
cope=box('Coping',(0,0,4.64),(8,2.34,.36));parts.append(cope);overlap(body,cope)
for i in range(4):
    merlon=box('Merlon_%d'%i,(-3+i*2,0,5.19),(1.06,2.1,.82));parts.append(merlon);overlap(cope,merlon)
reports=[export('low_city_wall_8m',parts)]
parts=[]
for sign in (-1,1):parts.append(box('ArchPier_%d'%sign,(sign*6.6,0,3),(1.2,2.5,6.2)))
for i in range(20):
    a=i*math.pi/20;b=(i+1)*math.pi/20
    inner=[(6*math.cos(t),6+2*math.sin(t)) for t in (a,b)]
    outer=[(7.2*math.cos(t),6+3.2*math.sin(t)) for t in (a,b)]
    profile=[inner[0],outer[0],outer[1],inner[1]]
    vs=[(x,y,z) for y in (-1.25,1.25) for x,z in profile]
    faces=[(0,3,2,1),(4,5,6,7),(0,1,5,4),(1,2,6,5),(2,3,7,6),(3,0,4,7)]
    parts.append(mesh('Voussoir_%02d'%i,vs,faces))
    # No gap between ring and solid upper spandrel.
    prof=[outer[0],(outer[0][0],9.6),(outer[1][0],9.6),outer[1]]
    vs=[(x,y,z) for y in (-1.25,1.25) for x,z in prof]
    parts.append(mesh('Spandrel_%02d'%i,vs,faces))
cope=box('GateCoping',(0,0,9.73),(14.5,2.8,.34));parts.append(cope)
for i in range(7):
    merlon=box('GateMerlon_%d'%i,(-6+i*2,0,10.27),(1.1,2.4,.82));parts.append(merlon);overlap(cope,merlon)
reports.append(export('north_gate_arch_12m',parts))
assert all(r['transforms_clean'] and r['triangles']<5000 for r in reports)
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'linvel_fortifications.blend'))
(ROOT/'model-report.json').write_text(json.dumps({'models':reports,'assembly_audit':audit},indent=2),encoding='utf-8')
result={'models':reports,'source':str(ROOT/'linvel_fortifications.blend')}
