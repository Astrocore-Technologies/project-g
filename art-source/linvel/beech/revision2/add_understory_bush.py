"""Original low beech regrowth, executed only through Blender MCP.

Connection map, metres: successive stem segments share endpoints and overlap
by their radii (>= .008); petiole roots overlap their measured stem by .012;
each curved, solid leaf overlaps the petiole tip by .012. Ground origin Z=0.
No terrain or scene placements are changed. Existing source edits are retained.
"""
import bpy, math, json
from mathutils import Vector
from pathlib import Path

source = Path('D:/projects/project-g/art-source/linvel/beech/revision2')
name = 'beech_understory_bush'
if name in bpy.data.collections:
    raise RuntimeError('Existing authored bush preserved.')
scene = bpy.data.scenes['BeechRevision2_AssetLibrary']
assert scene.unit_settings.scale_length == 1
material = next(obj.data.materials[0] for obj in bpy.data.collections['beech_canopy_0'].objects
                if obj.type == 'MESH' and len(obj.data.materials)>0)
collection = bpy.data.collections.new(name)
scene.collection.children.link(collection)
checked = []

def part(label, vertices, faces, colour, code):
    mesh = bpy.data.meshes.new(name+' '+label)
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    mesh.materials.append(material)
    obj = bpy.data.objects.new(name+' '+label, mesh)
    collection.objects.link(obj)
    paint = mesh.color_attributes.new(name='Paint', type='FLOAT_COLOR', domain='CORNER')
    for poly in mesh.polygons:
        poly.use_smooth = True
        for index in poly.loop_indices:
            p = mesh.vertices[mesh.loops[index].vertex_index].co
            wash = .90 + .10 * p.z
            paint.data[index].color = tuple(c*wash for c in colour)+(code,)
    points = [obj.matrix_world @ v.co for v in mesh.vertices]
    b = [[min(p[i] for p in points),max(p[i] for p in points)] for i in range(3)]
    assert all(math.isfinite(value) for axis in b for value in axis)
    checked.append(b)
    obj.hide_set(True)
    obj.hide_render = True
    return obj

def twig(a, b, radius):
    axis = (b-a).normalized()
    helper = Vector((0,0,1)) if abs(axis.z)<.98 else Vector((1,0,0))
    right = axis.cross(helper).normalized()
    up = right.cross(axis).normalized()
    vertices = [tuple(p+(right*math.cos(i*math.tau/6)+up*math.sin(i*math.tau/6))*r)
                for p,r in [(a,radius),(b,radius*.7)] for i in range(6)]
    faces = [tuple(range(5,-1,-1)), tuple(range(6,12))]
    faces += [(i,(i+1)%6,(i+1)%6+6,i+6) for i in range(6)]
    return part('Bark twig',vertices,faces,(.24,.21,.14),.85)

def leaf(base, direction, length, width, tone):
    forward = direction.normalized()
    right = forward.cross(Vector((0,0,1))).normalized()
    normal = right.cross(forward).normalized()
    base = base-forward*.012
    top = [base, base+forward*length*.48-right*width,
           base+forward*length*.52+normal*.045,
           base+forward*length*.48+right*width, base+forward*length]
    vertices = [tuple(p) for p in top]+[tuple(p-normal*.003) for p in top]
    front = [(0,1,2),(0,2,3),(1,4,2),(2,4,3)]
    faces = front+[tuple(i+5 for i in reversed(f)) for f in front]
    edge = [0,1,4,3]
    faces += [(edge[i],edge[(i+1)%4],edge[(i+1)%4]+5,edge[i]+5) for i in range(4)]
    return part('Leaf blade',vertices,faces,tone,1.0)

for stem in range(7):
    theta = stem*2.399
    radius = .60+(stem%3)*.18
    height = .55+(stem%4)*.13
    tip = Vector((math.cos(theta)*radius,math.sin(theta)*radius,height))
    points = [Vector((0,0,.02)),tip*.4+Vector((0,0,.14)),tip*.75+Vector((0,0,.1)),tip]
    for i in range(3): twig(points[i],points[i+1],.037-i*.009)
    for position in range(5):
        t = .34+position*.15
        anchor = points[1].lerp(tip,t)
        for side in [-1,1]:
            angle = theta+side*(.85+position*.08)
            direction = Vector((math.cos(angle),math.sin(angle),.26+position*.035))
            petiole_end = anchor+direction.normalized()*.075
            twig(anchor-direction.normalized()*.012,petiole_end,.012)
            tone = (.24,.36,.105) if (stem+position)%3 else (.30,.43,.135)
            leaf(petiole_end,direction,.30+(position%3)*.065,.11,tone)

catalog = json.loads((source/'catalog.json').read_text(encoding='utf-8'))
catalog.append({'id':name,'collision_boxes':[], 'license':'project-owned original geometry',
                'origin':'ground-centred; Blender Z up / glTF Y up',
                'purpose':'low flexible undergrowth, curved solid leaves, no movement collider'})
(source/'catalog.json').write_text(json.dumps(catalog,ensure_ascii=False,indent=2),encoding='utf-8')
bpy.context.view_layer.update()
bpy.ops.wm.save_as_mainfile(filepath=str(source/'beech_revision2_assets.blend'))
result = {'asset':name,'verified_parts':len(checked),'leaves':70,
          'height':max(b[2][1] for b in checked),'colliders':0}
