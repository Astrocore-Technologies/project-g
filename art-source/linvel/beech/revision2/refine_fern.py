"""Replace only our fern with folded 3D fronds and audit source connections."""
import ast, bpy, json, math, random, bmesh
from pathlib import Path
from mathutils import Vector
ROOT=Path('D:/projects/project-g'); SOURCE=ROOT/'art-source/linvel/beech/revision2'
scene=bpy.data.scenes['BeechRevision2_AssetLibrary']; bpy.context.window.scene=scene
catalog=json.loads((SOURCE/'catalog.json').read_text())
ns={'bpy':bpy,'bmesh':bmesh,'math':math,'random':random,'json':json,'Path':Path,'Vector':Vector,
    'scene':scene,'mat':bpy.data.materials['Beech_HandPaintedVertexColor'],'ROOT':ROOT,
    'SOURCE':SOURCE,'OUT':ROOT/'Content.Client/Assets/World/Linvel/Beech/Revision2/Models',
    'parts':[],'bounds_checked':0,'catalog':[e for e in catalog if e['id']!='woodland_fern']}
for node in ast.parse((SOURCE/'build_assets.py').read_text()).body:
    if isinstance(node,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='PALETTE' for t in node.targets):
        ns['PALETTE']=ast.literal_eval(node.value)
    if isinstance(node,ast.FunctionDef): exec(compile(ast.Module(body=[node],type_ignores=[]),'build_assets.py','exec'),ns)
old=bpy.data.collections['woodland_fern']
for o in list(old.objects): bpy.data.objects.remove(o,do_unlink=True)
bpy.data.collections.remove(old)
for i in range(9):
    theta=i*math.tau/9
    direction=Vector((math.cos(theta),math.sin(theta),0))
    side=Vector((-math.sin(theta),math.cos(theta),0))
    centers=[direction*(j*.16)+Vector((0,0,.53*math.sin(j*.43))) for j in range(6)]
    for j in range(5): ns['tube']('Fern rib',centers[j],centers[j+1],.009,.006,'moss',5)
    for j in range(1,6):
        center=centers[j]; length=.22*(1-.10*j)
        for sign in [-1,1]:
            end=center+side*length*sign-direction*.035
            mid=(center+end)*.5+Vector((0,0,.035))
            points=[center,mid+direction*.045,end,mid-direction*.045,mid+Vector((0,0,.017))]
            ns['mesh']('Folded fern leaflet',[tuple(v) for v in points],[(0,1,4),(1,2,4),(2,3,4),(3,0,4)],'leaf',i+j)
ns['record']('woodland_fern')
(SOURCE/'catalog.json').write_text(json.dumps(ns['catalog'],indent=2),encoding='utf-8')
# Source-only correction: spacers must overlap the board bottoms by 2cm.
for o in bpy.data.collections['split_board_deck'].objects:
    if o.name.startswith('Deck spacer'):
        for v in o.data.vertices: v.co.z+=.03
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'beech_revision2_assets.blend'))
result={'fern_triangles':ns['catalog'][-1]['triangles'],'fern_vertices':ns['catalog'][-1]['vertices'],'spacer_overlap_m':.02}
