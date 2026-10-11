"""Original Linvel asset authoring helpers. Execute inside Blender through MCP only.

Connection map (metres; checked from generated part bounds):
 foundation.top <-> wall.bottom: 0.04 m embed
 wall.top <-> cornice.bottom: 0.04 m embed
 roof_shell <-> overlapping tile rows: >= 0.015 m embed
 timber_post <-> crossbeam: 0.04 m embed
 window_recess <-> arch surround: continuous sampled perimeter
 dock_pile <-> crossbeam <-> planks: 0.035 m embed
 hull_outer <-> gunwale <-> ribs: continuous hull sample coordinates
 tree_trunk <-> branches: 0.08 m embed at each fork
 fountain_plinth <-> column: 0.04 m embed

All dimensions are authored in metres. Directed members use explicit endpoint
frames. No cylinder Euler rotations or unapplied scaling are used. Every part
records bounds and a semantic face ID for manual editing. No external geometry,
textures, fonts, or third-party helper source is used.
"""
import bpy
import bmesh
import math
import random
import json
import time
from pathlib import Path
from collections import defaultdict
from mathutils import Vector, Matrix
import numpy as np

ROOT = Path('D:/projects/project-g/art-source/linvel')
ASSETS = globals().get('ASSETS', {})
RNG = random.Random(684)
PALETTE = ['plaster', 'stone', 'tile', 'slate', 'oak', 'darkwood', 'glass', 'blue',
           'gold', 'iron', 'leaf', 'pink', 'ivory', 'red', 'soil', 'water']
COLORS = [(0.88, .82, .65), (.69, .70, .64), (.72, .32, .16), (.16, .29, .34),
          (.46, .29, .14), (.23, .17, .11), (.12, .27, .28), (.08, .29, .45),
          (.72, .50, .21), (.19, .23, .22), (.23, .42, .15), (.91, .56, .65),
          (.94, .88, .70), (.62, .20, .14), (.35, .29, .16), (.19, .50, .53)]

def setup():
    for folder in ['textures', 'exports', 'previews', 'reports', 'sources']:
        (ROOT / folder).mkdir(parents=True, exist_ok=True)
    sc = bpy.data.scenes.get('Linvel_AssetLibrary')
    if sc is None:
        sc = bpy.data.scenes.new('Linvel_AssetLibrary')
    bpy.context.window.scene = sc
    sc.unit_settings.system = 'METRIC'
    sc.unit_settings.scale_length = 1.0
    sc.render.engine = 'BLENDER_EEVEE'
    sc.render.resolution_x = 1200
    sc.render.resolution_y = 1200
    sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = 'PNG'
    sc.render.film_transparent = False
    sc.view_settings.view_transform = 'Standard'
    sc.view_settings.look = 'Medium High Contrast'
    world = bpy.data.worlds.get('Linvel_StudioWorld') or bpy.data.worlds.new('Linvel_StudioWorld')
    world.use_nodes = True
    world.node_tree.nodes.get('Background').inputs['Color'].default_value = (.59, .69, .78, 1)
    world.node_tree.nodes.get('Background').inputs['Strength'].default_value = .6
    sc.world = world
    if bpy.data.images.get('Linvel_PaintedAtlas') is None:
        make_atlas()
    make_materials()
    return sc

def make_atlas():
    """Paint deterministic broad colour strokes and grain into a portable PNG."""
    size, cell = 2048, 512
    pixels = np.ones((size, size, 4), dtype=np.float32)
    y, x = np.mgrid[0:cell, 0:cell].astype(np.float32) / cell
    rng = np.random.default_rng(684)
    for index, name in enumerate(PALETTE):
        base = np.array(COLORS[index], dtype=np.float32)
        wash = .026 * np.sin(x * 18 + y * 7) + .018 * np.cos(y * 29 - x * 10)
        # Broad painted washes compress well and avoid distant shimmering.
        if name in ('oak', 'darkwood'):
            grain = np.sin((x + .015 * np.sin(y * 19)) * 190)
            wash += .007 * grain + .015 * np.sin(x * 20 + y * 3)
        elif name in ('tile', 'slate'):
            wash += .025 * np.sin(y * 24) - .05 * (x - .4) ** 2
        elif name in ('blue', 'red', 'ivory'):
            wash += .003 * np.sin(x * 520) * np.sin(y * 520)
        elif name in ('plaster', 'stone'):
            wash += .010 * np.sin(x * 14 + np.sin(y * 13))
        elif name in ('leaf', 'pink'):
            wash += .055 * (1 - y) - .025 * x
        patch = np.clip(base[None, None, :] + wash[..., None], .02, .98)
        row, col = divmod(index, 4)
        pixels[row * cell:(row + 1) * cell, col * cell:(col + 1) * cell, :3] = patch
    img = bpy.data.images.get('Linvel_PaintedAtlas') or bpy.data.images.new('Linvel_PaintedAtlas', width=size, height=size, alpha=True)
    if img.packed_file:
        img.unpack(method='REMOVE')
    img.pixels.foreach_set(pixels.ravel())
    img.update()
    img.filepath_raw = str(ROOT / 'textures/linvel_painted_atlas.png')
    img.file_format = 'PNG'
    img.save()
    img.pack()

def make_materials():
    for name, rough, metal in [('Linvel_Painted', .82, 0), ('Linvel_Metal', .48, .55),
                              ('Linvel_Glazing', .23, 0), ('Linvel_Foliage', .85, 0)]:
        if bpy.data.materials.get(name):
            continue
        mat = bpy.data.materials.new(name)
        mat.use_nodes = True
        p = mat.node_tree.nodes.get('Principled BSDF')
        p.inputs['Roughness'].default_value = rough
        p.inputs['Metallic'].default_value = metal
        p.inputs['Specular IOR Level'].default_value = .28
        tex = mat.node_tree.nodes.new('ShaderNodeTexImage')
        tex.image = bpy.data.images['Linvel_PaintedAtlas']
        tex.interpolation = 'Linear'
        mat.node_tree.links.new(tex.outputs['Color'], p.inputs['Base Color'])
        mat.diffuse_color = (.75, .70, .58, 1)
        mat.use_backface_culling = False

def uv_patch(kind, points):
    ix = PALETTE.index(kind)
    row, col = divmod(ix, 4)
    return [((col + .035 + .93 * u) / 4, (row + .035 + .93 * v) / 4) for u, v in points]

class Builder:
    def __init__(self, key, label, category):
        if key in ASSETS or bpy.data.collections.get('LV_' + key):
            raise ValueError('Refusing to replace an existing asset: ' + key)
        self.key, self.label, self.category = key, label, category
        self.groups = defaultdict(lambda: {'v': [], 'f': [], 'uv': [], 'smooth': [], 'parts': []})
        self.bounds = {}
        self.joints = []

    def add(self, name, verts, faces, kind='stone', group='Structure', smooth=False, uvs=None):
        verts = [tuple(v) for v in verts]
        if not verts:
            return
        assert all(math.isfinite(c) for v in verts for c in v), name
        self.bounds[name] = [(min(v[i] for v in verts), max(v[i] for v in verts)) for i in range(3)]
        if kind in ('gold', 'iron') and group != 'Goods':
            group = 'Metalwork'
        elif kind == 'glass':
            group = 'Glazing'
        elif kind in ('leaf', 'pink'):
            group = 'Foliage'
        g = self.groups[group]
        offset = len(g['v'])
        g['v'].extend(verts)
        for i, face in enumerate(faces):
            g['f'].append(tuple(offset + j for j in face))
            if uvs:
                uv = uvs[i]
            elif len(face) == 4:
                uv = [(0, 0), (1, 0), (1, 1), (0, 1)]
            else:
                uv = [(.5 + .46 * math.cos(2 * math.pi * j / len(face)),
                       .5 + .46 * math.sin(2 * math.pi * j / len(face))) for j in range(len(face))]
            g['uv'].append(uv_patch(kind, uv))
            g['smooth'].append(smooth)
            g['parts'].append(name)
        return name

    def box(self, name, center, dims, kind='stone', group='Structure', bevel=.015):
        c = Vector(center)
        half = Vector(dims) * .5
        bm = bmesh.new()
        bmesh.ops.create_cube(bm, size=2)
        for v in bm.verts:
            v.co = c + Vector((v.co.x * half.x, v.co.y * half.y, v.co.z * half.z))
        if bevel > 0:
            bmesh.ops.bevel(bm, geom=list(bm.edges), offset=min(bevel, min(dims) * .24), segments=1, affect='EDGES')
        bm.verts.ensure_lookup_table()
        verts = [tuple(v.co) for v in bm.verts]
        faces = [tuple(v.index for v in f.verts) for f in bm.faces]
        bm.free()
        return self.add(name, verts, faces, kind, group)

    def joint(self, a, b, axis, minimum=.005):
        aa, bb = self.bounds[a][axis], self.bounds[b][axis]
        overlap = min(aa[1], bb[1]) - max(aa[0], bb[0])
        assert overlap >= minimum - 1e-5, (a, b, 'gap', overlap)
        self.joints.append({'a': a, 'b': b, 'axis': axis, 'overlap_m': round(overlap, 5)})

    def span(self, name, a, b, halfw, halfh, kind='oak', group='Joinery', sides=8):
        a, b = Vector(a), Vector(b)
        direction = (b - a).normalized()
        ref = Vector((0, 0, 1)) if abs(direction.z) < .9 else Vector((1, 0, 0))
        u = direction.cross(ref).normalized()
        v = direction.cross(u).normalized()
        if sides == 8:
            t = .22
            ring = [(-1 + t, -1), (1 - t, -1), (1, -1 + t), (1, 1 - t),
                    (1 - t, 1), (-1 + t, 1), (-1, 1 - t), (-1, -1 + t)]
        else:
            ring = [(math.cos(j * 2 * math.pi / sides), math.sin(j * 2 * math.pi / sides)) for j in range(sides)]
        verts = [p + u * x * halfw + v * y * halfh for p in (a, b) for x, y in ring]
        n = len(ring)
        faces = [tuple(range(n - 1, -1, -1)), tuple(range(n, n * 2))]
        faces += [(j, (j + 1) % n, (j + 1) % n + n, j + n) for j in range(n)]
        return self.add(name, verts, faces, kind, group, sides > 8)

    def lathe(self, name, center, profile, kind='stone', sides=32, group='Structure', closed=True):
        x, y, z = center
        verts = [(x + r * math.cos(i * 2 * math.pi / sides),
                  y + r * math.sin(i * 2 * math.pi / sides), z + h) for r, h in profile for i in range(sides)]
        faces = []
        for j in range(len(profile) - 1):
            for i in range(sides):
                k = j * sides + i
                faces.append((k, j * sides + (i + 1) % sides,
                              (j + 1) * sides + (i + 1) % sides, k + sides))
        if closed:
            faces += [tuple(range(sides - 1, -1, -1)), tuple((len(profile) - 1) * sides + i for i in range(sides))]
        return self.add(name, verts, faces, kind, group, True)

    def ribbon(self, name, points, radius=.025, kind='iron', group='Detail', sides=8):
        pts = [Vector(p) for p in points]
        verts = []
        u = None
        for j, p in enumerate(pts):
            direction = (pts[min(j + 1, len(pts) - 1)] - pts[max(0, j - 1)]).normalized()
            ref = Vector((0, 0, 1)) if abs(direction.z) < .9 else Vector((1, 0, 0))
            u = direction.cross(ref).normalized() if u is None else (u - direction * u.dot(direction)).normalized()
            v = direction.cross(u).normalized()
            for k in range(sides):
                angle = k * 2 * math.pi / sides
                verts.append(p + radius * (u * math.cos(angle) + v * math.sin(angle)))
        faces = []
        for j in range(len(pts) - 1):
            for k in range(sides):
                faces.append((j * sides + k, j * sides + (k + 1) % sides,
                              (j + 1) * sides + (k + 1) % sides, (j + 1) * sides + k))
        faces += [tuple(range(sides - 1, -1, -1)), tuple((len(pts) - 1) * sides + k for k in range(sides))]
        return self.add(name, verts, faces, kind, group, True)

    def branch(self, name, points, radii, kind='darkwood', sides=10):
        """Curved, tapering organic branch with explicit frames and clean transforms."""
        pts = [Vector(p) for p in points]
        verts = []
        u = None
        for j, (p, radius) in enumerate(zip(pts, radii)):
            direction = (pts[min(j + 1, len(pts) - 1)] - pts[max(j - 1, 0)]).normalized()
            ref = Vector((1, 0, 0)) if abs(direction.z) > .88 else Vector((0, 0, 1))
            u = direction.cross(ref).normalized() if u is None else (u - direction * u.dot(direction)).normalized()
            v = direction.cross(u).normalized()
            for k in range(sides):
                a = k * math.pi * 2 / sides
                verts.append(p + radius * (math.cos(a) * u + math.sin(a) * v))
        faces = [(j * sides + k, j * sides + (k + 1) % sides,
                  (j + 1) * sides + (k + 1) % sides, (j + 1) * sides + k)
                 for j in range(len(pts) - 1) for k in range(sides)]
        faces += [tuple(range(sides - 1, -1, -1)), tuple((len(pts) - 1) * sides + k for k in range(sides))]
        return self.add(name, verts, faces, kind, 'Branches', True)

    def finish(self):
        sc = bpy.data.scenes['Linvel_AssetLibrary']
        col = bpy.data.collections.new('LV_' + self.key)
        sc.collection.children.link(col)
        root = bpy.data.objects.new(self.key, None)
        col.objects.link(root)
        root['asset_id'] = 'linvel.' + self.key
        root['label_ru'] = self.label
        root['category'] = self.category
        root['units'] = 'metres'
        root['front_blender'] = '-Y'
        root['collision_policy'] = 'Author simple engine collision separately; decorative mesh is visual only.'
        part_names = sorted(self.bounds)
        part_ids = {name: i for i, name in enumerate(part_names)}
        objects = []
        for group, g in self.groups.items():
            mesh = bpy.data.meshes.new(self.key + '_' + group)
            mesh.from_pydata(g['v'], [], g['f'])
            mesh.update()
            obj = bpy.data.objects.new(mesh.name, mesh)
            col.objects.link(obj)
            obj.parent = root
            layer = mesh.uv_layers.new(name='UVMap')
            for poly, uv, smooth in zip(mesh.polygons, g['uv'], g['smooth']):
                for loop, coord in zip(poly.loop_indices, uv):
                    layer.data[loop].uv = coord
                poly.use_smooth = smooth
            attr = mesh.attributes.new('LinvelPartID', 'INT', 'FACE')
            attr.data.foreach_set('value', [part_ids[n] for n in g['parts']])
            bm = bmesh.new()
            bm.from_mesh(mesh)
            bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
            bm.to_mesh(mesh)
            bm.free()
            matname = {'Metalwork': 'Linvel_Metal', 'Glazing': 'Linvel_Glazing', 'Foliage': 'Linvel_Foliage'}.get(group, 'Linvel_Painted')
            mesh.materials.append(bpy.data.materials[matname])
            # Origin to geometry, while the asset root remains at ground/water line.
            mn = Vector(tuple(min(v[i] for v in g['v']) for i in range(3)))
            mx = Vector(tuple(max(v[i] for v in g['v']) for i in range(3)))
            center = (mn + mx) * .5
            mesh.transform(Matrix.Translation(-center))
            obj.location = center
            objects.append(obj)
        bpy.context.view_layer.update()
        assert all(tuple(o.scale) == (1, 1, 1) and max(abs(a) for a in o.rotation_euler) < 1e-7 for o in objects)
        verts = [v for g in self.groups.values() for v in g['v']]
        bounds = [(min(v[i] for v in verts), max(v[i] for v in verts)) for i in range(3)]
        tris = sum(sum(len(f) - 2 for f in g['f']) for g in self.groups.values())
        info = {'key': self.key, 'label_ru': self.label, 'category': self.category,
                'bounds_blender_m': bounds, 'dimensions_m': [b - a for a, b in bounds],
                'triangles': tris, 'meshes': len(objects), 'parts': len(part_names),
                'checked_joints': self.joints, 'part_ids': part_names}
        (ROOT / 'reports' / (self.key + '.json')).write_text(json.dumps(info, ensure_ascii=False, indent=2), encoding='utf-8')
        ASSETS[self.key] = info
        return info

def stone_courses(b, width, depth, height, base=0, offset=(0, 0), kind='stone', name='Masonry'):
    rows = max(1, round(height / .36))
    rh = height / rows
    ox, oy = offset
    for row in range(rows):
        z = base + (row + .5) * rh
        for face in range(4):
            length = width if face < 2 else depth
            n = max(1, round(length / .7))
            for j in range(n):
                c = -length / 2 + (j + .5) * length / n
                if face < 2:
                    pos = (ox + c, oy + (-1 if face == 0 else 1) * depth / 2, z)
                    dims = (length / n - .024, .20, rh - .02)
                else:
                    pos = (ox + (-1 if face == 2 else 1) * width / 2, oy + c, z)
                    dims = (.20, length / n - .024, rh - .02)
                b.box(f'{name}_{row}_{face}_{j}', pos, dims, kind, bevel=.025)

def arch(b, name, cx, y, z, width, height, kind='stone', trim=.16, depth=.23, group='Detail'):
    r = width * .5
    spring = z + height - r
    for sign in (-1, 1):
        b.box(name + '_Jamb' + str(sign), (cx + sign * (r + trim / 2), y, (z + spring) / 2),
              (trim, depth, spring - z + .035), kind, group)
        b.box(name + '_Foot' + str(sign), (cx + sign * (r + trim / 2), y, z + .10),
              (trim * 1.3, depth * 1.15, .20), kind, group)
    n = max(9, round(width * 8))
    for i in range(n):
        a0, a1 = i * math.pi / n + .009, (i + 1) * math.pi / n - .009
        verts = [(cx + rr * math.cos(a), yy, spring + rr * math.sin(a))
                 for yy in (y - depth / 2, y + depth / 2) for rr, a in ((r, a0), (r, a1), (r + trim, a1), (r + trim, a0))]
        b.add(f'{name}_Voussoir{i}', verts, [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)], kind, group)
    b.box(name + '_Keystone', (cx, y - .03, spring + r + trim * .55), (.20, depth * 1.25, trim * 1.35), kind, group)

def opening(b, name, cx, y, z, width=1.05, height=1.6, door=False, shutters=False, trim_kind='stone'):
    """Inset arch pane, thick surround, inset mullions, sill and shutter fittings."""
    r = width * .5
    spring = z + height - r
    outline = [(cx - r, y, z), (cx + r, y, z), (cx + r, y, spring)]
    outline += [(cx + r * math.cos(i * math.pi / 16), y, spring + r * math.sin(i * math.pi / 16)) for i in range(1, 17)]
    b.add(name + '_Inset', outline, [tuple(range(len(outline)))], 'darkwood' if door else 'glass', 'Glazing' if not door else 'Joinery')
    arch(b, name, cx, y - .07, z, width, height, trim_kind)
    b.box(name + '_Sill', (cx, y - .08, z - .06), (width + .45, .42, .14), trim_kind, 'Detail')
    if door:
        for j in range(7):
            x = cx - r + (j + .5) * width / 7
            top = spring + math.sqrt(max(0, r * r - (x - cx) ** 2))
            b.box(name + '_Plank' + str(j), (x, y - .012, (top + z) / 2), (width / 7 - .012, .05, top - z), 'oak', 'Joinery', .008)
        for zz in (z + .32, z + 1.1):
            b.box(name + '_Strap' + str(zz), (cx, y - .055, zz), (width * .86, .035, .075), 'iron')
        circle = [(cx + .19 + .06 * math.cos(i * 2 * math.pi / 16), y - .10, z + .84 + .06 * math.sin(i * 2 * math.pi / 16)) for i in range(17)]
        b.ribbon(name + '_Handle', circle, .012, 'gold')
    else:
        b.box(name + '_Mullion', (cx, y - .025, z + height * .48), (.06, .075, height * .92), 'oak', 'Joinery')
        b.box(name + '_Transom', (cx, y - .025, z + height * .48), (width, .075, .065), 'oak', 'Joinery')
        if shutters:
            for sign in (-1, 1):
                xx = cx + sign * (width / 2 + .32)
                b.box(name + '_Shutter' + str(sign), (xx, y + .01, z + .68), (.43, .085, 1.36), 'blue', 'Joinery')
                for zz in (z + .25, z + 1.1):
                    b.box(name + '_ShutterHinge' + str(sign) + str(zz), (xx, y - .045, zz), (.37, .035, .06), 'iron')

def roof(b, name, width, depth, eave_z, rise, kind='tile', center=(0, 0), hip=False):
    """Curved clay half-barrel shingles, distinct staggered courses and ridge caps."""
    ox, oy = center
    hw, hd = width / 2 + .35, depth / 2 + .35
    z_ridge = eave_z + rise
    verts = [(ox - hw, oy - hd, eave_z), (ox + hw, oy - hd, eave_z), (ox + hw, oy + hd, eave_z),
             (ox - hw, oy + hd, eave_z), (ox, oy - hd, z_ridge), (ox, oy + hd, z_ridge)]
    b.add(name + '_Underlay', verts, [(0, 4, 5, 3), (1, 2, 5, 4)], kind, 'Roof')
    for sign in (-1, 1):
        slope = Vector((sign * hw, 0, -rise))
        length = slope.length
        along = slope.normalized()
        across = Vector((0, 1, 0))
        normal = Vector((sign * rise / length, 0, hw / length))
        rows = max(3, math.ceil(length / .33))
        columns = max(4, math.ceil(2 * hd / .29))
        for row in range(rows):
            step = length / rows
            for col in range(columns):
                tw = 2 * hd / columns
                yy = -hd + (col + .5) * tw
                start = Vector((ox, oy + yy, z_ridge)) + along * (row * step - .035) + normal * .035
                vertices = []
                for d in (0, step + .065):
                    for k in range(6):
                        a = math.pi * k / 5
                        lateral = (k / 5 - .5) * (tw - .006)
                        arch_h = .035 * math.sin(a)
                        vertices.append(start + along * d + across * lateral + normal * arch_h)
                faces = [(k, k + 1, k + 7, k + 6) for k in range(5)]
                # Clay shingles have a solid underside and real thickness. The
                # underside embeds 20 mm into the underlay at the side edges.
                vertices += [vertices[k] - normal * .055 for k in (0, 5, 6, 11)]
                faces += [(0, 6, 14, 12), (5, 13, 15, 11),
                          (0, 12, 13, 5, 4, 3, 2, 1),
                          (6, 7, 8, 9, 10, 11, 15, 14), (12, 14, 15, 13)]
                b.add(f'{name}_Shingle{sign}_{row}_{col}', vertices, faces, kind, 'Roof', True)
        # Deep fascia and regularly spaced carved rafter ends.
        b.span(name + '_Fascia' + str(sign), (ox + sign * hw, oy - hd - .02, eave_z - .08),
               (ox + sign * hw, oy + hd + .02, eave_z - .08), .12, .09, 'darkwood')
        for j in range(max(4, round(depth / .7))):
            yy = oy - depth / 2 + (j + .5) * depth / max(4, round(depth / .7))
            b.span(name + '_Rafter' + str(sign) + str(j), (ox + sign * (hw - .36), yy, eave_z - .15),
                   (ox + sign * (hw + .08), yy, eave_z - .08), .065, .07, 'oak')
    for j in range(max(2, math.ceil(2 * hd / .30))):
        yy = oy - hd + j * .30
        b.span(name + '_RidgeCap' + str(j), (ox, yy, z_ridge + .045), (ox, min(oy + hd + .03, yy + .33), z_ridge + .045), .115, .09, kind, 'Roof', 12)
    for yy in (oy - hd - .015, oy + hd + .015):
        for sign in (-1, 1):
            b.span(name + '_GableBarge' + str(yy) + str(sign), (ox + sign * hw, yy, eave_z - .04),
                   (ox, yy, z_ridge + .05), .09, .07, 'oak')
    return name + '_Underlay'

def banner(b, name, cx, y, z, width=.8, height=2.3, color='blue'):
    cols, rows = 8, 16
    verts = []
    for j in range(rows + 1):
        t = j / rows
        for i in range(cols + 1):
            u = i / cols
            wave = .055 * math.sin(u * math.pi * 3 + t * 5) * (.3 + t)
            notch = .18 * abs(u - .5) * 2 * t ** 8
            verts.append((cx + (u - .5) * width, y + wave, z - t * height + notch))
    faces = [(j * (cols + 1) + i, j * (cols + 1) + i + 1, (j + 1) * (cols + 1) + i + 1, (j + 1) * (cols + 1) + i)
             for j in range(rows) for i in range(cols)]
    b.add(name + '_Cloth', verts, faces, color, 'Cloth', True)
    for sign in (-1, 1):
        pts = [(cx + sign * width * .46, y - .012 + .055 * math.sin((.04 if sign < 0 else .96) * math.pi * 3 + j / rows * 5), z - j / rows * height) for j in range(rows + 1)]
        b.ribbon(name + '_GoldHem' + str(sign), pts, .015, 'gold')
    # Original emblem: a compass ring, mountain chevron, and horizon line.
    zz = z - height * .39
    rad = width * .22
    circle = [(cx + rad * math.cos(i * math.pi * 2 / 40), y - .095, zz + rad * math.sin(i * math.pi * 2 / 40)) for i in range(41)]
    b.ribbon(name + '_Compass', circle, .019, 'gold')
    b.ribbon(name + '_Mountain', [(cx - rad, y - .12, zz - .06), (cx, y - .12, zz + rad * .72), (cx + rad, y - .12, zz - .06)], .018, 'gold')
    b.span(name + '_Horizon', (cx - rad * .8, y - .12, zz - .11), (cx + rad * .8, y - .12, zz - .11), .016, .018, 'gold')
    b.span(name + '_Rod', (cx - width * .67, y, z + .06), (cx + width * .67, y, z + .06), .035, .035, 'iron', sides=12)

def building(key, label, kind='house', width=6, depth=5.5, floors=2, roofkind='tile', finish=True):
    b = Builder(key, label, 'architecture')
    h, base = floors * 2.85, .46
    foundation = b.box('Foundation', (0, 0, .25), (width + .22, depth + .22, .5), 'stone')
    walls = b.box('Walls', (0, 0, base + h / 2), (width, depth, h), 'plaster', bevel=.025)
    b.joint(foundation, walls, 2, .03)
    stone_courses(b, width, depth, .85, base=0)
    for level in range(1, floors):
        b.box('FloorStringcourse' + str(level), (0, 0, base + level * 2.85), (width + .13, depth + .13, .16), 'stone')
    for x in (-width / 2, width / 2):
        for y in (-depth / 2, depth / 2):
            for j in range(round(h / .55)):
                b.box(f'Quoin_{x}_{y}_{j}', (x, y, base + j * .55 + .27), (.46 if j % 2 else .33, .33 if j % 2 else .46, .50), 'stone', bevel=.022)
    fy = -depth / 2 - .055
    opening(b, 'Entry', 0, fy - .07, .39, 1.35, 2.35, door=True)
    columns = max(2, round(width / 2.15))
    for floor in range(floors):
        zz = .95 + floor * 2.85
        for j in range(columns):
            xx = -width / 2 + (j + .5) * width / columns
            if floor == 0 and abs(xx) < 1.1:
                continue
            opening(b, f'FrontWindow{floor}_{j}', xx, fy - .08, zz, 1.0, 1.65, shutters=kind in ('house', 'healer', 'shop'))
            if floor == 0:
                b.box(f'Planter{j}', (xx, fy - .19, zz - .22), (1.0, .32, .27), 'oak', 'Joinery')
                for k in range(9):
                    leaf(b, f'PlanterLeaf{j}_{k}', Vector((xx + RNG.uniform(-.4, .4), fy - .18, zz - .03)), Vector((RNG.uniform(-.2, .2), -.08, .24)), .07)
    # Back and side windows are actual facade detail, not a blank rear shell.
    for floor in range(floors):
        for j in range(columns):
            xx = -width / 2 + (j + .5) * width / columns
            opening(b, f'RearWindow{floor}_{j}', xx, depth / 2 + .07, .95 + floor * 2.85, .95, 1.5)
        for side in (-1, 1):
            for j in range(max(2, round(depth / 2.2))):
                yy = -depth / 2 + (j + .5) * depth / max(2, round(depth / 2.2))
                # Construct the same complete arch in a local facade frame.
                temp = Builder('temporary', 'temporary', 'architecture')
                opening(temp, f'Side{floor}_{side}_{j}', yy, -width / 2 - .07, .95 + floor * 2.85, .9, 1.5)
                for group, g in temp.groups.items():
                    transformed = [(side * -v[1], v[0], v[2]) for v in g['v']]
                    # UVs in this temporary group are already atlas-space.
                    tgt = b.groups[group]
                    off = len(tgt['v'])
                    tgt['v'].extend(transformed)
                    tgt['f'].extend([tuple(off + i for i in f) for f in g['f']])
                    tgt['uv'].extend(g['uv']); tgt['smooth'].extend(g['smooth']); tgt['parts'].extend(g['parts'])
                    for name, bounds in temp.bounds.items():
                        b.bounds[name] = bounds
    cornice = b.box('EaveCornice', (0, 0, base + h - .05), (width + .23, depth + .23, .22), 'stone')
    b.joint(walls, cornice, 2)
    # Plaster gables with timber braces sit beneath the curved shingle roofs.
    for yy in (-depth / 2, depth / 2):
        b.add('Gable' + str(yy), [(-width / 2, yy, base + h), (width / 2, yy, base + h), (0, yy, base + h + width * .40)], [(0, 1, 2)], 'plaster')
        b.span('GableKingPost' + str(yy), (0, yy, base + h - .04), (0, yy, base + h + width * .38), .09, .08, 'oak')
    roof_part = roof(b, 'MainRoof', width, depth, base + h + .02, width * .40, roofkind)
    b.joint(cornice, roof_part, 2, .015)
    if width >= 4:
        for j in range(max(1, round(depth / 2.6))):
            dormer(b, f'Dormer{j}', width * .29, -depth / 2 + (j + .5) * depth / max(1, round(depth / 2.6)), base + h + width * .16, roofkind)
    # Stone chimney with rim, dark throat, and individual course joints.
    cx, cy = width * .29, depth * .22
    cz = base + h + width * .31
    b.box('ChimneyShaft', (cx, cy, cz + .45), (.58, .62, 1.4), 'stone')
    b.box('ChimneyCornice', (cx, cy, cz + 1.12), (.75, .78, .18), 'stone')
    b.box('ChimneyThroat', (cx, cy, cz + 1.23), (.46, .49, .035), 'darkwood')
    for zz in (cz + .1, cz + .45, cz + .8):
        b.box('ChimneyBand' + str(zz), (cx, cy - .32, zz), (.6, .035, .035), 'plaster')
    if kind in ('shop', 'workshop', 'healer'):
        awning(b, 'Porch', (-width * .27, fy - 1.25, 2.45), width * .72, 1.8, 'ivory' if kind == 'healer' else 'red')
    if kind == 'healer':
        banner(b, 'HerbalBanner', width * .35, fy - .22, h - .25, .70, 1.7, 'ivory')
    if kind == 'workshop':
        b.span('ShopSignBracket', (width * .35, fy, 2.9), (width * .35, fy - 1.0, 2.9), .05, .05, 'iron')
        b.box('WorkshopSign', (width * .35, fy - .75, 2.51), (.8, .10, .48), 'oak', 'Joinery')
    if floors >= 3 and kind == 'house':
        x, y, zz = -width * .22, fy - .67, 3.45
        b.box('BalconyDeck', (x, y, zz), (2.15, 1.35, .17), 'oak', 'Joinery')
        for xx in (x - .9, x, x + .9):
            b.span('BalconyCorbel' + str(xx), (xx, fy + .08, zz - .65), (xx, y - .64, zz - .06), .075, .08, 'oak')
        b.span('BalconyToprail', (x - 1.11, y - .70, zz + .92), (x + 1.11, y - .70, zz + .92), .04, .055, 'oak')
        for i in range(11):
            xx = x - 1.05 + i * .21
            b.span('BalconySpindle' + str(i), (xx, y - .70, zz + .02), (xx, y - .70, zz + .95), .026, .027, 'darkwood')
    return b.finish() if finish else b

def awning(b, name, center, width, depth, color='red'):
    x, y, z = center
    strips = max(6, round(width / .36))
    for i in range(strips):
        xx = x - width / 2 + i * width / strips
        verts = []
        for j in range(9):
            t = j / 8
            zz = z + .32 * t - .11 * math.sin(t * math.pi)
            verts.extend([(xx, y + (t - .5) * depth, zz), (xx + width / strips, y + (t - .5) * depth, zz)])
        faces = [(j * 2, j * 2 + 1, j * 2 + 3, j * 2 + 2) for j in range(8)]
        b.add(name + '_Canvas' + str(i), verts, faces, color if i % 2 else 'ivory', 'Cloth', True)
        # Scalloped valance, genuinely shaped geometry.
        pts = [(xx, y - depth / 2, z), (xx + width / strips, y - depth / 2, z)]
        pts += [(xx + width / strips * (1 - k / 8), y - depth / 2 - .01, z - .13 - .12 * math.sin(k / 8 * math.pi)) for k in range(9)]
        b.add(name + '_Scallop' + str(i), pts, [tuple(range(len(pts)))], color if i % 2 else 'ivory', 'Cloth')
    for sign in (-1, 1):
        xx = x + sign * (width / 2 - .06)
        post = b.span(name + '_Post' + str(sign), (xx, y - depth / 2, 0), (xx, y - depth / 2, z + .08), .06, .06, 'oak')
        beam = b.span(name + '_Brace' + str(sign), (xx, y - depth / 2, z - .55), (xx, y + depth / 2, z + .30), .04, .04, 'oak')
        b.joint(post, beam, 2)
    b.span(name + '_FrontPole', (x - width / 2 - .05, y - depth / 2, z + .03), (x + width / 2 + .05, y - depth / 2, z + .03), .045, .045, 'oak')

def leaf(b, name, center, direction, width=.12, color='leaf'):
    c, d = Vector(center), Vector(direction)
    length = d.length
    if length < .001:
        return
    f = d.normalized()
    ref = Vector((0, 0, 1)) if abs(f.z) < .9 else Vector((1, 0, 0))
    u = f.cross(ref).normalized() * width
    n = f.cross(u).normalized() * width * .17
    verts = [c, c + d * .35 - u, c + d * .78 - u * .60,
             c + d, c + d * .78 + u * .60, c + d * .35 + u,
             c + d * .50 + n]
    b.add(name, verts, [(0, 1, 6), (1, 2, 6), (2, 3, 6), (3, 4, 6), (4, 5, 6), (5, 0, 6)], color, 'Foliage', True)

def merge_component(b, temp, offset=(0, 0, 0), angle=0):
    co, si = math.cos(angle), math.sin(angle)
    ox, oy, oz = offset
    for group, g in temp.groups.items():
        transformed = [(ox + co * v[0] - si * v[1], oy + si * v[0] + co * v[1], oz + v[2]) for v in g['v']]
        tgt = b.groups[group]
        off = len(tgt['v'])
        tgt['v'].extend(transformed)
        tgt['f'].extend([tuple(off + i for i in f) for f in g['f']])
        tgt['uv'].extend(g['uv']); tgt['smooth'].extend(g['smooth']); tgt['parts'].extend(g['parts'])
        for name in set(g['parts']):
            indices = set(i for f, n in zip(g['f'], g['parts']) if n == name for i in f)
            vs = [transformed[i] for i in indices]
            b.bounds[name] = [(min(v[i] for v in vs), max(v[i] for v in vs)) for i in range(3)]

def dormer(b, name, x, y, z, kind):
    temp = Builder('temporary', '', '')
    temp.box(name + '_Cheeks', (0, 0, .45), (1.15, 1.1, .9), 'plaster')
    opening(temp, name + '_Pane', 0, -.60, .13, .62, .80)
    temp.add(name + '_Gable', [(-.58, -.55, .9), (.58, -.55, .9), (0, -.55, 1.36)], [(0, 1, 2)], 'plaster')
    roof(temp, name + '_Roof', 1.15, 1.1, .93, .46, kind)
    merge_component(b, temp, (x, y, z), math.pi / 2)

def turret_roof(b, name, x, y, z, radius, rise, sides=8, kind='slate'):
    rings = max(6, round(rise / .24))
    for row in range(rings):
        t0, t1 = row / rings, (row + 1.10) / rings
        r0, r1 = radius * (1 - t0), max(.035, radius * (1 - t1))
        for i in range(sides):
            phase = math.pi / 4 if sides == 4 else 0
            a0, a1 = phase + (i + .003) * math.pi * 2 / sides, phase + (i + .997) * math.pi * 2 / sides
            verts = [(x + r0 * math.cos(a0), y + r0 * math.sin(a0), z + rise * t0),
                     (x + r0 * math.cos(a1), y + r0 * math.sin(a1), z + rise * t0),
                     (x + r1 * math.cos(a1), y + r1 * math.sin(a1), z + rise * t1),
                     (x + r1 * math.cos(a0), y + r1 * math.sin(a0), z + rise * t1)]
            b.add(f'{name}_{row}_{i}', verts, [(0, 1, 2, 3)], kind, 'Roof')
    b.lathe(name + '_Finial', (x, y, z + rise), [(.10, 0), (.12, .11), (.05, .24), (.025, .52)], 'gold', 16)

def guild():
    b = building('adventurers_guild', 'Гильдия авантюристов', 'guild', 9.0, 10.0, 2, 'slate', False)
    fy = -5.1
    # Projecting entry porch: stone arcade with deep timber roof.
    for i, xx in enumerate((-3.2, 0, 3.2)):
        arch(b, 'GuildArcade' + str(i), xx, fy - .80, .45, 2.25, 3.05, trim=.22, depth=.60)
    b.box('ArcadeEntablature', (0, fy - .80, 3.62), (9.0, 1.25, .38), 'stone')
    for xx in (-4.3, -2.5, 2.5, 4.3):
        b.box('GuildButtress' + str(xx), (xx, fy - .10, 3.0), (.43, .60, 5.8), 'stone')
        b.box('ButtressCap' + str(xx), (xx, fy - .13, 5.9), (.61, .79, .19), 'stone')
    for xx in (-3.65, 3.65):
        banner(b, 'GuildStandard' + str(xx), xx, fy - .60, 6.0, .93, 3.05)
    # Tall asymmetrical bell/watch tower is part of the guild silhouette.
    tx, ty, tw, th = -2.8, -2.0, 2.65, 12.4
    b.box('GuildTower', (tx, ty, th / 2), (tw, tw, th), 'plaster')
    stone_courses(b, tw, tw, 1.2, offset=(tx, ty), name='TowerBase')
    for zz in (3.0, 6.0, 9.6, 11.9):
        b.box('TowerString' + str(zz), (tx, ty, zz), (tw + .23, tw + .23, .20), 'stone')
    for sign in (-1, 1):
        for ysign in (-1, 1):
            b.box(f'TowerCorner{sign}{ysign}', (tx + sign * tw / 2, ty + ysign * tw / 2, th / 2), (.29, .29, th), 'stone')
    opening(b, 'TowerLouver', tx, ty - tw / 2 - .075, 9.8, 1.05, 1.6)
    for i in range(7):
        b.box('BellLouver' + str(i), (tx, ty - tw / 2 - .10, 10.0 + i * .16), (.90, .08, .07), 'darkwood', 'Joinery')
    banner(b, 'TowerBanner', tx, ty - tw / 2 - .22, 9.3, 1.20, 4.5)
    turret_roof(b, 'GuildTowerRoof', tx, ty, th, 2.05, 2.8, 4)
    for xx in (-4.0, 4.0):
        turret_roof(b, 'GuildSmallSpire' + str(xx), xx, 2.7, 7.2, .78, 2.2)
    # Narrow front belfry and long blue standard establish the reference silhouette.
    b.box('FrontBelfry', (0, -4.4, 7.55), (2.30, 2.10, 8.30), 'plaster')
    for xx in (-1.16, 1.16):
        b.box('FrontBelfryPilaster' + str(xx), (xx, -5.45, 7.55), (.23, .32, 7.85), 'stone')
        b.lathe('BelfryPinnacle' + str(xx), (xx, -5.44, 11.5), [(.19, 0), (.19, .15), (.10, .32), (.035, .95)], 'stone', 12)
    b.box('BelfryUpperCornice', (0, -4.4, 11.70), (2.65, 2.45, .28), 'stone')
    opening(b, 'BelfryUpperPane', 0, -5.50, 9.6, .90, 1.60)
    banner(b, 'MainGuildStandard', 0, -5.76, 9.25, 1.50, 5.30)
    turret_roof(b, 'FrontBelfryRoof', 0, -4.4, 11.86, 1.88, 3.08, 4)
    return b.finish()

def healer():
    b = building('healing_house', 'Лечебный дом', 'healer', 7.5, 6.2, 2, 'tile', False)
    temp = building('temporary_wing', '', 'house', 3.4, 3.8, 1, 'tile', False)
    # Prefix wing names before merging, preserving editable semantic identifiers.
    for g in temp.groups.values():
        g['parts'] = ['HerbWing_' + n for n in g['parts']]
    merge_component(b, temp, (-4.4, 1.2, 0))
    b.box('HerbBed', (-4.4, -1.6, .27), (2.5, .85, .45), 'stone')
    for i in range(55):
        leaf(b, 'Herb' + str(i), (-4.4 + RNG.uniform(-1.1, 1.1), -1.6 + RNG.uniform(-.35, .35), .48),
             (RNG.uniform(-.12, .12), RNG.uniform(-.12, .12), RNG.uniform(.2, .45)), .065)
    return b.finish()

def shrine():
    b = building('harbor_shrine', 'Прибрежное святилище', 'shrine', 4.2, 6.0, 1, 'slate', False)
    b.box('ShrineBellBase', (0, 1.7, 6.35), (.75, .78, 2.1), 'stone')
    arch(b, 'BellArch', 0, 1.20, 6.0, .70, 1.10, trim=.12, depth=.40)
    b.lathe('BronzeBell', (0, 1.20, 6.02), [(.33, 0), (.30, .09), (.22, .22), (.14, .43), (.09, .48)], 'gold', 24)
    turret_roof(b, 'BellRoof', 0, 1.60, 7.15, .75, .95, 4)
    for xx in (-1.75, 1.75):
        b.box('ShrineButtress' + str(xx), (xx, -3.05, 1.65), (.33, .65, 2.6), 'stone')
        b.lathe('EntryColumn' + str(xx), (xx, -3.60, .12), [(.23, 0), (.23, .14), (.15, .23), (.15, 2.8), (.25, 2.9), (.25, 3.03)], 'stone', 20)
    banner(b, 'ShrineBanner', 1.5, -3.28, 3.2, .53, 1.6, 'ivory')
    return b.finish()

def shed(key, closed=False):
    b = Builder(key, 'Лодочный сарай — ' + ('закрытый' if closed else 'открытый'), 'harbor')
    w, d, z = 5.2, 6.3, 3.2
    for x in (-w / 2, w / 2):
        for j in range(4):
            yy = -d / 2 + j * d / 3
            base = b.box(f'ShedFoot{x}_{j}', (x, yy, .17), (.45, .45, .34), 'stone')
            post = b.span(f'ShedPost{x}_{j}', (x, yy, .12), (x, yy, z + .08), .12, .12, 'darkwood')
            b.joint(base, post, 2, .03)
            b.span(f'ShedKnee{x}_{j}', (x, yy, z - .68), (x - math.copysign(.62, x), yy, z + .035), .08, .08, 'oak')
    for yy in (-d / 2, d / 2):
        beam = b.span('ShedCrossbeam' + str(yy), (-w / 2 - .08, yy, z), (w / 2 + .08, yy, z), .14, .13, 'oak')
        for x in (-w / 2, w / 2):
            b.joint(f'ShedPost{x}_{0 if yy<0 else 3}', beam, 2, .03)
        b.span('ShedKingpost' + str(yy), (0, yy, z - .03), (0, yy, z + 1.74), .11, .1, 'oak')
    for i in range(24):
        xx = -w / 2 + (i + .5) * w / 24
        b.box('ShedFloorboard' + str(i), (xx, 0, .065), (w / 24 - .015, d, .13), 'oak', 'Joinery', .008)
    if closed:
        for side in (-1, 1):
            for j in range(26):
                yy = -d / 2 + (j + .5) * d / 26
                b.box(f'Siding{side}_{j}', (side * w / 2, yy, 1.62), (.12, d / 26 - .013, 2.9), 'oak', 'Joinery', .008)
        for j in range(24):
            xx = -w / 2 + (j + .5) * w / 24
            b.box('RearSiding' + str(j), (xx, d / 2, 1.62), (w / 24 - .013, .13, 2.9), 'darkwood', 'Joinery', .008)
        for sign in (-1, 1):
            for i in range(8):
                b.box(f'BoatDoor{sign}_{i}', (sign * 1.02 + (i - 3.5) * .25, -d / 2 - .08, 1.4), (.24, .13, 2.7), 'oak', 'Joinery')
            b.span('DoorBrace' + str(sign), (sign * .13, -d / 2 - .17, .3), (sign * 1.9, -d / 2 - .17, 2.5), .065, .06, 'darkwood')
    else:
        for yy in (-1.5, .6, 2.4):
            b.box('BoatCradle' + str(yy), (0, yy, .25), (2.2, .30, .36), 'oak', 'Joinery')
        for j in range(12):
            b.box('RearSlat' + str(j), (-w / 2 + (j + .5) * w / 12, d / 2, 1.5), (.22, .13, 2.7), 'darkwood', 'Joinery')
        b.span('HangingBeam', (-w / 2, .8, 2.75), (w / 2, .8, 2.75), .07, .06, 'oak')
        rope_coil(b, 'ShedRope', (1.7, .8, 1.9), .28, vertical=True)
    roof(b, 'ShedRoof', w, d, z + .06, 1.8, 'oak' if closed else 'slate')
    for yy in (-d / 2, d / 2):
        b.add('ShedGable' + str(yy), [(-w / 2, yy, z), (w / 2, yy, z), (0, yy, z + 1.8)], [(0, 1, 2)], 'darkwood', 'Joinery')
    return b.finish()

def watchtower():
    b = Builder('harbor_watchtower', 'Портовая сигнальная башня', 'harbor')
    b.lathe('TowerBase', (0, 0, 0), [(1.65, 0), (1.65, .35), (1.45, .48), (1.35, 1.0)], 'stone', 8)
    b.lathe('TowerShaft', (0, 0, .75), [(1.35, 0), (1.25, 6.1), (1.26, 6.35)], 'plaster', 8)
    for zz in (1.0, 3.5, 6.8):
        b.lathe('TowerBelt' + str(zz), (0, 0, zz), [(1.39, 0), (1.39, .15), (1.29, .23)], 'stone', 8)
    for i in range(8):
        a = math.pi / 8 + i * math.pi / 4
        b.span('TowerCorner' + str(i), (1.32 * math.cos(a), 1.32 * math.sin(a), .85),
               (1.25 * math.cos(a), 1.25 * math.sin(a), 6.9), .055, .055, 'stone')
    opening(b, 'TowerDoor', 0, -1.28, .28, .80, 1.9, True)
    opening(b, 'TowerSlit', 0, -1.25, 4.8, .45, 1.2)
    b.lathe('WatchBalcony', (0, 0, 6.95), [(1.32, 0), (1.65, .20), (1.65, .40)], 'stone', 8)
    for i in range(8):
        a = i * math.pi / 4
        x, y = 1.28 * math.cos(a), 1.28 * math.sin(a)
        b.span('LanternPost' + str(i), (x, y, 7.25), (x, y, 8.9), .075, .075, 'darkwood')
        a1 = (i + 1) * math.pi / 4
        b.span('BalconyRail' + str(i), (1.6 * math.cos(a), 1.6 * math.sin(a), 7.95), (1.6 * math.cos(a1), 1.6 * math.sin(a1), 7.95), .045, .045, 'oak')
    turret_roof(b, 'WatchRoof', 0, 0, 8.8, 1.95, 1.75, 8)
    banner(b, 'HarborSignal', .62, -1.49, 6.45, .70, 2.4)
    return b.finish()

def rope_coil(b, name, center, radius=.3, vertical=False):
    x, y, z = center
    pts = []
    for i in range(361):
        a = i / 360 * math.pi * 2 * 4.5
        r = radius * (.45 + .55 * i / 360)
        pts.append((x + r * math.cos(a), y + (.025 * i / 360 if vertical else r * math.sin(a)),
                    z + (r * math.sin(a) if vertical else .025 * i / 360)))
    b.ribbon(name, pts, .023, 'ivory', 'Detail', 6)

def dock(key, shape='straight'):
    b = Builder(key, 'Причал — ' + shape, 'harbor')
    w, d = (2.5, 6.0) if shape != 'landing' else (5.0, 4.0)
    for i in range(round(d / .25)):
        yy = -d / 2 + (i + .5) * d / round(d / .25)
        b.box('DockPlank' + str(i), (0, yy, 1.13), (w + RNG.uniform(-.06, .06), .235, .18), 'oak', 'Deck', .01)
    for x in (-w / 2 + .22, w / 2 - .22):
        for yy in (-d / 2 + .20, 0, d / 2 - .20):
            pile = b.lathe('Pile' + str(x) + str(yy), (x, yy, -1.7), [(.17, 0), (.16, 3.25), (.18, 3.35)], 'darkwood', 12, 'Joinery')
            beam = b.span('CrossBearer' + str(x) + str(yy), (-w / 2 - .08, yy, .98), (w / 2 + .08, yy, .98), .10, .12, 'oak')
            b.joint(pile, beam, 2, .03)
            plank = 'DockPlank' + str(max(0, min(round(d / .25) - 1, int((yy + d / 2) / .25))))
            b.joint(beam, plank, 2, .02)
            for z in (1.36, 1.45):
                pts = [(x + .185 * math.cos(i * math.pi * 2 / 24), yy + .185 * math.sin(i * math.pi * 2 / 24), z) for i in range(25)]
                b.ribbon('PileBinding' + str(x) + str(yy) + str(z), pts, .015, 'ivory')
            b.span('Diagonal' + str(x) + str(yy), (x, yy, -.8), (x, min(d / 2, yy + 1.3), .98), .065, .065, 'oak')
    if shape == 'landing':
        rope_coil(b, 'LandingRope', (1.4, 1.0, 1.25), .40)
    return b.finish()

def fountain():
    b = Builder('square_fountain', 'Фонтан центральной площади', 'civic')
    b.lathe('FountainSteps', (0, 0, 0), [(2.95, 0), (2.95, .17), (2.7, .17), (2.7, .34), (2.48, .34), (2.48, .52)], 'stone', 64)
    basin = b.lathe('CarvedBasin', (0, 0, .42), [(2.4, 0), (2.48, .12), (2.42, .25), (2.30, .32), (2.10, .30), (2.10, .14)], 'stone', 64)
    b.lathe('BasinWater', (0, 0, .57), [(2.12, 0), (2.12, .01)], 'water', 64)
    plinth = b.lathe('StatuePlinth', (0, 0, .48), [(.72, 0), (.72, .22), (.51, .32), (.50, 1.4), (.67, 1.49), (.67, 1.62)], 'stone', 32)
    b.joint(basin, plinth, 2, .04)
    b.lathe('UpperBowl', (0, 0, 1.87), [(.32, 0), (.65, .18), (1.08, .30), (1.13, .43), (1.02, .50), (.80, .45)], 'stone', 48)
    b.lathe('UpperWater', (0, 0, 2.27), [(.91, 0), (.91, .012)], 'water', 48)
    # Original cloaked navigator statue, holding a small compass sphere.
    b.lathe('StatueRobe', (0, 0, 2.18), [(.37, 0), (.40, .08), (.30, .55), (.23, 1.12), (.27, 1.26), (.14, 1.35)], 'slate', 24)
    b.lathe('StatueHead', (0, 0, 3.52), [(.09, 0), (.17, .10), (.18, .23), (.13, .35), (.025, .40)], 'slate', 24)
    b.span('StatueArm', (.19, 0, 3.36), (.61, -.08, 3.15), .075, .08, 'slate', sides=12)
    b.lathe('CompassOrb', (.62, -.08, 3.21), [(.04, 0), (.15, .08), (.17, .17), (.12, .27), (.025, .31)], 'gold', 24)
    for i in range(12):
        a = i * math.pi * 2 / 12
        b.ribbon('RobeFold' + str(i), [(.37 * math.cos(a), .37 * math.sin(a), 2.3), (.29 * math.cos(a + .1), .29 * math.sin(a + .1), 2.9), (.22 * math.cos(a), .22 * math.sin(a), 3.4)], .016, 'stone')
    for i in range(8):
        a = i * math.pi / 4
        pts = [(math.cos(a) * (1.0 + t * 1.1), math.sin(a) * (1.0 + t * 1.1), 2.3 - 1.7 * t * t) for t in np.linspace(0, 1, 18)]
        b.ribbon('WaterSpill' + str(i), pts, .025, 'water', sides=8)
    return b.finish()

def boat(key, sailing=False):
    b = Builder(key, 'Рыбацкий парусник' if sailing else 'Гребная рыбацкая лодка', 'harbor')
    length, width = (6.0, 2.05) if sailing else (4.1, 1.4)
    stations, sections = 28, 14
    def hullpoint(t, s, inner=False):
        yy = (t - .5) * length
        half = max(.055, width / 2 * max(0, 1 - abs(2 * t - 1) ** 2) ** .65)
        if inner: half = max(.035, half - .06)
        zz = .38 + .28 * abs(2 * t - 1) ** 3 - .84 * (1 - abs(s) ** 1.7)
        if inner: zz += .075
        return (s * half, yy, zz)
    for inside in (False, True):
        verts = [hullpoint(j / stations, -1 + i * 2 / sections, inside) for j in range(stations + 1) for i in range(sections + 1)]
        faces = [(j * (sections + 1) + i, j * (sections + 1) + i + 1,
                  (j + 1) * (sections + 1) + i + 1, (j + 1) * (sections + 1) + i)
                 for j in range(stations) for i in range(sections)]
        b.add('InnerHull' if inside else 'OuterHull', verts, faces, 'oak' if inside else 'darkwood', 'Hull', True)
    for side in (-1, 1):
        b.ribbon('Gunwale' + str(side), [hullpoint(j / stations, side) for j in range(stations + 1)], .065, 'oak', 'Hull', 10)
        for k in range(1, 5):
            s = side * (.20 + k * .15)
            b.ribbon(f'ClinkerSeam{side}_{k}', [hullpoint(j / stations, s) for j in range(stations + 1)], .019, 'oak', 'Hull', 6)
    b.ribbon('Keel', [hullpoint(j / stations, 0) for j in range(stations + 1)], .06, 'oak', 'Hull', 8)
    for j in range(2, 10):
        t = j / 11
        b.ribbon('Rib' + str(j), [hullpoint(t, -1 + i * 2 / sections, True) for i in range(sections + 1)], .028, 'oak', 'Joinery', 6)
    for j, t in enumerate((.24, .42, .64, .78)):
        left, right = hullpoint(t, -.94, True), hullpoint(t, .94, True)
        b.span('Thwart' + str(j), (left[0], left[1], .28), (right[0], right[1], .28), .13, .045, 'oak')
    # Rudder and stern tiller, actual directional geometry.
    b.box('Rudder', (0, -length / 2 - .04, -.23), (.12, .48, .82), 'oak', 'Joinery')
    b.span('Tiller', (0, -length / 2, .63), (0, -length / 2 + .75, .7), .035, .04, 'oak')
    if sailing:
        mast = b.span('Mast', (0, -.35, -.31), (0, -.35, 5.5), .070, .070, 'oak', sides=12)
        b.box('MastPartner', (0, -.35, .23), (.36, .28, .20), 'oak', 'Joinery')
        b.joint(mast, 'MastPartner', 2, .03)
        b.span('Boom', (-.12, -.35, 1.05), (2.4, -.35, 1.52), .048, .048, 'oak', sides=12)
        b.span('Yard', (-.10, -.35, 5.03), (2.6, -.35, 1.72), .042, .042, 'oak', sides=12)
        verts, faces, ids = [], [], {}
        n = 18
        p0, p1, p2 = Vector((0, -.35, 4.95)), Vector((.02, -.35, 1.18)), Vector((2.43, -.35, 1.59))
        for j in range(n + 1):
            for i in range(n + 1 - j):
                u, v = i / n, j / n
                p = p0 * (1 - u - v) + p1 * u + p2 * v
                p.y += .55 * math.sin(math.pi * u) * math.sin(math.pi * v) * math.sin(math.pi * (u + v))
                ids[j, i] = len(verts); verts.append(p)
        for j in range(n):
            for i in range(n - j):
                faces.append((ids[j, i], ids[j, i + 1], ids[j + 1, i]))
                if i < n - j - 1:
                    faces.append((ids[j, i + 1], ids[j + 1, i + 1], ids[j + 1, i]))
        b.add('BillowingSail', verts, faces, 'ivory', 'Sail', True)
        for i, (a, c) in enumerate(((p0, p1), (p1, p2), (p2, p0))):
            b.ribbon('SailBoltRope' + str(i), [a.lerp(c, t) for t in np.linspace(0, 1, 25)], .012, 'ivory', sides=6)
        for j, pt in enumerate(((0, length / 2, .59), (0, -length / 2, .58), (-width / 2, 0, .4), (width / 2, 0, .4))):
            b.ribbon('StandingRigging' + str(j), [(0, -.35, 5.3), pt], .014, 'ivory', sides=6)
        rope_coil(b, 'DeckRope', (.30, .83, -.13), .22)
    else:
        for sign in (-1, 1):
            b.span('OarShaft' + str(sign), (sign * .38, -.8, .38), (sign * 1.63, .3, .19), .025, .025, 'oak', sides=10)
            b.span('OarBlade' + str(sign), (sign * 1.6, .27, .2), (sign * 2.04, .67, .13), .105, .025, 'oak')
            b.span('Oarlock' + str(sign), (sign * width * .45, -.45, .3), (sign * width * .45, -.45, .52), .025, .025, 'iron')
    return b.finish()

def barrel(b, name, pos=(0, 0, 0), scale=1):
    x, y, z = pos
    profile = [(.32 * scale, 0), (.38 * scale, .12 * scale), (.44 * scale, .54 * scale), (.38 * scale, .94 * scale), (.32 * scale, 1.04 * scale)]
    for i in range(18):
        a0, a1 = (i + .02) * math.pi * 2 / 18, (i + .98) * math.pi * 2 / 18
        verts = [(x + r * math.cos(a), y + r * math.sin(a), z + h) for r, h in profile for a in (a0, a1)]
        faces = [(j * 2, j * 2 + 1, j * 2 + 3, j * 2 + 2) for j in range(len(profile) - 1)]
        b.add(name + '_Stave' + str(i), verts, faces, 'oak', 'Joinery', True)
    for zz, r in ((.10, .38), (.26, .415), (.78, .415), (.95, .36)):
        b.lathe(name + '_Hoop' + str(zz), (x, y, z + zz * scale), [(r * scale, 0), (r * scale, .055 * scale), ((r - .018) * scale, .055 * scale), ((r - .018) * scale, 0)], 'iron', 36)
    b.lathe(name + '_Lid', (x, y, z + 1.02 * scale), [(.31 * scale, 0), (.31 * scale, .035 * scale)], 'oak', 36, 'Joinery')
    for j in range(-2, 3):
        xx = j * .105 * scale
        length = math.sqrt(max(0, (.30 * scale) ** 2 - xx * xx))
        b.span(name + '_LidSeam' + str(j), (x + xx, y - length, z + 1.056 * scale), (x + xx, y + length, z + 1.056 * scale), .003, .002, 'darkwood')

def crate(b, name, pos=(0, 0, 0), dims=(.9, .7, .75), open_top=False):
    x, y, z = pos; w, d, h = dims
    for side in (-1, 1):
        for j in range(5):
            zz = z + (j + .5) * h / 5
            b.box(f'{name}_Front{side}_{j}', (x, y + side * d / 2, zz), (w, .075, h / 5 - .012), 'oak', 'Joinery', .007)
            b.box(f'{name}_Side{side}_{j}', (x + side * w / 2, y, zz), (.075, d, h / 5 - .012), 'oak', 'Joinery', .007)
        b.span(name + '_Brace' + str(side), (x - w / 2, y + side * (d / 2 + .06), z + .06), (x + w / 2, y + side * (d / 2 + .06), z + h - .06), .045, .035, 'darkwood')
    for xx in (-w / 2, w / 2):
        for yy in (-d / 2, d / 2):
            b.box(f'{name}_Corner{xx}_{yy}', (x + xx, y + yy, z + h / 2), (.13, .13, h + .05), 'darkwood', 'Joinery')
    if not open_top:
        for j in range(6):
            b.box(name + '_Lid' + str(j), (x - w / 2 + (j + .5) * w / 6, y, z + h), (w / 6 - .014, d, .07), 'oak', 'Joinery')

def market_stall(key, color='red', goods='fruit'):
    b = Builder(key, 'Рыночная лавка — ' + goods, 'market')
    awning(b, 'MarketCanopy', (0, 0, 2.32), 3.4, 2.2, color)
    for x in (-1.64, 1.64):
        b.span('RearCanopyPost' + str(x), (x, 1.1, 0), (x, 1.1, 2.68), .06, .06, 'oak')
    b.span('RearCanopyBeam', (-1.7, 1.1, 2.66), (1.7, 1.1, 2.66), .05, .05, 'oak')
    for j in range(11):
        b.box('CounterBoard' + str(j), (-1.47 + (j + .5) * 2.94 / 11, -.32, 1.04), (.253, 1.08, .13), 'oak', 'Joinery')
    for x in (-1.42, 1.42):
        b.box('CounterLeg' + str(x), (x, -.32, .55), (.13, 1.05, 1.10), 'darkwood', 'Joinery')
    for i, x in enumerate((-1.02, 0, 1.02)):
        crate(b, 'ProduceBin' + str(i), (x, -.26, 1.10), (.87, .82, .26), True)
        for j in range(17):
            px, py = x + RNG.uniform(-.32, .32), -.26 + RNG.uniform(-.29, .29)
            if goods == 'fruit':
                b.lathe(f'Apple{i}_{j}', (px, py, 1.29 + RNG.uniform(0, .07)), [(.025, 0), (.079, .025), (.087, .079), (.055, .136), (.022, .143)], 'red' if i % 2 else 'gold', 12, 'Goods')
            else:
                b.lathe(f'Bread{i}_{j}', (px, py, 1.30), [(.09, 0), (.11, .04), (.095, .11), (.02, .15)], 'ivory', 12, 'Goods')
    for i in range(7):
        b.box('CounterSlat' + str(i), (-1.4 + i * .47, -.91, .54), (.45, .07, .88), 'oak', 'Joinery')
    return b.finish()

def training_dummy():
    b = Builder('training_dummy', 'Тренировочный манекен Дорна', 'training')
    base = b.box('DummyBase', (0, 0, .09), (.9, .72, .18), 'darkwood', 'Joinery')
    post = b.span('DummyPost', (0, 0, .1), (0, 0, 1.85), .065, .065, 'oak')
    b.joint(base, post, 2, .02)
    b.lathe('StrawTorso', (0, 0, .8), [(.21, 0), (.29, .2), (.27, .70), (.15, .83)], 'ivory', 24)
    b.lathe('StrawHead', (0, 0, 1.7), [(.08, 0), (.17, .08), (.18, .25), (.12, .35), (.02, .39)], 'ivory', 24)
    b.span('DummyArms', (-.67, 0, 1.4), (.67, 0, 1.4), .06, .07, 'oak')
    for z in (1.0, 1.16, 1.45, 1.55):
        pts = [(.285 * math.cos(i * math.pi / 16), .285 * math.sin(i * math.pi / 16), z) for i in range(33)]
        b.ribbon('StrawBinding' + str(z), pts, .022, 'darkwood', sides=6)
    for i in range(28):
        a = RNG.random() * 2 * math.pi
        b.ribbon('StrawStem' + str(i), [(.27 * math.cos(a), .27 * math.sin(a), .85), (.26 * math.cos(a), .26 * math.sin(a), 1.3), (.20 * math.cos(a), .20 * math.sin(a), 1.65)], .006, 'gold', sides=4)
    return b.finish()

def pavilion():
    b = Builder('dorn_pavilion', 'Навес тренировочного двора Дорна', 'training')
    for x in (-3.0, 3.0):
        for y in (-1.7, 1.7):
            b.box('PavilionFoot' + str(x) + str(y), (x, y, .14), (.50, .50, .28), 'stone')
            b.span('PavilionPost' + str(x) + str(y), (x, y, .12), (x, y, 3.05), .12, .12, 'oak')
            b.span('PavilionBrace' + str(x) + str(y), (x, y, 2.27), (x - math.copysign(.75, x), y, 3.0), .07, .065, 'darkwood')
    for y in (-1.7, 1.7):
        b.span('PavilionBeam' + str(y), (-3.15, y, 3.0), (3.15, y, 3.0), .15, .13, 'oak')
    roof(b, 'PavilionRoof', 6.0, 3.4, 3.10, 1.5, 'slate')
    banner(b, 'DornPennantLeft', -2.4, -1.85, 2.90, .62, 1.55)
    banner(b, 'DornPennantRight', 2.4, -1.85, 2.90, .62, 1.55)
    return b.finish()

def weapon_rack():
    b = Builder('training_weapon_rack', 'Стойка тренировочного оружия', 'training')
    for x in (-1.02, 1.02):
        b.box('RackFoot' + str(x), (x, 0, .08), (.34, .8, .16), 'oak', 'Joinery')
        b.span('RackPost' + str(x), (x, 0, .1), (x, 0, 1.7), .065, .065, 'darkwood')
    for zz in (.35, 1.4):
        b.span('RackCrossbar' + str(zz), (-1.08, 0, zz), (1.08, 0, zz), .08, .06, 'oak')
    for i in range(6):
        x = -.84 + i * .33
        b.span('WoodenSwordBlade' + str(i), (x, -.09, .43), (x + .10, -.09, 1.42), .043, .022, 'oak')
        b.span('SwordGrip' + str(i), (x + .10, -.09, 1.42), (x + .12, -.09, 1.69), .025, .025, 'darkwood', sides=10)
        b.span('SwordGuard' + str(i), (x - .03, -.09, 1.43), (x + .24, -.09, 1.43), .025, .03, 'oak')
    return b.finish()

def props():
    results = []
    b = Builder('cargo_barrel', 'Деревянная бочка', 'props'); barrel(b, 'Barrel'); results.append(b.finish())
    b = Builder('cargo_crate', 'Транспортный ящик', 'props'); crate(b, 'Crate'); results.append(b.finish())
    b = Builder('cargo_sacks', 'Связка мешков с грузом', 'props')
    for i, (x, y) in enumerate(((-.30, 0), (.28, .13), (0, -.32))):
        b.lathe('Sack' + str(i), (x, y, 0), [(.25, 0), (.33, .10), (.34, .35), (.27, .60), (.10, .76), (.14, .82)], 'ivory', 24, 'Goods')
        b.lathe('SackTie' + str(i), (x, y, .74), [(.105, 0), (.105, .04)], 'darkwood', 20)
    results.append(b.finish())
    b = Builder('mooring_rope', 'Бухта швартового каната', 'harbor'); rope_coil(b, 'Coil', (0, 0, .024), .48); results.append(b.finish())
    b = Builder('harbor_bollard', 'Каменный кнехт', 'harbor')
    b.lathe('Bollard', (0, 0, 0), [(.27, 0), (.27, .10), (.18, .18), (.18, .64), (.28, .70), (.28, .81)], 'stone', 24)
    results.append(b.finish())
    b = Builder('public_bench', 'Деревянная городская скамья', 'props')
    for x in (-.8, .8):
        b.box('BenchFoot' + str(x), (x, 0, .23), (.16, .55, .46), 'stone')
        b.span('BackPost' + str(x), (x, .20, .18), (x, .29, 1.1), .055, .045, 'oak')
    for j in range(4):
        b.box('SeatSlat' + str(j), (0, -.24 + (j + .5) * .12, .49), (2.0, .11, .095), 'oak', 'Joinery')
    for j in range(3):
        b.box('BackSlat' + str(j), (0, .26, .7 + j * .15), (2.0, .085, .12), 'oak', 'Joinery')
    results.append(b.finish())
    b = Builder('street_lantern', 'Уличный фонарь', 'props')
    b.lathe('LampFoot', (0, 0, 0), [(.25, 0), (.25, .12), (.13, .26), (.10, .5)], 'stone', 24)
    b.span('LampPole', (0, 0, .35), (0, 0, 3.4), .042, .042, 'iron', sides=12)
    b.ribbon('CurledBracket', [(0, 0, 3.22), (.12, 0, 3.56), (.45, 0, 3.70), (.67, 0, 3.58), (.63, 0, 3.40)], .035, 'iron')
    b.box('LampGlass', (.62, 0, 3.02), (.30, .28, .54), 'ivory', 'Glazing', .025)
    for xx in (.44, .80):
        for yy in (-.16, .16):
            b.span('LampFrame' + str(xx) + str(yy), (xx, yy, 2.74), (xx, yy, 3.33), .025, .025, 'iron')
    turret_roof(b, 'LampCap', .62, 0, 3.3, .32, .22, 4)
    results.append(b.finish())
    b = Builder('guild_banner_pole', 'Стойка с городским знаменем', 'civic')
    b.lathe('BannerFoot', (0, 0, 0), [(.32, 0), (.32, .18), (.21, .28), (.14, .40)], 'stone', 24)
    b.span('BannerPole', (0, 0, .3), (0, 0, 4.8), .048, .048, 'oak', sides=12)
    banner(b, 'FreestandingStandard', .57, -.07, 4.3, .9, 2.2)
    b.lathe('PoleFinial', (0, 0, 4.78), [(.07, 0), (.12, .10), (.07, .24), (.01, .40)], 'gold', 20)
    results.append(b.finish())
    return results

def wall_module(key, kind='retaining'):
    b = Builder(key, 'Каменный модуль — ' + kind, 'modular')
    if kind == 'stairs':
        for j in range(8):
            height = (j + 1) * .19
            b.box('Step' + str(j), (0, -1.6 + (j + .5) * .4, height / 2), (2.8, .42, height), 'stone', bevel=.015)
            b.box('Nosing' + str(j), (0, -1.6 + j * .4, height - .02), (2.86, .12, .10), 'stone', bevel=.015)
        for sign in (-1, 1):
            b.span('StairParapet' + str(sign), (sign * 1.56, -1.6, .6), (sign * 1.56, 1.6, 2.12), .16, .25, 'stone')
    elif kind == 'balustrade':
        b.box('BalustradeFoot', (0, 0, .12), (4.0, .50, .24), 'stone')
        b.box('BalustradeCap', (0, 0, 1.15), (4.1, .47, .16), 'stone')
        for j in range(9):
            xx = -1.85 + j * 3.7 / 8
            b.lathe('Baluster' + str(j), (xx, 0, .2), [(.12, 0), (.12, .10), (.075, .2), (.13, .42), (.095, .64), (.08, .77), (.13, .88)], 'stone', 16)
    elif kind == 'cobbles':
        for row in range(10):
            for col in range(8):
                xx = -1.0 + (col + .5) * .25
                yy = -1.0 + (row + .5) * .20
                b.box(f'Cobble{row}_{col}', (xx, yy, .04), (.235, .186, .08 + RNG.uniform(-.005, .005)), 'stone', bevel=.023)
    else:
        h = 2.8 if kind == 'retaining' else 1.25
        b.box('WallCore', (0, 0, h / 2), (4.0, .62, h), 'stone')
        stone_courses(b, 4.0, .62, h, name='WallCourse')
        b.box('WallCap', (0, 0, h + .075), (4.03, .88, .18), 'stone')
        for xx in (-1.72, 1.72):
            b.box('WallPier' + str(xx), (xx, -.38, h / 2), (.38, .40, h + .07), 'stone')
    return b.finish()

def bridge():
    b = Builder('harbor_arch_bridge', 'Каменный арочный мост гавани', 'modular')
    w, depth, z, radius = 6.8, 2.6, 2.4, 2.2
    for y in (-depth / 2, depth / 2):
        arch(b, 'BridgeArch' + str(y), 0, y, .0, radius * 2, radius + .2, trim=.33, depth=.43)
        for sign in (-1, 1):
            b.box('BridgeAbutment' + str(y) + str(sign), (sign * 2.80, y, z / 2), (1.10, .65, z), 'stone')
        # Spandrels fill only above the arch; the passage remains open.
        for i in range(24):
            x0, x1 = -radius + i * radius * 2 / 24, -radius + (i + 1) * radius * 2 / 24
            h0 = .2 + math.sqrt(max(0, radius ** 2 - x0 ** 2)) + .28
            h1 = .2 + math.sqrt(max(0, radius ** 2 - x1 ** 2)) + .28
            b.add(f'Spandrel{y}_{i}', [(x0, y, h0), (x1, y, h1), (x1, y, z + .3), (x0, y, z + .3)], [(0, 1, 2, 3)], 'stone')
    b.box('BridgeDeck', (0, 0, z + .38), (w, depth + .10, .32), 'stone')
    for y in (-depth / 2, depth / 2):
        b.box('BridgeParapet' + str(y), (0, y, z + .90), (w, .32, .90), 'stone')
        b.box('BridgeRail' + str(y), (0, y, z + 1.39), (w + .12, .47, .16), 'stone')
        for i in range(10):
            b.box('BridgeParapetJoint' + str(y) + str(i), (-w / 2 + (i + .5) * w / 10, y - .17, z + .90), (w / 10 - .022, .03, .80), 'stone')
    return b.finish()

def farm_fence():
    b = Builder('orchard_fence', 'Ограда садов и пастбищ', 'modular')
    for x in (-2, 0, 2):
        b.span('FencePost' + str(x), (x, 0, 0), (x, 0, 1.2), .085, .085, 'oak')
        b.lathe('PostCap' + str(x), (x, 0, 1.19), [(.12, 0), (.10, .09), (.035, .16)], 'oak', 8, 'Joinery')
    for zz in (.43, .94):
        b.span('FenceRail' + str(zz), (-2.05, 0, zz), (2.05, 0, zz), .065, .07, 'oak')
    for x in (-1, 1):
        b.span('FenceDiagonal' + str(x), (x - .86, -.03, .34), (x + .86, -.03, 1.0), .035, .045, 'darkwood')
    return b.finish()

def tree(key, blossom=False, cypress=False):
    b = Builder(key, 'Кипарис' if cypress else ('Цветущее плодовое дерево' if blossom else 'Плодовое дерево'), 'vegetation')
    height = 7.2 if cypress else 5.5
    b.branch('TreeTrunk', [(0, 0, .27), (.10, -.03, .7), (-.06, .05, 1.6), (.08, .1, 2.7), (.04, .03, 3.7), (.12, .1, height * .86)], [.28, .24, .19, .13, .095, .025], sides=14)
    for i in range(6):
        a = i * math.pi / 3
        b.branch('Root' + str(i), [(0, 0, .37), (.20 * math.cos(a), .20 * math.sin(a), .13), (.56 * math.cos(a), .56 * math.sin(a), .033)], [.18, .12, .027])
    clusters = []
    if cypress:
        for j in range(26):
            t = j / 26
            radius = .82 * math.sin(math.pi * (.18 + .82 * t)) ** .7
            for i in range(7):
                a = i * math.pi * 2 / 7 + j * .23
                end = Vector((radius * math.cos(a), radius * math.sin(a), 1.2 + t * 5.7))
                b.span(f'CypressBranch{j}_{i}', (0, 0, end.z - .13), end, .013, .013, 'darkwood', 'Branches', 6)
                clusters.append((end, .24, .36))
    else:
        for i in range(10):
            a = i * math.pi * 2 / 10 + RNG.uniform(-.2, .2)
            length = RNG.uniform(1.3, 2.0)
            start = Vector((0, 0, RNG.uniform(1.8, 2.8)))
            end = Vector((length * math.cos(a), length * math.sin(a), RNG.uniform(3.4, 4.65)))
            mid = start.lerp(end, .48) + Vector((.06 * math.sin(a), -.06 * math.cos(a), -.17))
            branch = b.branch('Bough' + str(i), [start, mid, end], [.10, .067, .025])
            b.joint('TreeTrunk', branch, 2, .05)
            for j in range(3):
                aa = a + RNG.uniform(-.55, .55)
                tip = end + Vector((.48 * math.cos(aa), .48 * math.sin(aa), RNG.uniform(.15, .7)))
                b.span(f'Twig{i}_{j}', end - (end - start) * .13, tip, .021, .021, 'oak', 'Branches', 8)
                clusters.append((tip, .63, .55))
        clusters.append((Vector((0, 0, 4.8)), .86, .60))
        clusters.append((Vector((0, 0, 3.8)), 1.10, .68))
        for i in range(4):
            a = i * math.pi / 2
            clusters.append((Vector((.65 * math.cos(a), .65 * math.sin(a), 4.15)), .81, .61))
    for index, (center, rx, rz) in enumerate(clusters):
        count = 22 if cypress else (78 if blossom else 100)
        for k in range(count):
            direction = Vector((RNG.uniform(-1, 1), RNG.uniform(-1, 1), RNG.uniform(-.8, .8))).normalized()
            r = RNG.random() ** (1 / 3)
            p = center + Vector((direction.x * rx, direction.y * rx, direction.z * rz)) * r
            if blossom:
                flower(b, f'Blossom{index}_{k}', p, .075 + RNG.random() * .035, 'pink')
                if k % 4 == 0:
                    leaf(b, f'SpringLeaf{index}_{k}', p, direction * .21, .075)
            else:
                leaf(b, f'Leaf{index}_{k}', p, direction * (.16 if cypress else RNG.uniform(.21, .34)), .055 if cypress else .115)
    return b.finish()

def flower(b, name, p, radius=.10, color='pink'):
    c = Vector(p)
    normal = Vector((RNG.uniform(-.7, .7), RNG.uniform(-.7, .7), RNG.uniform(.2, 1))).normalized()
    u = normal.cross(Vector((1, 0, 0))).normalized()
    v = normal.cross(u).normalized()
    for k in range(5):
        a = k * math.pi * 2 / 5
        f = u * math.cos(a) + v * math.sin(a)
        side = normal.cross(f)
        vertices = [c, c + f * radius * .50 + side * radius * .36,
                    c + f * radius * .94 + side * radius * .20 + normal * radius * .08,
                    c + f * radius * 1.05 + normal * radius * .14,
                    c + f * radius * .94 - side * radius * .20 + normal * radius * .08,
                    c + f * radius * .50 - side * radius * .36,
                    c + f * radius * .55 + normal * radius * .20]
        b.add(name + '_Petal' + str(k), vertices, [(0, 1, 6), (1, 2, 6), (2, 3, 6), (3, 4, 6), (4, 5, 6), (5, 0, 6)], color, 'Foliage', True)
    b.add(name + '_Pollen', [c + normal * radius * .07, c + u * radius * .16, c + v * radius * .16, c - u * radius * .16, c - v * radius * .16], [(0, 1, 2), (0, 2, 3), (0, 3, 4), (0, 4, 1)], 'ivory', 'Foliage')

def garden_props():
    out = []
    b = Builder('flower_planter', 'Керамический горшок с цветами', 'vegetation')
    b.lathe('ClayPot', (0, 0, 0), [(.18, 0), (.21, .04), (.30, .53), (.33, .55), (.33, .62), (.28, .62), (.26, .53), (.16, .10)], 'tile', 32)
    b.lathe('PotSoil', (0, 0, .51), [(.26, 0), (.26, .015)], 'soil', 24)
    for i in range(24):
        x, y = RNG.uniform(-.19, .19), RNG.uniform(-.19, .19)
        z = RNG.uniform(.8, 1.25)
        b.span('FlowerStem' + str(i), (x, y, .52), (x * 1.7, y * 1.7, z), .007, .007, 'leaf', 'Foliage', 6)
        flower(b, 'PlanterFlower' + str(i), (x * 1.7, y * 1.7, z), .075)
        leaf(b, 'PlanterFoliage' + str(i), (x, y, .65), (RNG.uniform(-.18, .18), RNG.uniform(-.18, .18), .16), .07)
    out.append(b.finish())
    b = Builder('garden_shrub', 'Садовый кустарник', 'vegetation')
    for i in range(650):
        a = RNG.uniform(0, math.pi * 2); r = RNG.random() ** .5 * .7
        p = Vector((r * math.cos(a), r * math.sin(a), RNG.uniform(.10, .85) * (1 - r * .35)))
        leaf(b, 'ShrubLeaf' + str(i), p, Vector((math.cos(a), math.sin(a), RNG.uniform(-.1, .4))) * .20, .09)
    out.append(b.finish())
    b = Builder('meadow_flowers', 'Луговые цветы и трава', 'vegetation')
    for i in range(100):
        x, y = RNG.uniform(-.55, .55), RNG.uniform(-.55, .55)
        leaf(b, 'GrassBlade' + str(i), (x, y, 0), (RNG.uniform(-.06, .06), RNG.uniform(-.06, .06), RNG.uniform(.16, .40)), .015)
        if i < 24:
            zz = RNG.uniform(.18, .32)
            b.span('MeadowStem' + str(i), (x, y, 0), (x, y, zz), .004, .004, 'leaf', 'Foliage', 4)
            flower(b, 'MeadowFlower' + str(i), (x, y, zz), .035, 'ivory')
    out.append(b.finish())
    b = Builder('crop_row', 'Ряд огородных растений', 'vegetation')
    for i in range(9):
        x = -.9 + i * .225
        for j in range(8):
            a = j * math.pi / 4
            leaf(b, f'CropLeaf{i}_{j}', (x, 0, .03), (.19 * math.cos(a), .19 * math.sin(a), .20), .065)
    out.append(b.finish())
    return out

def rock(key, dims=(2.6, 2.1, 2.0)):
    b = Builder(key, 'Прибрежный валун', 'nature')
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=3, radius=1)
    for v in bm.verts:
        n = .91 + RNG.random() * .18
        v.co.x *= dims[0] * .5 * n
        v.co.y *= dims[1] * .5 * n
        v.co.z = max(0, (v.co.z * .5 + .5) * dims[2] * n - .13)
    bm.verts.ensure_lookup_table()
    b.add('OrganicRock', [tuple(v.co) for v in bm.verts], [tuple(v.index for v in f.verts) for f in bm.faces], 'stone', 'Rock', True)
    bm.free()
    return b.finish()

def workshop(key, smith=False):
    b = building(key, 'Кузнечная мастерская' if smith else 'Столярная мастерская', 'workshop', 6.5, 5.8, 1, 'tile', False)
    # Covered side workspace remains part of this building asset.
    temp = Builder('temporary', '', '')
    for x in (-1.3, 1.3):
        for y in (-1.7, 1.7):
            temp.span('WorkyardPost' + str(x) + str(y), (x, y, 0), (x, y, 2.6), .10, .10, 'oak')
    roof(temp, 'WorkyardRoof', 2.6, 3.4, 2.65, .65, 'slate')
    temp.box('Workbench', (0, 0, .94), (2.3, .95, .19), 'oak', 'Joinery')
    for x in (-.95, .95):
        temp.box('WorkbenchLeg' + str(x), (x, 0, .46), (.14, .73, .92), 'darkwood', 'Joinery')
    if smith:
        temp.box('ForgeHearth', (0, 1.18, .70), (1.15, .95, 1.4), 'stone')
        temp.box('ForgeMouth', (0, .68, .95), (.75, .05, .50), 'darkwood')
        temp.box('ForgeChimney', (0, 1.18, 2.70), (.63, .69, 2.45), 'stone')
        temp.box('AnvilStump', (0, -.10, .32), (.58, .58, .64), 'darkwood', 'Joinery')
        temp.add('Anvil', [(-.35, -.21, .67), (.35, -.21, .67), (.35, .21, .67), (-.35, .21, .67),
                          (-.62, -.17, .89), (.43, -.17, .89), (.43, .17, .89), (-.62, .17, .89)],
                 [(0, 1, 2, 3), (4, 7, 6, 5), (0, 4, 5, 1), (1, 5, 6, 2), (2, 6, 7, 3), (3, 7, 4, 0)], 'iron')
    else:
        for i in range(6):
            temp.box('Lumber' + str(i), (RNG.uniform(-.3, .3), .98, .20 + i * .14), (2.7, .25, .13), 'oak', 'Joinery')
        temp.span('SawHandle', (-.8, -.15, 1.08), (.7, -.15, 1.08), .025, .032, 'iron')
    merge_component(b, temp, (4.6, 0, 0))
    return b.finish()

def watermill():
    b = building('watermill', 'Водяная мельница', 'mill', 5.2, 5.6, 2, 'tile', False)
    x, y, z, radius = 3.50, .55, 1.95, 1.80
    for xx in (x - .40, x + .40):
        circle = [(xx, y + radius * math.cos(i * math.pi * 2 / 64), z + radius * math.sin(i * math.pi * 2 / 64)) for i in range(65)]
        b.ribbon('WheelRim' + str(xx), circle, .10, 'darkwood', 'Joinery')
        for i in range(12):
            a = i * math.pi / 6
            b.span('WheelSpoke' + str(xx) + str(i), (xx, y, z), (xx, y + radius * math.cos(a), z + radius * math.sin(a)), .05, .065, 'oak')
    for i in range(24):
        a = i * math.pi / 12
        b.span('WheelPaddle' + str(i), (x - .46, y + radius * math.cos(a), z + radius * math.sin(a)),
               (x + .46, y + radius * math.cos(a), z + radius * math.sin(a)), .16, .08, 'oak')
    b.span('MillAxle', (2.3, y, z), (4.2, y, z), .15, .15, 'darkwood', sides=16)
    return b.finish()

def cart():
    b = Builder('handcart', 'Хозяйственная телега', 'props')
    for i in range(8):
        b.box('CartFloor' + str(i), (-.65 + (i + .5) * 1.3 / 8, 0, .75), (.15, 1.90, .12), 'oak', 'Joinery')
    for x in (-.75, .75):
        for j in range(4):
            b.box('CartSide' + str(x) + str(j), (x, 0, .86 + j * .14), (.09, 2.0, .12), 'oak', 'Joinery')
        for y in (-.91, .91):
            b.span('CartCorner' + str(x) + str(y), (x, y, .69), (x, y, 1.4), .055, .055, 'darkwood')
    for y in (-.95, .95):
        for j in range(4):
            b.box('CartEnd' + str(y) + str(j), (0, y, .86 + j * .14), (1.5, .09, .12), 'oak', 'Joinery')
    for x in (-.89, .89):
        pts = [(x, .24 + .53 * math.cos(i * math.pi * 2 / 48), .55 + .53 * math.sin(i * math.pi * 2 / 48)) for i in range(49)]
        b.ribbon('WheelTyre' + str(x), pts, .055, 'iron')
        b.ribbon('WheelWood' + str(x), pts, .044, 'oak', 'Joinery')
        for i in range(12):
            a = i * math.pi / 6
            b.span('CartSpoke' + str(x) + str(i), (x, .24, .55), (x, .24 + .49 * math.cos(a), .55 + .49 * math.sin(a)), .023, .029, 'oak')
        b.span('WheelHub' + str(x), (x - .12, .24, .55), (x + .12, .24, .55), .12, .12, 'darkwood', sides=16)
    b.span('CartAxle', (-1.04, .24, .55), (1.04, .24, .55), .065, .065, 'iron', sides=12)
    for x in (-.55, .55):
        b.span('CartShaft' + str(x), (x, -.4, .64), (x, -3.0, .42), .045, .05, 'oak')
    return b.finish()

def fishing_net():
    b = Builder('fishing_net_rack', 'Сушильная стойка с рыбацкой сетью', 'harbor')
    for x in (-1.1, 1.1):
        b.span('NetPost' + str(x), (x, 0, 0), (x, 0, 2.0), .06, .06, 'oak')
        b.box('NetFoot' + str(x), (x, 0, .08), (.25, .65, .16), 'darkwood', 'Joinery')
    b.span('NetCrossbeam', (-1.2, 0, 1.90), (1.2, 0, 1.90), .055, .055, 'oak')
    for family in (-1, 1):
        for i in range(-8, 18):
            pts = []
            for j in range(25):
                zz = .32 + j * 1.50 / 24
                xx = -1.04 + i * .15 + family * (zz - .32) * .42
                if -1.06 <= xx <= 1.06:
                    pts.append((xx, -.03 - .1 * math.sin((xx + 1.1) * math.pi / 2.2) * math.sin((zz - .32) / 1.5 * math.pi), zz))
            if len(pts) > 1:
                b.ribbon(f'Net{family}_{i}', pts, .005, 'ivory', sides=4)
    for i in range(9):
        b.lathe('NetFloat' + str(i), (-1.0 + i * .25, -.03, 1.77), [(.025, 0), (.044, .028), (.035, .07)], 'oak', 10, 'Joinery')
    return b.finish()

def make_studio():
    sc = bpy.data.scenes['Linvel_AssetLibrary']
    col = bpy.data.collections.get('Linvel_Studio')
    if col:
        return col
    col = bpy.data.collections.new('Linvel_Studio')
    sc.collection.children.link(col)
    mesh = bpy.data.meshes.new('StudioGround')
    mesh.from_pydata([(-300, -300, -.04), (300, -300, -.04), (300, 300, -.04), (-300, 300, -.04)], [], [(0, 1, 2, 3)])
    ground = bpy.data.objects.new('StudioGround', mesh)
    col.objects.link(ground)
    mat = bpy.data.materials.new('Linvel_StudioGround')
    mat.diffuse_color = (.73, .78, .76, 1)
    mat.use_nodes = True
    mat.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (.73, .78, .76, 1)
    mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'].default_value = .95
    mesh.materials.append(mat)
    cam = bpy.data.cameras.new('Linvel_ReviewCamera')
    cam.type = 'ORTHO'
    cam.lens = 50
    obj = bpy.data.objects.new(cam.name, cam)
    col.objects.link(obj)
    sc.camera = obj
    for name, type_, loc, energy, color in [('Linvel_Sun', 'SUN', (-8, -12, 18), 2.2, (1, .89, .72)),
                                           ('Linvel_Fill', 'AREA', (12, 2, 16), 1600, (.67, .81, 1))]:
        data = bpy.data.lights.new(name, type_)
        data.energy = energy
        data.color = color
        if type_ == 'SUN':
            data.angle = .12
        else:
            data.shape = 'DISK'; data.size = 12
        ob = bpy.data.objects.new(name, data)
        col.objects.link(ob)
        ob.location = loc
        ob.rotation_euler = (Vector((0, 0, 2)) - ob.location).to_track_quat('-Z', 'Y').to_euler()
    return col

def show_asset(key, angle=45):
    sc = bpy.data.scenes['Linvel_AssetLibrary']
    bpy.context.window.scene = sc
    make_studio()
    for col in sc.collection.children:
        if col.name.startswith('LV_'):
            col.hide_render = False
            sc.view_layers[0].layer_collection.children[col.name].exclude = col.name != 'LV_' + key
            if col.name == 'LV_' + key:
                for ob in col.objects:
                    ob.hide_set(False)
    bounds = ASSETS[key]['bounds_blender_m']
    bpy.data.objects['StudioGround'].location.z = bounds[2][0] - .025
    center = Vector(((bounds[0][0] + bounds[0][1]) / 2, (bounds[1][0] + bounds[1][1]) / 2,
                     (bounds[2][0] + bounds[2][1]) / 2))
    d = max(ASSETS[key]['dimensions_m'])
    horizontal = math.sqrt(1.2 ** 2 + 1.65 ** 2)
    sc.camera.location = center + Vector((d * 1.2, -d * 1.65, d * horizontal * math.tan(math.radians(angle))))
    sc.camera.rotation_euler = (center - sc.camera.location).to_track_quat('-Z', 'Y').to_euler()
    sc.camera.data.ortho_scale = d * 1.45
    for screen in bpy.data.screens:
        for area in screen.areas:
            if area.type == 'VIEW_3D':
                area.spaces.active.region_3d.view_location = center
                area.spaces.active.region_3d.view_distance = d * 1.7
                area.spaces.active.shading.type = 'MATERIAL'
    bpy.context.view_layer.update()
    return {'key': key, 'camera_scale': sc.camera.data.ortho_scale}

def render_asset(key, size=1200):
    show_asset(key)
    sc = bpy.data.scenes['Linvel_AssetLibrary']
    sc.render.resolution_x = size
    sc.render.resolution_y = size
    sc.render.filepath = str(ROOT / 'previews' / (key + '.png'))
    bpy.ops.render.render(write_still=True)
    return sc.render.filepath

def export_asset(key):
    """Export existing edited Blender geometry; never regenerate it during export."""
    show_asset(key)
    col = bpy.data.collections['LV_' + key]
    bpy.ops.object.select_all(action='DESELECT')
    for ob in col.objects:
        ob.hide_set(False)
        ob.select_set(True)
    root = col.objects[key]
    bpy.context.view_layer.objects.active = root
    file = ROOT / 'exports' / (key + '.glb')
    staging = ROOT / 'exports' / '_staging'
    staging.mkdir(exist_ok=True)
    temporary = staging / (key + '.glb')
    outcome = bpy.ops.export_scene.gltf(filepath=str(temporary), export_format='GLB', use_selection=True, use_active_scene=True,
                export_yup=True, export_apply=True, export_texcoords=True, export_normals=True,
                export_materials='EXPORT', export_animations=False, export_cameras=False,
                export_lights=False, export_extras=True, export_image_format='AUTO')
    assert outcome == {'FINISHED'} and temporary.stat().st_size > 1000
    for attempt in range(6):
        try:
            temporary.replace(file)
            break
        except OSError:
            if attempt == 5: raise
            time.sleep(.2)
    # The native .blend library is the editable source. Avoid libraries.write(scene),
    # which triggers a native crash in the installed Blender 5.2.2 build.
    ASSETS[key]['glb'] = 'exports/' + key + '.glb'
    ASSETS[key]['blend'] = 'linvel_asset_library.blend'
    ASSETS[key]['glb_bytes'] = file.stat().st_size
    return {'key': key, 'bytes': file.stat().st_size}

def audit_assets():
    failures = []
    summary = []
    for key in sorted(ASSETS):
        col = bpy.data.collections['LV_' + key]
        root = col.objects.get(key)
        if not root or tuple(root.location) != (0, 0, 0):
            failures.append(key + ': invalid root pivot')
        for ob in col.objects:
            if ob.type != 'MESH': continue
            if max(abs(c) for c in ob.rotation_euler) > 1e-6 or any(abs(c - 1) > 1e-6 for c in ob.scale):
                failures.append(ob.name + ': unapplied rotation/scale')
            if not ob.data.uv_layers.active or not ob.data.materials:
                failures.append(ob.name + ': missing UV/material')
            if any(not math.isfinite(c) for v in ob.data.vertices for c in v.co):
                failures.append(ob.name + ': non-finite vertex')
            if any(p.area < 1e-12 for p in ob.data.polygons):
                failures.append(ob.name + ': zero-area polygon')
        info = ASSETS[key]
        summary.append({k: info[k] for k in ('key', 'label_ru', 'category', 'triangles', 'meshes', 'parts', 'dimensions_m')})
    report = {'blender_version': bpy.app.version_string, 'modeling_transport': 'official Blender Lab MCP',
              'reference': 'User-supplied Linvel art sheet codex-clipboard-db2c4496-7746-414c-bef3-b0f40036da7b.png',
              'asset_count': len(ASSETS), 'triangles_total': sum(i['triangles'] for i in ASSETS.values()),
              'units': 'metres', 'export_up_axis': '+Y', 'export_front_axis': '+Z',
              'atlas_size_px': [2048, 2048], 'external_model_or_texture_assets_used': False,
              'failures': failures, 'assets': summary}
    (ROOT / 'reports/blender-audit.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    (ROOT / 'catalog.json').write_text(json.dumps(list(ASSETS.values()), ensure_ascii=False, indent=2), encoding='utf-8')
    return {k: report[k] for k in ('asset_count', 'triangles_total', 'failures')}

def render_gallery():
    """Contact-sheet scene, deliberately unrelated to any in-game city layout."""
    sc = bpy.data.scenes.new('Linvel_Catalog')
    sc.world = bpy.data.worlds['Linvel_StudioWorld']
    sc.render.engine = 'BLENDER_EEVEE'
    sc.view_settings.view_transform = 'Standard'
    sc.view_settings.look = 'Medium High Contrast'
    keys = sorted(ASSETS, key=lambda k: (ASSETS[k]['category'], k))
    cols, step, target = 7, 10, 5.2
    for index, key in enumerate(keys):
        column, row = index % cols, index // cols
        col = bpy.data.collections['LV_' + key]
        col.hide_render = False
        ob = bpy.data.objects.new('Catalog_' + key, None)
        ob.instance_type = 'COLLECTION'; ob.instance_collection = col
        sc.collection.objects.link(ob)
        info = ASSETS[key]
        factor = target / max(info['dimensions_m'])
        bounds = info['bounds_blender_m']
        ob.location = (column * step, row * step, -bounds[2][0] * factor)
        ob.scale = (factor,) * 3
        text = bpy.data.curves.new('Caption_' + key, 'FONT')
        text.body = f'{index + 1:02d}  {key}'
        text.size = .32; text.align_x = 'CENTER'
        caption = bpy.data.objects.new(text.name, text)
        sc.collection.objects.link(caption)
        caption.location = (column * step, row * step - 3.2, .015)
        ink = bpy.data.materials.get('Linvel_CatalogInk')
        if ink is None:
            ink = bpy.data.materials.new('Linvel_CatalogInk'); ink.use_nodes = True
            ink.node_tree.nodes['Principled BSDF'].inputs['Base Color'].default_value = (.06, .09, .11, 1)
        text.materials.append(ink)
    ground = bpy.data.objects['StudioGround'].copy()
    ground.data = ground.data.copy()
    ground.location.z = 0
    sc.collection.objects.link(ground)
    for name in ('Linvel_Sun', 'Linvel_Fill'):
        light = bpy.data.objects[name].copy(); light.data = light.data.copy()
        sc.collection.objects.link(light)
    cam = bpy.data.cameras.new('Linvel_CatalogCamera'); cam.type = 'ORTHO'; cam.ortho_scale = 99
    camera = bpy.data.objects.new(cam.name, cam)
    sc.collection.objects.link(camera)
    center = Vector((30, 34, 1.4))
    camera.location = center + Vector((28, -77, 106))
    camera.rotation_euler = (center - camera.location).to_track_quat('-Z', 'Y').to_euler()
    sc.camera = camera
    sc.render.resolution_x = 3200; sc.render.resolution_y = 3200; sc.render.resolution_percentage = 100
    sc.render.image_settings.file_format = 'PNG'
    sc.render.filepath = str(ROOT / 'previews/linvel_catalog.png')
    bpy.context.window.scene = sc
    bpy.ops.render.render(write_still=True)
    bpy.context.window.scene = bpy.data.scenes['Linvel_AssetLibrary']
    return sc.render.filepath
