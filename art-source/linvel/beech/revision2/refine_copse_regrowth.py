"""Add original beech regrowth within existing physically solid root banks.
Connection map: sapling bases enter bank surface by 6cm; branch roots enter
stems by 2cm; foliage masses overlap branch tips by at least 8cm.
Root-bank size and collision stay unchanged. Run only via Blender MCP.
"""
import ast, bpy, bmesh, math, random, json
from pathlib import Path
from mathutils import Vector
ROOT=Path('D:/projects/project-g'); SOURCE=ROOT/'art-source/linvel/beech/revision2'
scene=bpy.data.scenes['BeechRevision2_AssetLibrary']; bpy.context.window.scene=scene
catalog=json.loads((SOURCE/'catalog.json').read_text())
ns={'bpy':bpy,'bmesh':bmesh,'math':math,'random':random,'json':json,'Path':Path,'Vector':Vector,
 'scene':scene,'mat':bpy.data.materials['Beech_HandPaintedVertexColor'],'ROOT':ROOT,'SOURCE':SOURCE,
 'OUT':ROOT/'Content.Client/Assets/World/Linvel/Beech/Revision2/Models','parts':[],'bounds_checked':0,
 'catalog':[e for e in catalog if not e['id'].startswith('beech_copse_')]}
for node in ast.parse((SOURCE/'build_assets.py').read_text()).body:
 if isinstance(node,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='PALETTE' for t in node.targets): ns['PALETTE']=ast.literal_eval(node.value)
 if isinstance(node,ast.FunctionDef): exec(compile(ast.Module(body=[node],type_ignores=[]),'build_assets.py','exec'),ns)
for variant in range(2):
 id='beech_copse_'+str(variant); old=bpy.data.collections[id]; old.name=id+'_sparse_previous'
 ns['parts']=[]
 for source in list(old.objects):
  obj=source.copy(); obj.data=source.data.copy(); scene.collection.objects.link(obj)
  obj.hide_set(False); obj.hide_render=False; ns['parts'].append(obj)
 for i,(x,y) in enumerate([(-3.4,-2.6),(-1.1,-2.7),(1.1,-2.7),(3.4,-2.4),(-3.4,-.5),(3.4,-.1),(-3.2,2.6),(-1,2.6),(1.1,2.6),(3.2,2.5)]):
  height=4.0+(i*7+variant*3)%5*.43
  base=Vector((x,y,1.15)); tip=base+Vector((.18*math.sin(i),.15*math.cos(i),height))
  ns['tube']('Young beech',base-Vector((0,0,.06)),tip,.17,.028,'bark',8)
  for j in range(3):
   angle=i*2.2+j*2.1; branch=base+Vector((0,0,height*.55+j*.3)); end=branch+Vector((math.cos(angle)*1.0,math.sin(angle)*1.0,.55))
   ns['tube']('Young beech branch',branch-Vector((0,0,.02)),end,.065,.014,'bark',6)
   ns['blob']('Regrowth leaf mass',end+Vector((0,0,.4)),(2.5,2.2,1.8),'leaflight' if j==2 else 'leaf',i+variant*31+j,12,7)
  ns['blob']('Sapling crown',tip-Vector((0,0,.15)),(2.4,2.1,1.7),'leaflight',i+41,12,7)
 boxes=next(e for e in catalog if e['id']==id)['collision_boxes']
 ns['record'](id,boxes)
(SOURCE/'catalog.json').write_text(json.dumps(ns['catalog'],indent=2),encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'beech_revision2_assets.blend'))
result={'copses':[{k:e[k] for k in ['id','triangles','bounds']} for e in ns['catalog'] if e['id'].startswith('beech_copse_')],'verified_parts':ns['bounds_checked']}
