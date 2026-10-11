# Connection map (metres, before geometry creation):
# pelvis Z .78..1.10 overlaps torso Z .915..1.465 by .185.
# torso/neck/head overlap >= .02; head top is 2.00, feet bottom .00.
# shoulder ellipsoids cross their pivot by .07; elbows/wrists overlap >= .025.
# hip ellipsoids cross their pivot by .07; knees/ankles overlap >= .025.
# sword grip crosses the wrist by .04, guard/blade overlap .01.
# All mesh scales are applied; named empty nodes deliberately retain hinge origins.
import bpy
import math
from pathlib import Path
from mathutils import Vector, Quaternion

ROOT = Path('D:/projects/project-g')
SOURCE = ROOT / 'art-source/characters/primitive-actors'
ASSETS = ROOT / 'Content.Client/Assets/Characters/Primitives'
SOURCE.mkdir(parents=True, exist_ok=True)
ASSETS.mkdir(parents=True, exist_ok=True)
scene = bpy.data.scenes.new('PrimitiveFigureLibrary')
bpy.context.window.scene = scene
scene.unit_settings.system = 'METRIC'
scene.render.fps = 24
collection = scene.collection

def material(name, color, metal=0):
    m = bpy.data.materials.new(name)
    m.diffuse_color = (*color, 1)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get('Principled BSDF')
    bsdf.inputs['Base Color'].default_value = (*color, 1)
    bsdf.inputs['Roughness'].default_value = .82
    bsdf.inputs['Metallic'].default_value = metal
    return m

cloth = material('FigureCloth', (.16, .35, .52))
skin = material('FigureSkin', (.85, .74, .55))
dark = material('FigureBoots', (.08, .13, .20))
eyes = material('FigureDirection', (.025, .07, .12))
steel = material('FigureBlade', (.68, .79, .88), .25)
brass = material('FigureGuard', (.66, .45, .18), .1)

def empty(name, parent, position):
    o = bpy.data.objects.new(name, None)
    collection.objects.link(o)
    o.parent = parent
    o.location = position
    o.rotation_mode = 'QUATERNION'
    return o

def ellipsoid(name, parent, center, radii, mat):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16, ring_count=8, radius=1)
    o = bpy.context.object
    o.name = name
    o.scale = radii
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    o.parent = parent
    o.location = center
    o.data.materials.append(mat)
    for p in o.data.polygons: p.use_smooth = True
    return o

def box(name, parent, center, half, mat, bevel=.025):
    bpy.ops.mesh.primitive_cube_add(size=2)
    o = bpy.context.object
    o.name = name
    o.scale = half
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    if bevel:
        mod = o.modifiers.new('RoundedEdges', 'BEVEL')
        mod.width = bevel
        mod.segments = 2
        bpy.ops.object.modifier_apply(modifier=mod.name)
    o.parent = parent
    o.location = center
    o.data.materials.append(mat)
    return o

rig = empty('Figure', None, (0, 0, 0))
pose = empty('Pose', rig, (0, 0, .94))
torso = empty('Torso', pose, (0, 0, 0))
ellipsoid('Pelvis', pose, (0, 0, 0), (.25, .16, .16), cloth)
box('Body', torso, (0, 0, .25), (.27, .17, .275), cloth, .11)
ellipsoid('Neck', torso, (0, 0, .60), (.09, .09, .10), skin)
head = empty('Head', torso, (0, 0, .66))
ellipsoid('Face', head, (0, 0, .16), (.22, .21, .24), skin)
# +Z in Godot is forward; Blender -Y converts to +Z in glTF.
box('Eyes', head, (0, -.205, .19), (.105, .015, .026), eyes, .012)
ellipsoid('Nose', head, (0, -.218, .12), (.042, .035, .045), skin)
joints = {'Pose':pose, 'Torso':torso, 'Head':head}
for suffix, sign in [('L', 1), ('R', -1)]:
    arm = empty('Arm'+suffix, torso, (sign*.35, 0, .46))
    ellipsoid('UpperArm'+suffix, arm, (0, 0, -.15), (.105, .105, .22), cloth)
    forearm = empty('Forearm'+suffix, arm, (0, 0, -.34))
    ellipsoid('LowerArm'+suffix, forearm, (0, 0, -.13), (.082, .085, .195), cloth)
    hand = empty('Hand'+suffix, forearm, (0, 0, -.30))
    ellipsoid('Palm'+suffix, hand, (0, 0, -.025), (.082, .078, .105), skin)
    leg = empty('Leg'+suffix, pose, (sign*.15, 0, -.015))
    ellipsoid('Thigh'+suffix, leg, (0, 0, -.19), (.13, .13, .26), cloth)
    shin = empty('Shin'+suffix, leg, (0, 0, -.43))
    ellipsoid('Calf'+suffix, shin, (0, 0, -.16), (.105, .11, .22), dark)
    box('Foot'+suffix, shin, (0, -.065, -.365), (.115, .18, .13), dark, .065)
    joints.update({'Arm'+suffix:arm, 'Forearm'+suffix:forearm, 'Hand'+suffix:hand,
                   'Leg'+suffix:leg, 'Shin'+suffix:shin})
weapon = empty('Weapon', joints['HandR'], (0, -.02, -.01))
box('Grip', weapon, (0, -.06, 0), (.035, .09, .035), dark, .018)
box('Guard', weapon, (0, -.145, 0), (.15, .025, .035), brass, .015)
box('Blade', weapon, (0, -.47, 0), (.035, .325, .016), steel, .012)
empty('BladeBase', weapon, (0, -.16, 0))
empty('BladeTip', weapon, (0, -.795, 0))

clips = {
 'idle':2, 'combat_idle':2, 'idle_look':3, 'walk':1, 'walk_sword':1, 'run':.65, 'run_sword':.65,
 'dash':1, 'basic_left':1, 'basic_right':1, 'thrust':1, 'sweep':1, 'rend':1, 'breaker':1,
 'pommel':1, 'hamstring':1, 'riposte':1, 'whirl':1, 'breath':1, 'finisher':1,
 'cast_start':.3, 'cast_hold':1, 'cast_release':1, 'cast_self':1,
 'block_enter':.22, 'block_hold':1, 'block_exit':.22, 'block_hit':.3,
 'parry':.5, 'parry_success':.45, 'hit_front':.35, 'hit_back':.35, 'hit_left':.35, 'hit_right':.35,
 'stun_loop':1, 'airborne':1, 'knockdown':.35, 'down_hold':1, 'recover':.6,
 'death_back':1.1, 'dead_back':1, 'death_front':1.1, 'dead_front':1,
 'wave':2, 'nod':1, 'no':1, 'bow':1.5, 'point':1.4, 'cheer':2, 'clap':2,
 'sit_down':.8, 'sit_idle':2, 'stand_up':.7, 'talk':2, 'pickup':1, 'gather':1.5,
 'craft':1.5, 'interact':1, 'equip':.55, 'unequip':.55, 'npc_windup':1, 'npc_strike':1,
 'boss_windup':1, 'boss_slam':1, 'dummy_hit':.35,
}

def sample(name, t):
    r = {j:(0,0,0) for j in joints}
    dz = 0
    pulse = math.sin(math.pi*t)
    cycle = math.sin(t*math.tau)
    def put(j,x=0,y=0,z=0): r[j]=(math.radians(x),math.radians(y),math.radians(z))
    put('ArmL', z=-5); put('ArmR', z=5)
    if name.startswith(('walk','run')):
        amp=38 if name.startswith('run') else 25
        put('LegL',amp*cycle); put('LegR',-amp*cycle)
        put('ShinL',max(0,-cycle)*48);put('ShinR',max(0,cycle)*48)
        put('ArmL',-amp*.55*cycle,z=-5);put('ArmR',amp*.55*cycle,z=5)
        if name.endswith('sword'):put('ForearmR',-35)
        put('Torso',-6 if name.startswith('run') else -2)
        dz=abs(cycle)*.035
    elif name in ('idle','combat_idle','idle_look','breath'):
        put('Torso',2*cycle);put('Head',-cycle,18*cycle if name=='idle_look' else 0)
        if name=='combat_idle':put('ArmR',-20,z=8);put('ForearmR',-50);put('ArmL',-10,z=-10)
        if name=='breath':put('Head',-8*pulse);put('ArmL',-20*pulse);put('ArmR',-20*pulse)
    elif name in ('basic_left','basic_right','thrust','sweep','rend','breaker','pommel','hamstring','riposte','whirl','finisher','npc_strike','boss_slam','npc_windup','boss_windup'):
        side=-1 if name in ('basic_left','sweep','hamstring') else 1
        # Contact is .35 of the visual timeline; preparation never invents a hit.
        prepare=min(t/.28,1) if t<.35 else max(0,1-(t-.35)/.65)
        swing=max(0,math.sin((t-.24)/.5*math.pi)) if .24<t<.74 else 0
        if name.endswith('windup'):prepare=t;swing=0
        put('Torso',(-10 if name=='hamstring' else 12)*prepare,side*(32*prepare-60*swing))
        put('ArmR',-85*prepare,0,side*(40*prepare-75*swing));put('ForearmR',-35*prepare+25*swing)
        put('ArmL',-20*prepare,0,-18*prepare);put('Head',-8*prepare)
        if name in ('breaker','finisher','boss_slam','boss_windup'):put('ArmR',-155*prepare);put('ArmL',-145*prepare);put('Torso',-18*prepare+48*swing)
        if name in ('thrust','riposte','pommel'):put('ArmR',-65*prepare-35*swing,z=8);put('ForearmR',-65*prepare+60*swing)
        if name=='whirl':put('Pose',y=360*t);put('ArmR',-65*pulse,z=55*pulse)
        if name=='hamstring':dz=-.16*prepare;put('LegL',-25*prepare);put('ShinL',40*prepare)
    elif name.startswith('cast'):
        weight=min(1,t*3) if name=='cast_start' else 1 if name=='cast_hold' else pulse
        put('ArmL',-70*weight,z=-30*weight);put('ArmR',-70*weight,z=30*weight);put('ForearmL',-25*weight);put('ForearmR',-25*weight);put('Head',-8*weight)
    elif name.startswith(('block','parry')):
        weight=t if name=='block_enter' else 1-t if name=='block_exit' else 1
        put('ArmR',-70*weight,z=-30*weight);put('ForearmR',-65*weight);put('ArmL',-60*weight,z=35*weight);put('ForearmL',-45*weight);put('Torso',-8*weight,15*weight)
        if name.startswith('parry'):put('ArmR',-70+30*cycle,z=-30+40*cycle)
        if name=='block_hit':put('Torso',-8+15*pulse,15)
    elif name.startswith('hit') or name=='dummy_hit':
        put('Torso',(-20 if name=='hit_back' else 20)*pulse,0,(-15 if name=='hit_left' else 15 if name=='hit_right' else 0)*pulse)
    elif name=='stun_loop':put('Head',15,10*cycle,8*cycle);put('Torso',8,0,3*cycle)
    elif name=='dash':put('Torso',-35*pulse);put('ArmL',25*pulse);put('ArmR',25*pulse);put('LegL',-25*pulse);put('LegR',25*pulse)
    elif name=='airborne':put('Torso',-18);put('ArmL',-30,z=-45);put('ArmR',-30,z=45);put('LegL',-25);put('ShinL',50);put('LegR',-12);put('ShinR',30)
    elif name in ('knockdown','down_hold','recover','death_back','dead_back','death_front','dead_front'):
        weight=t if name in ('knockdown','death_back','death_front') else 1-t if name=='recover' else 1
        put('Pose',(-90 if 'front' in name else 90)*weight);dz=-.74*weight
        put('ArmL',-10*weight,z=-30*weight);put('ArmR',-10*weight,z=30*weight);put('ShinL',18*weight)
    elif name in ('sit_down','sit_idle','stand_up'):
        weight=t if name=='sit_down' else 1-t if name=='stand_up' else 1
        dz=-.47*weight;put('LegL',-85*weight);put('LegR',-85*weight);put('ShinL',85*weight);put('ShinR',85*weight);put('ArmL',-25*weight);put('ArmR',-25*weight)
    elif name=='wave':put('ArmL',-15,z=-150*pulse);put('ForearmL',35*math.sin(t*math.tau*3)*pulse)
    elif name=='nod':put('Head',20*math.sin(t*math.tau*2)*pulse)
    elif name=='no':put('Head',30*math.sin(t*math.tau*2)*pulse)
    elif name=='bow':put('Torso',45*pulse);put('Head',10*pulse)
    elif name=='point':put('ArmL',-85*pulse);put('Head',y=15*pulse)
    elif name=='cheer':put('ArmL',-10,z=-160*pulse);put('ArmR',-10,z=160*pulse);dz=.06*abs(cycle)
    elif name=='clap':put('ArmL',-70*pulse,z=18*pulse);put('ArmR',-70*pulse,z=-18*pulse);put('ForearmL',-30-12*cycle);put('ForearmR',-30-12*cycle)
    elif name in ('pickup','gather'):put('Torso',45*pulse);put('ArmR',-60*pulse);put('LegL',-25*pulse);put('ShinL',50*pulse);dz=-.10*pulse
    elif name in ('talk','interact','craft'):put('ArmL',-30*pulse,z=-10*pulse);put('ArmR',-45*pulse-10*cycle);put('ForearmR',-20*pulse);put('Head',5*cycle)
    elif name in ('equip','unequip'):put('ArmR',-25*pulse,z=-35*pulse);put('ForearmR',-75*pulse);put('Torso',y=10*pulse)
    return r,dz

# NLA tracks with the same name on each rigid part are merged by the glTF exporter.
# No armature, skinning, bone names or root-motion tracks are involved.
rest = {name:o.location.copy() for name,o in joints.items()}
for name,duration in clips.items():
    frames=max(2,round(duration*24))
    for joint,o in joints.items():
        o.animation_data_create()
        action=bpy.data.actions.new(name+'_'+joint)
        o.animation_data.action=action
        for frame in range(frames+1):
            rotations,dz=sample(name,frame/frames)
            x,y,z=rotations[joint]
            # Convert desired Godot axes to Blender axes (X,Z,-Y).
            o.rotation_quaternion=Quaternion((0,0,1),y) @ Quaternion((0,1,0),-z) @ Quaternion((1,0,0),x)
            o.keyframe_insert('rotation_quaternion',frame=frame)
            if joint=='Pose':
                o.location=rest[joint]+Vector((0,0,dz))
                o.keyframe_insert('location',frame=frame)
        track=o.animation_data.nla_tracks.new()
        track.name=name
        strip=track.strips.new(name,0,action)
        strip.extrapolation='NOTHING'
        strip.blend_type='REPLACE'
        o.animation_data.action=None
        o.rotation_quaternion=Quaternion()
        o.location=rest[joint]

scene.frame_set(0)
for o in scene.objects:o.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.context.view_layer.update()
mesh_objects=[o for o in scene.objects if o.type=='MESH']
assert all(max(abs(s-1) for s in o.scale)<1e-5 for o in mesh_objects)
# Bounds are checked on the authored rest pose, without evaluating stacked NLA strips.
for o in joints.values():
    for track in o.animation_data.nla_tracks:track.mute=True
bpy.context.view_layer.update()
bounds=[o.matrix_world@Vector(c) for o in mesh_objects for c in o.bound_box]
limits={axis:(min(v[i] for v in bounds),max(v[i] for v in bounds)) for i,axis in enumerate('XYZ')}
assert abs(limits['Z'][0])<.02 and 1.98<limits['Z'][1]<2.02,limits
for o in joints.values():
    for track in o.animation_data.nla_tracks:track.mute=False
bpy.ops.export_scene.gltf(filepath=str(ASSETS/'primitive_figure.glb'),export_format='GLB',use_selection=True,use_active_scene=True,
    export_animations=True,export_animation_mode='NLA_TRACKS',export_force_sampling=True,export_frame_range=False,
    export_optimize_animation_size=False,export_optimize_animation_keep_anim_object=True)
for o in joints.values():
    for track in o.animation_data.nla_tracks:track.mute=True
    o.rotation_quaternion=Quaternion();o.location=rest[next(k for k,v in joints.items() if v==o)]
scene.frame_set(0)
bpy.ops.wm.save_as_mainfile(filepath=str(SOURCE/'primitive_figure.blend'))
result={'source':str(SOURCE/'primitive_figure.blend'),'glb':str(ASSETS/'primitive_figure.glb'),
        'mesh_count':len(mesh_objects),'triangles':sum(len(o.data.polygons)*2 for o in mesh_objects),
        'clips':len(clips),'rest_bounds':limits,'skeletons':0}
