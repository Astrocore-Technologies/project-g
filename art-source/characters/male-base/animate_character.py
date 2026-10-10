"""Author the Project G humanoid rig and animation library in a NEW staging file.

Blender 4.5: --background --python animate_character.py -- --source male-base.blend --out staging
The original sculpt is never overwritten. Actions remain editable in the delivered blend.
"""
import argparse, json, math, sys
from pathlib import Path
import bpy, bmesh
from mathutils import Vector, Euler, Quaternion, Matrix

p=argparse.ArgumentParser(); p.add_argument('--source',required=True); p.add_argument('--out',required=True)
a=p.parse_args(sys.argv[sys.argv.index('--')+1:]); out=Path(a.out).resolve()
out.mkdir(parents=True,exist_ok=True)
if (out/'male-animated.blend').exists(): raise RuntimeError('Use a fresh staging directory')
bpy.ops.wm.open_mainfile(filepath=str(Path(a.source).resolve()))
scene=bpy.context.scene
meshes=[o for o in scene.objects if o.type=='MESH' and (o.name.startswith('Body_Male') or o.name.startswith('Face_') or o.name.startswith('Hair_'))]
print('MODULES',[(o.name,len(o.data.vertices)) for o in meshes])
assert len(meshes)==7, [o.name for o in meshes]
for o in list(scene.objects):
    if o not in meshes: bpy.data.objects.remove(o,do_unlink=True)
for o in meshes:
    o.hide_set(False); o.hide_render=False
    for c in o.users_collection: c.hide_viewport=False; c.hide_render=False
    bpy.context.view_layer.objects.active=o; o.select_set(True)
    bpy.ops.object.transform_apply(location=True,rotation=True,scale=True); o.select_set(False)
body=next(o for o in meshes if o.name.startswith('Body'))
# Add local support edges at bending joints without remeshing the feet or painted briefs.
bm=bmesh.new(); bm.from_mesh(body.data)
edges=[]
for e in bm.edges:
    c=(e.verts[0].co+e.verts[1].co)*.5
    joint=(abs(c.z-.50)<.10 or abs(c.z-.91)<.10 or (c.z>1.29 and (abs(abs(c.x)-.48)<.10 or abs(abs(c.x)-.23)<.09)))
    if joint and e.calc_length()>.035: edges.append(e)
bmesh.ops.subdivide_edges(bm,edges=edges,cuts=1,use_grid_fill=True)
bm.to_mesh(body.data); bm.free(); body.data.update()
armdata=bpy.data.armatures.new('Humanoid'); rig=bpy.data.objects.new('Humanoid',armdata); scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active=rig; rig.select_set(True); bpy.ops.object.mode_set(mode='EDIT')
defs={}
def bone(name,head,tail,parent=None):
    b=armdata.edit_bones.new(name); b.head=head; b.tail=tail
    if parent: b.parent=armdata.edit_bones[parent]
    defs[name]=(Vector(head),Vector(tail))
bone('root',(0,0,0),(0,0,.15))
bone('pelvis',(0,.012,.91),(0,.008,1.07),'root')
bone('spine',(0,.008,1.07),(0,.006,1.28),'pelvis')
bone('chest',(0,.006,1.28),(0,.01,1.45),'spine')
bone('neck',(0,.01,1.45),(0,.007,1.55),'chest')
bone('head',(0,.007,1.55),(0,.012,1.80),'neck')
for side,sg in [('L',1),('R',-1)]:
    bone('clavicle.'+side,(sg*.045,0,1.435),(sg*.225,0,1.407),'chest')
    bone('upper_arm.'+side,(sg*.225,0,1.407),(sg*.48,0,1.407),'clavicle.'+side)
    bone('forearm.'+side,(sg*.48,0,1.407),(sg*.75,0,1.407),'upper_arm.'+side)
    bone('hand.'+side,(sg*.75,0,1.407),(sg*.851,0,1.407),'forearm.'+side)
    for digit,y,length in [('index',-.042,.12),('middle',-.014,.14),('ring',.016,.12),('little',.044,.10)]:
        bone(digit+'.'+side,(sg*.85,y,1.407),(sg*(.85+length*.52),y,1.405),'hand.'+side)
        bone(digit+'_tip.'+side,(sg*(.85+length*.52),y,1.405),(sg*(.85+length),y,1.404),digit+'.'+side)
    bone('thumb.'+side,(sg*.78,-.03,1.403),(sg*.84,-.09,1.393),'hand.'+side)
    bone('thigh.'+side,(sg*.085,.012,.925),(sg*.126,-.011,.50),'pelvis')
    bone('shin.'+side,(sg*.126,-.011,.50),(sg*.136,.008,.105),'thigh.'+side)
    bone('foot.'+side,(sg*.136,.008,.105),(sg*.136,-.14,.025),'shin.'+side)
    bone('toe.'+side,(sg*.136,-.14,.025),(sg*.136,-.219,.024),'foot.'+side)
bpy.ops.object.mode_set(mode='OBJECT')
def distance(v,h,t):
    d=t-h; f=max(0,min(1,(v-h).dot(d)/d.length_squared)); return (v-(h+f*d)).length
for obj in meshes:
    obj.vertex_groups.clear()
    groups={n:obj.vertex_groups.new(name=n) for n in defs if n!='root'}
    for v in obj.data.vertices:
        x,y,z=v.co; s='L' if x>=0 else 'R'; ax=abs(x)
        if obj!=body: choices=['head']
        elif z>1.29 and ax>.19:
            choices=['chest','clavicle.'+s,'upper_arm.'+s,'forearm.'+s,'hand.'+s]
            if ax>.83: choices=['hand.'+s]+[n for n in defs if n.endswith('.'+s) and (n.split('.')[0] in ['index','middle','ring','little','thumb'] or '_tip.' in n)]
            elif ax>.77 and y<-.04: choices=['hand.'+s,'thumb.'+s]
        elif z<.99:
            choices=['pelvis','thigh.'+s,'shin.'+s,'foot.'+s,'toe.'+s]
            if z<.16: choices=['shin.'+s,'foot.'+s,'toe.'+s]
        else: choices=['pelvis','spine','chest','neck','head']
        weights=sorted([(n,1/max(.012,distance(v.co,*defs[n]))**4) for n in choices],key=lambda x:-x[1])[:4]
        total=sum(w for _,w in weights)
        for n,w in weights: groups[n].add([v.index],w/total,'REPLACE')
    mod=obj.modifiers.new('Humanoid_skin','ARMATURE'); mod.object=rig; obj.parent=rig
    obj['shared_skeleton']='Humanoid'; obj.select_set(True)
rig.show_in_front=True
# Original simple training sword, skinned rigidly to the right hand.
parts=[]
# The grip crosses all four curled fingers along the palm width (Y in the T rest pose).
# Its blade exits on the thumb/index side; hand orientation, not an offset, raises it in combat.
for name,location,scale in [('Grip',(-.883,0,1.380),(.018,.065,.018)),('Guard',(-.883,-.068,1.380),(.090,.014,.022)),('Blade',(-.883,-.393,1.380),(.034,.315,.009))]:
    bpy.ops.mesh.primitive_cube_add(size=2,location=location); obj=bpy.context.object; obj.name=name; obj.scale=scale
    bpy.ops.object.transform_apply(location=False,rotation=False,scale=True); parts.append(obj)
bpy.ops.object.select_all(action='DESELECT')
for obj in parts: obj.select_set(True)
bpy.context.view_layer.objects.active=parts[0]; bpy.ops.object.join(); sword=parts[0]; sword.name='Sword'
bpy.ops.object.transform_apply(location=True,rotation=True,scale=True)
mat=bpy.data.materials.new('Sword_Steel'); mat.diffuse_color=(.42,.51,.58,1); mat.use_nodes=True
mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value=(.42,.51,.58,1)
mat.node_tree.nodes['Principled BSDF'].inputs['Metallic'].default_value=.55
mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value=.4
sword.data.materials.append(mat); group=sword.vertex_groups.new(name='hand.R'); group.add(list(range(len(sword.data.vertices))),1,'REPLACE')
mod=sword.modifiers.new('Weapon_socket','ARMATURE'); mod.object=rig; sword.parent=rig; meshes.append(sword)
FPS=30; scene.render.fps=FPS
rest={b.name:b.matrix_local.to_quaternion() for b in armdata.bones}
manifest=[]
def orient_world(name,q):
    pb=rig.pose.bones[name]
    parent_delta=pb.parent.matrix.to_quaternion() @ pb.parent.bone.matrix_local.to_quaternion().inverted() if pb.parent else Quaternion()
    pb.rotation_quaternion=rest[name].inverted() @ parent_delta.inverted() @ q
    bpy.context.view_layer.update()
def solve_chain(upper,lower,target,pole):
    # Analytic two-bone IK, baked into ordinary editable FK actions on export.
    first=rig.pose.bones[upper]; second=rig.pose.bones[lower]
    start=first.head.copy(); target=Vector(target); axis=target-start
    l1=first.bone.length; l2=second.bone.length; length=max(.02,min(axis.length,l1+l2-.002)); axis.normalize()
    across=Vector(pole)-start; across-=axis*across.dot(axis)
    if across.length<.001: across=Vector((0,-1,0))
    across.normalize(); along=(l1*l1-l2*l2+length*length)/(2*length)
    elbow=start+axis*along+across*math.sqrt(max(0,l1*l1-along*along))
    for name,desired in [(upper,elbow-start),(lower,start+axis*length-elbow)]:
        h,t=defs[name]; orient_world(name,(t-h).rotation_difference(desired) @ rest[name])
def authored_ik(name,t,root_location):
    if name in ['t_pose','death_front','death_back','dead_front','dead_back','sit_down','sit_idle','stand_up']: return
    if name.startswith(('walk','run')):
        running=name.startswith('run'); travel=.74 if running else .56
        for side,offset in [('L',0),('R',.5)]:
            phase=(t+offset)%1; sg=1 if side=='L' else -1
            if phase<.5: forward=travel*(.5-phase*2); lift=0
            else: u=(phase-.5)*2; forward=travel*(-.5+u);lift=(.14 if running else .07)*math.sin(u*math.pi)
            solve_chain('thigh.'+side,'shin.'+side,(sg*.136,-forward,.105+lift),(sg*.16,-1,.45))
            orient_world('foot.'+side,rest['foot.'+side])
    if name in ['pickup','gather'] and 0<t<1:
        for side,sg in [('L',1),('R',-1)]:
            solve_chain('thigh.'+side,'shin.'+side,(sg*.136,0,.105),(sg*.18,-1,.5))
            orient_world('foot.'+side,rest['foot.'+side])
        solve_chain('upper_arm.R','forearm.R',(-.08,-.48,.25),(-.6,-.4,.7))
    gestures=name in ['wave','point','clap','talk','craft','interact'] or name.startswith('cast')
    active=(0<t<1) or name=='cast_hold'
    if gestures and active:
        if name=='wave': target=(-.49,-.04,1.84)
        elif name in ['point','interact']: target=(-.21,-.52,1.38)
        elif name=='clap': target=(-.03 if t in [.25,.62] else -.23,-.35,1.38)
        else: target=(-.20,-.34,1.35)
        solve_chain('upper_arm.R','forearm.R',target,(-.7,-.05,1.3))
        if name.startswith('cast') or name in ['clap','craft']:
            solve_chain('upper_arm.L','forearm.L',(-target[0],target[1],target[2]),(.7,-.05,1.3))
    combat=name in ['combat_idle','walk_sword','run_sword','basic_left','basic_right','thrust','sweep','rend','breaker','pommel','hamstring','riposte','whirl','finisher','block_hold','block_enter','block_exit','block_hit','parry','parry_success','dash']
    if not combat:return
    wrist=(-.31,-.29,1.20); blade=(0,-.22,1)
    stages={
        'basic_left':((-.58,.00,1.48),(.16,-.43,1.18),(0,-1,.05)),
        'basic_right':((.12,-.35,1.52),(-.55,-.25,1.20),(-.7,-.5,.1)),
        'thrust':((-.24,-.10,1.38),(-.22,-.52,1.38),(0,-1,0)),
        'sweep':((-.64,.00,1.28),(.23,-.36,1.28),(.8,-.5,0)),
        'rend':((-.39,-.03,1.77),(.12,-.40,1.10),(.5,-.8,-.3)),
        'breaker':((-.18,-.06,1.83),(-.18,-.46,1.05),(0,-.8,-.5)),
        'pommel':((-.25,-.10,1.36),(-.23,-.48,1.39),(0,.15,1)),
        'hamstring':((-.58,-.05,1.15),(.03,-.36,.98),(.7,-.5,-.2)),
        'riposte':((-.23,-.17,1.42),(-.15,-.51,1.34),(0,-1,.15)),
        'finisher':((-.42,-.02,1.78),(.09,-.40,1.02),(.35,-.7,-.6)),
    }
    if name in stages and 0<t<1:
        wind,contact,direction=stages[name]; wrist=wind if t<.3 else contact
        blade=(0,.20,1) if t<.3 else direction
    if name.startswith(('block','parry')):
        wrist=(-.20,-.34,1.38); blade=(.75,0,.75)
        if name.startswith('parry'):wrist=(-.42,-.28,1.52);blade=(-.5,-.2,.9)
    if name=='whirl' and 0<t<1:
        wrist=(-.64,-.15,1.30);blade=(-1,0,.08)
        rot=rig.pose.bones['root'].matrix.to_quaternion();wrist=rot@Vector(wrist);blade=rot@Vector(blade)
    wrist=Vector(wrist)+Vector(root_location)
    solve_chain('upper_arm.R','forearm.R',wrist,(-.42,.06,1.08))
    # Keep the palm aligned with the forearm while the sword points along the authored blade axis.
    # Mapping both axes prevents the sideways wrist twist caused by a single-vector rotation.
    blade=Vector(blade).normalized()
    forearm=rig.pose.bones['forearm.R']; forward=(forearm.tail-forearm.head).normalized()
    forward-=blade*forward.dot(blade)
    if forward.length<.01: forward=Vector((0,-1,0))-blade*Vector((0,-1,0)).dot(blade)
    x_axis=-forward.normalized(); y_axis=-blade; z_axis=x_axis.cross(y_axis).normalized()
    orient_world('hand.R',Matrix((x_axis,y_axis,z_axis)).transposed().to_quaternion() @ rest['hand.R'])
def idle(armed=False):
    d={'upper_arm.L':(0,76,-4),'upper_arm.R':(0,-76,4),'forearm.L':(0,0,-9),'forearm.R':(0,0,9)}
    if armed:
        d.update({'upper_arm.R':(12,-58,30),'forearm.R':(0,-16,53),'hand.R':(0,0,-12), 'upper_arm.L':(0,70,-12),'forearm.L':(0,0,-22),'chest':(0,0,-7)})
        for digit in ['index','middle','ring','little']:
            d[digit+'.R']=(0,-65,0); d[digit+'_tip.R']=(0,-65,0)
        d['thumb.R']=(25,-20,-25)
    return d
def pose(base,**kw):
    d=base.copy(); d.update({k.replace('__','.'):v for k,v in kw.items()}); return d
def action(name,seconds,keys,loop=False):
    rig.animation_data_clear(); scene.frame_start=1; scene.frame_end=round(seconds*FPS)+1
    for t,angles,location in keys:
        frame=1+t*seconds*FPS
        for pb in rig.pose.bones:
            pb.rotation_mode='QUATERNION'
            q=Euler(tuple(math.radians(v) for v in angles.get(pb.name,(0,0,0))),'XYZ').to_quaternion()
            pb.rotation_quaternion=rest[pb.name].inverted() @ q @ rest[pb.name]
            pb.location=(0,0,0)
            if pb.name=='root': pb.location=rest['root'].inverted() @ Vector(location)
        bpy.context.view_layer.update()
        authored_ik(name,t,location)
        for pb in rig.pose.bones:
            pb.keyframe_insert('rotation_quaternion',frame=frame,group=pb.name)
            pb.keyframe_insert('location',frame=frame,group=pb.name)
    ac=rig.animation_data.action; ac.name=name; ac.use_fake_user=True
    # Linear sampled keys avoid overshoot through feet/weapon grips and support normalized combat seeks.
    for layer in ac.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                for fc in bag.fcurves:
                    for key in fc.keyframe_points: key.interpolation='LINEAR'
    manifest.append({'name':name,'seconds':seconds,'loop':loop})
def keys_pose(d): return [(0,d,(0,0,0)),(1,d,(0,0,0))]
I=idle(); C=idle(True)
action('t_pose',1,keys_pose({}))
for name,base in [('idle',I),('combat_idle',C)]:
    action(name,3,[(0,base,(0,0,0)),(.5,pose(base,chest=(1.4,0,base.get('chest',(0,0,0))[2]),neck=(-1,0,0)),(0,0,.006)),(1,base,(0,0,0))],True)
action('idle_look',3,[(0,I,(0,0,0)),(.35,pose(I,head=(0,0,23)),(0,0,0)),(.7,pose(I,head=(0,0,-18)),(0,0,0)),(1,I,(0,0,0))])
for armed in [False,True]:
    for running,duration in [(False,1),(True,.65)]:
        base=C if armed else I; keys=[]
        for k in range(17):
            t=k/16; d=base.copy(); amplitude=37 if running else 25
            for side,phase in [('L',0),('R',math.pi)]:
                wave=math.sin(t*math.tau+phase); knee=max(0,-wave)*(66 if running else 34)
                d['thigh.'+side]=(-amplitude*wave,0,0); d['shin.'+side]=(knee,0,0)
                d['foot.'+side]=(amplitude*wave-knee*.7,0,0)
                if not armed or side=='L': d['upper_arm.'+side]=(wave*amplitude*.65,76 if side=='L' else -76,0)
            d['chest']=(8 if running else 3,0,math.sin(t*math.tau)*3)
            keys.append((t,d,(0,0,(.018 if running else .009)*(1-math.cos(t*math.tau*2)))))
        action(('run' if running else 'walk')+('_sword' if armed else ''),duration,keys,True)
for name,ang in [('turn_left',25),('turn_right',-25)]:
    action(name,.5,[(0,I,(0,0,0)),(.5,pose(I,pelvis=(0,0,ang),thigh__L=(-12,0,0),shin__L=(18,0,0)),(0,0,.012)),(1,I,(0,0,0))])
for name,base in [('move_start',pose(I,chest=(12,0,0))),('move_stop',pose(I,chest=(-7,0,0)))]:
    action(name,.3,[(0,I,(0,0,0)),(.5,base,(0,0,-.015)),(1,I,(0,0,0))])
# Shared contact marker .35 matches SwordAttackAnimation's confirmed impact phase.
attacks={
 'basic_left': [(-15,-48,55),(12,-40,-70),-24,28],
 'basic_right': [(8,-35,-45),(-8,-45,70),25,-28],
 'thrust': [(0,-65,60),(0,-10,88),-12,12],
 'sweep': [(0,-15,-45),(0,-30,115),-55,58],
 'rend': [(-35,-20,-20),(30,-62,85),-20,35],
 'breaker': [(-65,-10,-30),(45,-50,85),-12,10],
 'pommel': [(0,-68,70),(0,-42,110),-5,15],
 'hamstring': [(15,-70,-30),(35,-70,90),-35,35],
 'riposte': [(0,-50,45),(-8,-16,98),-15,20],
 'finisher': [(-55,-5,-40),(40,-65,95),-35,40],
}
for name,(start,end,twist0,twist1) in attacks.items():
    wind=pose(C,upper_arm__R=start,forearm__R=(0,-15,45),chest=(0,0,twist0))
    hit=pose(C,upper_arm__R=end,forearm__R=(0,0,5),chest=(18 if name in ['breaker','finisher','hamstring'] else 5,0,twist1))
    if name=='hamstring':
        wind.update({'thigh.L':(-22,0,0),'shin.L':(35,0,0),'thigh.R':(-22,0,0),'shin.R':(35,0,0)})
        hit.update({k:v for k,v in wind.items() if k.startswith(('thigh','shin'))})
    action(name,1,[(0,C,(0,0,0)),(.22,wind,(0,0,-.025)),(.35,hit,(0,0,-.035 if name=='hamstring' else 0)),(.62,hit,(0,0,0)),(1,C,(0,0,0))])
whirl=[]
for t,angle in [(0,0),(.20,-50),(.35,80),(.52,200),(.68,320),(.82,360),(1,360)]:
    d=pose(C,root=(0,0,angle),upper_arm__R=(0,-25,10),forearm__R=(0,0,15)) if t not in [0,1] else pose(C,root=(0,0,angle))
    whirl.append((t,d,(0,0,0)))
action('whirl',1,whirl)
action('breath',1,[(0,C,(0,0,0)),(.15,pose(I,chest=(12,0,0)),(0,0,-.012)),(.35,pose(I,chest=(-5,0,0)),(0,0,.015)),(.7,pose(I,chest=(8,0,0)),(0,0,0)),(1,C,(0,0,0))])
D=pose(C,chest=(28,0,0),thigh__L=(-35,0,0),shin__L=(55,0,0),thigh__R=(25,0,0),shin__R=(25,0,0))
action('dash',1,[(0,C,(0,0,0)),(.16,D,(0,0,-.06)),(.72,D,(0,0,-.06)),(1,C,(0,0,0))])
G=pose(C,upper_arm__R=(-15,-28,68),forearm__R=(0,-25,65),upper_arm__L=(0,45,-55),forearm__L=(0,0,-55),chest=(6,0,-12))
for name,start,end,duration in [('block_enter',C,G,.18),('block_exit',G,C,.18),('equip',I,C,.55),('unequip',C,I,.55)]:
    action(name,duration,[(0,start,(0,0,0)),(1,end,(0,0,0))])
action('block_hold',1,keys_pose(G),True)
for name,target in [('block_hit',pose(G,chest=(-12,0,-18))),('parry',pose(G,upper_arm__R=(-8,-30,105),chest=(0,0,22))),('parry_success',pose(G,upper_arm__R=(-8,-22,115),chest=(0,0,28)))]:
    action(name,.38,[(0,G,(0,0,0)),(.35,target,(0,0,-.01)),(1,C if name!='block_hit' else G,(0,0,0))])
cast=pose(I,upper_arm__L=(0,20,-65),forearm__L=(0,0,-40),upper_arm__R=(0,-20,65),forearm__R=(0,0,40))
for name,start,end,duration in [('cast_start',I,cast,.3),('cast_cancel',cast,I,.2),('cast_release',cast,pose(cast,forearm__R=(0,0,0),upper_arm__R=(0,0,90)),.4),('cast_self',cast,pose(cast,chest=(-6,0,0)),.5)]:
    action(name,duration,[(0,start,(0,0,0)),(.6,end,(0,0,0)),(1,I if 'release' in name or 'self' in name else end,(0,0,0))])
action('cast_hold',1,[(0,cast,(0,0,0)),(.5,pose(cast,chest=(-3,0,0)),(0,0,.005)),(1,cast,(0,0,0))],True)
for name,ang in [('front',(-15,0,0)),('back',(18,0,0)),('left',(0,-17,12)),('right',(0,17,-12))]:
    action('hit_'+name,.28,[(0,C,(0,0,0)),(.25,pose(C,chest=ang,head=ang),(0,0,-.014)),(1,C,(0,0,0))])
stun=pose(I,chest=(15,0,0),head=(12,0,0),upper_arm__L=(0,65,0),upper_arm__R=(0,-65,0))
action('stun_enter',.2,[(0,I,(0,0,0)),(1,stun,(0,0,0))])
action('stun_loop',1,[(0,stun,(0,0,0)),(.5,pose(stun,chest=(15,5,0),head=(8,-5,0)),(0,0,0)),(1,stun,(0,0,0))],True)
action('stun_exit',.2,[(0,stun,(0,0,0)),(1,I,(0,0,0))])
for name,sg in [('front',1),('back',-1)]:
    fall=pose(I,pelvis=(sg*88,0,0),chest=(sg*3,0,0),upper_arm__L=(0,40,15),upper_arm__R=(0,-40,-15),shin__L=(15,0,0),shin__R=(8,0,0))
    action('death_'+name,1.1,[(0,I,(0,0,0)),(.25,pose(I,chest=(sg*28,0,0),thigh__L=(-22,0,0),shin__L=(30,0,0)),(0,0,-.15)),(.8,fall,(0,0,-.79)),(1,fall,(0,0,-.80))])
    action('dead_'+name,1,[(0,fall,(0,0,-.80)),(1,fall,(0,0,-.80))],True)
reach=pose(I,upper_arm__R=(0,-12,88),forearm__R=(0,0,12))
pickup=pose(reach,chest=(52,0,0),thigh__L=(-28,0,0),shin__L=(40,0,0),thigh__R=(-28,0,0),shin__R=(40,0,0))
for name,target,duration in [('interact',reach,.8),('pickup',pickup,1),('talk',pose(reach,forearm__R=(0,0,50)),1.5),('gather',pickup,1.3),('craft',pose(reach,upper_arm__L=(0,35,-65),forearm__L=(0,0,-40)),1.0)]:
    action(name,duration,[(0,I,(0,0,0)),(.35,target,(0,0,-.24 if name in ['pickup','gather'] else 0)),(.65,pose(target,hand__R=(12,0,0)),(0,0,-.24 if name in ['pickup','gather'] else 0)),(1,I,(0,0,0))],name in ['gather','craft','talk'])
wave=pose(I,upper_arm__R=(0,32,20),forearm__R=(0,55,0),hand__R=(0,0,15))
action('wave',2,[(0,I,(0,0,0)),(.2,wave,(0,0,0)),(.35,pose(wave,hand__R=(0,0,-22)),(0,0,0)),(.5,wave,(0,0,0)),(.65,pose(wave,hand__R=(0,0,-22)),(0,0,0)),(.8,wave,(0,0,0)),(1,I,(0,0,0))])
for name,target in [('nod',pose(I,head=(20,0,0))),('no',pose(I,head=(0,0,25))),('bow',pose(I,chest=(40,0,0),head=(10,0,0))),('point',reach),('cheer',pose(I,upper_arm__L=(0,-50,0),upper_arm__R=(0,50,0),forearm__L=(0,-25,0),forearm__R=(0,25,0))),('clap',pose(cast,hand__L=(0,0,25),hand__R=(0,0,-25)))]:
    action(name,1.8,[(0,I,(0,0,0)),(.25,target,(0,0,0)),(.42,I if name in ['nod','no','clap'] else target,(0,0,0)),(.62,target,(0,0,0)),(1,I,(0,0,0))])
sit=pose(I,thigh__L=(-90,0,10),thigh__R=(-90,0,-10),shin__L=(8,0,0),shin__R=(8,0,0),foot__L=(82,0,0),foot__R=(82,0,0),chest=(8,0,0),forearm__L=(0,0,-12),forearm__R=(0,0,12))
action('sit_down',.8,[(0,I,(0,0,0)),(1,sit,(0,0,-.74))])
action('sit_idle',2,[(0,sit,(0,0,-.74)),(1,sit,(0,0,-.74))],True)
action('stand_up',.8,[(0,sit,(0,0,-.74)),(1,I,(0,0,0))])
rig.animation_data_clear()
rig.animation_data_create()
rig.animation_data.action=bpy.data.actions['idle']
rig.animation_data.action_slot=bpy.data.actions['idle'].slots[0]
for pb in rig.pose.bones: pb.rotation_quaternion=Quaternion(); pb.location=(0,0,0)
scene.frame_set(1)
rig['rest_pose']='T'; rig['animation_contact_normalized']=.35
for o in meshes: o.select_set(True)
rig.select_set(True); bpy.context.view_layer.objects.active=rig
bpy.ops.wm.save_as_mainfile(filepath=str(out/'male-animated.blend'))
bpy.ops.export_scene.gltf(filepath=str(out/'male-animated.glb'),export_format='GLB',use_selection=True,
    export_animations=True,export_animation_mode='ACTIONS',export_force_sampling=True,export_anim_single_armature=True,
    export_materials='EXPORT',export_all_influences=False,export_influence_nb=4)
# Save the artist-facing source with a clean T rest pose and only one head/hair visible.
rig.animation_data.action=None
for pb in rig.pose.bones: pb.rotation_quaternion=Quaternion(); pb.location=(0,0,0)
for obj in meshes:
    hidden=obj.name.startswith(('Face_02','Face_03','Hair_02','Hair_03'))
    obj.hide_set(hidden); obj.hide_render=hidden
bpy.ops.wm.save_as_mainfile(filepath=str(out/'male-animated.blend'))
(out/'animations.json').write_text(json.dumps({'fps':FPS,'bones':len(defs),'clips':manifest},indent=2))
print('ANIMATION_LIBRARY_OK',len(manifest),'clips',len(defs),'bones')
