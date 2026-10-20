"""Run inside Blender through MCP. Original untextured review proxies, not final art.

CONNECTION MAP (metres, verified from each part's baked vertex bounds):
 body.top <-> roof.eaves.bottom: 0.12m overlap along Z.
 guild.body <-> side_tower: >0.15m overlap on X/Y/Z.
 side_tower.top <-> tower_roof.bottom: 0.12m on Z.
 lighthouse.shaft <-> lantern.bottom: 0.20m on Z.
 lantern.top <-> beacon_roof.bottom: 0.12m on Z.
 ship.hull <-> mast.bottom: 0.25m on Z; sail panels intersect mast 0.04m.
 No subsurf, no rotated cylinders, no unapplied mesh scale.
"""
import bpy, bmesh, math, json
from pathlib import Path
from mathutils import Vector

ROOT=Path('D:/projects/project-g/art-source/linvel/masterplan')
OUT=Path('D:/projects/project-g/Content.Client/Assets/World/Linvel/City/Masterplan/Kit')
OUT.mkdir(parents=True,exist_ok=True)
assert (ROOT/'blockout-silhouettes-2d.png').exists(),'2D sheet must precede geometry'
assert bpy.data.scenes.get('Linvel_MasterplanKit') is None,'Refuse to replace manually editable source'
sc=bpy.data.scenes.new('Linvel_MasterplanKit');bpy.context.window.scene=sc
sc.unit_settings.system='METRIC';sc.unit_settings.scale_length=1
palette={'Plaster':(.77,.66,.45,1),'Roof':(.50,.20,.10,1),'BlueRoof':(.075,.21,.27,1),
 'Stone':(.57,.55,.45,1),'Timber':(.27,.16,.08,1),'Waterline':(.10,.28,.34,1),'Sail':(.87,.79,.59,1)}
mats={}
for name,col in palette.items():
    m=bpy.data.materials.new('LV_Blockout_'+name);m.diffuse_color=col;m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF');p.inputs['Base Color'].default_value=col
    p.inputs['Roughness'].default_value=.92;mats[name]=m
report=[]

def bounds(obj):
    vs=[obj.matrix_world@v.co for v in obj.data.vertices]
    return [[min(v[i] for v in vs),max(v[i] for v in vs)] for i in range(3)]
def overlap(a,b,axis=2,minimum=.005):
    aa,bb=bounds(a),bounds(b);n=min(aa[axis][1],bb[axis][1])-max(aa[axis][0],bb[axis][0])
    assert n>=minimum,(a.name,b.name,axis,n);return round(n,4)
def mesh(name,v,f,mat,col):
    me=bpy.data.meshes.new(name);me.from_pydata(v,[],f);me.update()
    bm=bmesh.new();bm.from_mesh(me);bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(me);bm.free()
    ob=bpy.data.objects.new(name,me);col.objects.link(ob);me.materials.append(mats[mat]);return ob
def box(name,c,d,mat,col):
    bm=bmesh.new();bmesh.ops.create_cube(bm,size=2)
    for v in bm.verts:
        v.co=Vector((v.co.x*d[0]/2+c[0],v.co.y*d[1]/2+c[1],v.co.z*d[2]/2+c[2]))
    me=bpy.data.meshes.new(name);bm.to_mesh(me);bm.free()
    ob=bpy.data.objects.new(name,me);col.objects.link(ob);me.materials.append(mats[mat]);return ob
def roof(name,w,dep,eave,peak,mat,col,cx=0,cy=0):
    # A closed gabled prism. Coordinates are baked; roof spans measured body bounds.
    v=[(cx+x,cy+y,z) for y in [-dep/2,dep/2] for x,z in [(-w/2,eave),(w/2,eave),(0,peak)]]
    return mesh(name,v,[(0,2,1),(3,4,5),(0,1,4,3),(1,2,5,4),(2,0,3,5)],mat,col)
def cylinder(name,r,z0,z1,col,mat='Stone',segments=12):
    v=[(r*math.cos(i*math.tau/segments),r*math.sin(i*math.tau/segments),z) for z in [z0,z1] for i in range(segments)]
    f=[tuple(reversed(range(segments))),tuple(range(segments,segments*2))]
    f += [(i,(i+1)%segments,(i+1)%segments+segments,i+segments) for i in range(segments)]
    return mesh(name,v,f,mat,col)

layout=json.loads((ROOT/'layout.json').read_text(encoding='utf-8'))
for kind,(w,dep,h) in layout['kits'].items():
    col=bpy.data.collections.new('LV_MP_'+kind);sc.collection.children.link(col)
    root=bpy.data.objects.new('block_'+kind,None);col.objects.link(root)
    joints=[]
    if kind=='ship':
        # Deliberately abstract vessel clearance envelope, no final ship model.
        v=[(-w*.3,-dep*.42,0),(w*.3,-dep*.42,0),(w*.3,dep*.42,0),(-w*.3,dep*.42,0),
           (-w*.5,-dep*.38,3),(0,-dep*.5,3),(w*.5,-dep*.38,3),(w*.5,dep*.38,3),(0,dep*.5,3),(-w*.5,dep*.38,3)]
        hull=mesh('HullEnvelope',v,[(0,3,2,1),(0,1,6,5,4),(1,2,7,6),(2,3,9,8,7),(3,0,4,9),(4,5,6,7,8,9)],'Timber',col)
        mast=box('MastEnvelope',(0,0,(h+2.75)/2),(.45,.45,h-2.75),'Timber',col)
        sail=box('SailEnvelope',(0,0,h*.65),(w*.83,.12,h*.45),'Sail',col)
        joints.append(overlap(hull,mast));joints.append(overlap(mast,sail,1))
    elif kind=='lighthouse':
        body=cylinder('Shaft',4.35,0,27.8,col)
        lantern=cylinder('Lantern',5.0,27.6,31.1,col,'Waterline')
        cap=mesh('BeaconRoof',[(5.5*math.cos(i*math.tau/12),5.5*math.sin(i*math.tau/12),30.98) for i in range(12)]+[(0,0,34)],
          [tuple(reversed(range(12)))]+[(i,(i+1)%12,12) for i in range(12)],'BlueRoof',col)
        joints.extend([overlap(body,lantern),overlap(lantern,cap)])
    elif kind=='guild':
        body=box('GuildHall',(0,0,8.25),(w,dep,16.5),'Plaster',col);b=bounds(body)
        cap=roof('GuildRoof',b[0][1]-b[0][0]+1,dep+1,b[2][1]-.12,24,'BlueRoof',col)
        joints.append(overlap(body,cap))
        for x in [-10.5,10.5]:
            t=box('FlankingTower',(x,-6,11),(6.5,7,22),'Stone',col);tb=bounds(t)
            tr=roof('TowerRoof',tb[0][1]-tb[0][0]+.7,7.7,tb[2][1]-.12,29,'BlueRoof',col,x,-6)
            joints.extend([overlap(body,t,0),overlap(t,tr)])
    else:
        body=box('Body',(0,0,h*.72/2),(w,dep,h*.72),'Plaster' if kind!='tower' else 'Stone',col)
        bb=bounds(body);cap=roof('Roof',bb[0][1]-bb[0][0]+.7,bb[1][1]-bb[1][0]+.7,bb[2][1]-.12,h,'BlueRoof' if kind=='shrine' else 'Roof',col)
        joints.append(overlap(body,cap))
    for ob in list(col.objects):
        if ob.type!='MESH':continue
        ob.parent=root
        # Mesh origins at geometry; root preserves the ground anchor.
        center=sum((v.co for v in ob.data.vertices),Vector())/len(ob.data.vertices)
        for v in ob.data.vertices:v.co-=center
        ob.location=center
        assert tuple(ob.scale)==(1.,1.,1.) and all(abs(v)<1e-7 for v in ob.rotation_euler)
    for ob in bpy.data.objects:ob.select_set(False)
    for ob in col.objects:ob.select_set(True)
    bpy.context.view_layer.objects.active=root
    bpy.context.view_layer.update()
    path=OUT/('block_'+kind+'.glb')
    bpy.ops.export_scene.gltf(filepath=str(path),export_format='GLB',use_selection=True,use_active_scene=True,export_animations=False,export_extras=True)
    meshes=[o for o in col.objects if o.type=='MESH']
    tris=sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in meshes)
    report.append(dict(kind=kind,meshes=len(meshes),triangles=tris,joints=joints,path=str(path)))
sc['purpose']='Review-only untextured building mass kit. 2D sheet and layout precede this source.'
bpy.ops.wm.save_as_mainfile(filepath=str(ROOT/'linvel_blockout_kit.blend'),copy=True)
(ROOT/'kit-validation.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
result={'scene':sc.name,'source':str(ROOT/'linvel_blockout_kit.blend'),'exports':len(report),'total_triangles':sum(r['triangles'] for r in report),'checked_joints':sum(len(r['joints']) for r in report)}
