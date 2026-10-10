"""Create original Project G modular character meshes; run with Blender 4.5+.

The delivered .blend is the editable art source. This generator writes only to
an explicit staging directory and refuses to overwrite it without --overwrite.
No game logic, animation controller, third-party model, or external texture.
"""
import argparse
import json
import math
from pathlib import Path
import sys

import bpy
import bmesh
from mathutils import Vector
from mathutils.bvhtree import BVHTree


parser = argparse.ArgumentParser()
parser.add_argument('--out', required=True)
parser.add_argument('--overwrite', action='store_true')
args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
OUT = Path(args.out).resolve()
if (OUT / 'male-base.blend').exists() and not args.overwrite:
    raise RuntimeError('Refusing to overwrite an editable source; use a new staging directory.')
for folder in ('exports', 'renders'):
    (OUT / folder).mkdir(parents=True, exist_ok=True)

bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
for data in list(bpy.data.collections):
    if data.name != 'Collection':
        bpy.data.collections.remove(data)
scene = bpy.context.scene
scene.unit_settings.system = 'METRIC'
scene.unit_settings.scale_length = 1.0

# One opaque vertex-color material across all modules limits material surfaces.
palette = bpy.data.materials.new('Character_Palette')
palette.use_nodes = True
shader = palette.node_tree.nodes.get('Principled BSDF')
shader.inputs['Roughness'].default_value = 0.83
shader.inputs['Specular IOR Level'].default_value = 0.23
vcol = palette.node_tree.nodes.new('ShaderNodeVertexColor')
vcol.layer_name = 'Color'
palette.node_tree.links.new(vcol.outputs['Color'], shader.inputs['Base Color'])

SKIN = (0.50, 0.245, 0.155, 1)
LIP = (0.38, 0.145, 0.102, 1)
SHADOW = (0.20, 0.074, 0.044, 1)
HAIR = (0.073, 0.033, 0.020, 1)
HAIR_LIT = (0.115, 0.054, 0.030, 1)
HAIR_DARK = (0.045, 0.019, 0.012, 1)
EYE = (0.89, 0.85, 0.71, 1)
IRIS = (0.10, 0.19, 0.17, 1)
PUPIL = (0.010, 0.017, 0.015, 1)
CLOTH = (0.045, 0.105, 0.135, 1)
TRIM = (0.18, 0.255, 0.255, 1)


def paint(obj, color, facet=0.0):
    obj.data.materials.clear()
    obj.data.materials.append(palette)
    old = obj.data.color_attributes.get('Color')
    if old:
        obj.data.color_attributes.remove(old)
    attr = obj.data.color_attributes.new(name='Color', type='FLOAT_COLOR', domain='CORNER')
    for face in obj.data.polygons:
        # Subtle deterministic facet tint, independent of lighting and topology order.
        multiplier = 1.0 + facet * math.sin(face.index * 12.9898)
        value = tuple(min(1, max(0, c * multiplier)) for c in color[:3]) + (1,)
        for index in face.loop_indices:
            attr.data[index].color = value
    return obj


def soft_shading(obj):
    # Low polygon count controls the silhouette; smooth normals keep skin readable.
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    return obj


def bell(value, center, radius):
    return math.exp(-((value-center)/radius)**2)


def painted_skin(obj, region):
    """Broad anatomical color fields, interpolated across vertices without facet noise."""
    attr = obj.data.color_attributes['Color']
    for loop in obj.data.loops:
        p = obj.matrix_world @ obj.data.vertices[loop.vertex_index].co
        x, y, z = p
        front = min(1.0, max(0.0, -y/.055))
        warmth = 0.0
        if region == 'body':
            # Chest, collar and abdomen remain large, quiet painted shapes.
            shade = .08*bell(abs(x), .14, .045)*bell(z, 1.16, .16)*front
            shade += .20*bell(z, 1.315-.11*abs(x), .022)*bell(abs(x), .10, .085)*front
            shade += .10*bell(x, 0, .022)*bell(z, 1.365, .077)*front
            shade += .14*bell(z, 1.438-.12*abs(x), .013)*bell(abs(x), .085, .068)*front
            shade += .09*bell(x, 0, .020)*bell(z, 1.122, .020)*front
            shade += .10*bell(abs(x), .135, .065)*bell(z, .50, .053)*front
            shade += .12*bell(abs(x), .47, .047)*bell(z, 1.407, .07)
            shade += .14*bell(z, 1.52, .060)*bell(x, 0, .08)
            shade += .09*bell(x, 0, .035)*bell(z, 1.27, .19)*max(0, y/.12)
            highlight = .065*bell(abs(x), .10, .07)*bell(z, 1.385, .05)*front
            highlight += .045*bell(x, 0, .10)*bell(z, 1.22, .09)*front
            warmth = .035*bell(abs(x), .85, .12)+.035*bell(z, .51, .055)
        else:
            shade = .18*bell(z, 1.538, .042)
            shade += .11*bell(z, 1.618, .022)*bell(abs(x), .077, .028)*front
            shade += .15*bell(z, 1.674, .021)*bell(abs(x), .043, .031)*front
            shade += .06*bell(z, 1.574, .017)*bell(x, 0, .045)*front
            highlight = .08*bell(z, 1.70, .044)*bell(x, 0, .053)*front
            warmth = .07*bell(z, 1.635, .039)*bell(abs(x), .075, .036)*front
        value = 1-shade+highlight
        attr.data[loop.index].color = (SKIN[0]*value*(1+warmth),
            SKIN[1]*value*(1-warmth*.32), SKIN[2]*value*(1-warmth*.24), 1)


def mesh(name, vertices, faces, color=SKIN):
    data = bpy.data.meshes.new(name)
    data.from_pydata(vertices, [], faces)
    data.update()
    obj = bpy.data.objects.new(name, data)
    scene.collection.objects.link(obj)
    paint(obj, color)
    # Recalculate outward normals after constructing rings, mirrored sides, and caps.
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.ops.object.mode_set(mode='EDIT')
    bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.mesh.normals_make_consistent(inside=False)
    bpy.ops.object.mode_set(mode='OBJECT')
    return obj


def ellipsoid(name, center, scale, color=SKIN, segments=16, rings=10):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, location=center)
    obj = bpy.context.object
    obj.name = name
    obj.scale = scale
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    return paint(obj, color)


def ring_mesh(name, rings, segments=16, color=SKIN, axis='Z'):
    vertices = []
    for position, radius_a, radius_b, offset_a, offset_b in rings:
        for i in range(segments):
            angle = 2 * math.pi * i / segments
            a = offset_a + radius_a * math.sin(angle)
            b = offset_b - radius_b * math.cos(angle)
            vertices.append((a, b, position) if axis == 'Z' else (position, a, b))
    faces = [tuple(reversed(range(segments)))]
    for j in range(len(rings) - 1):
        for i in range(segments):
            a, b = j * segments + i, j * segments + (i + 1) % segments
            faces.append((a, b, b + segments, a + segments))
    faces.append(tuple((len(rings) - 1) * segments + i for i in range(segments)))
    return mesh(name, vertices, faces, color)


def join(objects, name):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active = objects[0]
    bpy.ops.object.join()
    obj = bpy.context.object
    obj.name = name
    scene.cursor.location = (0, 0, 0)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    return obj


def lock(name, points, width, depth, color=HAIR):
    vertices = []
    for j, position in enumerate(points):
        p = Vector(position)
        tangent = Vector(points[min(j + 1, len(points) - 1)]) - Vector(points[max(0, j - 1)])
        tangent.normalize()
        reference = (p - Vector((0, .010, 1.655))).normalized() if color != SKIN else Vector((0, 1, 0))
        side = tangent.cross(reference)
        if side.length < 0.05:
            side = tangent.cross(Vector((1, 0, 0)))
        side.normalize()
        normal = side.cross(tangent).normalized()
        taper = (0.65, 1.0, 0.78, 0.05)[j] if len(points) == 4 else 1 - 0.9 * j / (len(points) - 1)
        for k in range(6):
            angle = 2 * math.pi * k / 6
            v = p + side * math.cos(angle) * width * taper + normal * math.sin(angle) * depth * taper
            vertices.append(tuple(v))
    faces = [tuple(reversed(range(6)))]
    for j in range(len(points) - 1):
        for k in range(6):
            a, b = j * 6 + k, j * 6 + (k + 1) % 6
            faces.append((a, b, b + 6, a + 6))
    faces.append(tuple((len(points) - 1) * 6 + k for k in range(6)))
    return mesh(name, vertices, faces, color)


def foot(side):
    # Sectioned sole, raised instep, narrow heel, and wider metatarsal pad.
    # The medial arch is lifted independently from the outside weight-bearing edge.
    sections = [
        (.067,.025,.066,.010), (.047,.040,.087,.005),
        (.012,.044,.125,.005), (-.024,.044,.128,.012),
        (-.067,.043,.110,.023), (-.110,.052,.080,.012),
        (-.147,.061,.063,.005), (-.174,.062,.051,.005),
        (-.189,.054,.037,.006),
    ]
    contour=[(0,1),(.66,.95),(.95,.72),(1,.34),(.72,0),
             (0,0),(-.72,0),(-1,.34),(-.95,.72),(-.66,.95)]
    vertices=[]
    for y,width,top,sole in sections:
        for horizontal,height in contour:
            arch=.012*bell(y,-.064,.041)*max(0,-horizontal)*max(0,1-height*3)
            vertices.append((side*(.136+horizontal*width),y,
                             sole+(top-sole)*height+arch))
    faces=[tuple(reversed(range(10)))]
    for j in range(len(sections)-1):
        for k in range(10):
            a=j*10+k; b=j*10+(k+1)%10
            faces.append((a,b,b+10,a+10))
    faces.append(tuple((len(sections)-1)*10+k for k in range(10)))
    result=[mesh('Foot_arch_heel_ball',vertices,faces)]
    # Big toe is medial, with progressively shorter outer toes.
    for offset,tip,radius,height in [(-.044,-.262,.018,.021),
            (-.014,-.259,.013,.017),(.011,-.245,.012,.016),
            (.034,-.230,.0105,.014),(.054,-.211,.0095,.012)]:
        base=-.164
        result.append(ellipsoid('Toe',(side*(.136+offset),(base+tip)/2,.006+height),
            (radius,(base-tip)/2,height),SKIN,10,8))
    for offset,z in [(-.031,.126),(.033,.112)]:
        result.append(ellipsoid('Ankle_bone',(side*(.136+offset),.010,z),
            (.013,.019,.022),SKIN,10,6))
    for obj in result:
        # Keep an adult foot length near 28 cm instead of an oversized shoe silhouette.
        for vertex in obj.data.vertices:
            world=obj.matrix_world @ vertex.co
            world.y *= .85
            world.x=side*.136+(world.x-side*.136)*.92
            vertex.co=obj.matrix_world.inverted() @ world
    return result


# Continuous body: volume union removes intersecting shoulder/hip primitive seams.
parts = [ring_mesh('Torso', [
    (.86, .134, .092, 0, .015), (.95, .156, .103, 0, .012),
    (1.06, .158, .098, 0, .007), (1.16, .157, .097, 0, .004),
    (1.28, .185, .101, 0, .004), (1.38, .222, .112, 0, .006),
    (1.44, .207, .094, 0, .009), (1.48, .102, .071, 0, .010),
], 20)]
parts.append(ellipsoid('Pelvis', (0, .015, .92), (.153, .102, .11)))
parts.append(ring_mesh('Neck', [(1.42, .065, .058, 0, .012), (1.51, .057, .056, 0, .012), (1.565, .060, .061, 0, .007)], 16))
for side in (-1, 1):
    parts.append(ellipsoid('Pectoral', (side * .090, -.072, 1.373), (.100, .047, .068)))
    parts.append(ellipsoid('Deltoid', (side * .223, 0, 1.414), (.090, .083, .080)))
    arm = [(.21, .078, .077), (.29, .072, .071), (.37, .068, .065),
           (.46, .047, .048), (.49, .045, .048), (.55, .056, .054),
           (.64, .042, .042), (.72, .029, .030), (.765, .026, .027)]
    parts.append(ring_mesh('Arm', [(side*x, y, z, 0, 1.407) for x,y,z in arm], 16, axis='X'))
    parts.append(ellipsoid('Palm', (side*.800, -.002, 1.407), (.065, .047, .025)))
    # Four individually separated fingers and a forward-opposed thumb.
    for j, (y, length) in enumerate([(-.034,.107), (-.011,.119), (.013,.107), (.035,.087)]):
        x0 = .830
        points = [(side*x0,y,1.407), (side*(x0+.035),y,1.407),
                  (side*(x0+length-.023),y,1.405), (side*(x0+length),y,1.404)]
        parts.append(lock('Finger', points, .0102, .0117, SKIN))
    parts.append(lock('Thumb', [(side*.778,-.029,1.403),(side*.800,-.065,1.399),
                               (side*.834,-.083,1.394),(side*.852,-.090,1.393)], .018,.019,SKIN))
    parts.append(ring_mesh('Leg', [
        (.99,.083,.091,side*.079,.014), (.87,.087,.094,side*.094,.011),
        (.75,.090,.093,side*.110,.014), (.63,.074,.075,side*.120,.006),
        (.52,.052,.056,side*.124,-.013), (.47,.051,.055,side*.127,-.010),
        (.38,.062,.068,side*.129,.014), (.29,.052,.059,side*.131,.023),
        (.19,.037,.043,side*.134,.018), (.10,.034,.039,side*.136,.002),
    ], 16))
    parts.extend(foot(side))
body = join(parts, 'Body_Male_TPose')
for vertex in body.data.vertices:
    # Readable glove/weapon attachment silhouette at the distant MMO camera.
    if abs(vertex.co.x) > .760:
        sign = 1 if vertex.co.x > 0 else -1
        vertex.co.x = sign*(.760+(abs(vertex.co.x)-.760)*1.18)
        vertex.co.y *= 1.25
        vertex.co.z = 1.407+(vertex.co.z-1.407)*1.25
remesh = body.modifiers.new('Continuous_anatomy', 'REMESH')
remesh.mode = 'VOXEL'
remesh.voxel_size = .006
remesh.use_smooth_shade = False
bpy.ops.object.modifier_apply(modifier=remesh.name)
smooth = body.modifiers.new('Relax_transitions', 'SMOOTH')
smooth.factor = .65
smooth.iterations = 5
bpy.ops.object.modifier_apply(modifier=smooth.name)
decimate = body.modifiers.new('Preserve_small_anatomy', 'DECIMATE')
decimate.ratio = .035
bpy.ops.object.modifier_apply(modifier=decimate.name)
detail_mask=body.vertex_groups.new(name='Upper_body_simplification')
for vertex in body.data.vertices:
    detail_mask.add([vertex.index],0 if vertex.co.z < .19 else 1,'REPLACE')
decimate = body.modifiers.new('Low_poly_body', 'DECIMATE')
decimate.ratio = .42
decimate.vertex_group=detail_mask.name
decimate.vertex_group_factor=1
bpy.ops.object.modifier_apply(modifier=decimate.name)
detail_mask=body.vertex_groups.get('Upper_body_simplification')
if detail_mask:
    body.vertex_groups.remove(detail_mask)
for vertex in body.data.vertices:
    # Retain the toe pads; avoid pulling sparse vertices into sharp ground-contact spikes.
    if vertex.co.z < .02:
        vertex.co.z=.004+max(0,vertex.co.z-.004)*.70

# Permanent boxer briefs are vertex colors on the SAME continuous body mesh.
# Bisecting at the hem/band boundaries gives clean straight color transitions.
bm = bmesh.new()
bm.from_mesh(body.data)
for height in (.829, .842, .987, 1.018):
    bmesh.ops.bisect_plane(bm, geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
        plane_co=(0,0,height), plane_no=(0,0,1), dist=.00001,
        clear_inner=False, clear_outer=False)
bm.to_mesh(body.data)
bm.free()
body.data.update()
paint(body, SKIN)
soft_shading(body)
painted_skin(body, 'body')
body_colors = body.data.color_attributes['Color']
for polygon in body.data.polygons:
    height = sum(body.data.vertices[i].co.z for i in polygon.vertices)/len(polygon.vertices)
    if .829 <= height <= 1.018:
        color = TRIM if height < .842 or height >= .987 else CLOTH
        for index in polygon.loop_indices:
            p=body.data.vertices[body.data.loops[index].vertex_index].co
            value=.88+.12*max(0,-p.y/.11)
            body_colors.data[index].color = tuple(c*value for c in color[:3])+(1,)
body['underwear']='Permanent vertex-colored region of the body mesh; not a detachable object.'


def flat_feature(name, points, color):
    return mesh(name, points, [tuple(range(len(points)))], color)


def head(index):
    # The scalp/neck fitting envelope is shared; jaw, chin and facial landmarks vary.
    jaw = [.079,.086,.096][index]
    chin = [.043,.051,.061][index]
    cheek = [.109,.105,.112][index]
    head_rings = [
        (1.510,chin,.047,0,-.019), (1.538,jaw,.071,0,-.010),
        (1.588,cheek*.96,.084,0,-.002), (1.636,cheek,.091,0,.002),
        (1.687,.105,.093,0,.006), (1.738,.101,.089,0,.012),
        (1.785,.074,.068,0,.012), (1.808,.025,.025,0,.013),
        (1.812,.002,.002,0,.013),
    ]
    main_head = ring_mesh('Head', head_rings, 16)
    nose_width = [.016,.019,.023][index]
    nose_tip = [-.121,-.132,-.125][index]
    nose = mesh('Nose', [(-.010,-.077,1.679),(.010,-.077,1.679),
        (-nose_width,nose_tip+.014,1.619),(nose_width,nose_tip+.014,1.619),
        (0,nose_tip,1.630),(-nose_width*1.18,-.080,1.615),
        (nose_width*1.18,-.080,1.615),(0,-.103,1.610)],
        [(0,1,4),(0,4,2),(1,3,4),(2,4,3,7),(0,2,5),(1,6,3),
         (2,7,5),(3,6,7),(0,5,7,6,1)],SKIN)
    anatomy=[main_head,nose]
    for side in (-1,1):
        anatomy.append(ellipsoid('Ear', (side*.106,.009,1.636),
            (.023,.018,.032),SKIN,8,4))
    main_head=join(anatomy,'Head_Anatomy')
    volume=main_head.modifiers.new('Integrated_face_anatomy','REMESH')
    volume.mode='VOXEL'; volume.voxel_size=.0018
    bpy.ops.object.modifier_apply(modifier=volume.name)
    relax=main_head.modifiers.new('Face_plane_transitions','SMOOTH')
    relax.factor=.6; relax.iterations=4
    bpy.ops.object.modifier_apply(modifier=relax.name)
    reduce=main_head.modifiers.new('Low_poly_face','DECIMATE')
    reduce.ratio=min(1,780/(2*len(main_head.data.polygons)))
    bpy.ops.object.modifier_apply(modifier=reduce.name)
    paint(main_head,SKIN)
    soft_shading(main_head)
    painted_skin(main_head,'head')
    surface=BVHTree.FromPolygons([v.co for v in main_head.data.vertices],
        [list(p.vertices) for p in main_head.data.polygons])
    parts = [main_head]
    def face_y(x, z, offset=.0015):
        # Sample the sculpted low-poly face so painted features follow its surface.
        hit, _, _, _ = surface.ray_cast(Vector((x,-1,z)),Vector((0,1,0)))
        if hit is not None:
            return hit.y-offset
        lo, hi = head_rings[0], head_rings[1]
        for j in range(len(head_rings)-1):
            if head_rings[j][0] <= z <= head_rings[j+1][0]:
                lo, hi = head_rings[j], head_rings[j+1]
                break
        t = min(1,max(0,(z-lo[0])/(hi[0]-lo[0])))
        rx=lo[1]*(1-t)+hi[1]*t; ry=lo[2]*(1-t)+hi[2]*t
        cy=lo[4]*(1-t)+hi[4]*t
        ax=abs(x)
        for j in range(4):
            a=j*math.pi/8; b=(j+1)*math.pi/8
            xa,xb=rx*math.sin(a),rx*math.sin(b)
            if xa <= ax <= xb:
                u=(ax-xa)/(xb-xa)
                return cy-ry*(math.cos(a)*(1-u)+math.cos(b)*u)-offset
        return cy-offset
    def feature(name, xz, color, offset=.0008):
        # A fitted fan prevents non-planar eyebrow polygons from cutting into skin.
        coords=[]
        for a,b in zip(xz,xz[1:]+xz[:1]):
            coords.extend([a,((a[0]+b[0])/2,(a[1]+b[1])/2)])
        center=(sum(x for x,z in coords)/len(coords),sum(z for x,z in coords)/len(coords))
        points=[(x,face_y(x,z,offset),z) for x,z in coords+[center]]
        result=mesh(name,points,[(i,(i+1)%len(coords),len(coords))
            for i in range(len(coords))],color)
        subdiv=result.modifiers.new('Fit_painted_feature','SUBSURF')
        subdiv.subdivision_type='SIMPLE'; subdiv.levels=2
        bpy.ops.object.modifier_apply(modifier=subdiv.name)
        for vertex in result.data.vertices:
            vertex.co.y=face_y(vertex.co.x,vertex.co.z,offset)
        paint(result,color)
        return soft_shading(result)
    for side in (-1,1):
        x = side * [.043,.044,.045][index]
        z = [1.667,1.663,1.660][index]
        width = [.024,.023,.025][index]
        height = [.0055,.0045,.005][index]
        coords = [(x-width,z),(x-width*.5,z+height),
                  (x+width*.5,z+height),(x+width,z),
                  (x+width*.5,z-height*.65),(x-width*.5,z-height*.65)]
        parts.append(feature('Eye',coords,(.43,.40,.32,1)))
        parts.append(feature('Pupil',[(x-.004,z+height),(x+.004,z+height),
            (x+.004,z-height*.65),(x-.004,z-height*.65)],PUPIL,.0011))
        brow_inner = z + [.020,.013,.022][index]
        brow_outer = z + [.022,.027,.019][index]
        pts = [(side*.019,brow_inner),(side*.043,brow_inner+.006),
               (side*.071,brow_outer+.004),(side*.072,brow_outer-.001),
               (side*.043,brow_inner),(side*.020,brow_inner-.004)]
        parts.append(feature('Brow',pts,HAIR))
    mouth = [.027,.029,.034][index]
    smile = [.002,-.001,.006][index]
    # Mouth plane follows the lower face; no painted-on grin or facial texture dependency.
    parts.append(feature('Mouth_Line',[(-mouth,1.578+smile),(0,1.576),
        (mouth,1.578+smile),(0,1.573)],(.22,.075,.042,1)))
    parts.append(feature('Lower_Lip',[(-.017,1.572),(0,1.574),(.017,1.572),
        (0,1.569)],(.43,.18,.117,1)))
    result = join(parts,['Face_01_Soft','Face_02_Angular','Face_03_Broad'][index])
    return result


def scalp(name, kind):
    vertices=[]
    segments=16
    levels=5
    for j in range(levels):
        for i in range(segments):
            a=2*math.pi*i/segments
            low=1.681+.047*math.cos(a)
            if kind==0:
                low += .011*math.sin(a)*max(0,math.cos(a))
            if kind==1:
                low += .007*math.cos(a*5)
            if kind==2:
                low += .006
            phi_max=math.acos((low-1.655)/.181)
            phi=.015+(phi_max-.015)*j/(levels-1)
            radius = 1.0 + (.045*math.sin(a*5) if kind==1 else 0)
            vertices.append((.119*radius*math.sin(phi)*math.sin(a),
                             .010-.111*radius*math.sin(phi)*math.cos(a),
                             1.655+.190*math.cos(phi)+(.007*math.cos(a*3)*math.sin(phi) if kind==1 else 0)))
    faces=[tuple(reversed(range(segments)))]
    for j in range(levels-1):
        for i in range(segments):
            a=j*segments+i; b=j*segments+(i+1)%segments
            faces.append((a,b,b+segments,a+segments))
    faces.append(tuple((levels-1)*segments+i for i in reversed(range(segments))))
    return mesh(name,vertices,faces,HAIR)


def hair(index):
    parts=[scalp('Scalp',index)]
    if index==0:
        parts.append(lock('Broad_Swept_Fringe',[(.053,-.070,1.780),(.010,-.090,1.805),
            (-.052,-.099,1.766),(-.089,-.075,1.704)],.036,.021,HAIR))
        parts.append(lock('Part_Ridge',[(.040,-.057,1.809),(.018,-.009,1.838),
            (-.012,.052,1.816),(-.045,.097,1.746)],.026,.014,HAIR))
    elif index==1:
        for i,x in enumerate((-.065,0,.063)):
            parts.append(lock('Broad_Tuft',[(x*.7,-.008,1.816),(x,-.025,1.850),
                (x+.008,-.067,1.809),(x-.014,-.103,1.727-i*.010)],.034,.024,HAIR))
        parts.append(lock('Crown_Tuft',[(.013,.010,1.820),(-.022,.035,1.860),
            (-.052,.051,1.853),(-.094,.065,1.815)],.025,.021,HAIR))
    else:
        for x in (-.042,.028):
            parts.append(lock('Swept_Back',[(x,-.070,1.751), (x*.91,-.010,1.851-abs(x)*.25),
                (x*.75,.075,1.817-abs(x)*.20),(x*.40,.122,1.733)],.028,.012,HAIR))
        parts.append(ellipsoid('Tail_Knot',(0,.125,1.724),(.047,.043,.037),HAIR,8,4))
        parts.append(lock('Short_Tail',[(0,.140,1.730),(.005,.199,1.689),
            (.020,.213,1.620),(.039,.180,1.552)],.038,.031,HAIR))
        parts.append(ellipsoid('Hair_Tie',(0,.154,1.707),(.039,.014,.021),TRIM,8,4))
    # Unite the masses so the hairstyle reads as sculpted locks instead of stacked blocks.
    tie = parts.pop() if index == 2 else None
    result = join(parts,['Hair_01_SidePart','Hair_02_Tousled','Hair_03_TiedBack'][index])
    volume = result.modifiers.new('Connected_hair_masses', 'REMESH')
    volume.mode = 'VOXEL'; volume.voxel_size = .003
    bpy.ops.object.modifier_apply(modifier=volume.name)
    relax = result.modifiers.new('Broad_lock_transitions', 'SMOOTH')
    relax.factor=.45; relax.iterations=2
    bpy.ops.object.modifier_apply(modifier=relax.name)
    reduce = result.modifiers.new('Low_poly_hair', 'DECIMATE')
    reduce.ratio=min(1, [290,340,420][index]/len(result.data.polygons))
    bpy.ops.object.modifier_apply(modifier=reduce.name)
    paint(result, HAIR)
    soft_shading(result)
    attr=result.data.color_attributes['Color']
    for loop in result.data.loops:
        x,y,z=result.data.vertices[loop.vertex_index].co
        value=.72+.32*min(1,max(0,(z-1.66)/.17))
        value+=.15*bell(x,-.035,.042)*bell(z,1.79,.054)
        attr.data[loop.index].color=tuple(c*value for c in HAIR[:3])+(1,)
    if tie:
        result=join([result,soft_shading(tie)],result.name)
    return result


heads=[head(i) for i in range(3)]
hairs=[hair(i) for i in range(3)]
assets=[body]+heads+hairs

# Adult proportions: all interchangeable heads/hair share exactly the same transform.
for obj in heads+hairs:
    for vertex in obj.data.vertices:
        vertex.co.x *= .96
        vertex.co.y *= .96
        vertex.co.z = 1.510 + (vertex.co.z-1.510)*.92

# Put each mesh at the same world/attachment origin. Module swaps require no offsets.
for obj in assets:
    scene.cursor.location=(0,0,0)
    bpy.context.view_layer.objects.active=obj
    bpy.ops.object.select_all(action='DESELECT')
    obj.select_set(True)
    bpy.ops.object.origin_set(type='ORIGIN_CURSOR')
    obj['project_g_module']=obj.name
    obj['pose']='T pose; arms horizontal; palms down; Blender front -Y'


def export(name, objects):
    bpy.ops.object.select_all(action='DESELECT')
    for obj in assets:
        obj.hide_set(False)
        obj.hide_render=False
    for obj in objects:
        obj.select_set(True)
    bpy.context.view_layer.objects.active=objects[0]
    bpy.ops.export_scene.gltf(filepath=str(OUT/'exports'/name), export_format='GLB',
        use_selection=True, export_yup=True, export_apply=True,
        export_animations=False, export_cameras=False, export_lights=False,
        export_extras=True, export_materials='EXPORT')


export('body_male_tpose.glb',[body])
for obj in heads+hairs:
    export(obj.name.lower()+'.glb',[obj])
export('male_default_tpose.glb',[body,heads[0],hairs[0]])

stats={}
for obj in assets:
    obj.data.calc_loop_triangles()
    stats[obj.name]={'vertices':len(obj.data.vertices),'triangles':len(obj.data.loop_triangles),
                     'materials':len(obj.data.materials)}
manifest={'height_m':1.788,'pose':'T','sex':'adult male','build':'average',
          'rigged':False,'animations':False,'underwear':'integrated body vertex colors',
          'source_forward':'-Y','gltf_forward':'+Z',
          'parts':stats,'combinations':9,'material':'opaque vertex color / rough dielectric',
          'art_direction':'smooth skin normals, broad painted color fields, low-poly silhouettes',
          'note':'First art prototype; no animation-ready deformation topology or LODs claimed.'}
(OUT/'manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')

# Organize the editable source; variants are hidden, never discarded.
for obj in assets:
    collection=bpy.data.collections.new(obj.name)
    scene.collection.children.link(collection)
    for old in list(obj.users_collection):
        old.objects.unlink(obj)
    collection.objects.link(obj)
    hidden=obj in heads[1:]+hairs[1:]
    obj.hide_render=hidden
    obj.hide_set(hidden)

scene.render.engine='CYCLES'
scene.cycles.samples=40
scene.cycles.use_denoising=True
scene.render.resolution_percentage=100
scene.render.image_settings.file_format='PNG'
scene.world.color=(.24,.24,.24)
scene.view_settings.view_transform='AgX'
scene.render.film_transparent=False

studio=bpy.data.collections.new('STUDIO_not_exported')
scene.collection.children.link(studio)


def to_studio(obj):
    for old in list(obj.users_collection):
        old.objects.unlink(obj)
    studio.objects.link(obj)


def light(name,location,energy,size,color):
    data=bpy.data.lights.new(name,'AREA'); data.energy=energy; data.shape='DISK'; data.size=size; data.color=color
    obj=bpy.data.objects.new(name,data); studio.objects.link(obj); obj.location=location
    obj.rotation_euler=(Vector((0,0,1.0))-obj.location).to_track_quat('-Z','Y').to_euler()


light('Key_softbox',(-3,-4,6),420,4.0,(1,.86,.72))
light('Fill_softbox',(4,-2,4),260,3.5,(.75,.87,1))
light('Rim_softbox',(0,3,5),500,3.0,(1,.91,.76))
bpy.ops.mesh.primitive_plane_add(size=200)
floor=bpy.context.object; floor.name='Studio_Ground'; floor.location.z=.003; to_studio(floor)
floor_mat=bpy.data.materials.new('Studio_WarmGrey'); floor_mat.diffuse_color=(.16,.20,.20,1)
floor.data.materials.append(floor_mat)
camera_data=bpy.data.cameras.new('Review_Camera'); camera=bpy.data.objects.new('Review_Camera',camera_data)
studio.objects.link(camera); scene.camera=camera; camera_data.type='ORTHO'; camera_data.lens=50


def aim(location,target,scale):
    camera.location=location
    camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler()
    camera_data.ortho_scale=scale


aim((3,-7,2.8),(0,0,.92),2.48)
scene.render.resolution_x=1100; scene.render.resolution_y=1100
# Useful material view and framing on opening the .blend.
for screen in bpy.data.screens:
    for area in screen.areas:
        if area.type=='VIEW_3D':
            area.spaces.active.shading.type='MATERIAL'
            area.spaces.active.region_3d.view_distance=3.0
            area.spaces.active.region_3d.view_location=(0,0,.95)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'male-base.blend'))


def render(name):
    scene.render.filepath=str(OUT/'renders'/name)
    bpy.ops.render.render(write_still=True)


aim((.68,-1.15,.55),(0,-.075,.09),.67)
scene.render.resolution_x=1500; scene.render.resolution_y=1000
render('feet-detail.png')
aim((3,-7,2.8),(0,0,.92),2.48)
scene.render.resolution_x=1100; scene.render.resolution_y=1100
render('male-hero.png')


def hide_sources():
    for obj in assets:
        obj.hide_render=True


def duplicate(obj, x=0, angle=0):
    copy=obj.copy(); copy.data=obj.data
    scene.collection.objects.link(copy)
    copy.hide_render=False; copy.hide_set(False)
    copy.location=(x,0,0); copy.rotation_euler.z=angle
    return copy


hide_sources()
copies=[]
for i,x in enumerate((-2.13,0,2.13)):
    for obj in [body,heads[i],hairs[i]]:
        copies.append(duplicate(obj,x))
aim((0,-9,2.65),(0,0,.93),6.65)
scene.render.resolution_x=2100; scene.render.resolution_y=850
render('three-characters.png')
for obj in copies: bpy.data.objects.remove(obj,do_unlink=True)

# Three head-only comparisons keep facial structure and hairstyle differences clear.
for mode in ('faces','hairstyles','hairstyles-profile'):
    copies=[]
    for i,x in enumerate((-.33,0,.33)):
        angle=math.pi/2 if mode.endswith('profile') else 0
        for obj in ([heads[i]] if mode=='faces' else [heads[0],hairs[i]]):
            copies.append(duplicate(obj,x,angle))
    aim((0,-5,1.82),(0,0,1.684),1.18)
    scene.render.resolution_x=1800; scene.render.resolution_y=850
    render(mode+'.png')
    for obj in copies: bpy.data.objects.remove(obj,do_unlink=True)

copies=[]
for x,angle in [(-1.35,0),(0,math.pi/2),(1.35,math.pi)]:
    for obj in [body,heads[0],hairs[0]]:
        copies.append(duplicate(obj,x,angle))
aim((0,-9,1.05),(0,0,.92),4.9)
scene.render.resolution_x=2100; scene.render.resolution_y=1000
render('turnaround.png')
print('CHARACTER_BUILD_OK '+json.dumps(stats))
