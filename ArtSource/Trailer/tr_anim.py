"""EMBER trailer - animation director for one environment scene.

Replays the game's own wiring: when the ember (or a released prop) enters a trigger, the class Condition->Action
scripts on that node run (HitAndShowHide, HitAndPlayAnimation, HitAndClone, HitAndDestroy, HitAndSetTransform,
TriggerSound, Checkpoint). Objects that become active run their OnEnable behaviours (FadeInLight, PopIn,
ExplodeOnEnable, ShakeOnEnable, SkyBlend, VolumeBlend, AudioSource.playOnAwake, particles). Ambient scripts
(Flicker, Bob, KinematicMover, looping clips, LookAtTarget, CameraFacing) run over the whole timeline.

All motion maths is done in Unity coordinates (so it reads like the level builders) and converted when keyed.
"""
import math

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector
from mathutils.bvhtree import BVHTree

import shots as S
import tr_common as C
import tr_fx
import tr_materials as M

FPS = S.FPS
R_BALL = 0.5
G = 9.81
UP_U = np.array([0.0, 1.0, 0.0])


def t_of(frame):
    return frame / FPS


def ease(k, kind="inout"):
    k = min(max(k, 0.0), 1.0)
    if kind == "linear":
        return k
    if kind == "in":
        return k * k
    if kind == "out":
        return 1 - (1 - k) * (1 - k)
    if kind == "outcubic":
        return 1 - (1 - k) ** 3
    return k * k * (3 - 2 * k)


def ease_out_back(x):
    c1 = 1.70158
    c3 = c1 + 1
    return 1 + c3 * (x - 1) ** 3 + c1 * (x - 1) ** 2


def catmull(points, k):
    """Centripetal-ish Catmull-Rom through points (list of np arrays), k in [0,1] by segment index."""
    n = len(points)
    if n == 1:
        return points[0]
    seg = min(int(k * (n - 1)), n - 2)
    u = k * (n - 1) - seg
    p0 = points[max(seg - 1, 0)]
    p1 = points[seg]
    p2 = points[seg + 1]
    p3 = points[min(seg + 2, n - 1)]
    return 0.5 * ((2 * p1) + (-p0 + p2) * u + (2 * p0 - 5 * p1 + 4 * p2 - p3) * u * u + (-p0 + 3 * p1 - 3 * p2 + p3) * u ** 3)


def resample_by_length(points, n=400):
    """Dense polyline of a Catmull-Rom curve and its cumulative length (for constant-speed travel)."""
    pts = np.array([catmull(points, i / (n - 1)) for i in range(n)])
    d = np.r_[0.0, np.cumsum(np.linalg.norm(np.diff(pts, axis=0), axis=1))]
    return pts, d


def at_length(pts, d, s):
    s = min(max(s, 0.0), d[-1])
    i = min(np.searchsorted(d, s), len(d) - 1)
    if i == 0:
        return pts[0]
    f = (s - d[i - 1]) / max(d[i] - d[i - 1], 1e-9)
    return pts[i - 1] + (pts[i] - pts[i - 1]) * f


def look_quat(pos_b, tgt_b, roll=0.0):
    d = (Vector(tgt_b) - Vector(pos_b))
    if d.length < 1e-6:
        d = Vector((0, 1, 0))
    q = d.normalized().to_track_quat("-Z", "Y")
    if roll:
        q = q @ Quaternion((0, 0, 1), roll)
    return q


class Director:
    def __init__(self, env):
        self.env = env
        self.ex = env.ex
        self.sc = env.scene
        self.events = {}           # nid -> [(frame, active)]
        self.sfx = []              # dicts: frame, clip, gain, label
        self.log = []              # (frame, text)
        self.shakes = []           # (frame, amplitude, duration)
        self.fx_moving = []        # (key, ps, windows, world_fn, seed)
        self.keys = {}             # (object name, data path, index) -> {frame: value}
        self.const_keys = {}       # (object name, data path, index) -> {frame: value} (CONSTANT)
        self.clones = []           # clone records (template, objects)
        self.ball_pos = {}         # frame -> Unity position of the shot ball (ball proxy, look-at targets)
        self.cam_pos = {}          # frame -> Blender camera location (camera proxy)
        self.light_fade = []       # (light object, frame, duration, flare)
        self.colliders = None
        self.frames = (1, S.TOTAL)
        self.shot_list = [s for s in S.SHOTS if s["env"] == env.name]
        if self.shot_list:
            self.frames = (max(1, self.shot_list[0]["start"] - 48), min(S.TOTAL, self.shot_list[-1]["end"] + 2))
        self.by_path = {}
        for n in self.ex.nodes:
            self.by_path.setdefault(n["path"], []).append(n["id"])
        self.initial_rules()

    # -------------------------------------------------------------- lookup
    def node(self, path_end, index=0):
        hits = [n["id"] for n in self.ex.nodes if n["path"] == path_end or n["path"].endswith("/" + path_end)]
        if not hits:
            raise KeyError(f"{self.env.name}: no node '{path_end}'")
        return hits[index]

    def child(self, nid, name):
        for c in self.ex.children.get(nid, []):
            if self.ex.nodes[c]["name"] == name:
                return c
        raise KeyError(f"{self.ex.nodes[nid]['path']}: no child '{name}'")

    def descend(self, nid, rel):
        for part in [p for p in rel.split("/") if p]:
            nid = self.child(nid, part)
        return nid

    def world_u(self, nid):
        return np.array(self.ex.nodes[nid]["wm"], dtype=float).reshape(4, 4)

    def pos_u(self, nid):
        return self.world_u(nid)[:3, 3].copy()

    def beh(self, nid, type_):
        return self.ex.beh(self.ex.nodes[nid], type_)

    # -------------------------------------------------------------- start-of-level rules
    def initial_rules(self):
        # Checkpoint.Start: litFx.SetActive(isStart)
        for n in self.ex.nodes:
            for b in n.get("beh", []):
                if b["type"] == "Checkpoint":
                    lit = C.ref_node(b["f"].get("litFx"))
                    if lit is not None:
                        self.set_active(lit, 0, bool(b["f"].get("isStart")))

    # -------------------------------------------------------------- activity
    def set_active(self, nid, frame, active):
        self.events.setdefault(nid, []).append((frame, bool(active)))

    def own_active(self, nid, frame):
        state = self.ex.nodes[nid]["active"]
        for f, a in sorted(self.events.get(nid, []), key=lambda e: e[0]):
            if f <= frame:
                state = a
        return state

    def effective(self, nid, frame):
        while nid >= 0:
            if not self.own_active(nid, frame):
                return False
            nid = self.ex.nodes[nid]["parent"]
        return True

    def change_frames(self, nid):
        out = set()
        k = nid
        while k >= 0:
            out.update(f for f, _ in self.events.get(k, []))
            k = self.ex.nodes[k]["parent"]
        return sorted(out)

    def transitions(self, nid):
        """[(frame, effective)] from frame 0 on, only where it changes."""
        cur = self.effective(nid, 0)
        out = [(0, cur)]
        for f in self.change_frames(nid):
            if f <= 0:
                continue
            v = self.effective(nid, f)
            if v != cur:
                out.append((f, v))
                cur = v
        if self.effective(nid, 0) != out[0][1]:
            out[0] = (0, self.effective(nid, 0))
        return out

    # -------------------------------------------------------------- sound
    def cue(self, frame, clip, gain=1.0, label="", pos_u=None):
        if clip:
            self.sfx.append({"frame": int(frame), "clip": clip, "gain": float(gain), "label": label,
                             "pos": None if pos_u is None else [float(x) for x in pos_u]})

    # -------------------------------------------------------------- Condition -> Action replay
    def fire(self, nid, frame, who="Player", exit=False, hitter_pos=None):
        n = self.ex.nodes[nid]
        did = False
        for b in n.get("beh", []):
            t, f = b["type"], b["f"]
            name = f.get("objectName")
            if name is not None and name != who:
                continue
            if t == "HitAndShowHide":
                tgt = C.ref_node(f.get("target"))
                show = f.get("action") == "Show"
                if tgt is not None:
                    if exit and f.get("cancelOnExit"):
                        self.set_active(tgt, frame, not show)
                    elif not exit:
                        self.set_active(tgt, frame, show)
                    did = True
            elif t == "HitAndPlayAnimation":
                tgt = C.ref_node(f.get("target"))
                clip = f.get("exitAnimName") if exit else f.get("enterAnimName")
                if tgt is not None and clip:
                    self.play_clip(tgt, clip, frame)
                    did = True
            elif t == "HitAndDestroy" and not exit:
                tgt = C.ref_node(f.get("target"))
                if tgt is not None:
                    self.set_active(tgt, frame, False)
                    did = True
            elif t == "TriggerSound" and not exit:
                self.cue(frame, (f.get("clip") or {}).get("audio"), f.get("volume", 0.9), n["name"], self.pos_u(nid))
            elif t == "Checkpoint" and not exit:
                lit = C.ref_node(f.get("litFx"))
                if lit is not None and not self.effective(lit, frame):
                    self.set_active(lit, frame, True)
                    self.cue(frame, (f.get("sound") or {}).get("audio"), 0.6, "checkpoint", self.pos_u(nid))
            elif t == "Collectible" and not exit:
                fx = C.ref_node(f.get("pickupFx"))
                self.set_active(nid, frame, False)
                if fx is not None:
                    self.set_active(fx, frame, True)
                self.cue(frame, (f.get("sound") or {}).get("audio"), 0.7, "shard", self.pos_u(nid))
        if did or not exit:
            self.log.append((frame, ("exit " if exit else "fire ") + n["path"] + " by " + who))
        return did

    def behaviour_targets(self, nid, type_):
        return [b for b in self.ex.nodes[nid].get("beh", []) if b["type"] == type_]

    # -------------------------------------------------------------- legacy clips
    def clip_by_name(self, anim_nid, name):
        a = self.ex.nodes[anim_nid].get("anim") or {}
        for cid in a.get("clips", []):
            if self.ex.clips[cid]["name"] == name:
                return self.ex.clips[cid]
        for c in self.ex.clips:
            if c["name"] == name:
                return c
        raise KeyError(f"clip {name} not on {self.ex.nodes[anim_nid]['path']}")

    def play_clip(self, anim_nid, name, frame, loop_until=None, speed=1.0):
        clip = self.clip_by_name(anim_nid, name)
        length = max(float(clip["length"]), 1.0 / FPS)
        groups = {}
        for cv in clip["curves"]:
            groups.setdefault(cv["path"], []).append(cv)
        if loop_until is None:
            nfr = int(math.ceil(length / speed * FPS)) + 1
            frames = [frame + i for i in range(nfr)]
            times = [min(i * speed / FPS, length) for i in range(nfr)]
        else:
            frames = list(range(frame, loop_until + 1))
            times = [((f - frame) * speed / FPS) % length for f in frames]
        for path, cvs in groups.items():
            try:
                tgt = self.descend(anim_nid, path)
            except KeyError:
                continue
            n = self.ex.nodes[tgt]
            ob = self.env.obj[tgt]
            props = {cv["prop"]: cv for cv in cvs}

            def sample(prop, default):
                cv = props.get(prop)
                if cv is None:
                    return np.full(len(times), default)
                s = np.asarray(cv["s"], dtype=float)
                return np.interp(np.asarray(times) * cv["rate"], np.arange(len(s)), s)

            if any(p.startswith("m_LocalPosition") for p in props):
                lp = n["lp"]
                xs, ys, zs = sample("m_LocalPosition.x", lp[0]), sample("m_LocalPosition.y", lp[1]), sample("m_LocalPosition.z", lp[2])
                vals = [tuple(C.u_pos((x, y, z))) for x, y, z in zip(xs, ys, zs)]
                self.key_vec(ob, "location", frames, vals, hold_before=tuple(C.u_pos(lp)))
            if any(p.startswith("m_LocalRotation") for p in props):
                lr = n["lr"]
                qs = [C.u_quat((x, y, z, w)) for x, y, z, w in zip(sample("m_LocalRotation.x", lr[0]), sample("m_LocalRotation.y", lr[1]),
                                                                    sample("m_LocalRotation.z", lr[2]), sample("m_LocalRotation.w", lr[3]))]
                qs = self.continuous_quats(qs)
                self.key_vec(ob, "rotation_quaternion", frames, [tuple(q) for q in qs], hold_before=tuple(C.u_quat(lr)))
            if any(p.startswith("m_LocalScale") for p in props):
                ls = n["ls"]
                vals = [tuple(C.u_scale((x, y, z))) for x, y, z in zip(sample("m_LocalScale.x", ls[0]), sample("m_LocalScale.y", ls[1]), sample("m_LocalScale.z", ls[2]))]
                self.key_vec(ob, "scale", frames, vals, hold_before=tuple(C.u_scale(ls)))
        # animation events (PlaySfx) -> sound cues
        if loop_until is None:
            for e in clip.get("events", []):
                if e["fn"] == "PlaySfx":
                    self.cue(frame + e["t"] / speed * FPS, e["s"], 0.8, clip["name"], self.pos_u(anim_nid))
        else:
            for e in clip.get("events", []):
                if e["fn"] == "PlaySfx":
                    f0 = frame + e["t"] / speed * FPS
                    while f0 <= loop_until:
                        if self.in_shots(f0):
                            self.cue(f0, e["s"], 0.5, clip["name"], self.pos_u(anim_nid))
                        f0 += length / speed * FPS
        self.log.append((frame, f"clip {name} on {self.ex.nodes[anim_nid]['path']}"))

    def in_shots(self, frame):
        return any(s["start"] <= frame <= s["end"] for s in self.shot_list)

    @staticmethod
    def continuous_quats(qs):
        out = [qs[0]]
        for q in qs[1:]:
            if out[-1].dot(q) < 0:
                q = -q
            out.append(q)
        return out

    # -------------------------------------------------------------- keys
    def key_vec(self, ob, path, frames, values, hold_before=None, interp="LINEAR"):
        """Object transform keys are written at once (the collision BVH evaluates them), merged with earlier keys."""
        if not frames:
            return
        fr = list(frames)
        vals = [tuple(v) for v in values]
        if hold_before is not None:
            fr = [fr[0] - 1] + fr
            vals = [tuple(hold_before)] + vals
        C.set_vec_keys(ob, path, fr, vals, interp=interp, replace=False)

    def key_scalar(self, id_name, path, frame, value, constant=False, id_kind="OBJECT"):
        buf = self.const_keys if constant else self.keys
        buf.setdefault((id_kind + ":" + id_name, path, 0) if id_kind != "OBJECT" else (id_name, path, 0), {})[frame] = value

    # -------------------------------------------------------------- triggers (static volumes, Unity space)
    def trigger_volumes(self):
        if hasattr(self, "_triggers"):
            return self._triggers
        vols = []
        for n in self.ex.nodes:
            behs = [b for b in n.get("beh", []) if b["type"] in ("HitAndShowHide", "HitAndPlayAnimation", "HitAndClone", "HitAndDestroy",
                                                                 "HitAndSetTransform", "TriggerSound", "Checkpoint", "Collectible", "HitAndLoadScene")]
            if not behs:
                continue
            trig_behs = [b for b in behs if b["f"].get("checkType", "Trigger") == "Trigger" or b["type"] in ("Checkpoint", "Collectible", "TriggerSound")]
            if not trig_behs:
                continue
            for c in n.get("colliders", []):
                if not c.get("trigger"):
                    continue
                M = self.world_u(n["id"])
                inv = np.linalg.inv(M)
                if c["type"] == "BoxCollider":
                    half = np.array(c["size"]) * 0.5
                    vols.append(("box", n["id"], inv, np.array(c["center"]), half, M))
                elif c["type"] == "SphereCollider":
                    vols.append(("sphere", n["id"], inv, np.array(c["center"]), float(c["radius"]), M))
        self._triggers = vols
        return vols

    def inside(self, vol, p_u, margin):
        kind, nid, inv, center, size, M = vol
        lp = inv[:3, :3] @ p_u + inv[:3, 3] - center
        sx = np.linalg.norm(M[:3, 0])
        m = margin / max(sx, 1e-6)
        if kind == "box":
            return bool(np.all(np.abs(lp) <= size + m))
        return float(np.linalg.norm(lp)) <= size + m

    def scan_triggers(self, frames, positions_u, who="Player", radius=R_BALL, skip=(), on_enter=None):
        """Fire triggers the path enters (and cancel-on-exit ones it leaves). Returns [(frame, nid)] entered."""
        entered = []
        state = {}
        for f, p in zip(frames, positions_u):
            for vol in self.trigger_volumes():
                nid = vol[1]
                if nid in skip:
                    continue
                if not self.effective(nid, f):
                    continue
                now = self.inside(vol, p, radius)
                was = state.get(nid, False)
                if now and not was:
                    entered.append((f, nid))
                    if on_enter is None or on_enter(f, nid) is not False:
                        self.fire(nid, f, who)
                elif was and not now:
                    self.fire(nid, f, who, exit=True)
                state[nid] = now
        return entered

    # -------------------------------------------------------------- collision (Blender proxies of the Unity colliders)
    def build_collider_proxies(self):
        if self.colliders is not None:
            return
        coll = bpy.data.collections.new("COLLIDERS_" + self.env.name)
        self.sc.collection.children.link(coll)
        coll.hide_render = True
        cube = bpy.data.meshes.get("COL_cube")
        if cube is None:
            cube = bpy.data.meshes.new("COL_cube")
            v = [(x, y, z) for x in (-1, 1) for y in (-1, 1) for z in (-1, 1)]
            f = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
            cube.from_pydata(v, [], f)
        self.colliders = []
        for n in self.ex.nodes:
            for i, c in enumerate(n.get("colliders", [])):
                if c.get("trigger") or not c.get("enabled", True):
                    continue
                parent = self.env.obj[n["id"]]
                if c["type"] == "MeshCollider" and c.get("mesh", -1) >= 0:
                    me = self.env.mesh(c["mesh"])
                    if me is None:
                        continue
                    ob = bpy.data.objects.new(f"COL:{n['id']}:{i}", me)
                    coll.objects.link(ob)
                    ob.parent = parent
                else:
                    ob = bpy.data.objects.new(f"COL:{n['id']}:{i}", cube)
                    coll.objects.link(ob)
                    ob.parent = parent
                    center = c.get("center", [0, 0, 0])
                    if c["type"] == "BoxCollider":
                        size = c["size"]
                    elif c["type"] == "SphereCollider":
                        size = [c["radius"] * 2] * 3
                    else:
                        r = c.get("radius", 0.5)
                        h = c.get("height", 1.0)
                        size = [r * 2, r * 2, r * 2]
                        size[c.get("direction", 1)] = max(h, r * 2)
                    ob.location = C.u_pos(center)
                    ob.scale = C.u_scale([s * 0.5 for s in size])
                ob.display_type = "WIRE"
                self.colliders.append((n["id"], ob))

    def collision_bvh(self, frame, exclude_nodes=()):
        """World-space BVH (Blender coords) of the colliders active at `frame`, with animation applied."""
        self.build_collider_proxies()
        if bpy.context.window is not None and bpy.context.window.scene != self.sc:
            bpy.context.window.scene = self.sc
        self.sc.frame_set(int(frame))
        self.sc.view_layers[0].update()
        verts, polys = [], []
        for nid, ob in self.colliders:
            if nid in exclude_nodes or not self.effective(nid, frame):
                continue
            m = ob.matrix_world
            base = len(verts)
            verts.extend(m @ v.co for v in ob.data.vertices)
            polys.extend([base + i for i in p.vertices] for p in ob.data.polygons)
        return BVHTree.FromPolygons(verts, polys) if polys else None

    def ground_y(self, bvh, x_u, z_u, y_from):
        """Unity y of the highest collider surface below (x, y_from, z)."""
        if bvh is None:
            return None
        o = C.u_pos((x_u, y_from, z_u))
        hit = bvh.ray_cast(o, Vector((0, 0, -1)), 200.0)
        if hit[0] is None:
            return None
        return hit[0].z

    # -------------------------------------------------------------- the ember (one rig per shot)
    def ball_rig(self, shot_id):
        env = self.env
        pl = self.node("Player")
        vis = self.child(pl, "Visual")
        mr = self.ex.nodes[vis]["mr"]
        root = bpy.data.objects.new(f"BALL_{shot_id}", None)
        root.empty_display_size = 0.6
        env.coll.objects.link(root)
        me = env.mesh(mr["mesh"])
        v = bpy.data.objects.new(f"BALLV_{shot_id}", me)
        env.coll.objects.link(v)
        v.parent = root
        v.rotation_mode = "QUATERNION"
        for i, slot in enumerate(v.material_slots):
            slot.link = "OBJECT"
            slot.material = env.obj[vis].material_slots[i].material if i < len(env.obj[vis].material_slots) else None
        v.visible_shadow = False
        Ld = self.ex.nodes[self.child(pl, "EmberLight")]["light"]
        ld = bpy.data.lights.new(f"BALLL_{shot_id}", "POINT")
        ld.color = tuple(Ld["colorLin"][:3])
        ld.energy = 4 * math.pi ** 2 * float(Ld["intensity"])
        ld.use_soft_falloff = False
        ld.shadow_soft_size = 0.1
        ld.use_shadow = True
        ld.use_custom_distance = True
        ld.cutoff_distance = float(Ld["range"]) * 1.2
        lo = bpy.data.objects.new(f"BALLL_{shot_id}", ld)
        env.coll.objects.link(lo)
        lo.parent = root
        rig = {"root": root, "visual": v, "light": lo, "shot": shot_id}
        return rig

    def solve_ball(self, shot, segments, bvh_frame=None, rig=None, who="Player", triggers=True, pre=12, post=2, skip_triggers=(),
                   t_start=0.0):
        """Ember path for a shot. Segments (Unity coords, seconds from shot start):
          ("at", (x, z) or (x, y, z))           place
          ("roll", dur, [(x, z) ...], ease)      roll through the points (y from the colliders)
          ("wait", dur)
          ("jump", dur, (x, z), apex)            parabola onto the target
          ("path", dur, [(x, y, z) ...], ease)   explicit 3D path (no ground snapping)
          ("ride", dur, fn(t)->unity pos)        attached to a moving platform
          ("teleport", (x, y, z))                instantaneous
        Returns dict(frames, pos_u (n,3), rig)."""
        start, end = shot["start"] - pre, shot["end"] + post
        frames = list(range(start, end + 1))
        bvh = self.collision_bvh(bvh_frame or (shot["start"] + shot["end"]) // 2)
        track = []                         # (time, pos_u, grounded)
        cur = None
        t = float(t_start)                 # negative: the ember is already moving when the shot starts

        def ground(x, z, y_hint):
            y = self.ground_y(bvh, x, z, y_hint + 2.5)
            return (y if y is not None else y_hint - R_BALL) + R_BALL

        for seg in segments:
            kind = seg[0]
            if kind == "at":
                p = seg[1]
                if len(p) == 2:
                    cur = np.array([p[0], ground(p[0], p[1], 1.0), p[1]])     # ray from 3.5 m: under arches, not on them
                else:
                    cur = np.array(p, dtype=float)
                track.append((t, cur.copy(), True))
            elif kind == "teleport":
                cur = np.array(seg[1], dtype=float)
                track.append((t + 1e-4, cur.copy(), False))
            elif kind == "wait":
                t += seg[1]
                track.append((t, cur.copy(), True))
            elif kind == "roll":
                dur, pts = seg[1], seg[2]
                kind_e = seg[3] if len(seg) > 3 else "inout"
                ctrl = [cur[[0, 2]]] + [np.array(p[:2] if len(p) == 2 else (p[0], p[2]), dtype=float) for p in pts]
                dense, dist = resample_by_length(ctrl)
                n = max(2, int(dur * FPS * 2))
                y = cur[1]
                for i in range(1, n + 1):
                    k = i / n
                    xz = at_length(dense, dist, ease(k, kind_e) * dist[-1])
                    y = ground(xz[0], xz[1], y)
                    track.append((t + k * dur, np.array([xz[0], y, xz[1]]), True))
                t += dur
                cur = track[-1][1].copy()
            elif kind == "jump":
                dur, tgt = seg[1], seg[2]
                apex = seg[3] if len(seg) > 3 else 1.6
                p0 = cur.copy()
                y1 = ground(tgt[0], tgt[1], p0[1] + apex)
                p1 = np.array([tgt[0], y1, tgt[1]])
                n = max(2, int(dur * FPS * 2))
                for i in range(1, n + 1):
                    k = i / n
                    base = p0 + (p1 - p0) * k
                    # parabola with apex height above the higher end
                    h = max(p0[1], p1[1]) + apex - base[1]
                    base[1] += 4 * h * k * (1 - k) * (1.0 if k < 1 else 0)
                    track.append((t + k * dur, base, k >= 1.0))
                t += dur
                cur = p1.copy()
            elif kind == "path":
                dur, pts = seg[1], seg[2]
                kind_e = seg[3] if len(seg) > 3 else "inout"
                ctrl = [cur] + [np.array(p, dtype=float) for p in pts]
                dense, dist = resample_by_length(ctrl)
                n = max(2, int(dur * FPS * 2))
                for i in range(1, n + 1):
                    k = i / n
                    track.append((t + k * dur, at_length(dense, dist, ease(k, kind_e) * dist[-1]), False))
                t += dur
                cur = track[-1][1].copy()
            elif kind == "ride":
                dur, fn = seg[1], seg[2]
                n = max(2, int(dur * FPS))
                for i in range(0, n + 1):
                    tt = t + i / n * dur
                    track.append((tt, np.asarray(fn(shot["start"] + tt * FPS), dtype=float), True))
                t += dur
                cur = track[-1][1].copy()
        # sample per frame (track times are from shot start)
        tt = np.array([x[0] for x in track])
        pp = np.array([x[1] for x in track])
        pos = []
        for f in frames:
            ts = (f - shot["start"]) / FPS
            if ts <= tt[0]:
                pos.append(pp[0])
            elif ts >= tt[-1]:
                pos.append(pp[-1])
            else:
                i = np.searchsorted(tt, ts)
                a = (ts - tt[i - 1]) / max(tt[i] - tt[i - 1], 1e-9)
                # teleport: no interpolation across a jump in position
                if np.linalg.norm(pp[i] - pp[i - 1]) > 3.0 and tt[i] - tt[i - 1] < 0.01:
                    pos.append(pp[i - 1] if a < 1 else pp[i])
                else:
                    pos.append(pp[i - 1] + (pp[i] - pp[i - 1]) * a)
        pos = np.array(pos)
        rig = rig or self.ball_rig(shot["id"])
        self.key_ball(rig, frames, pos, shot)
        if triggers:
            core = [(f, p) for f, p in zip(frames, pos) if shot["start"] <= f <= shot["end"]]
            self.scan_triggers([c[0] for c in core], [c[1] for c in core], who=who, skip=skip_triggers)
        for f, p in zip(frames, pos):
            if shot["start"] - 1 <= f <= shot["end"] + 1:
                self.ball_pos[f] = p
        res = {"frames": frames, "pos": pos, "rig": rig}
        self.trail(rig, frames, pos, shot)
        return res

    def key_ball(self, rig, frames, pos_u, shot):
        pos_b = np.array([tuple(C.u_pos(p)) for p in pos_u])
        root, vis, light = rig["root"], rig["visual"], rig["light"]
        C.set_vec_keys(root, "location", frames, pos_b)
        # rolling: integrate the rotation from the horizontal travel (sub-steps for smoothness)
        q = Quaternion()
        quats = [q.copy()]
        for i in range(1, len(pos_b)):
            d = Vector(pos_b[i] - pos_b[i - 1])
            d.z = 0.0
            dist = d.length
            if dist > 1e-6:
                axis = Vector((0, 0, 1)).cross(d).normalized()
                q = Quaternion(axis, dist / R_BALL) @ q
            quats.append(q.copy())
        quats = self.continuous_quats(quats)
        C.set_vec_keys(vis, "rotation_quaternion", frames, [tuple(x) for x in quats])
        # breathing light (PlayerFX)
        base = light.data.energy
        en = [base * (1 + 0.08 * math.sin(t_of(f) * 7.3) + 0.05 * math.sin(t_of(f) * 13.1)) for f in frames]
        C.set_keys(light.data, "energy", frames, en)
        # visible only inside its shot
        for ob in (root, vis, light):
            for f, h in ((0, True), (shot["start"] - 1, False), (shot["end"] + 2, True)):
                ob.hide_render = h
                ob.keyframe_insert("hide_render", frame=f)
            fc = C.fcurve(ob, "hide_render")
            for k in fc.keyframe_points:
                k.interpolation = "CONSTANT"

    def trail(self, rig, frames, pos_u, shot):
        """Spark trail (Player/Trail) births along the path; rate = 3 + 7 * speed (PlayerFX)."""
        pl = self.node("Player")
        tn = self.child(pl, "Trail")
        ps = self.ex.nodes[tn]["ps"]
        pos_u = np.asarray(pos_u)
        fr = np.asarray(frames, dtype=float)

        def world_fn(t):
            f = t * FPS
            i = np.clip(np.searchsorted(fr, f), 1, len(fr) - 1)
            a = (f - fr[i - 1]) / max(fr[i] - fr[i - 1], 1e-9)
            p = pos_u[i - 1] + (pos_u[i] - pos_u[i - 1]) * np.clip(a, 0, 1)
            M = np.eye(4)
            M[:3, 3] = p
            return M

        speed = np.r_[0.0, np.linalg.norm(np.diff(pos_u, axis=0), axis=1) * FPS]
        windows = []
        rates = []
        for f, v in zip(frames, speed):
            if shot["start"] - 30 <= f <= shot["end"]:
                windows.append((t_of(f), t_of(f + 1)))
                rates.append(3.0 + 7.0 * min(v, 15.0))
        self.fx_moving.append(("TRAIL_" + shot["id"], ps, list(zip(windows, rates)), world_fn, C.stable_seed(shot["id"], "trail"), None))
        # aura: one camera-facing glow parented to the rig
        an = self.child(pl, "Aura")
        aps = self.ex.nodes[an]["ps"]
        self.fx_moving.append(("AURA_" + shot["id"], aps, [((t_of(shot["start"] - 2), t_of(shot["end"] + 2)), None)], None,
                               C.stable_seed(shot["id"], "aura"), rig["root"]))

    # -------------------------------------------------------------- props moved by script
    def spawn(self, template_path, name, frame, frames, pos_u, rot_quats_b=None, until=None):
        """Instance a HitAndClone template as a new object tree, keyed along pos_u (Unity) for `frames`."""
        tpl = self.ex.templates[template_path]
        env = self.env
        objs = {}
        root = None
        for tn in tpl["nodes"]:
            mr = tn.get("mr")
            data = env.mesh(mr["mesh"]) if mr and mr.get("mesh", -1) >= 0 else None   # template meshes share the scene's mesh list
            ob = bpy.data.objects.new(f"{name}:{tn['name']}", data)
            env.coll.objects.link(ob)
            ob.rotation_mode = "QUATERNION"
            if tn["parent"] >= 0:
                ob.parent = objs[tn["parent"]]
                ob.location = C.u_pos(tn["lp"])
                ob.rotation_quaternion = C.u_quat(tn["lr"])
                ob.scale = C.u_scale(tn["ls"])
            else:
                root = ob
            if data is not None:
                for i, slot in enumerate(ob.material_slots):
                    slot.link = "OBJECT"
                    if i < len(mr["mats"]):
                        slot.material = M.material(env.ex, mr["mats"][i])
                ob.visible_shadow = mr.get("cast", "On") != "Off"
            if "light" in tn:
                L = tn["light"]
                ld = bpy.data.lights.new(f"{name}:L", "POINT")
                ld.energy = 4 * math.pi ** 2 * float(L["intensity"])
                ld.color = tuple(L["colorLin"][:3])
                ld.use_soft_falloff = False
                ld.use_shadow = False
                ld.use_custom_distance = True
                ld.cutoff_distance = float(L["range"]) * 1.2
                lo = bpy.data.objects.new(f"{name}:L", ld)
                env.coll.objects.link(lo)
                lo.parent = ob
            objs[tn["id"]] = ob
        C.set_vec_keys(root, "location", frames, [tuple(C.u_pos(p)) for p in pos_u])
        if rot_quats_b is not None:
            C.set_vec_keys(root, "rotation_quaternion", frames, [tuple(q) for q in self.continuous_quats(rot_quats_b)])
        last = until if until is not None else frames[-1]
        for ob in objs.values():
            for f, h in ((0, True), (frame, False), (last + 1, True)):
                ob.hide_render = h
                ob.keyframe_insert("hide_render", frame=f)
            for k in C.fcurve(ob, "hide_render").keyframe_points:
                k.interpolation = "CONSTANT"
            for ch in ob.children:
                if ch.type == "LIGHT":
                    for f, h in ((0, True), (frame, False), (last + 1, True)):
                        ch.hide_render = h
                        ch.keyframe_insert("hide_render", frame=f)
        # particles of the template (boulder fire) follow the root
        for tn in tpl["nodes"]:
            if "ps" in tn:
                fr = np.asarray(frames, dtype=float)
                pu = np.asarray(pos_u)

                def world_fn(t, fr=fr, pu=pu):
                    f = t * FPS
                    i = np.clip(np.searchsorted(fr, f), 1, len(fr) - 1)
                    a = np.clip((f - fr[i - 1]) / max(fr[i] - fr[i - 1], 1e-9), 0, 1)
                    M = np.eye(4)
                    M[:3, 3] = pu[i - 1] + (pu[i] - pu[i - 1]) * a
                    return M
                self.fx_moving.append((f"{name}:{tn['name']}", tn["ps"], [((t_of(frame), t_of(last)), None)], world_fn,
                                       C.stable_seed(name, tn["name"]), None))
        return root

    def roll_quats(self, pos_u, radius):
        q = Quaternion()
        out = [q.copy()]
        pb = [C.u_pos(p) for p in pos_u]
        for i in range(1, len(pb)):
            d = pb[i] - pb[i - 1]
            d.z = 0
            if d.length > 1e-6:
                q = Quaternion(Vector((0, 0, 1)).cross(d).normalized(), d.length / radius) @ q
            out.append(q.copy())
        return out

    # -------------------------------------------------------------- cameras
    def camera(self, shot, keys, fov=40.0, target="ball", ease_kind="inout", fstop=None, focus="target",
               shake=0.0, roll=0.0, handheld=0.0, fov_end=None):
        """keys: [(u in 0..1, cam pos (Unity), target (Unity) or None -> shot target)]. target: 'ball' or a Unity point."""
        start, end = shot["start"], shot["end"]
        frames = list(range(start - 1, end + 2))
        cd = bpy.data.cameras.new("CAM_" + shot["id"])
        cd.sensor_fit = "VERTICAL"
        cd.sensor_height = 24.0
        cd.clip_start = 0.1
        cd.clip_end = 900.0
        cam = bpy.data.objects.new("CAM_" + shot["id"], cd)
        self.env.coll.objects.link(cam)
        cam.rotation_mode = "QUATERNION"
        us = [k[0] for k in keys]
        P = [np.array(k[1], dtype=float) for k in keys]
        T = [None if (len(k) < 3 or k[2] is None) else np.array(k[2], dtype=float) for k in keys]
        locs, rots, lens, focus_d = [], [], [], []
        rr = C.rng(C.stable_seed(shot["id"], "cam"))
        ph = rr.random(6) * 100
        shakes = [s for s in self.shakes if start <= s[0] <= end]
        for f in frames:
            k = min(max((f - start) / max(end - start, 1), 0.0), 1.0)
            ke = ease(k, ease_kind)
            # position spline over the key times
            if len(keys) == 1:
                p = P[0]
                tg = T[0]
            else:
                j = min(max(np.searchsorted(us, ke) - 1, 0), len(us) - 2)
                a = (ke - us[j]) / max(us[j + 1] - us[j], 1e-9)
                p = catmull(P, (j + a) / (len(P) - 1))
                if T[j] is not None and T[j + 1] is not None:
                    tg = catmull([t if t is not None else P[0] for t in T], (j + a) / (len(T) - 1))
                else:
                    tg = None
            if tg is None:
                if callable(target):
                    tg = np.asarray(target(f), dtype=float)
                elif isinstance(target, str) and target == "ball":
                    tg = self.ball_pos.get(f, self.ball_pos.get(min(self.ball_pos, key=lambda x: abs(x - f)) if self.ball_pos else f, np.zeros(3)))
                    tg = np.asarray(tg) + np.array([0.0, 0.2, 0.0])
                else:
                    tg = np.array(target, dtype=float)
            pos = np.array(p, dtype=float)
            # handheld drift and shakes (Unity units)
            if handheld:
                tt = t_of(f)
                pos = pos + handheld * np.array([math.sin(tt * 0.9 + ph[0]) + 0.5 * math.sin(tt * 2.3 + ph[1]),
                                                  math.sin(tt * 0.7 + ph[2]) + 0.5 * math.sin(tt * 1.9 + ph[3]),
                                                  0.5 * math.sin(tt * 1.1 + ph[4])])
            for (sf, amp, dur) in shakes:
                if sf <= f <= sf + dur * FPS:
                    kk = 1 - (f - sf) / (dur * FPS)
                    pos = pos + (rr.random(3) * 2 - 1) * amp * kk * kk * 0.6
            pb = C.u_pos(pos)
            tb = C.u_pos(tg)
            locs.append(tuple(pb))
            rots.append(look_quat(pb, tb, math.radians(roll)))
            fv = fov if fov_end is None else fov + (fov_end - fov) * ke
            lens.append(C.lens_from_vfov(fv))
            focus_d.append((Vector(pb) - Vector(tb)).length)
            self.cam_pos[f] = tuple(pb)
        rots = self.continuous_quats(rots)
        C.set_vec_keys(cam, "location", frames, locs)
        C.set_vec_keys(cam, "rotation_quaternion", frames, [tuple(q) for q in rots])
        C.set_keys(cd, "lens", frames, lens)
        cd.dof.use_dof = fstop is not None
        if fstop is not None:
            cd.dof.aperture_fstop = fstop
            C.set_keys(cd, "dof.focus_distance", frames, focus_d)
        cam["shot"] = shot["id"]
        return cam

    # -------------------------------------------------------------- ambient scripts over the whole timeline
    def ambient(self):
        f0, f1 = self.frames
        frames = list(range(f0, f1 + 1, 2))
        for n in self.ex.nodes:
            ob = self.env.obj[n["id"]]
            for b in n.get("beh", []):
                t, f = b["type"], b["f"]
                if t == "Bob":
                    amp = np.array(f.get("amplitude", [0, 0.25, 0]), dtype=float)
                    period = float(f.get("period", 4.0)) or 4.0
                    spin = np.array(f.get("spinDegreesPerSecond", [0, 0, 0]), dtype=float)
                    phase = float(C.rng(C.stable_seed(self.env.name, n["id"], "bob")).random()) * period
                    lp = np.array(n["lp"], dtype=float)
                    if np.any(amp != 0):
                        vals = [tuple(C.u_pos(lp + amp * math.sin((t_of(fr) + phase) / period * 2 * math.pi))) for fr in frames]
                        self.key_vec(ob, "location", frames, vals)
                    if np.any(spin != 0):
                        q0 = C.u_quat(n["lr"])
                        axis_b = C.u_dir(spin / np.linalg.norm(spin))
                        rate = math.radians(np.linalg.norm(spin))
                        qs = self.continuous_quats([q0 @ Quaternion(axis_b, -rate * t_of(fr)) for fr in frames])
                        self.key_vec(ob, "rotation_quaternion", frames, [tuple(q) for q in qs])
                elif t == "KinematicMover" and f.get("mode") == "PingPong":
                    travel = np.array(f.get("travel", [0, 0, 4]), dtype=float)
                    speed = float(f.get("speed", 1.5))
                    pause = float(f.get("pause", 1.2))
                    L = np.linalg.norm(travel)
                    if L < 1e-4:
                        continue
                    leg = L / speed
                    cyc = 2 * (leg + pause)
                    lp = np.array(n["lp"], dtype=float)
                    vals = []
                    for fr in frames:
                        tt = t_of(fr) % cyc
                        if tt < leg:
                            k = tt / leg
                        elif tt < leg + pause:
                            k = 1.0
                        elif tt < 2 * leg + pause:
                            k = 1 - (tt - leg - pause) / leg
                        else:
                            k = 0.0
                        vals.append(tuple(C.u_pos(lp + travel * (k * k * (3 - 2 * k)))))
                    self.key_vec(ob, "location", frames, vals)
                elif t == "Flicker":
                    lo = self.env.light_obj.get(n["id"])
                    if lo is not None:
                        lo["flicker"] = (float(f.get("amount", 0.15)), float(f.get("speed", 6.0)))
                elif t == "LookAtTarget":
                    con = ob.constraints.new("DAMPED_TRACK")
                    con.target = self.ball_proxy()
                    con.track_axis = "TRACK_NEGATIVE_Y"
            a = n.get("anim")
            if a and a.get("auto") and a.get("clip", -1) >= 0:
                clip = self.ex.clips[a["clip"]]
                if clip.get("loop") or a.get("wrap") == "Loop":
                    self.play_clip(n["id"], clip["name"], f0, loop_until=f1)

    def ball_proxy(self):
        name = "BALL_PROXY_" + self.env.name
        ob = bpy.data.objects.get(name)
        if ob is None:
            ob = bpy.data.objects.new(name, None)
            self.env.coll.objects.link(ob)
        return ob

    # -------------------------------------------------------------- OnEnable behaviours
    def on_enable_effects(self):
        """Run OnEnable behaviours at every frame a node becomes effectively active (after all events)."""
        touched = set()
        for nid in list(self.events.keys()):
            stack = [nid]
            while stack:
                k = stack.pop()
                if k in touched:
                    continue
                touched.add(k)
                stack.extend(self.ex.children.get(k, []))
        for k in sorted(touched):
            for (f, v) in self.transitions(k)[1:]:
                if not v:
                    continue
                n = self.ex.nodes[k]
                for b in n.get("beh", []):
                    t, fd = b["type"], b["f"]
                    if t == "FadeInLight" and k in self.env.light_obj:
                        self.light_fade.append((self.env.light_obj[k], f, float(fd.get("duration", 0.9)), float(fd.get("flare", 1.8))))
                    elif t == "PopIn":
                        self.popin(k, f, fd)
                    elif t == "ExplodeOnEnable":
                        self.explode(k, f, fd)
                    elif t == "ShakeOnEnable":
                        self.shakes.append((f, float(fd.get("amplitude", 0.35)), float(fd.get("duration", 0.6))))
                        self.cue(f, (fd.get("sound") or {}).get("audio"), 1.0, "shake", self.pos_u(k))
                    elif t == "SkyBlend":
                        self.sky_blend(f, fd)
                    elif t == "VolumeBlend":
                        self.volume_blend(f, fd)
                for au in n.get("audio", []):
                    if au.get("playOnAwake") and not au.get("loop") and au.get("clip"):
                        self.cue(f, au["clip"], float(au.get("volume", 1.0)), n["name"], self.pos_u(k))

    def popin(self, nid, frame, fd):
        n = self.ex.nodes[nid]
        ob = self.env.obj[nid]
        target = np.array(n["ls"], dtype=float)
        start = target * np.array(fd.get("fromScale", [0.02, 0.02, 0.02]), dtype=float)
        delay = float(fd.get("delay", 0.0))
        dur = max(float(fd.get("duration", 0.5)), 1.0 / FPS)
        over = fd.get("overshoot", True)
        frames, vals = [], []
        n_fr = int(math.ceil((delay + dur) * FPS)) + 1
        for i in range(n_fr + 1):
            tt = i / FPS
            if tt < delay:
                s = start
            else:
                k = min((tt - delay) / dur, 1.0)
                e = ease_out_back(k) if over else 1 - (1 - k) ** 2
                s = start + (target - start) * e
            frames.append(frame + i)
            vals.append(tuple(C.u_scale(s)))
        self.key_vec(ob, "scale", frames, vals, hold_before=tuple(C.u_scale(start)))

    def explode(self, nid, frame, fd):
        """ExplodeOnEnable: impulses on every child rigidbody, then a light ballistic integration with a floor bounce."""
        force = float(fd.get("force", 6.0))
        upward = float(fd.get("upward", 0.6))
        bias = np.array(fd.get("bias", [0, 0, 1]), dtype=float)
        bias = bias / (np.linalg.norm(bias) + 1e-9)
        center = self.pos_u(nid)
        floor = center[1]
        r = C.rng(C.stable_seed(self.env.name, nid, "explode"))
        bodies = []
        stack = list(self.ex.children.get(nid, []))
        while stack:
            k = stack.pop()
            stack.extend(self.ex.children.get(k, []))
            if self.ex.nodes[k].get("rb"):
                bodies.append(k)
        # the root sits at the wall; ExplodeOnEnable uses (rb.position - transform.position)
        dur = 3.5
        nfr = int(dur * FPS)
        for k in bodies:
            p = self.pos_u(k)
            d = p - center
            d = d / (np.linalg.norm(d) + 1e-9) + bias * 0.8 + UP_U * upward
            v = d / (np.linalg.norm(d) + 1e-9) * force * r.uniform(0.7, 1.3)
            w = (r.random(3) * 2 - 1) * force
            half = 0.35
            pos, quats = [], []
            q = Quaternion()
            dt = 1.0 / (FPS * 4)
            for i in range(nfr + 1):
                pos.append(p.copy())
                quats.append(q.copy())
                for _ in range(4):
                    v[1] -= G * dt
                    p = p + v * dt
                    if p[1] < floor + half:
                        p[1] = floor + half
                        if v[1] < 0:
                            v[1] = -v[1] * 0.25
                        v[0] *= 0.8
                        v[2] *= 0.8
                        w *= 0.8
                    ang = np.linalg.norm(w) * dt
                    if ang > 1e-6:
                        q = Quaternion(C.u_dir(w / np.linalg.norm(w)), ang) @ q
            # world -> parent local (the chunks' parent is static)
            ob = self.env.obj[k]
            self.sc.view_layers[0].update()
            parent = ob.parent
            pinv = parent.matrix_world.inverted() if parent else Matrix()
            frames = [frame + i for i in range(nfr + 1)]
            q0 = C.u_quat(self.ex.nodes[k]["lr"])
            locs = [tuple(pinv @ C.u_pos(x)) for x in pos]
            rots = self.continuous_quats([qq @ q0 for qq in quats])
            self.key_vec(ob, "location", frames, locs, hold_before=locs[0])
            self.key_vec(ob, "rotation_quaternion", frames, [tuple(x) for x in rots], hold_before=tuple(q0))

    def sky_blend(self, frame, fd):
        """SkyBlend: sky _Blend 0->1, sun disc direction, fog colour/density, ambient, sun light (smoothstep)."""
        dur = float(fd.get("duration", 6.0))
        w = self.sc.world.node_tree
        nfr = int(dur * FPS)
        frames = [frame + i for i in range(nfr + 1)]
        ks = [ease(i / nfr, "inout") for i in range(nfr + 1)]
        r = self.ex.render
        fog0 = np.array(r["fogColorLin"][:3])
        fog1 = np.array(fd["fogTo"]["lin"][:3])           # RenderSettings.fogColor is converted gamma -> linear
        d0, d1 = float(r["fogDensity"]), float(fd.get("fogDensityTo", 0.004))
        amb0 = np.array(r.get("ambientGame") or r["ambient"])[:3]
        amb1 = np.array(fd["ambientTo"]["raw"][:3])
        self.key_scalar(w.name, 'nodes["sky_blend"].outputs[0].default_value', frame - 1, 0.0, id_kind="WORLD")
        for f, k in zip(frames, ks):
            self.key_scalar(w.name, 'nodes["sky_blend"].outputs[0].default_value', f, k, id_kind="WORLD")
            for i in range(3):
                self.keys.setdefault(("WORLD:" + w.name, 'nodes["ambient"].outputs[0].default_value', i), {})[f] = amb0[i] + (amb1[i] - amb0[i]) * k
                self.keys.setdefault(("SCENE:" + self.sc.name, '["fog_color"]', i), {})[f] = fog0[i] + (fog1[i] - fog0[i]) * k
            self.keys.setdefault(("SCENE:" + self.sc.name, '["fog_density"]', 0), {})[f] = d0 + (d1 - d0) * k
        # sun disc direction (Unity space) -> sky node
        sky_m = self.ex.materials[r["skybox"]]
        sd0 = np.array(sky_m["props"]["_SunDirection"]["v"][:3], dtype=float)
        sd1 = np.array(fd.get("sunDirectionTo", [0.25, 0.4, 1.0]), dtype=float)
        for f, k in zip(frames, ks):
            sd = sd0 + (sd1 - sd0) * k
            b = (-sd[0], -sd[2], sd[1])
            for i in range(3):
                self.keys.setdefault(("WORLD:" + w.name, f'nodes["sun_dir"].inputs[{i}].default_value', 0), {})[f] = b[i]
        sun_id = C.ref_node(fd.get("sun"))
        if sun_id is not None and sun_id in self.env.light_obj:
            lo = self.env.light_obj[sun_id]
            L = self.ex.nodes[sun_id]["light"]
            e0 = lo.data.energy
            e1 = math.pi * float(fd.get("sunIntensityTo", 2.2))
            c0 = np.array(L["colorLin"][:3])
            c1 = np.array(fd["sunColorTo"]["lin"][:3])
            for f, k in zip(frames, ks):
                self.keys.setdefault(("LIGHT:" + lo.data.name, "energy", 0), {})[f] = e0 + (e1 - e0) * k
                for i in range(3):
                    self.keys.setdefault(("LIGHT:" + lo.data.name, "color", i), {})[f] = c0[i] + (c1[i] - c0[i]) * k
        self.log.append((frame, "SkyBlend"))

    def volume_blend(self, frame, fd):
        """VolumeBlend: compositor parameters from the active profile to the fade-in profile (smoothstep)."""
        import tr_post
        env = self.env
        if not getattr(env, "post_b", None):
            return
        ng = self.sc.compositing_node_group
        a = tr_post.param_values(env.post)
        b = tr_post.param_values(env.post_b)
        dur = float(fd.get("duration", 5.0))
        nfr = int(dur * FPS)
        for i in range(nfr + 1):
            k = ease(i / nfr, "inout")
            for name in a:
                if name in ng.nodes:
                    self.keys.setdefault(("NODETREE:" + ng.name, f'nodes["{name}"].outputs[0].default_value', 0), {})[frame + i] = a[name] + (b[name] - a[name]) * k
        for name in a:
            if name in ng.nodes:
                self.keys.setdefault(("NODETREE:" + ng.name, f'nodes["{name}"].outputs[0].default_value', 0), {})[frame - 1] = a[name]
        self.log.append((frame, "VolumeBlend"))

    # -------------------------------------------------------------- finalize: visibility, lights, particles, buffered keys
    def finalize(self):
        """Call after on_enable_effects() and the cameras."""
        env = self.env
        # visibility keys (also when only the frame-0 state differs from the exported activeSelf)
        static = self.ex.effective_active()
        for nid in range(len(self.ex.nodes)):
            tr = self.transitions(nid)
            if len(tr) <= 1 and tr[0][1] == static[nid]:
                continue
            hide_all = self.ex.nodes[nid]["path"].split("/")[0] in ("Player", "CameraRig")
            for ob in [env.obj[nid]] + env.extras(nid):
                for f, v in tr:
                    ob.hide_render = (not v) or hide_all
                    ob.keyframe_insert("hide_render", frame=max(f, 0))
                for k in C.fcurve(ob, "hide_render").keyframe_points:
                    k.interpolation = "CONSTANT"
        # lights: flicker noise + fade-ins
        fades = {}
        for lo, f, dur, flare in self.light_fade:
            fades.setdefault(lo.name, []).append((f, dur, flare))
        for nid, lo in env.light_obj.items():
            base = env.base_energy.get(lo.name, lo.data.energy)
            keys = {0: base}
            for (f, dur, flare) in sorted(fades.get(lo.name, [])):
                nfr = max(1, int(dur * FPS))
                keys[f - 1] = base
                for i in range(nfr + 1):
                    k = i / nfr
                    keys[f + i] = base * (k * k * (3 - 2 * k)) * (1 + (flare - 1) * math.sin(k * math.pi))
            ext = ("LIGHT:" + lo.data.name, "energy", 0)
            if ext in self.keys:
                for f, v in self.keys.pop(ext).items():
                    keys[f] = v
            frames = sorted(keys)
            C.set_keys(lo.data, "energy", frames, [keys[f] for f in frames])
            fl = lo.get("flicker")
            if fl:
                fc = C.fcurve(lo.data, "energy")
                mod = fc.modifiers.new("NOISE")
                mod.scale = max(1.0, FPS / max(fl[1], 0.1))
                mod.strength = 2.0 * fl[0] * base
                mod.phase = float(C.rng(C.stable_seed(lo.name)).random() * 50)
        # buffered keys (objects, world, scene props, compositor)
        for (owner, path, idx), kv in list(self.keys.items()) + [((o, p, i), kv) for (o, p, i), kv in self.const_keys.items()]:
            id_data = self.resolve(owner)
            if id_data is None:
                continue
            frames = sorted(kv)
            constant = (owner, path, idx) in self.const_keys and kv is self.const_keys[(owner, path, idx)]
            C.set_keys(id_data, path, frames, [kv[f] for f in frames], index=idx, interp="CONSTANT" if constant else "LINEAR", replace=False)
        # proxies for look-at constraints and camera-facing sprites
        bp = self.ball_proxy()
        if self.ball_pos:
            fr = sorted(self.ball_pos)
            C.set_vec_keys(bp, "location", fr, [tuple(C.u_pos(self.ball_pos[f])) for f in fr])
        cp = env.camera_proxy()
        if self.cam_pos:
            fr = sorted(self.cam_pos)
            C.set_vec_keys(cp, "location", fr, [self.cam_pos[f] for f in fr])
        self.particles()

    def resolve(self, owner):
        if ":" in owner and owner.split(":", 1)[0] in ("WORLD", "SCENE", "LIGHT", "NODETREE"):
            kind, name = owner.split(":", 1)
            if kind == "WORLD":                       # node values live on the world's embedded node tree
                w = bpy.data.worlds.get(name)
                return w.node_tree if w else None
            return {"SCENE": bpy.data.scenes, "LIGHT": bpy.data.lights, "NODETREE": bpy.data.node_groups}[kind].get(name)
        return bpy.data.objects.get(owner)

    def particles(self):
        """Emitter windows from the activity timeline (static emitters) plus the moving / cloned ones."""
        f0, f1 = self.frames
        t0, t1 = t_of(f0), t_of(f1)
        active = {}
        for nid in self.env.ps_nodes:
            tr = self.transitions(nid)
            win = []
            on = None
            for f, v in tr:
                if v and on is None:
                    on = -100.0 if f == 0 else t_of(f)
                elif not v and on is not None:
                    win.append((on, t_of(f)))
                    on = None
            if on is not None:
                win.append((on, None))
            ps = self.ex.nodes[nid]["ps"]
            if not ps["main"].get("playOnAwake", True):
                win = []
            active[nid] = win
        tr_fx.build_static(self.env, t0, t1, active=active)
        for key, ps, windows, world_fn, seed, parent in self.fx_moving:
            tr_fx.build_moving(self.env, key, ps, windows, world_fn, seed, parent, t0, t1)

    # -------------------------------------------------------------- reports
    def write_reports(self):
        import json
        import os
        d = C.ensure_dir(C.REPORTS)
        with open(os.path.join(d, f"events_{self.env.name}.txt"), "w", encoding="utf-8") as fh:
            for f, txt in sorted(self.log):
                fh.write(f"{f:6.0f}  {txt}\n")
        with open(os.path.join(d, f"sfx_{self.env.name}.json"), "w", encoding="utf-8") as fh:
            json.dump(sorted(self.sfx, key=lambda x: x["frame"]), fh, indent=1)
