"""Add original working details to the saved Blender library, without rebuilding it."""
import ast, bpy, json, math, random, bmesh
from pathlib import Path
from mathutils import Vector
ROOT=Path('D:/projects/project-g'); SOURCE=ROOT/'art-source/linvel/beech/revision2'
OUT=ROOT/'Content.Client/Assets/World/Linvel/Beech/Revision2/Models'
scene=bpy.data.scenes['BeechRevision2_AssetLibrary']; bpy.context.window.scene=scene
ns={'bpy':bpy,'bmesh':bmesh,'math':math,'random':random,'json':json,'Path':Path,'Vector':Vector,
    'scene':scene,'mat':bpy.data.materials['Beech_HandPaintedVertexColor'],
    'ROOT':ROOT,'SOURCE':SOURCE,'OUT':OUT,'parts':[],'bounds_checked':0,
    'catalog':json.loads((SOURCE/'catalog.json').read_text())}
tree=ast.parse((SOURCE/'build_assets.py').read_text())
for node in tree.body:
    if isinstance(node,ast.Assign) and any(isinstance(t,ast.Name) and t.id=='PALETTE' for t in node.targets):
        ns['PALETTE']=ast.literal_eval(node.value)
    if isinstance(node,ast.FunctionDef):
        exec(compile(ast.Module(body=[node],type_ignores=[]),str(SOURCE/'build_assets.py'),'exec'),ns)
box,beam,tube,blob,record=(ns[name] for name in ['box','beam','tube','blob','record'])
# Connections: feet overlap rails by 0.03, rails overlap tabletop by 0.05.
for x in [-1.25,1.25]:
    for y in [-.48,.48]: beam('Bench foot',(x,y,-.03),(x,y,.88),.12,.15)
for y in [-.5,.5]: beam('Bench sill',(-1.4,y,.80),(1.4,y,.80),.15,.15)
for i in range(7): box('Work surface',(-1.32+i*.44,0,.88),(.43,1.35,.14),'fresh',.018)
box('Wooden tool tray',(.85,0,1.03),(.55,.65,.12),'wood',.025)
record('forest_workbench',[{'center':[0,.5,0],'size':[2.7,1,1.2],'part':'structure'}])
for y in [-1.9,0,1.9]: beam('Deck spacer',(-4,y,-.12),(4,y,-.12),.16,.22)
for i in range(16): box('Raised working board',(-3.75+i*.5,0,.09),(.49,4.8,.18),'fresh',.018)
record('split_board_deck',[{'center':[0,.09,0],'size':[8,.18,4.8],'walkable':True}])
for x in [-1.8,1.8]: beam('Drying upright',(x,0,-.03),(x,0,1.9),.09,.11)
for z in [.55,1.20,1.85]: beam('Drying bar',(-1.9,0,z),(1.9,0,z),.07,.08)
for i in range(9):
    x=-1.55+i*.38
    for j in range(3): blob('Drying herb',(x,.05,1.80-j*.13),(.16,.10,.27),'moss',i*3+j,8,4)
record('herb_drying_frame',[{'center':[x,.9,0],'size':[.12,1.85,.12],'part':'post'} for x in [-1.8,1.8]])
for i in range(15):
    t=i*math.tau/15
    blob('Hearth stone',(math.cos(t)*1.1,math.sin(t)*1.1,.15),(.47,.35,.34),'stone',i,10,6)
blob('Used charcoal hearth',(0,0,.06),(1.9,1.9,.15),'coal',9,18,6)
for i in range(5): tube('Blackened firewood',(-.65,.16*i-.3,.16),(.6,.16*i-.3,.16),.07,.055,'coal',8)
record('forest_firepit')
for i in range(3):
    blob('Worn sack',(-.6+i*.55,0,.38),(.65,.55,.8),'cloth',i,14,8)
    tube('Sack tie',(-.6+i*.55,0,.64),(-.6+i*.55,0,.79),.05,.07,'reed',8)
record('worn_supply_bundle',[{'center':[0,.38,0],'size':[1.7,.75,.6],'part':'structure'}])
(SOURCE/'catalog.json').write_text(json.dumps(ns['catalog'],indent=2),encoding='utf-8')
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'beech_revision2_assets.blend'))
result={'assets':len(ns['catalog']),'added':5,'bounds_checks':ns['bounds_checked']}
