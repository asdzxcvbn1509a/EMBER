"""EMBER asset kit - procedural props built in Blender (bpy + bmesh).

Run headless:
  blender.exe -b --factory-startup -P build_models.py -- --out <fbx dir> --blend <EmberKit.blend> [--font <ttf>] [--only A,B]

Conventions
  * 1 Blender unit = 1 metre, Z up. The FRONT of every model faces Blender -Y,
    which becomes Unity +Z after FBX export (forward -Z, up Y, apply transform).
  * Pivot = bottom centre unless noted (spheres/crystals: centre).
  * Material slot names (M_*) match the URP materials in Assets/_Ember/Materials,
    so Unity's importer remaps them by name.
"""
import bpy
import bmesh
import math
import os
import random
import sys
from mathutils import Matrix, Vector

# ------------------------------------------------------------------ arguments
argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
def _arg(flag, default=None):
    return argv[argv.index(flag) + 1] if flag in argv else default

OUT = _arg("--out", os.path.join(os.path.dirname(__file__), "fbx"))
BLEND = _arg("--blend", os.path.join(os.path.dirname(__file__), "EmberKit.blend"))
FONT = _arg("--font")
ONLY = set(_arg("--only", "").split(",")) - {""}
os.makedirs(OUT, exist_ok=True)

# ------------------------------------------------------------------ materials
MAT_DEFS = {
    # name: (base rgb, metallic, roughness, emission rgb, emission strength)
    "M_Stone":      ((0.42, 0.44, 0.50), 0.0, 0.85, None, 0),
    "M_StoneDark":  ((0.20, 0.21, 0.25), 0.0, 0.90, None, 0),
    "M_StoneTrim":  ((0.58, 0.55, 0.50), 0.0, 0.70, None, 0),
    "M_Statue":     ((0.36, 0.38, 0.43), 0.0, 0.80, None, 0),
    "M_Iron":       ((0.12, 0.12, 0.13), 0.8, 0.45, None, 0),
    "M_Gold":       ((1.00, 0.74, 0.32), 1.0, 0.30, None, 0),
    "M_Brass":      ((0.78, 0.55, 0.26), 1.0, 0.38, None, 0),
    "M_Wood":       ((0.36, 0.22, 0.12), 0.0, 0.75, None, 0),
    "M_Basalt":     ((0.10, 0.08, 0.08), 0.0, 0.80, None, 0),
    "M_Limestone":  ((0.90, 0.86, 0.78), 0.0, 0.75, None, 0),
    "M_Cloth":      ((0.55, 0.08, 0.10), 0.0, 0.90, None, 0),
    "M_ClothGold":  ((0.90, 0.70, 0.30), 0.3, 0.60, None, 0),
    "M_Bark":       ((0.30, 0.20, 0.15), 0.0, 0.90, None, 0),
    "M_Leaf":       ((0.95, 0.66, 0.76), 0.0, 0.80, None, 0),
    "M_Cloud":      ((1.00, 0.95, 0.97), 0.0, 1.00, None, 0),
    "M_RuneAmber":  ((0.20, 0.10, 0.03), 0.0, 0.50, (1.0, 0.62, 0.22), 6),
    "M_RuneRed":    ((0.20, 0.03, 0.03), 0.0, 0.50, (1.0, 0.16, 0.10), 6),
    "M_RuneCyan":   ((0.03, 0.12, 0.16), 0.0, 0.50, (0.30, 0.90, 1.00), 6),
    "M_Glow":       ((0.30, 0.08, 0.02), 0.0, 0.60, (1.0, 0.35, 0.08), 8),
    "M_Crystal":    ((0.55, 0.88, 1.00), 0.0, 0.10, (0.35, 0.80, 1.00), 3),
    "M_Ember":      ((0.15, 0.05, 0.02), 0.0, 0.60, (1.0, 0.42, 0.10), 5),
    "M_EyeRed":     ((0.20, 0.02, 0.02), 0.0, 0.40, (1.0, 0.10, 0.06), 10),
    "M_EyeAmber":   ((0.20, 0.10, 0.02), 0.0, 0.40, (1.0, 0.65, 0.20), 10),
}
_mats = {}

def mat(name):
    if name in _mats:
        return _mats[name]
    m = bpy.data.materials.new(name)
    base, metal, rough, emi, emi_s = MAT_DEFS[name]
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (*base, 1.0)
        bsdf.inputs["Metallic"].default_value = metal
        bsdf.inputs["Roughness"].default_value = rough
        if emi:
            bsdf.inputs["Emission Color"].default_value = (*emi, 1.0)
            bsdf.inputs["Emission Strength"].default_value = emi_s
    m.diffuse_color = (*base, 1.0)
    _mats[name] = m
    return m

# ------------------------------------------------------------------ bmesh helpers
class Kit:
    """Accumulates geometry for one mesh object with named material slots."""

    def __init__(self, *mat_names):
        self.bm = bmesh.new()
        self.mats = list(mat_names)

    def mi(self, name):
        if name not in self.mats:
            self.mats.append(name)
        return self.mats.index(name)

    # -- internals
    def _faces_of(self, verts):
        return list({f for v in verts for f in v.link_faces})

    def _finish_part(self, verts, m, bevel=0.0, segments=1):
        idx = self.mi(m)
        for f in self._faces_of(verts):
            f.material_index = idx
        if bevel > 0:
            edges = list({e for v in verts for e in v.link_edges})
            bmesh.ops.bevel(self.bm, geom=edges, offset=bevel, segments=segments,
                            profile=0.5, affect='EDGES', clamp_overlap=True)
        return verts

    # -- primitives (sizes in metres, positions = centre unless noted)
    def box(self, size, center, m, bevel=0.0, rot=None):
        sx, sy, sz = size
        mtx = Matrix.Translation(Vector(center))
        if rot:
            mtx = mtx @ rot
        mtx = mtx @ Matrix.Diagonal((sx, sy, sz, 1.0))
        ret = bmesh.ops.create_cube(self.bm, size=1.0, matrix=mtx)
        return self._finish_part(ret["verts"], m, bevel)

    def cyl(self, r1, r2, h, segs, base, m, axis="Z", bevel=0.0, caps=True):
        """Cylinder/cone. base = centre of the bottom cap; extends +h along axis."""
        bx, by, bz = base
        if axis == "Z":
            mtx = Matrix.Translation((bx, by, bz + h / 2))
        elif axis == "X":
            mtx = Matrix.Translation((bx + h / 2, by, bz)) @ Matrix.Rotation(math.radians(90), 4, "Y")
        else:  # Y
            mtx = Matrix.Translation((bx, by + h / 2, bz)) @ Matrix.Rotation(math.radians(-90), 4, "X")
        ret = bmesh.ops.create_cone(self.bm, cap_ends=caps, cap_tris=False, segments=segs,
                                    radius1=r1, radius2=r2, depth=h, matrix=mtx)
        return self._finish_part(ret["verts"], m, bevel)

    def ico(self, r, center, m, subdiv=2, scale=(1, 1, 1), jitter=0.0, seed=0):
        mtx = Matrix.Translation(Vector(center)) @ Matrix.Diagonal((*scale, 1.0))
        ret = bmesh.ops.create_icosphere(self.bm, subdivisions=subdiv, radius=r, matrix=mtx)
        verts = ret["verts"]
        if jitter > 0:
            rnd = random.Random(seed)
            c = Vector(center)
            for v in verts:
                d = (v.co - c)
                if d.length > 1e-6:
                    v.co += d.normalized() * rnd.uniform(-jitter, jitter)
        return self._finish_part(verts, m)

    def uvsphere(self, r, center, m, u=24, v=16):
        mtx = Matrix.Translation(Vector(center))
        ret = bmesh.ops.create_uvsphere(self.bm, u_segments=u, v_segments=v, radius=r, matrix=mtx)
        return self._finish_part(ret["verts"], m)

    def ring(self, r_out, r_in, h, segs, base, m, rot=None):
        """Flat annulus (washer) from base z to base z + h."""
        bx, by, bz = base
        mtx = Matrix.Translation((bx, by, bz))
        if rot:
            mtx = mtx @ rot
        bm = self.bm
        rows = []
        for (r, z) in ((r_out, 0), (r_out, h), (r_in, h), (r_in, 0)):
            row = []
            for i in range(segs):
                a = 2 * math.pi * i / segs
                row.append(bm.verts.new(mtx @ Vector((r * math.cos(a), r * math.sin(a), z))))
            rows.append(row)
        new_verts = [v for row in rows for v in row]
        for k in range(4):
            ra, rb = rows[k], rows[(k + 1) % 4]
            for i in range(segs):
                j = (i + 1) % segs
                bm.faces.new((ra[i], ra[j], rb[j], rb[i]))
        bm.normal_update()
        return self._finish_part(new_verts, m)

    def torus(self, R, r, center, m, segs=32, tube=8, axis="Z"):
        bm = self.bm
        rot = Matrix.Identity(4)
        if axis == "Y":
            rot = Matrix.Rotation(math.radians(90), 4, "X")
        elif axis == "X":
            rot = Matrix.Rotation(math.radians(90), 4, "Y")
        mtx = Matrix.Translation(Vector(center)) @ rot
        grid = []
        for i in range(segs):
            u = 2 * math.pi * i / segs
            row = []
            for j in range(tube):
                w = 2 * math.pi * j / tube
                p = Vector(((R + r * math.cos(w)) * math.cos(u), (R + r * math.cos(w)) * math.sin(u), r * math.sin(w)))
                row.append(bm.verts.new(mtx @ p))
            grid.append(row)
        for i in range(segs):
            for j in range(tube):
                a, b = grid[i][j], grid[(i + 1) % segs][j]
                c, d = grid[(i + 1) % segs][(j + 1) % tube], grid[i][(j + 1) % tube]
                bm.faces.new((a, b, c, d))
        bm.normal_update()
        return self._finish_part([v for row in grid for v in row], m)

    def arch_ring(self, r_in, r_out, depth, segs, center, m, start=0.0, end=180.0):
        """Semicircular arch ring in the XZ plane, extruded along Y."""
        bm = self.bm
        cx, cy, cz = center
        cols = []
        for i in range(segs + 1):
            a = math.radians(start + (end - start) * i / segs)
            ca, sa = math.cos(a), math.sin(a)
            col = [bm.verts.new((cx + r * ca, cy + y, cz + r * sa))
                   for (r, y) in ((r_in, -depth / 2), (r_out, -depth / 2), (r_out, depth / 2), (r_in, depth / 2))]
            cols.append(col)
        for i in range(segs):
            a, b = cols[i], cols[i + 1]
            for k in range(4):
                bm.faces.new((a[k], b[k], b[(k + 1) % 4], a[(k + 1) % 4]))
        bm.faces.new(list(reversed(cols[0])))
        bm.faces.new(cols[-1])
        bm.normal_update()
        return self._finish_part([v for c in cols for v in c], m)

    def octa(self, h, w, center, m, tilt=(0, 0)):
        """Elongated octahedron crystal (h = full height)."""
        bm = self.bm
        mtx = (Matrix.Translation(Vector(center)) @ Matrix.Rotation(math.radians(tilt[0]), 4, "X")
               @ Matrix.Rotation(math.radians(tilt[1]), 4, "Y"))
        top = bm.verts.new(mtx @ Vector((0, 0, h / 2)))
        bot = bm.verts.new(mtx @ Vector((0, 0, -h / 2)))
        ring = [bm.verts.new(mtx @ Vector((w * math.cos(a), w * math.sin(a), 0)))
                for a in [i * math.pi / 2 + math.pi / 4 for i in range(4)]]
        for i in range(4):
            j = (i + 1) % 4
            bm.faces.new((ring[i], ring[j], top))
            bm.faces.new((ring[j], ring[i], bot))
        bm.normal_update()
        return self._finish_part([top, bot] + ring, m)

    # -- output
    def build(self, name, uv="box", uv_scale=0.5, sphere_center=(0, 0, 0)):
        bm = self.bm
        bm.normal_update()
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        layer = bm.loops.layers.uv.verify()
        sc = Vector(sphere_center)
        for f in bm.faces:
            n = f.normal
            ax = max(range(3), key=lambda i: abs(n[i]))
            for loop in f.loops:
                co = loop.vert.co
                if uv == "sphere":
                    d = (co - sc).normalized()
                    u = 0.5 + math.atan2(d.y, d.x) / (2 * math.pi)
                    v = 0.5 + math.asin(max(-1.0, min(1.0, d.z))) / math.pi
                    loop[layer].uv = (u, v)
                elif ax == 0:
                    loop[layer].uv = (co.y * uv_scale, co.z * uv_scale)
                elif ax == 1:
                    loop[layer].uv = (co.x * uv_scale, co.z * uv_scale)
                else:
                    loop[layer].uv = (co.x * uv_scale, co.y * uv_scale)
        me = bpy.data.meshes.new(name)
        bm.to_mesh(me)
        bm.free()
        for mn in self.mats:
            me.materials.append(mat(mn))
        ob = bpy.data.objects.new(name, me)
        return ob


def link(objs, collection):
    for o in objs:
        collection.objects.link(o)
        link(list(o.children), collection)


def parent(child, par, local=(0, 0, 0)):
    child.parent = par
    child.location = Vector(local)
    return child

# ------------------------------------------------------------------ models
MODELS = {}

def model(fn):
    MODELS[fn.__name__] = fn
    return fn

R = random.Random(1337)

# ---- architecture -----------------------------------------------------------
@model
def Pillar():
    k = Kit("M_Stone", "M_StoneTrim")
    k.box((1.0, 1.0, 0.3), (0, 0, 0.15), "M_StoneTrim", bevel=0.04)
    k.cyl(0.46, 0.44, 0.1, 16, (0, 0, 0.3), "M_StoneTrim")
    k.cyl(0.38, 0.34, 3.1, 12, (0, 0, 0.4), "M_Stone")
    k.cyl(0.36, 0.44, 0.12, 16, (0, 0, 3.5), "M_StoneTrim")
    k.box((0.96, 0.96, 0.28), (0, 0, 3.76), "M_StoneTrim", bevel=0.04)
    return [k.build("Pillar")]

@model
def PillarBroken():
    k = Kit("M_Stone", "M_StoneTrim")
    k.box((1.0, 1.0, 0.3), (0, 0, 0.15), "M_StoneTrim", bevel=0.04)
    k.cyl(0.46, 0.44, 0.1, 16, (0, 0, 0.3), "M_StoneTrim")
    verts = k.cyl(0.38, 0.36, 1.8, 12, (0, 0, 0.4), "M_Stone")
    rnd = random.Random(7)
    for v in verts:
        if v.co.z > 2.1:
            v.co.z += rnd.uniform(-0.35, 0.15)
    k.ico(0.28, (0.75, -0.35, 0.18), "M_Stone", subdiv=1, jitter=0.07, seed=3)
    k.ico(0.2, (-0.6, 0.55, 0.14), "M_Stone", subdiv=1, jitter=0.05, seed=4)
    return [k.build("PillarBroken")]

@model
def Arch():
    k = Kit("M_Stone", "M_StoneTrim")
    for x in (-2.5, 2.5):
        k.box((1.0, 1.1, 0.35), (x, 0, 0.175), "M_StoneTrim", bevel=0.04)
        k.box((1.0, 1.0, 2.9), (x, 0, 1.8), "M_Stone", bevel=0.04)
        k.box((1.1, 1.1, 0.25), (x, 0, 3.3), "M_StoneTrim", bevel=0.03)
    k.arch_ring(2.0, 3.0, 1.0, 14, (0, 0, 3.42), "M_Stone")
    k.box((0.7, 1.15, 0.9), (0, 0, 6.1), "M_StoneTrim", bevel=0.05)
    return [k.build("Arch")]

@model
def Railing():
    k = Kit("M_StoneTrim")
    k.box((2.0, 0.28, 0.14), (0, 0, 0.07), "M_StoneTrim", bevel=0.02)
    k.box((2.0, 0.32, 0.14), (0, 0, 0.88), "M_StoneTrim", bevel=0.02)
    for i in range(5):
        k.cyl(0.07, 0.05, 0.67, 8, (-0.8 + 0.4 * i, 0, 0.14), "M_StoneTrim")
    return [k.build("Railing")]

@model
def Altar():
    k = Kit("M_Stone", "M_StoneTrim", "M_RuneAmber")
    k.cyl(2.6, 2.6, 0.15, 32, (0, 0, 0), "M_StoneTrim")
    k.cyl(2.15, 2.15, 0.15, 32, (0, 0, 0.15), "M_Stone")
    k.cyl(1.7, 1.7, 0.12, 32, (0, 0, 0.30), "M_StoneTrim")
    k.ring(1.38, 1.22, 0.015, 32, (0, 0, 0.42), "M_RuneAmber")
    k.ring(0.5, 0.42, 0.015, 24, (0, 0, 0.42), "M_RuneAmber")
    return [k.build("Altar")]

@model
def Stairs():
    """One stair segment: 4 m wide, 5 steps x 0.2 m rise, ascending toward the front (-Y)."""
    k = Kit("M_Stone", "M_StoneTrim")
    for s in range(5):
        h = 0.2 * (s + 1)
        k.box((4.0, 0.5, h), (0, -0.25 - 0.5 * s, h / 2), "M_Stone" if s % 2 else "M_StoneTrim", bevel=0.02)
    return [k.build("Stairs")]

@model
def Sarcophagus():
    k = Kit("M_Stone", "M_StoneTrim", "M_RuneAmber")
    k.box((1.2, 2.4, 0.9), (0, 0, 0.45), "M_Stone", bevel=0.05)
    k.box((1.35, 2.55, 0.22), (0, 0, 1.01), "M_StoneTrim", bevel=0.05)
    k.ico(0.28, (0, -0.8, 1.2), "M_StoneTrim", subdiv=1, scale=(1, 0.9, 0.6))
    k.box((0.42, 1.4, 0.2), (0, 0.15, 1.2), "M_StoneTrim", bevel=0.06)
    k.box((0.08, 0.6, 0.02), (0, 0.2, 1.31), "M_RuneAmber")
    return [k.build("Sarcophagus")]

# ---- puzzle pieces ----------------------------------------------------------
@model
def PressurePlate():
    f = Kit("M_StoneTrim", "M_StoneDark")
    f.cyl(1.32, 1.32, 0.05, 24, (0, 0, -0.05), "M_StoneDark")
    f.ring(1.32, 1.14, 0.14, 24, (0, 0, 0), "M_StoneTrim")
    frame = f.build("Frame")
    d = Kit("M_Stone", "M_RuneAmber")
    d.cyl(1.1, 1.1, 0.1, 24, (0, 0, 0.02), "M_Stone")
    d.ring(0.78, 0.64, 0.012, 24, (0, 0, 0.12), "M_RuneAmber")
    d.cyl(0.2, 0.2, 0.012, 12, (0, 0, 0.12), "M_RuneAmber")
    for i in range(4):
        a = i * math.pi / 2
        d.box((0.08, 0.32, 0.012), (0.42 * math.cos(a), 0.42 * math.sin(a), 0.126), "M_RuneAmber",
              rot=Matrix.Rotation(a, 4, "Z"))
    disc = d.build("Disc")
    return [frame, disc]

@model
def Gate():
    k = Kit("M_Iron")
    for i in range(11):
        k.cyl(0.06, 0.06, 4.2, 8, (-2.0 + 0.4 * i, 0, 0), "M_Iron")
    for z in (0.6, 2.1, 3.6):
        k.box((4.4, 0.13, 0.13), (0, 0, z), "M_Iron", bevel=0.01)
    k.box((4.6, 0.2, 0.22), (0, 0, 4.2), "M_Iron", bevel=0.02)
    return [k.build("Gate")]

@model
def SealSlab():
    k = Kit("M_StoneDark", "M_RuneRed")
    k.box((2.4, 0.4, 3.0), (0, 0, 1.5), "M_StoneDark", bevel=0.05)
    rot = Matrix.Rotation(math.radians(90), 4, "X")
    k.ring(0.85, 0.7, 0.02, 32, (0, -0.21, 1.5), "M_RuneRed", rot=rot)
    k.box((0.08, 0.02, 0.9), (0, -0.215, 1.5), "M_RuneRed")
    k.box((0.9, 0.02, 0.08), (0, -0.215, 1.5), "M_RuneRed")
    return [k.build("SealSlab")]

@model
def Lantern():
    k = Kit("M_Iron", "M_Gold")
    k.box((0.5, 0.5, 0.15), (0, 0, 0.075), "M_Iron", bevel=0.03)
    k.cyl(0.07, 0.06, 1.5, 8, (0, 0, 0.15), "M_Iron")
    k.cyl(0.29, 0.29, 0.05, 6, (0, 0, 1.65), "M_Gold")
    for i in range(6):
        a = i * math.pi / 3
        k.cyl(0.025, 0.025, 0.45, 6, (0.25 * math.cos(a), 0.25 * math.sin(a), 1.7), "M_Iron")
    k.cyl(0.31, 0.31, 0.04, 6, (0, 0, 2.15), "M_Gold")
    k.cyl(0.37, 0.05, 0.26, 6, (0, 0, 2.19), "M_Iron")
    k.ico(0.06, (0, 0, 2.5), "M_Gold", subdiv=1)
    return [k.build("Lantern")]

@model
def Brazier():
    k = Kit("M_Iron", "M_Glow")
    for i in range(3):
        a = i * 2 * math.pi / 3
        rot = Matrix.Rotation(a, 4, "Z") @ Matrix.Rotation(math.radians(12), 4, "X")
        k.box((0.09, 0.09, 1.05), (0.32 * math.cos(a + math.pi / 2), 0.32 * math.sin(a + math.pi / 2), 0.5), "M_Iron", rot=rot)
    k.torus(0.3, 0.035, (0, 0, 0.5), "M_Iron", segs=16, tube=6)
    k.cyl(0.25, 0.62, 0.36, 12, (0, 0, 0.84), "M_Iron", caps=False)
    k.cyl(0.25, 0.25, 0.03, 12, (0, 0, 0.84), "M_Iron")
    k.cyl(0.56, 0.56, 0.04, 12, (0, 0, 1.1), "M_Glow")
    return [k.build("Brazier")]

@model
def Checkpoint():
    k = Kit("M_StoneTrim", "M_Iron", "M_Glow")
    k.box((0.55, 0.55, 0.85), (0, 0, 0.425), "M_StoneTrim", bevel=0.04)
    k.cyl(0.14, 0.3, 0.16, 10, (0, 0, 0.85), "M_Iron", caps=False)
    k.cyl(0.14, 0.14, 0.02, 10, (0, 0, 0.85), "M_Iron")
    k.cyl(0.27, 0.27, 0.03, 10, (0, 0, 0.97), "M_Glow")
    return [k.build("Checkpoint")]

@model
def ExitPortal():
    k = Kit("M_Stone", "M_RuneAmber", "M_Gold")
    k.cyl(2.6, 2.6, 0.2, 32, (0, 0, 0), "M_Stone")
    k.ring(2.2, 1.95, 0.015, 32, (0, 0, 0.2), "M_RuneAmber")
    k.ring(1.0, 0.9, 0.015, 24, (0, 0, 0.2), "M_RuneAmber")
    k.torus(1.9, 0.16, (0, 0, 1.9), "M_Gold", segs=40, tube=8, axis="Y")
    for a in (90, 210, 330):
        r = math.radians(a)
        k.ico(0.16, (1.9 * math.cos(r), 0, 1.9 + 1.9 * math.sin(r)), "M_RuneAmber", subdiv=1)
    return [k.build("ExitPortal")]

@model
def TeleportRing():
    k = Kit("M_StoneTrim", "M_Gold", "M_RuneAmber")
    k.cyl(1.45, 1.4, 0.15, 24, (0, 0, 0), "M_StoneTrim")
    k.ring(1.15, 1.0, 0.012, 24, (0, 0, 0.15), "M_RuneAmber")
    k.torus(1.2, 0.1, (0, 0, 1.2), "M_Gold", segs=32, tube=8, axis="Y")
    for a in (0, 90, 180, 270):
        r = math.radians(a)
        k.ico(0.12, (1.2 * math.cos(r), 0, 1.2 + 1.2 * math.sin(r)), "M_RuneAmber", subdiv=1)
    return [k.build("TeleportRing")]

@model
def Shard():
    k = Kit("M_Crystal")
    k.octa(0.8, 0.17, (0, 0, 0), "M_Crystal")
    k.octa(0.36, 0.08, (0.2, 0.05, -0.12), "M_Crystal", tilt=(15, 25))
    k.octa(0.3, 0.07, (-0.18, -0.06, -0.15), "M_Crystal", tilt=(-10, -30))
    return [k.build("Shard")]

@model
def CrystalSeal():
    k = Kit("M_Crystal", "M_StoneDark")
    k.cyl(1.5, 1.5, 0.2, 16, (0, 0, 0), "M_StoneDark")
    for i in range(9):
        a = i * 2 * math.pi / 9
        h = 2.2 + 0.4 * math.sin(i * 1.7)
        k.octa(h, 0.22, (1.05 * math.cos(a), 1.05 * math.sin(a), 0.2 + h / 2 - 0.1), "M_Crystal",
               tilt=(-12 * math.sin(a), 12 * math.cos(a)))
    return [k.build("CrystalSeal")]

# ---- nature / decor ---------------------------------------------------------
def rubble(name, seed, size):
    k = Kit("M_Stone")
    rnd = random.Random(seed)
    k.ico(size, (0, 0, 0), "M_Stone", subdiv=1, scale=(1, rnd.uniform(0.7, 1.1), rnd.uniform(0.55, 0.8)),
          jitter=size * 0.22, seed=seed)
    return k.build(name)

for _i in range(1, 6):
    MODELS[f"Rubble_{_i}"] = (lambda i=_i: [rubble(f"Rubble_{i}", 100 + i, 0.22 + 0.05 * i)])

def rock(name, seed):
    k = Kit("M_StoneDark")
    rnd = random.Random(seed)
    k.ico(1.0, (0, 0, 0.35), "M_StoneDark", subdiv=2, scale=(1.0, rnd.uniform(0.7, 1.0), 0.6), jitter=0.22, seed=seed)
    return k.build(name)

for _i in range(1, 4):
    MODELS[f"Rock_{_i}"] = (lambda i=_i: [rock(f"Rock_{i}", 200 + i)])

def stalagmite(name, seed, h):
    k = Kit("M_StoneDark")
    verts = k.cyl(0.6, 0.05, h, 8, (0, 0, 0), "M_StoneDark")
    rnd = random.Random(seed)
    for v in verts:
        v.co.x += rnd.uniform(-0.08, 0.08)
        v.co.y += rnd.uniform(-0.08, 0.08)
    k.cyl(0.35, 0.03, h * 0.55, 7, (0.45, 0.2, 0), "M_StoneDark")
    return k.build(name)

MODELS["Stalagmite_1"] = lambda: [stalagmite("Stalagmite_1", 301, 2.6)]
MODELS["Stalagmite_2"] = lambda: [stalagmite("Stalagmite_2", 302, 1.7)]

@model
def Chain():
    k = Kit("M_Iron")
    for i in range(18):
        axis = "Y" if i % 2 else "X"
        k.torus(0.07, 0.018, (0, 0, -0.11 * i - 0.07), "M_Iron", segs=10, tube=5, axis=axis)
    return [k.build("Chain")]

@model
def Banner():
    k = Kit("M_Iron", "M_Cloth", "M_ClothGold")
    k.cyl(0.035, 0.035, 1.3, 8, (-0.65, 0, 3.0), "M_Iron", axis="X")
    cols, rows = 4, 12
    bm = k.bm
    grid = []
    for r in range(rows + 1):
        z = 3.0 - 2.6 * r / rows
        row = []
        for c in range(cols + 1):
            x = -0.5 + c / cols
            y = 0.05 * math.sin(r * 0.9 + c * 0.7)
            if r == rows:
                z -= 0.35 * (1 - abs(x) * 2)  # pointed bottom
            row.append(bm.verts.new((x, y, z)))
        grid.append(row)
    faces = []
    for r in range(rows):
        for c in range(cols):
            faces.append(bm.faces.new((grid[r][c], grid[r + 1][c], grid[r + 1][c + 1], grid[r][c + 1])))
    ci = k.mi("M_Cloth")
    for f in faces:
        f.material_index = ci
    # double sided: duplicate with flipped normals, offset slightly
    ret = bmesh.ops.duplicate(bm, geom=faces)
    dup = [g for g in ret["geom"] if isinstance(g, bmesh.types.BMFace)]
    bmesh.ops.reverse_faces(bm, faces=dup)
    for v in {v for f in dup for v in f.verts}:
        v.co.y += 0.01
    k.octa(0.5, 0.18, (0, -0.06, 1.9), "M_ClothGold", tilt=(90, 0))
    return [k.build("Banner")]

@model
def Tree():
    k = Kit("M_Bark", "M_Leaf")
    k.cyl(0.26, 0.16, 1.4, 6, (0, 0, 0), "M_Bark")
    k.cyl(0.16, 0.1, 1.0, 6, (0.12, 0, 1.38), "M_Bark")
    for (r, c, s) in ((1.0, (0.15, 0, 2.7), 11), (0.75, (-0.55, 0.25, 2.35), 12), (0.7, (0.75, -0.3, 2.3), 13),
                      (0.6, (0.1, 0.55, 3.3), 14)):
        k.ico(r, c, "M_Leaf", subdiv=1, scale=(1, 1, 0.8), jitter=r * 0.15, seed=s)
    return [k.build("Tree")]

@model
def CloudPuff():
    k = Kit("M_Cloud")
    for (r, c, s) in ((1.4, (0, 0, 0.6), 21), (1.0, (1.4, 0.2, 0.4), 22), (1.1, (-1.3, -0.1, 0.45), 23),
                      (0.8, (0.5, -0.9, 0.3), 24), (0.9, (-0.4, 0.9, 0.35), 25), (0.7, (2.3, -0.2, 0.2), 26)):
        verts = k.ico(r, c, "M_Cloud", subdiv=2, jitter=r * 0.06, seed=s)
        for v in verts:
            v.co.z = max(v.co.z, 0.0)
    return [k.build("CloudPuff")]

# ---- stage 1 ----------------------------------------------------------------
@model
def WardenStatue():
    b = Kit("M_Statue", "M_StoneTrim", "M_Iron")
    b.box((1.3, 1.3, 0.45), (0, 0, 0.225), "M_StoneTrim", bevel=0.05)
    b.cyl(0.62, 0.34, 1.95, 12, (0, 0, 0.45), "M_Statue")
    b.box((1.0, 0.5, 0.3), (0, 0, 2.4), "M_Statue", bevel=0.08)
    b.box((0.1, 0.06, 1.7), (0, -0.55, 1.25), "M_Iron")
    b.box((0.48, 0.1, 0.09), (0, -0.55, 2.05), "M_Iron")
    b.box((0.08, 0.08, 0.3), (0, -0.55, 2.25), "M_Iron")
    for x in (-0.13, 0.13):
        b.ico(0.11, (x, -0.52, 2.12), "M_Statue", subdiv=1)
    for x in (-0.42, 0.42):
        b.box((0.2, 0.22, 0.9), (x, -0.2, 1.95), "M_Statue", bevel=0.05,
              rot=Matrix.Rotation(math.radians(-25 if x < 0 else 25), 4, "Y"))
    body = b.build("Body")
    h = Kit("M_Statue", "M_StoneDark")
    h.ico(0.34, (0, 0.03, 0.3), "M_Statue", subdiv=2, scale=(1.0, 1.05, 1.15))
    h.box((0.36, 0.1, 0.3), (0, -0.3, 0.26), "M_StoneDark", bevel=0.04)
    h.cyl(0.28, 0.12, 0.25, 8, (0, 0.12, 0.58), "M_Statue")
    head = h.build("Head")
    head.location = (0, 0, 2.55)
    eyes = {}
    for nm, mn in (("EyesRed", "M_EyeRed"), ("EyesAmber", "M_EyeAmber")):
        e = Kit(mn)
        for x in (-0.09, 0.09):
            e.ico(0.045, (x, -0.36, 0.28), mn, subdiv=1)
        eo = e.build(nm)
        parent(eo, head)
        eyes[nm] = eo
    return [body, head]

# ---- stage 2 ----------------------------------------------------------------
@model
def LavaRaft():
    k = Kit("M_Basalt", "M_Glow")
    k.cyl(1.6, 1.6, 0.45, 8, (0, 0, -0.45), "M_Basalt", bevel=0.04)
    k.ring(1.6, 1.32, 0.2, 8, (0, 0, 0), "M_Basalt")
    k.cyl(1.35, 0.5, 0.45, 8, (0, 0, -0.9), "M_Glow")
    return [k.build("LavaRaft")]

@model
def IronBall():
    k = Kit("M_Iron", "M_Brass")
    k.uvsphere(0.7, (0, 0, 0), "M_Iron")
    k.torus(0.705, 0.035, (0, 0, 0), "M_Brass", segs=32, tube=6)
    k.torus(0.705, 0.035, (0, 0, 0), "M_Brass", segs=32, tube=6, axis="X")
    return [k.build("IronBall", uv="sphere")]

@model
def Counterweight():
    k = Kit("M_Brass", "M_Iron")
    k.uvsphere(0.7, (0, 0, 0), "M_Brass")
    k.torus(0.705, 0.04, (0, 0, 0), "M_Iron", segs=32, tube=6)
    k.torus(0.16, 0.045, (0, 0, 0.74), "M_Iron", segs=16, tube=6, axis="Y")
    return [k.build("Counterweight", uv="sphere")]

@model
def SunStone():
    k = Kit("M_Gold", "M_RuneAmber")
    k.ico(0.6, (0, 0, 0), "M_Gold", subdiv=2)
    k.torus(0.605, 0.04, (0, 0, 0), "M_RuneAmber", segs=32, tube=6)
    k.torus(0.605, 0.04, (0, 0, 0), "M_RuneAmber", segs=32, tube=6, axis="X")
    return [k.build("SunStone", uv="sphere")]

@model
def Chute():
    """Open U-channel, 4 m long along Y, floor top at z = 0."""
    k = Kit("M_Iron", "M_Brass")
    k.box((2.0, 4.0, 0.15), (0, 0, -0.075), "M_Iron", bevel=0.02)
    for x in (-1.075, 1.075):
        k.box((0.15, 4.0, 0.65), (x, 0, 0.25), "M_Iron", bevel=0.02)
        k.box((0.2, 4.0, 0.06), (x, 0, 0.6), "M_Brass")
    return [k.build("Chute")]

@model
def Funnel():
    k = Kit("M_Iron", "M_Brass")
    k.cyl(0.9, 3.0, 1.3, 16, (0, 0, 0), "M_Iron", caps=False)
    k.torus(3.0, 0.08, (0, 0, 1.3), "M_Brass", segs=32, tube=6)
    return [k.build("Funnel")]

@model
def CrackedWall():
    k = Kit("M_Basalt", "M_Glow")
    k.box((6.0, 1.0, 4.0), (0, 0, 2.0), "M_Basalt", bevel=0.05)
    pts = [(-1.6, 0.3), (-0.9, 1.2), (-1.2, 2.1), (-0.3, 2.9), (0.4, 2.3), (1.1, 3.1), (1.7, 2.0), (1.0, 1.1), (1.5, 0.3)]
    for (x0, z0), (x1, z1) in zip(pts, pts[1:]):
        L = math.hypot(x1 - x0, z1 - z0)
        ang = math.atan2(z1 - z0, x1 - x0)
        k.box((L + 0.06, 0.04, 0.07), ((x0 + x1) / 2, -0.505, (z0 + z1) / 2), "M_Glow",
              rot=Matrix.Rotation(-ang, 4, "Y"))
    return [k.build("CrackedWall")]

@model
def WallChunks():
    objs = []
    rnd = random.Random(44)
    for i in range(12):
        k = Kit("M_Basalt")
        x = -2.4 + (i % 4) * 1.6 + rnd.uniform(-0.2, 0.2)
        z = 0.6 + (i // 4) * 1.3 + rnd.uniform(-0.15, 0.15)
        k.ico(0.55, (0, 0, 0), "M_Basalt", subdiv=1, scale=(1.1, 0.7, 0.9), jitter=0.12, seed=500 + i)
        o = k.build(f"Chunk_{i + 1}")
        o.location = (x, 0, z)
        objs.append(o)
    return objs

@model
def Furnace():
    k = Kit("M_Basalt", "M_Iron", "M_Glow")
    k.cyl(4.3, 4.2, 1.0, 16, (0, 0, 0), "M_Basalt")
    verts = k.uvsphere(4.0, (0, 0, 1.0), "M_Basalt", u=16, v=12)
    for v in verts:
        if v.co.z < 1.0:
            v.co.z = 1.0
    k.box((2.6, 1.2, 2.2), (0, -3.6, 2.1), "M_Glow")
    k.box((3.2, 1.0, 0.4), (0, -3.75, 3.3), "M_Iron", bevel=0.05)
    k.cyl(0.7, 0.55, 5.0, 10, (0, 0.6, 4.6), "M_Iron")
    k.torus(4.05, 0.12, (0, 0, 2.0), "M_Iron", segs=32, tube=6)
    return [k.build("Furnace")]

@model
def Hammer():
    f = Kit("M_Iron", "M_Wood")
    for x in (-1.5, 1.5):
        f.box((0.5, 0.5, 4.2), (x, 0, 2.1), "M_Wood", bevel=0.04)
    f.box((3.6, 0.5, 0.5), (0, 0, 4.35), "M_Wood", bevel=0.04)
    f.cyl(0.18, 0.18, 3.2, 10, (-1.6, 0, 3.6), "M_Iron", axis="X")
    frame = f.build("Frame")
    a = Kit("M_Iron", "M_Wood")
    a.box((0.35, 4.0, 0.35), (0, -2.0, 0), "M_Wood", bevel=0.03)
    a.box((1.2, 1.1, 1.1), (0, -4.0, -0.2), "M_Iron", bevel=0.06)
    arm = a.build("Arm")
    arm.location = (0, 0, 3.6)
    return [frame, arm]

@model
def Anvil():
    k = Kit("M_Iron")
    k.box((0.9, 0.6, 0.35), (0, 0, 0.175), "M_Iron", bevel=0.04)
    k.box((0.5, 0.35, 0.4), (0, 0, 0.55), "M_Iron", bevel=0.03)
    k.box((1.1, 0.45, 0.25), (0, 0, 0.87), "M_Iron", bevel=0.03)
    k.cyl(0.2, 0.02, 0.6, 8, (0.55, 0, 0.87), "M_Iron", axis="X")
    return [k.build("Anvil")]

@model
def Gear():
    k = Kit("M_Brass", "M_Iron")
    k.ring(1.0, 0.75, 0.3, 32, (0, 0, -0.15), "M_Brass")
    for i in range(16):
        a = 2 * math.pi * i / 16
        k.box((0.22, 0.3, 0.3), (1.08 * math.cos(a), 1.08 * math.sin(a), 0), "M_Brass",
              rot=Matrix.Rotation(a, 4, "Z"))
    for i in range(4):
        a = math.pi / 2 * i
        k.box((0.75, 0.12, 0.2), (0.37 * math.cos(a), 0.37 * math.sin(a), 0), "M_Iron",
              rot=Matrix.Rotation(a, 4, "Z"))
    k.cyl(0.18, 0.18, 0.4, 12, (0, 0, -0.2), "M_Iron")
    return [k.build("Gear")]

@model
def ForgeGate():
    objs = []
    for nm, x in (("DoorL", -1.15), ("DoorR", 1.15)):
        k = Kit("M_Iron", "M_Brass")
        k.box((2.3, 0.4, 4.6), (0, 0, 2.3), "M_Iron", bevel=0.03)
        for z in (0.8, 2.3, 3.8):
            k.box((2.32, 0.44, 0.16), (0, 0, z), "M_Brass")
        for zz in (0.8, 2.3, 3.8):
            for xx in (-0.8, 0, 0.8):
                k.ico(0.06, (xx, -0.23, zz), "M_Brass", subdiv=1)
        o = k.build(nm)
        o.location = (x, 0, 0)
        objs.append(o)
    fr = Kit("M_Basalt", "M_Glow")
    for x in (-2.8, 2.8):
        fr.box((0.9, 1.2, 5.2), (x, 0, 2.6), "M_Basalt", bevel=0.05)
    fr.box((6.5, 1.2, 0.9), (0, 0, 5.4), "M_Basalt", bevel=0.05)
    fr.box((4.4, 0.05, 0.12), (0, -0.62, 5.1), "M_Glow")
    objs.append(fr.build("GateFrame"))
    return objs

@model
def Hopper():
    k = Kit("M_Iron", "M_Brass")
    for x in (-0.9, 0.9):
        for y in (-0.9, 0.9):
            k.box((0.15, 0.15, 3.0), (x, y, 1.5), "M_Iron")
    k.cyl(0.55, 1.5, 1.2, 4, (0, 0, 2.9), "M_Iron", caps=False)
    k.torus(1.5, 0.06, (0, 0, 4.1), "M_Brass", segs=4, tube=4)
    return [k.build("Hopper")]

# ---- stage 3 ----------------------------------------------------------------
def island(name, radius, seed):
    k = Kit("M_Limestone", "M_StoneDark")
    rnd = random.Random(seed)
    top = k.cyl(radius, radius * 0.97, 0.6, 14, (0, 0, -0.6), "M_Limestone")
    for v in top:
        if v.co.z < -0.3:
            d = Vector((v.co.x, v.co.y, 0))
            if d.length > 0.1:
                v.co += d.normalized() * rnd.uniform(-0.15, 0.1)
    # rocky underside: narrow tip at the bottom, wide just under the paving
    under = k.cyl(0.25, radius * 0.92, radius * 1.5, 12, (0, 0, -0.6 - radius * 1.5), "M_StoneDark")
    for v in under:
        d = Vector((v.co.x, v.co.y, 0))
        if d.length > 0.3 and v.co.z < -0.7:
            v.co += d.normalized() * rnd.uniform(-0.3, 0.3)
            v.co.z += rnd.uniform(-0.2, 0.2)
    # (blossom clumps on the paving were removed after the first in-engine review: they read as pink rocks;
    #  the cherry trees carry the pink accent instead)
    return k.build(name)

MODELS["Island_S"] = lambda: [island("Island_S", 3.5, 601)]
MODELS["Island_M"] = lambda: [island("Island_M", 5.0, 602)]
MODELS["Island_L"] = lambda: [island("Island_L", 7.0, 603)]

@model
def RotatingBridge():
    k = Kit("M_Limestone", "M_Gold")
    k.box((2.6, 12.0, 0.5), (0, 0, -0.25), "M_Limestone", bevel=0.04)
    for x in (-1.32, 1.32):
        k.box((0.1, 12.0, 0.12), (x, 0, 0.0), "M_Gold")
    k.cyl(0.9, 0.7, 0.8, 16, (0, 0, -1.3), "M_Gold")
    k.ring(0.6, 0.45, 0.012, 16, (0, 0, 0.0), "M_Gold")
    return [k.build("RotatingBridge")]

@model
def OrreryPlanet():
    k = Kit("M_Gold", "M_RuneAmber")
    k.cyl(0.05, 0.05, 2.2, 6, (0, 0, 0), "M_Gold", axis="X")
    k.uvsphere(0.45, (2.2, 0, 0), "M_Gold", u=16, v=10)
    k.torus(0.7, 0.03, (2.2, 0, 0), "M_RuneAmber", segs=24, tube=4)
    k.uvsphere(0.25, (0, 0, 0), "M_RuneAmber", u=12, v=8)
    return [k.build("OrreryPlanet")]

@model
def Beacon():
    k = Kit("M_Limestone", "M_Gold", "M_Glow", "M_StoneTrim")
    k.cyl(3.2, 3.0, 1.0, 8, (0, 0, 0), "M_StoneTrim", bevel=0.05)
    k.cyl(2.2, 1.8, 9.0, 8, (0, 0, 1.0), "M_Limestone")
    for z in (3.5, 6.5, 9.5):
        k.cyl(2.25 - z * 0.04, 2.25 - z * 0.04, 0.25, 8, (0, 0, z), "M_Gold")
    k.cyl(3.2, 3.2, 0.5, 8, (0, 0, 10.0), "M_StoneTrim", bevel=0.05)
    k.cyl(1.0, 2.4, 1.4, 12, (0, 0, 10.5), "M_Gold", caps=False)
    k.cyl(1.0, 1.0, 0.05, 12, (0, 0, 10.5), "M_Gold")
    k.cyl(2.2, 2.2, 0.05, 12, (0, 0, 11.7), "M_Glow")
    return [k.build("Beacon")]

@model
def LanternDawn():
    k = Kit("M_Limestone", "M_Gold", "M_StoneTrim")
    k.box((1.2, 1.2, 0.4), (0, 0, 0.2), "M_StoneTrim", bevel=0.05)
    k.cyl(0.25, 0.2, 2.2, 8, (0, 0, 0.4), "M_Limestone")
    k.cyl(0.55, 0.55, 0.08, 8, (0, 0, 2.6), "M_Gold")
    for i in range(8):
        a = i * math.pi / 4
        k.cyl(0.03, 0.03, 0.7, 6, (0.48 * math.cos(a), 0.48 * math.sin(a), 2.68), "M_Gold")
    k.cyl(0.6, 0.6, 0.06, 8, (0, 0, 3.38), "M_Gold")
    k.cyl(0.7, 0.05, 0.6, 8, (0, 0, 3.44), "M_Gold")
    return [k.build("LanternDawn")]

# ---- title / player ---------------------------------------------------------
@model
def Logo():
    if not FONT or not os.path.exists(FONT):
        print("Logo skipped: font not found", FONT)
        return []
    cu = bpy.data.curves.new("LogoCurve", "FONT")
    cu.body = "EMBER"
    cu.font = bpy.data.fonts.load(FONT)
    cu.size = 1.0
    cu.space_character = 1.12
    cu.extrude = 0.12
    cu.bevel_depth = 0.02
    cu.bevel_resolution = 1
    cu.align_x = "CENTER"
    cu.align_y = "BOTTOM"
    tmp = bpy.data.objects.new("LogoTmp", cu)
    bpy.context.scene.collection.objects.link(tmp)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(tmp.evaluated_get(dg))
    bpy.data.objects.remove(tmp)
    me.transform(Matrix.Rotation(math.radians(90), 4, "X"))
    me.materials.clear()
    me.materials.append(mat("M_Gold"))
    return [bpy.data.objects.new("Logo", me)]

@model
def EmberCore():
    k = Kit("M_Ember")
    k.ico(0.5, (0, 0, 0), "M_Ember", subdiv=2, jitter=0.025, seed=9)
    return [k.build("EmberCore", uv="sphere")]

# ------------------------------------------------------------------ build + export
def export_fbx(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    stack = list(objs)
    while stack:
        o = stack.pop()
        o.select_set(True)
        stack.extend(o.children)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.export_scene.fbx(
        filepath=os.path.join(OUT, name + ".fbx"), use_selection=True,
        object_types={"MESH", "EMPTY"}, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z", axis_up="Y", bake_space_transform=True, use_mesh_modifiers=True,
        mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False, path_mode="STRIP")


def main():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    scene = bpy.context.scene
    names = [n for n in MODELS if not ONLY or n in ONLY]
    cols = 8
    report = []
    for i, name in enumerate(names):
        objs = MODELS[name]()
        if not objs:
            continue
        col = bpy.data.collections.new(name)
        scene.collection.children.link(col)
        link([o for o in objs if o.parent is None], col)
        bpy.context.view_layer.update()
        export_fbx([o for o in objs if o.parent is None], name)
        # lay the kit out on a grid inside the .blend (export used the origin)
        offset = Vector(((i % cols) * 9.0, -(i // cols) * 9.0, 0))
        for o in objs:
            if o.parent is None:
                o.location += offset
        tris = sum(len(p.vertices) - 2 for o in col.all_objects if o.type == "MESH" for p in o.data.polygons)
        report.append(f"{name:16s} objects={len(col.all_objects):2d} tris={tris}")
    bpy.ops.wm.save_as_mainfile(filepath=BLEND)
    print("\n".join(report))
    print(f"EXPORTED {len(report)} models to {OUT}")


main()
