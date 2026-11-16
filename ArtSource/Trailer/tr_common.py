"""EMBER trailer - shared helpers: paths, Unity -> Blender conversion, export loaders, keyframes.

Unity is left-handed Y-up, Blender right-handed Z-up. The FBX round trip of the game's models
(Blender -Y front, axis_forward=-Z, axis_up=Y, then Unity's X flip) gives:
    position  p_b = (-x, -z, y)        rotation q_b(w, x, y, z) = (w, x, z, -y)        scale s_b = (sx, sz, sy)
Mesh vertices and normals map like positions; triangle winding is reversed (the map is a reflection).
Cameras, lights and text need one more local rotation (FIX_VIEW) because Unity looks along +Z with +Y up
while Blender cameras/lights look along -Z with +Y up.
"""
import json
import math
import os
import sys
import time

import bpy
import numpy as np
from bpy_extras import anim_utils
from mathutils import Matrix, Quaternion, Vector

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
EXPORT = os.path.join(HERE, "export")
ASSETS = os.path.join(HERE, "assets")
BUILD = os.path.join(ROOT, "Build", "Trailer")
REPORTS = os.path.join(BUILD, "reports")
BLEND = os.path.join(HERE, "EMBER_Trailer.blend")
UNITY_ASSETS = os.path.join(ROOT, "EMBER", "Assets")
GAME_AUDIO = os.path.join(UNITY_ASSETS, "_Ember", "Audio")
FONT_FUTURA = os.path.join(UNITY_ASSETS, "Downloads", "Session3", "Fonts", "Futura Bold.TTF")
SCREENSHOTS = os.path.join(ROOT, "Docs", "Screenshots")
DOCS_TRAILER = os.path.join(ROOT, "Docs", "Trailer")

FPS = 24
ENVS = ["Title", "Stage1", "Stage2", "Stage3", "Ending"]

FIX_VIEW = Quaternion((0.0, 0.0, math.sqrt(0.5), math.sqrt(0.5)))   # 180 deg about (0,1,1)/sqrt2
C3 = Matrix(((-1, 0, 0), (0, 0, -1), (0, 1, 0)))
C4 = C3.to_4x4()

_t0 = time.time()


def log(*a):
    print("[trailer %6.1fs]" % (time.time() - _t0), *a, flush=True)


def args_after_dashes():
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def ensure_dir(p):
    os.makedirs(p, exist_ok=True)
    return p


# ------------------------------------------------------------------ conversion
def u_pos(v):
    return Vector((-v[0], -v[2], v[1]))


def u_dir(v):
    return Vector((-v[0], -v[2], v[1]))


def u_quat(q):
    """Unity quaternion [x, y, z, w] -> Blender Quaternion."""
    x, y, z, w = q
    return Quaternion((w, x, z, -y))


def u_scale(s):
    return Vector((s[0], s[2], s[1]))


def u_matrix(m16):
    """Unity localToWorld (row-major 16 floats) -> Blender 4x4."""
    m = Matrix((m16[0:4], m16[4:8], m16[8:12], m16[12:16]))
    return C4 @ m @ C4.inverted()


def u_euler_quat(pitch, yaw, roll=0.0):
    """Unity Quaternion.Euler(pitch, yaw, roll) (degrees) as a Unity quaternion [x, y, z, w] (Z, then X, then Y)."""
    def axis(ax, deg):
        h = math.radians(deg) * 0.5
        s = math.sin(h)
        return Quaternion((math.cos(h), ax[0] * s, ax[1] * s, ax[2] * s))
    q = axis((0, 1, 0), yaw) @ axis((1, 0, 0), pitch) @ axis((0, 0, 1), roll)   # unity-space math (same algebra)
    return [q.x, q.y, q.z, q.w]


def u_look_rotation(forward, up=(0.0, 1.0, 0.0)):
    """Blender world rotation for a Blender camera/light that looks along Unity direction `forward`."""
    f = u_dir(forward).normalized()
    u = u_dir(up).normalized()
    return f.to_track_quat('-Z', 'Y') if abs(f.dot(u)) < 0.999 else f.to_track_quat('-Z', 'X')


def lens_from_vfov(fov_deg, sensor_h=24.0):
    return (sensor_h * 0.5) / math.tan(math.radians(fov_deg) * 0.5)


def np_u_pos(a):
    """(n,3) Unity positions/normals -> Blender."""
    b = np.empty_like(a)
    b[:, 0] = -a[:, 0]
    b[:, 1] = -a[:, 2]
    b[:, 2] = a[:, 1]
    return b


# ------------------------------------------------------------------ export loading
class Export:
    """One exported scene: scene.json + meshes.bin."""

    def __init__(self, scene):
        self.scene = scene
        d = os.path.join(EXPORT, scene)
        with open(os.path.join(d, "scene.json"), encoding="utf-8") as f:
            self.doc = json.load(f)
        with open(os.path.join(d, "meshes.bin"), "rb") as f:
            self.bin = f.read()
        self.nodes = self.doc["nodes"]
        self.meshes = self.doc["meshes"]
        self.materials = self.doc["materials"]
        self.textures = self.doc["textures"]
        self.clips = self.doc["clips"]
        self.templates = self.doc.get("templates", {})
        self.audio = self.doc.get("audio", {})
        self.render = self.doc["render"]
        self.children = {}
        for n in self.nodes:
            self.children.setdefault(n["parent"], []).append(n["id"])

    def f32(self, ref, width):
        if not ref:
            return None
        off, count = ref
        return np.frombuffer(self.bin, dtype="<f4", count=count * width, offset=off).reshape(count, width)

    def u32(self, ref):
        if not ref:
            return None
        off, count = ref
        return np.frombuffer(self.bin, dtype="<u4", count=count, offset=off)

    def node(self, id_):
        return self.nodes[id_]

    def find(self, name=None, path_end=None, nodes=None):
        out = []
        for n in nodes or self.nodes:
            if name is not None and n["name"] != name:
                continue
            if path_end is not None and not n["path"].endswith(path_end):
                continue
            out.append(n)
        return out

    def find1(self, name=None, path_end=None):
        r = self.find(name, path_end)
        if not r:
            raise KeyError(f"{self.scene}: node not found name={name} path_end={path_end}")
        return r[0]

    def behaviours(self, n, type_name=None):
        return [b for b in n.get("beh", []) if type_name is None or b["type"] == type_name]

    def beh(self, n, type_name):
        b = self.behaviours(n, type_name)
        return b[0] if b else None

    def effective_active(self):
        eff = {}
        for n in self.nodes:            # parents come before children (DFS order)
            p = n["parent"]
            eff[n["id"]] = n["active"] and (p < 0 or eff[p])
        return eff


def field(b, path, default=None):
    """Serialized field of an exported behaviour (propertyPath)."""
    if b is None:
        return default
    return b["f"].get(path, default)


def ref_node(v):
    return v.get("node") if isinstance(v, dict) else None


def color_lin(v):
    """Exported colour dict {raw, lin} or list -> linear rgb tuple."""
    if isinstance(v, dict):
        v = v.get("lin") or v.get("raw")
    return tuple(v[:3])


def srgb_to_lin(c):
    def f(x):
        if x <= 0.04045:
            return x / 12.92
        if x < 1.0:
            return ((x + 0.055) / 1.055) ** 2.4
        return x ** 2.2
    return tuple(f(x) for x in c)


# ------------------------------------------------------------------ keyframes (Blender 5 layered actions)
INTERP = {"CONSTANT": 0, "LINEAR": 1, "BEZIER": 2}


def channelbag(id_data):
    ad = id_data.animation_data or id_data.animation_data_create()
    if ad.action is None:
        ad.action = bpy.data.actions.new(id_data.name + "_Action")
    if ad.action_slot is None:
        slots = [s for s in ad.action.slots if s.target_id_type == id_data.id_type]
        ad.action_slot = slots[0] if slots else ad.action.slots.new(id_type=id_data.id_type, name=id_data.name)
    return anim_utils.action_ensure_channelbag_for_slot(ad.action, ad.action_slot)


def fcurve(id_data, data_path, index=0, replace=False):
    cb = channelbag(id_data)
    fc = cb.fcurves.find(data_path, index=index)
    if fc is not None and replace:
        cb.fcurves.remove(fc)
        fc = None
    if fc is None:
        fc = cb.fcurves.new(data_path, index=index)
    return fc


def set_keys(id_data, data_path, frames, values, index=0, interp="LINEAR", extrapolate=None, replace=True):
    """Write many keys at once. values: list of floats (one channel).
    replace=False merges with the keys already on the curve (new frames win)."""
    if len(frames) == 0:
        return None
    merged = {float(f): float(v) for f, v in zip(frames, values)}
    old_interp = {}
    if not replace:
        cb = channelbag(id_data)
        old = cb.fcurves.find(data_path, index=index)
        if old is not None and len(old.keyframe_points):
            co = np.empty(2 * len(old.keyframe_points), dtype=np.float32)
            old.keyframe_points.foreach_get("co", co)
            ips = np.empty(len(old.keyframe_points), dtype=np.int32)
            old.keyframe_points.foreach_get("interpolation", ips)
            for f, v, ip in zip(co[0::2], co[1::2], ips):
                if float(f) not in merged:
                    merged[float(f)] = float(v)
                    old_interp[float(f)] = int(ip)
    fc = fcurve(id_data, data_path, index, replace=True)
    fr = sorted(merged)
    n = len(fr)
    fc.keyframe_points.add(n)
    co = np.empty(2 * n, dtype=np.float32)
    co[0::2] = fr
    co[1::2] = [merged[f] for f in fr]
    fc.keyframe_points.foreach_set("co", co)
    ip = np.array([old_interp.get(f, INTERP[interp]) for f in fr], dtype=np.int32)
    fc.keyframe_points.foreach_set("interpolation", ip)
    if interp == "BEZIER":
        for k in fc.keyframe_points:
            k.handle_left_type = k.handle_right_type = "AUTO_CLAMPED"
    fc.update()
    if extrapolate:
        fc.extrapolation = extrapolate
    return fc


def set_vec_keys(id_data, data_path, frames, vectors, interp="LINEAR", replace=True):
    vectors = np.asarray(vectors, dtype=np.float64)
    for i in range(vectors.shape[1]):
        set_keys(id_data, data_path, frames, vectors[:, i], index=i, interp=interp, replace=replace)


def key_const(id_data, data_path, frame, value, index=-1):
    """Sparse CONSTANT key (visibility, toggles)."""
    if index >= 0:
        fc = fcurve(id_data, data_path, index)
    else:
        fc = fcurve(id_data, data_path, 0)
    k = fc.keyframe_points.insert(frame, value, options={"FAST"})
    k.interpolation = "CONSTANT"
    return fc


def smoothstep(x):
    x = min(max(x, 0.0), 1.0)
    return x * x * (3 - 2 * x)


def rng(seed):
    return np.random.default_rng(seed & 0xFFFFFFFF)


def stable_seed(*parts):
    h = 2166136261
    for p in parts:
        for ch in str(p):
            h = ((h ^ ord(ch)) * 16777619) & 0xFFFFFFFF
    return h
