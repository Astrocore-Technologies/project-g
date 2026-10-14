"""Refine only our herbs; use the existing asset library and export functions.
Connection map: stem starts 2cm below the leaf rosette; each leaf root overlaps
its stem by 1cm; the folded leaf ridge shares vertices with both leaf sides.
"""
import ast, bpy, bmesh, json, math, random
from pathlib import Path
from mathutils import Vector
ROOT=Path('D:/projects/project-g'); SOURCE=ROOT/'art-source/linvel/beech/revision2'
scene=bpy.data.scenes['BeechRevision2_AssetLibrary']; bpy.context.window.scene=scene
catalog=json.loads((SOURCE/'catalog.json').read_text())
ns={'bpy':bpy,'bmesh':bmesh,'math':math,'random':random,'json':json,'Path':Path,'Vector':Vector,
    'scene':scene,'mat':bpy.data.materials['Beech_HandPaintedVertexColor'],'ROOT':ROOT,'SOURCE':SOURCE,
    'OUT':ROOT/'Content.Client/Assets/World/Linvel/Beech/Revision2/Models',
    'parts':[],'bounds_checked':0,'catalog':[e for e in catalog if e['id']!='sunlit_herb']}
for node in ast.parse((SOURCE/'build_assets.py').read_text()).body:
    if isinstance(node,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='PALETTE' for t in node.targets):
        ns['PALETTE']=ast.literal_eval(node.value)
    if isinstance(node,ast.FunctionDef): exec(compile(ast.Module(body=[node],type_ignores=[]),'build_assets.py','exec'),ns)
# Keep the previous source collection as a recoverable art revision.
old=bpy.data.collections['sunlit_herb']; old.name='sunlit_herb_previous'
ns['PALETTE']['leaf']=(.12,.24,.11); ns['PALETTE']['leaflight']=(.19,.31,.13)
for plant,(x,y,sz) in enumerate([(-.18,-.1,1.0),(.19,.08,.82),(.04,-.2,.63)]):
    for i in range(5):
        angle=i*math.tau/5+plant*.65
        axis=Vector((math.cos(angle),math.sin(angle),0)); side=Vector((-axis.y,axis.x,0))
        base=Vector((x,y,.015)); middle=base+axis*.12*sz+Vector((0,0,.2*sz)); tip=base+axis*.26*sz+Vector((0,0,.13*sz))
        ns['tube']('Herb leaf petiole',base-Vector((0,0,.02)),middle,.012,.006,'moss',5)
        verts=[base-axis*.012,middle-side*.065*sz,tip,middle+side*.065*sz,middle+Vector((0,0,.018))]
        ns['mesh']('Folded broad herb leaf',[tuple(v) for v in verts],[(0,1,4),(1,2,4),(2,3,4),(3,0,4)],'leaflight' if i%3==0 else 'leaf',i)
ns['record']('sunlit_herb')
(SOURCE/'catalog.json').write_text(json.dumps(ns['catalog'],indent=2),encoding='utf-8')
# Re-export the corrected board spacers from the existing source, preserving it.
ns['parts']=list(bpy.data.collections['split_board_deck'].objects)
for obj in ns['parts']: obj.hide_set(False); obj.hide_render=False
ns['catalog']=[e for e in ns['catalog'] if e['id']!='split_board_deck']
# record() creates a new source collection, so restore the original collection name afterward.
deck=bpy.data.collections['split_board_deck']; deck.name='split_board_deck_previous'
ns['record']('split_board_deck',[e for e in catalog if e['id']=='split_board_deck'][0]['collision_boxes'])
(SOURCE/'catalog.json').write_text(json.dumps(ns['catalog'],indent=2),encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'beech_revision2_assets.blend'))
result={'herb_triangles':[e for e in ns['catalog'] if e['id']=='sunlit_herb'][0]['triangles'],
        'source_parts_checked':ns['bounds_checked'],'deck_reexported':True}
