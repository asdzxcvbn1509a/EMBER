"""EMBER trailer - deterministic particles from the exported Unity ParticleSystems.

numpy generates one birth record per particle (time, position, velocity, size, colour, seed) over the time
windows in which the emitter is active. The records are stored as a point cloud; one Geometry Nodes group per
emitter evaluates the motion in closed form from Scene Time and instances camera-facing quads. No simulation
cache, so any frame can be rendered on its own.
"""
import math

import bpy
import numpy as np
from mathutils import Matrix, Vector

import tr_common as C
import tr_materials as M

GRAVITY = 9.81


# ------------------------------------------------------------------ Unity MinMaxCurve / gradients
def mm_sample(c, n, r, k=None):
    """Sample a MinMaxCurve n times (constant, two constants or curve*mult at k)."""
    mode = c.get("mode", "Constant")
    if mode == "Constant":
        return np.full(n, float(c["constant"]))
    if mode == "TwoConstants":
        return r.uniform(float(c["min"]), float(c["max"]), n)
    if mode in ("Curve", "TwoCurves"):
        keys = c.get("curve") or [[0, 1, 0, 0], [1, 1, 0, 0]]
        kk = np.zeros(n) if k is None else k
        return np.interp(kk, [p[0] for p in keys], [p[1] for p in keys]) * float(c.get("mult", 1.0))
    return np.full(n, float(c.get("constant", 0.0)))


def mm_const(c):
    mode = c.get("mode", "Constant")
    if mode == "TwoConstants":
        return 0.5 * (float(c["min"]) + float(c["max"]))
    return float(c.get("constant", 0.0))


def gradient_keys(grad):
    """Unity gradient -> sorted [(t, r, g, b, a)] (colour keys are sRGB -> linear)."""
    if not grad:
        return [(0.0, 1, 1, 1, 1), (1.0, 1, 1, 1, 1)]
    cols = grad["colors"]
    alps = grad["alphas"]
    ts = sorted(set([c[0] for c in cols] + [a[0] for a in alps]))
    ct = [c[0] for c in cols]
    out = []
    for t in ts:
        rgb = [np.interp(t, ct, [c[i + 1] for c in cols]) for i in range(3)]
        rgb = C.srgb_to_lin(rgb)
        a = np.interp(t, [a[0] for a in alps], [a[1] for a in alps])
        out.append((t, rgb[0], rgb[1], rgb[2], a))
    return out


def start_color(main):
    sc = main.get("startColor") or {}
    if sc.get("mode") == "Color":
        c = sc["color"]
        rgb = C.srgb_to_lin(c[:3])
        return (rgb[0], rgb[1], rgb[2], c[3])
    return (1.0, 1.0, 1.0, 1.0)


# ------------------------------------------------------------------ shapes (Unity emitter local space)
def shape_sample(shape, n, r):
    """Return (positions, directions) in Unity emitter-local space."""
    if not shape.get("enabled", True):
        pos = np.zeros((n, 3))
        dirs = np.tile([0.0, 0.0, 1.0], (n, 1))
        return pos, dirs
    t = shape.get("type", "Cone")
    rad = float(shape.get("radius", 1.0))
    thick = float(shape.get("thickness", 1.0))
    if t in ("Cone", "ConeVolume", "ConeShell", "ConeVolumeShell"):
        ang = math.radians(float(shape.get("angle", 25.0)))
        rho = np.sqrt(r.uniform(1.0 - thick, 1.0, n)) if thick > 0 else np.ones(n)
        phi = r.uniform(0, 2 * math.pi, n)
        x, y = rho * np.cos(phi), rho * np.sin(phi)
        pos = np.stack([x * rad, y * rad, np.zeros(n)], 1)
        tilt = np.tan(ang)
        dirs = np.stack([x * tilt, y * tilt, np.ones(n)], 1)
    elif t in ("Box", "BoxShell", "BoxEdge"):
        s = np.array(shape.get("scale", [1, 1, 1]), dtype=float)
        pos = (r.random((n, 3)) - 0.5) * s
        dirs = np.tile([0.0, 0.0, 1.0], (n, 1))
    elif t in ("Circle", "CircleEdge"):
        rho = np.sqrt(r.uniform(1.0 - thick, 1.0, n))
        phi = r.uniform(0, 2 * math.pi, n)
        x, y = np.cos(phi), np.sin(phi)
        pos = np.stack([x * rho * rad, y * rho * rad, np.zeros(n)], 1)
        dirs = np.stack([x, y, np.zeros(n)], 1)
    elif t in ("Sphere", "SphereShell", "Hemisphere", "HemisphereShell"):
        v = r.normal(size=(n, 3))
        v /= np.linalg.norm(v, axis=1, keepdims=True) + 1e-9
        if t.startswith("Hemi"):
            v[:, 2] = np.abs(v[:, 2])
        rr = rad * np.cbrt(r.uniform((1.0 - thick) ** 3, 1.0, n)) if thick > 0 else np.full(n, rad)
        pos = v * rr[:, None]
        dirs = v
    else:
        pos = np.zeros((n, 3))
        v = r.normal(size=(n, 3))
        dirs = v / (np.linalg.norm(v, axis=1, keepdims=True) + 1e-9)
    # shape transform (position, rotation in degrees)
    rot = shape.get("rotation", [0, 0, 0])
    if any(abs(a) > 1e-6 for a in rot):
        R = unity_euler_matrix(rot)
        pos = pos @ R.T
        dirs = dirs @ R.T
    pos = pos + np.array(shape.get("position", [0, 0, 0]), dtype=float)
    dirs /= np.linalg.norm(dirs, axis=1, keepdims=True) + 1e-9
    return pos, dirs


def unity_euler_matrix(e):
    """Unity Euler (x, y, z degrees; applied z, x, y) as a 3x3 rotation in Unity space."""
    x, y, z = [math.radians(a) for a in e]
    Rx = np.array([[1, 0, 0], [0, math.cos(x), -math.sin(x)], [0, math.sin(x), math.cos(x)]])
    Ry = np.array([[math.cos(y), 0, math.sin(y)], [0, 1, 0], [-math.sin(y), 0, math.cos(y)]])
    Rz = np.array([[math.cos(z), -math.sin(z), 0], [math.sin(z), math.cos(z), 0], [0, 0, 1]])
    return Ry @ Rx @ Rz


# ------------------------------------------------------------------ births
def births(ps, windows, world_fn, seed, local=False):
    """Birth records for one Unity ParticleSystem.

    windows: list of (t_on, t_off) seconds in which the emitter is active (t_off may be None = forever).
    world_fn(t) -> 4x4 Unity localToWorld (numpy) of the emitter at time t (or a constant matrix).
    Returns dict of numpy arrays in Blender space (world, or emitter-local when local=True)."""
    main, em, shape = ps["main"], ps["emission"], ps["shape"]
    r = C.rng(seed)
    times = []
    rate0 = mm_const(em["rateOverTime"]) if em.get("enabled", True) else 0.0
    loop = main.get("loop", True)
    lt = main["startLifetime"]
    max_life = float(lt["max"]) if lt.get("mode") == "TwoConstants" else float(lt.get("constant", 1.0))
    for w in windows:
        # window: (t_on, t_off) or ((t_on, t_off), rate override)
        if isinstance(w[0], (tuple, list)):
            (t_on, t_off), rate = w[0], (w[1] if w[1] is not None else rate0)
        else:
            (t_on, t_off), rate = w, rate0
        t_end = t_off if t_off is not None else 1e9
        if rate > 0 and t_off is not None and t_off - t_on < 0.25:
            # short windows (moving emitters, one per frame): stochastic count keeps the average rate
            cnt = rate * (t_off - t_on)
            k = int(cnt) + (1 if r.random() < cnt - int(cnt) else 0)
            if k:
                times.append(t_on + r.random(k) * (t_off - t_on))
        elif rate > 0:
            if loop:
                # a running system: only births that can still be alive inside the rendered window matter
                start = max(t_on, WINDOW_START[0] - max_life - 0.5)
                stop = min(t_end, WINDOW_END[0])
            else:
                start, stop = t_on, min(t_end, t_on + float(main.get("duration", 1.0)))
            n = max(0, int((stop - start) * rate))
            if n:
                tt = np.linspace(start, stop, n, endpoint=False) + r.uniform(0, 1.0 / rate, n)
                times.append(tt)
        for b in em.get("bursts", []):
            cnt = int(round(mm_const(b["count"])))
            cycles = max(1, int(b.get("cycles", 1)))
            for c in range(cycles):
                tb = t_on + float(b["time"]) + c * float(b.get("interval", 0.01))
                if tb <= t_end:
                    times.append(np.full(cnt, tb))
    if not times:
        return None
    tb = np.concatenate(times)
    n = len(tb)
    life = mm_sample(main["startLifetime"], n, r)
    speed = mm_sample(main["startSpeed"], n, r)
    size = mm_sample(main["startSize"], n, r)
    pos_l, dir_l = shape_sample(shape, n, r)
    grav = mm_const(main.get("gravity", {"mode": "Constant", "constant": 0}))
    # emitter transform at birth
    p_w = np.empty((n, 3))
    v_w = np.empty((n, 3))
    scale_f = np.ones(n)
    if callable(world_fn):
        mats = [world_fn(t) for t in tb]
    else:
        mats = None
        Mw = np.eye(4) if world_fn is None else np.asarray(world_fn)
    for i in range(n):
        Mi = mats[i] if mats is not None else Mw
        if local:
            Mi = np.eye(4)
        p_w[i] = Mi[:3, :3] @ pos_l[i] + Mi[:3, 3]
        d = Mi[:3, :3] @ dir_l[i]
        ln = np.linalg.norm(d)
        v_w[i] = d / (ln + 1e-9) * speed[i]
        scale_f[i] = 1.0
    seed_v = r.random(n)
    return {
        "tb": tb, "life": life, "size": size * scale_f, "seed": seed_v,
        "p": C.np_u_pos(p_w), "v": C.np_u_pos(v_w), "grav": grav,
    }


WINDOW_START = [0.0]       # first second that will be rendered for this environment (set by tr_anim)
WINDOW_END = [90.0]


# ------------------------------------------------------------------ Geometry Nodes
def _quad_mesh():
    me = bpy.data.meshes.get("FX_quad")
    if me:
        return me
    me = bpy.data.meshes.new("FX_quad")
    me.from_pydata([(-0.5, -0.5, 0), (0.5, -0.5, 0), (0.5, 0.5, 0), (-0.5, 0.5, 0)], [], [(0, 1, 2, 3)])
    uv = me.uv_layers.new(name="UVMap")
    uv.data.foreach_set("uv", [0, 0, 1, 0, 1, 1, 0, 1])
    return me


def _quad_object():
    ob = bpy.data.objects.get("FX_quad")
    if ob is None:
        ob = bpy.data.objects.new("FX_quad", _quad_mesh())
    return ob


def gn_group(key, size_curve, grad, grav, noise_amp, noise_freq, spin):
    name = "FXG_" + key
    ng = bpy.data.node_groups.get(name)
    if ng:
        return ng
    ng = bpy.data.node_groups.new(name, "GeometryNodeTree")
    ng.is_modifier = True
    ng.interface.new_socket(name="Geometry", in_out="INPUT", socket_type="NodeSocketGeometry")
    ng.interface.new_socket(name="Geometry", in_out="OUTPUT", socket_type="NodeSocketGeometry")
    N = ng.nodes
    L = ng.links
    gi = N.new("NodeGroupInput")
    go = N.new("NodeGroupOutput")

    def named(nm, dtype="FLOAT"):
        n = N.new("GeometryNodeInputNamedAttribute")
        n.data_type = dtype
        n.inputs["Name"].default_value = nm
        return n.outputs["Attribute"]

    def mth(op, a, b=None, c=None):
        n = N.new("ShaderNodeMath")
        n.operation = op
        for i, v in enumerate((a, b, c)):
            if v is None:
                continue
            if isinstance(v, (int, float)):
                n.inputs[i].default_value = float(v)
            else:
                L.new(v, n.inputs[i])
        return n.outputs[0]

    def vm(op, a, b=None, scale=None):
        n = N.new("ShaderNodeVectorMath")
        n.operation = op
        for i, v in enumerate((a, b)):
            if v is None:
                continue
            if isinstance(v, (tuple, list)):
                n.inputs[i].default_value = v
            else:
                L.new(v, n.inputs[i])
        if scale is not None:
            if isinstance(scale, (int, float)):
                n.inputs["Scale"].default_value = scale
            else:
                L.new(scale, n.inputs["Scale"])
        return n.outputs["Vector"]

    st = N.new("GeometryNodeInputSceneTime")
    tb, life = named("tb"), named("life")
    age = mth("SUBTRACT", st.outputs["Seconds"], tb)
    k = mth("DIVIDE", age, life)
    dead = mth("MAXIMUM", mth("LESS_THAN", age, 0.0), mth("GREATER_THAN", k, 1.0))
    delete = N.new("GeometryNodeDeleteGeometry")
    delete.domain = "POINT"
    L.new(gi.outputs[0], delete.inputs["Geometry"])
    L.new(dead, delete.inputs["Selection"])
    # position: p + v*age + 0.5 g age^2 (+ noise)
    pos = N.new("GeometryNodeInputPosition").outputs[0]
    v = named("v", "FLOAT_VECTOR")
    p = vm("ADD", pos, vm("SCALE", v, None, age))
    if abs(grav) > 1e-6:
        g2 = mth("MULTIPLY", mth("MULTIPLY", age, age), -0.5 * GRAVITY * grav)
        comb = N.new("ShaderNodeCombineXYZ")
        L.new(g2, comb.inputs["Z"])
        p = vm("ADD", p, comb.outputs[0])
    if noise_amp > 1e-6:
        sd = named("seed")
        w = mth("MULTIPLY", age, 2 * math.pi * max(noise_freq, 0.05))
        ramp = mth("MINIMUM", mth("MULTIPLY", age, 2.0), 1.0)
        comb = N.new("ShaderNodeCombineXYZ")
        for i, (axis, ph) in enumerate((("X", 13.1), ("Y", 29.7), ("Z", 47.3))):
            s = mth("SINE", mth("MULTIPLY_ADD", sd, ph, w))
            L.new(mth("MULTIPLY", mth("MULTIPLY", s, ramp), noise_amp), comb.inputs[axis])
        p = vm("ADD", p, comb.outputs[0])
    setp = N.new("GeometryNodeSetPosition")
    L.new(delete.outputs[0], setp.inputs["Geometry"])
    L.new(p, setp.inputs["Position"])
    # size over lifetime (Float Curve)
    fc = N.new("ShaderNodeFloatCurve")
    curve = fc.mapping.curves[0]
    pts = size_curve or [[0, 1, 0, 0], [1, 1, 0, 0]]
    while len(curve.points) > 2:
        curve.points.remove(curve.points[-1])
    curve.points[0].location = (pts[0][0], pts[0][1])
    curve.points[1].location = (pts[-1][0], pts[-1][1])
    for q in pts[1:-1]:
        curve.points.new(q[0], q[1])
    for pt in curve.points:
        pt.handle_type = "AUTO"
    fc.mapping.clip_min_y = -10
    fc.mapping.clip_max_y = 10
    fc.mapping.use_clip = False
    fc.mapping.update()
    L.new(k, fc.inputs["Value"])
    scale = mth("MULTIPLY", fc.outputs[0], named("size"))
    # colour over lifetime (Color Ramp, rgba)
    cr = N.new("ShaderNodeValToRGB")
    els = cr.color_ramp.elements
    keys = grad
    while len(els) > 1:
        els.remove(els[-1])
    els[0].position = keys[0][0]
    els[0].color = keys[0][1:5]
    for kk in keys[1:]:
        e = els.new(kk[0])
        e.color = kk[1:5]
    L.new(k, cr.inputs["Fac"])
    sc_ = named("scol", "FLOAT_COLOR")
    mixc = N.new("ShaderNodeMix")
    mixc.data_type = "RGBA"
    mixc.blend_type = "MULTIPLY"
    ins = {s.identifier: s for s in mixc.inputs}
    ins["Factor_Float"].default_value = 1.0
    L.new(cr.outputs["Color"], ins["A_Color"])
    L.new(sc_, ins["B_Color"])
    col_rgb = [s for s in mixc.outputs if s.identifier == "Result_Color"][0]
    # alpha = ramp alpha * start alpha (Mix multiplies rgb only)
    sep = N.new("FunctionNodeSeparateColor")
    L.new(col_rgb, sep.inputs["Color"])
    sepa = N.new("FunctionNodeSeparateColor")
    L.new(sc_, sepa.inputs["Color"])
    alpha = mth("MULTIPLY", cr.outputs["Alpha"], sepa.outputs["Alpha"])
    cc = N.new("FunctionNodeCombineColor")
    L.new(sep.outputs["Red"], cc.inputs["Red"])
    L.new(sep.outputs["Green"], cc.inputs["Green"])
    L.new(sep.outputs["Blue"], cc.inputs["Blue"])
    L.new(alpha, cc.inputs["Alpha"])
    store = N.new("GeometryNodeStoreNamedAttribute")
    store.data_type = "FLOAT_COLOR"
    store.domain = "POINT"
    store.inputs["Name"].default_value = "pcol"
    L.new(setp.outputs[0], store.inputs["Geometry"])
    L.new(cc.outputs["Color"], store.inputs["Value"])
    # camera-facing quads
    cam = N.new("GeometryNodeInputActiveCamera")
    oi = N.new("GeometryNodeObjectInfo")
    oi.transform_space = "RELATIVE"
    L.new(cam.outputs[0], oi.inputs["Object"])
    to_cam = vm("SUBTRACT", oi.outputs["Location"], N.new("GeometryNodeInputPosition").outputs[0])
    align = N.new("FunctionNodeAlignRotationToVector")
    align.axis = "Z"
    L.new(to_cam, align.inputs["Vector"])
    rot = align.outputs["Rotation"]
    if spin:
        rr = N.new("FunctionNodeRotateRotation")
        rr.rotation_space = "LOCAL"
        e = N.new("FunctionNodeAxisAngleToRotation")
        e.inputs["Axis"].default_value = (0, 0, 1)
        L.new(mth("MULTIPLY", named("seed"), 6.2832), e.inputs["Angle"])
        L.new(rot, rr.inputs["Rotation"])
        L.new(e.outputs[0], rr.inputs["Rotate By"])
        rot = rr.outputs[0]
    qi = N.new("GeometryNodeObjectInfo")
    qi.inputs["Object"].default_value = _quad_object()
    qi.inputs["As Instance"].default_value = True
    inst = N.new("GeometryNodeInstanceOnPoints")
    L.new(store.outputs[0], inst.inputs["Points"])
    L.new(qi.outputs["Geometry"], inst.inputs["Instance"])
    L.new(rot, inst.inputs["Rotation"])
    L.new(scale, inst.inputs["Scale"])
    real = N.new("GeometryNodeRealizeInstances")      # so Set Material applies; pcol becomes a mesh attribute
    L.new(inst.outputs[0], real.inputs["Geometry"])
    setm = N.new("GeometryNodeSetMaterial")
    L.new(real.outputs[0], setm.inputs["Geometry"])
    L.new(setm.outputs[0], go.inputs[0])
    ng["setm"] = setm.name
    return ng


def emitter_object(env, nid, rec, ps, local_parent=None, name=None):
    """Point cloud object + GN modifier for one emitter (nid: exported node, or None with an explicit name)."""
    if name is None:
        name = "FX:" + env.oname(env.ex.nodes[nid])
    me = bpy.data.meshes.new(name)
    cnt = len(rec["tb"])
    me.vertices.add(cnt)
    me.vertices.foreach_set("co", rec["p"].astype(np.float32).ravel())
    for nm, arr, dt in (("tb", rec["tb"], "FLOAT"), ("life", rec["life"], "FLOAT"), ("size", rec["size"], "FLOAT"),
                        ("seed", rec["seed"], "FLOAT")):
        a = me.attributes.new(nm, dt, "POINT")
        a.data.foreach_set("value", arr.astype(np.float32))
    a = me.attributes.new("v", "FLOAT_VECTOR", "POINT")
    a.data.foreach_set("vector", rec["v"].astype(np.float32).ravel())
    sc = np.tile(np.array(start_color(ps["main"]), dtype=np.float32), (cnt, 1))
    a = me.attributes.new("scol", "FLOAT_COLOR", "POINT")
    a.data.foreach_set("color", sc.ravel())
    ob = bpy.data.objects.new(name, me)
    env.coll.objects.link(ob)
    if local_parent is not None:
        ob.parent = local_parent
    main = ps["main"]
    sol = ps.get("sizeOverLifetime", {})
    size_curve = sol["size"].get("curve") if sol.get("enabled") and sol.get("size") else None
    col = ps.get("colorOverLifetime", {})
    grad = gradient_keys(col["color"].get("gradient")) if col.get("enabled") and col.get("color") else gradient_keys(None)
    nz = ps.get("noise", {})
    noise_amp = mm_const(nz["strength"]) * 0.35 if nz.get("enabled") else 0.0
    noise_freq = float(nz.get("frequency", 0.5)) if nz.get("enabled") else 0.5
    grav = rec["grav"]
    spin = ps["main"]["startRotation"].get("mode") == "TwoConstants"
    key = f"{len(grad)}_{C.stable_seed(size_curve, grad, grav, round(noise_amp, 3), round(noise_freq, 3), spin) % 10**8}"
    ng = gn_group(key, size_curve, grad, grav, noise_amp, noise_freq, spin)
    md = ob.modifiers.new("FX", "NODES")
    md.node_group = ng
    # per-emitter material: Set Material lives in a per-emitter copy only when materials differ
    rmat = (ps.get("renderer") or {}).get("mat", -1)
    mat = M.particle_material(env.ex, rmat, "particle")
    ng_e = ng
    setm = ng_e.nodes.get(ng["setm"])
    if setm.inputs["Material"].default_value not in (None, mat):
        ng_e = ng.copy()
        md.node_group = ng_e
        setm = ng_e.nodes.get(ng["setm"])
    setm.inputs["Material"].default_value = mat
    ob.visible_shadow = False
    return ob


def unity_world(n):
    return np.array(n["wm"], dtype=float).reshape(4, 4)


def build_static(env, t0, t1, active=None):
    """Emitters with fixed transforms, active over [t0, t1] (or windows from `active`: nid -> list of windows)."""
    WINDOW_START[0], WINDOW_END[0] = t0, t1
    made = 0
    for nid in env.ps_nodes:
        n = env.ex.nodes[nid]
        ps = n["ps"]
        if active is not None and nid in active:
            windows = active[nid]
        else:
            windows = [(t0 - 100.0, None)] if env.eff[nid] and ps["main"].get("playOnAwake", True) else []
        if not windows:
            continue
        local = ps["main"].get("space") == "Local"
        rec = births(ps, windows, unity_world(n), C.stable_seed(env.name, nid), local=local)
        if rec is None:
            continue
        emitter_object(env, nid, rec, ps, local_parent=env.obj[nid] if local else None)
        made += 1
    C.log(f"{env.name}: {made} particle emitters")


def build_moving(env, key, ps, windows, world_fn, seed, parent, t0, t1):
    """Emitter that is not a scene node: the ember's trail / aura, cloned props (boulder fire)."""
    WINDOW_START[0], WINDOW_END[0] = t0, t1
    local = parent is not None
    rec = births(ps, windows, None if local else world_fn, seed, local=local)
    if rec is None:
        return None
    return emitter_object(env, None, rec, ps, local_parent=parent, name="FX:" + key)
