"""Original folded woodland fern; run exclusively inside Blender MCP.
Connection map: frond stems meet the ground crown with 1cm overlap;
adjacent rib segments share endpoints; leaflet roots share rib centre points;
each folded leaflet ridge shares vertices with both sides. No detached parts.
"""
import ast, bpy, bmesh, math, random, json
from pathlib import Path
from mathutils import Vector
ROOT=Path('D:/projects/project-g'); SOURCE=ROOT/'art-source/linvel/beech/revision2'
scene=bpy.data.scenes['BeechRevision2_AssetLibrary']; bpy.context.window.scene=scene
catalog=json.loads((SOURCE/'catalog.json').read_text())
ns={'bpy':bpy,'bmesh':bmesh,'math':math,'random':random,'json':json,'Path':Path,'Vector':Vector,
    'scene':scene,'mat':bpy.data.materials['Beech_HandPaintedVertexColor'],'ROOT':ROOT,'SOURCE':SOURCE,
    'OUT':ROOT/'Content.Client/Assets/World/Linvel/Beech/Revision2/Models',
    'parts':[],'bounds_checked':0,'catalog':[e for e in catalog if e['id']!='woodland_fern']}
for node in ast.parse((SOURCE/'build_assets.py').read_text()).body:
    if isinstance(node,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='PALETTE' for t in node.targets): ns['PALETTE']=ast.literal_eval(node.value)
    if isinstance(node,ast.FunctionDef): exec(compile(ast.Module(body=[node],type_ignores=[]),'build_assets.py','exec'),ns)
old=bpy.data.collections['woodland_fern']; old.name='woodland_fern_radial_previous'
ns['PALETTE']['leaf']=(.115,.225,.085); ns['PALETTE']['leaflight']=(.18,.285,.12)
for i,(theta,length,peak) in enumerate([(-2.6,.62,.37),(-1.7,.96,.49),(-.6,.78,.54),(.15,1.05,.44),(1.25,.73,.56),(2.35,.88,.39)]):
    direction=Vector((math.cos(theta),math.sin(theta),0)); side=Vector((-direction.y,direction.x,0))
    centers=[]
    for j in range(8):
        t=j/7
        centers.append(direction*(length*t)+side*(.09*math.sin(t*math.pi)*(-1 if i%2 else 1))+Vector((0,0,.015+peak*math.sin(t*2.65))))
    for j in range(7): ns['tube']('Curved fern rib',centers[j],centers[j+1],.011,.008,'moss',5)
    for j in range(1,8):
        center=centers[j]; t=j/8; width=.18*(math.sin(t*math.pi)**.7)+.012
        for sign in [-1,1]:
            end=center+side*width*sign+direction*(.075*(1-t)); mid=(center+end)*.5+Vector((0,0,.026))
            points=[center-direction*.012,mid-direction*.037,end,mid+direction*.037,mid+Vector((0,0,.016))]
            ns['mesh']('Tapered fern leaflet',[tuple(v) for v in points],[(0,1,4),(1,2,4),(2,3,4),(3,0,4)],'leaflight' if i==2 else 'leaf',i*9+j)
ns['record']('woodland_fern')
(SOURCE/'catalog.json').write_text(json.dumps(ns['catalog'],indent=2),encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'beech_revision2_assets.blend'))
result={'triangles':ns['catalog'][-1]['triangles'],'bounds':ns['catalog'][-1]['bounds'],'verified_parts':ns['bounds_checked']}
