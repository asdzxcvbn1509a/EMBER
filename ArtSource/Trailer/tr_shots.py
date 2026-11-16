"""EMBER trailer - shot choreography (positions in Unity coordinates, as in BuildStage*.cs).

Each shot has an `events` part (ember path, props, triggers) and a `camera` part. All events of an environment
run first, then the OnEnable effects resolve (shakes, fades), then the cameras are keyed.
"""
import math

import bpy
import numpy as np
from mathutils import Quaternion, Vector

import shots as S
import tr_anim as A
import tr_common as C

FPS = S.FPS


def sh(id_):
    return S.SHOT[id_]


def frame_at(shot, seconds):
    return int(round(shot["start"] + seconds * FPS))


# ------------------------------------------------------------------ helpers shared by shots
def flash(d, pos_u, frame, color=(1.0, 0.85, 0.6), energy=600.0, dur=0.5, radius=0.3):
    """Short point-light flash (teleports, landings)."""
    ld = bpy.data.lights.new(f"FLASH_{frame}", "POINT")
    ld.color = color
    ld.use_soft_falloff = False
    ld.shadow_soft_size = radius
    ld.use_shadow = False
    ld.use_custom_distance = True
    ld.cutoff_distance = 25.0
    lo = bpy.data.objects.new(ld.name, ld)
    d.env.coll.objects.link(lo)
    lo.location = C.u_pos(pos_u)
    n = int(dur * FPS)
    frames = [frame - 1] + [frame + i for i in range(n + 1)]
    vals = [0.0] + [energy * (1 - i / n) ** 2 for i in range(n + 1)]
    C.set_keys(ld, "energy", frames, vals)
    return lo


def exposure_offset(d, shot, ev):
    """Per-shot exposure trim (EV) on the environment compositor, constant over the shot."""
    ng = d.sc.compositing_node_group
    if ng is None or "p_exposure" not in ng.nodes:
        return
    base = ng.nodes["p_exposure"].outputs[0].default_value
    key = ("NODETREE:" + ng.name, 'nodes["p_exposure"].outputs[0].default_value', 0)
    buf = d.const_keys.setdefault(key, {})
    buf.setdefault(0, base)
    buf[shot["start"]] = base + ev
    buf[shot["end"] + 1] = base


def set_transform(d, nid, frame, pos_world_u):
    """HitAndSetTransform on a target (World space): the drawbridge is laid down instantly."""
    n = d.ex.nodes[nid]
    ob = d.env.obj[nid]
    parent = n["parent"]
    Pw = d.world_u(parent) if parent >= 0 else np.eye(4)
    local = np.linalg.inv(Pw) @ np.r_[np.asarray(pos_world_u, dtype=float), 1.0]
    d.key_vec(ob, "location", [frame], [tuple(C.u_pos(local[:3]))], hold_before=tuple(C.u_pos(n["lp"])), interp="CONSTANT")
    q = C.u_quat([0, 0, 0, 1])
    d.key_vec(ob, "rotation_quaternion", [frame], [tuple(q)], hold_before=tuple(C.u_quat(n["lr"])), interp="CONSTANT")


def chute_motion(p0, p1, radius, accel, start_frame, extra=None):
    """A ball released at the top of a straight chute (Unity points on its surface): constant acceleration.
    Returns frames, positions (ball centres) and the arrival frame."""
    p0 = np.asarray(p0, dtype=float)
    p1 = np.asarray(p1, dtype=float)
    L = np.linalg.norm(p1 - p0)
    dirv = (p1 - p0) / L
    up = np.array([0.0, 1.0, 0.0]) - dirv * dirv[1]
    up /= np.linalg.norm(up)
    t_end = math.sqrt(2 * L / accel)
    n = int(math.ceil(t_end * FPS))
    frames, pos = [], []
    for i in range(n + 1):
        t = min(i / FPS, t_end)
        s = 0.5 * accel * t * t
        frames.append(start_frame + i)
        pos.append(p0 + dirv * s + up * radius)
    return frames, pos, start_frame + n, accel * t_end


# ------------------------------------------------------------------ Title: S01, S18
def title_pre(d):
    braziers = [n["id"] for n in d.ex.nodes if n["path"].endswith("Puzzles/Brazier/Lit")]
    for b in braziers:
        d.set_active(b, 0, False)
        d.set_active(b, S.HITS["landing"] + 6, True)        # the braziers wake when the spark lands
    logo = d.node("Decor/EMBER_Logo")
    d.set_active(logo, 0, False)
    d.set_active(logo, sh("S18")["start"] - 2, True)
    start = d.node("Decor/Engraving START")                  # game UI, not story
    d.set_active(start, 0, False)
    # the logo's own lights come up with the logo
    for n in d.ex.nodes:
        if "light" in n and n["path"].startswith("LEVEL/Lights/"):
            p = np.array(n["wm"]).reshape(4, 4)[:3, 3]
            if p[2] > 5.5 and p[1] > 4.0:                    # point (0,5.2,10.2) and spot (0,9,6) at the logo
                d.set_active(n["id"], 0, False)
                d.set_active(n["id"], sh("S18")["start"] + 12, True)
                d.light_fade.append((d.env.light_obj[n["id"]], sh("S18")["start"] + 12, 2.0, 1.3))


def S01_events(d):
    s = sh("S01")
    land = S.HITS["landing"]
    rest = np.array([0.0, d_floor(d, 0.0, 1.5) + A.R_BALL, 1.5])
    t_land = (land - s["start"]) / FPS
    d.s01 = d.solve_ball(s, [("at", (0.0, 17.0, 1.5)), ("path", t_land, [(0.0, 9.0, 1.45), tuple(rest)], "in"), ("wait", 4.0)],
                         triggers=False, pre=2)
    flash(d, rest + [0, 0.3, 0], land, color=(1.0, 0.6, 0.25), energy=900.0, dur=0.8)
    d.shakes.append((land, 0.12, 0.5))
    d.cue(land, "sfx_impact", 0.9, "landing")
    d.cue(land, "sfx_ignite", 0.8, "landing")


def S01_camera(d):
    s = sh("S01")
    d.camera(s, [(0.0, (0.0, 1.7, -5.8)), (1.0, (0.0, 2.1, -4.6))], fov=46, target="ball", ease_kind="inout", fstop=3.2)


def S18_events(d):
    s = sh("S18")
    rest = (0.0, d_floor(d, 0.0, 1.5) + A.R_BALL, 1.5)
    d.solve_ball(s, [("at", rest), ("wait", 11.0)], triggers=False, pre=4)


def S18_camera(d):
    s = sh("S18")
    d.camera(s, [(0.0, (1.8, 2.0, -2.6), (0.0, 1.4, 1.5)),
                 (0.55, (0.0, 4.0, -8.0), (0.0, 3.2, 7.0)),
                 (1.0, (0.0, 4.6, -9.6), (0.0, 3.7, 7.0))], fov=42, ease_kind="inout", fstop=5.6)   # rune rings drop out of frame


def d_floor(d, x, z, y_hint=1.0, frame=None):
    bvh = d.collision_bvh(frame or d.frames[0] + 1)
    y = d.ground_y(bvh, x, z, y_hint + 2.5)
    return y if y is not None else 0.0


# ------------------------------------------------------------------ Stage 1: S02 - S06
def stage1_pre(d):
    # keyboard prompts ("ROLL / W A S D OR ARROWS", "JUMP / SPACE") are game UI, not story; the in-world hints stay
    for name in ("Decor/Engraving ROLL", "Decor/Engraving JUMP"):
        d.set_active(d.node(name), 0, False)
    # Hall of Wardens: the first two plates were pressed before S05
    pre = sh("S05")["start"] - 26
    d.fire(d.node("Puzzles/Plate_1C_1"), pre)
    d.fire(d.node("Puzzles/Plate_1C_2"), pre + 4)


def S02_events(d):
    s = sh("S02")
    d.solve_ball(s, [("at", (0.0, 8.0)), ("roll", 3.6, [(0.05, 13.0), (0.0, 19.6)], "linear")], t_start=-0.5)


def S02_camera(d):
    s = sh("S02")
    d.camera(s, [(0.0, (1.3, 1.05, 13.4)), (1.0, (1.1, 1.15, 23.6))], fov=44, target="ball", ease_kind="linear", fstop=2.8,
             handheld=0.02)


def S03_events(d):
    s = sh("S03")
    d.solve_ball(s, [("at", (0.0, 20.6)), ("roll", 1.55, [(0.0, 25.0)], "out"), ("wait", 1.75),
                     ("roll", 1.9, [(0.0, 28.4), (0.0, 32.0)], "in")], t_start=-0.35, bvh_frame=s["end"])


def S03_camera(d):
    s = sh("S03")
    # from room 1B over the chasm, looking back: planks rise out of the mist towards the camera
    d.camera(s, [(0.0, (2.3, 4.1, 38.6), (0.0, 0.0, 26.2)), (1.0, (1.5, 3.3, 37.0), (0.0, 0.2, 29.4))],
             fov=44, ease_kind="inout", fstop=5.0, handheld=0.015)


def S04_events(d):
    s = sh("S04")
    d.solve_ball(s, [("at", (0.6, 38.6)), ("roll", 1.25, [(-1.6, 41.4), (-3.4, 42.9)], "linear"),
                     ("jump", 0.62, (-6.25, 44.0), 1.5), ("wait", 2.0)], t_start=-0.25)


def S04_camera(d):
    s = sh("S04")
    d.camera(s, [(0.0, (3.6, 2.5, 36.6), (-4.0, 1.4, 46.4)), (1.0, (2.0, 3.3, 38.6), (-2.6, 2.0, 49.5))],
             fov=46, ease_kind="inout", fstop=4.5, handheld=0.015)


def S05_events(d):
    s = sh("S05")
    d.solve_ball(s, [("at", (0.0, 86.5)), ("roll", 2.05, [(-4.0, 86.5), (-8.5, 86.5)], "inout"), ("wait", 3.0)], t_start=-0.25)


def S05_camera(d):
    s = sh("S05")
    d.camera(s, [(0.0, (2.6, 1.4, 82.4), (-5.0, 0.6, 87.0)), (0.45, (4.4, 4.0, 80.0), (-4.5, 0.8, 88.0)),
                 (1.0, (6.2, 9.8, 75.4), (-2.6, 0.5, 90.8))], fov=48, ease_kind="inout", fstop=5.6)


def S06_events(d):
    s = sh("S06")
    d.solve_ball(s, [("at", (0.0, 106.4)), ("roll", 2.9, [(0.0, 111.0), (0.0, 114.4)], "out"), ("wait", 2.0)], t_start=-0.4,
                 skip_triggers=())


def S06_camera(d):
    s = sh("S06")
    # orbit round the ember as it climbs onto the dais; portal and moonbeam behind it
    d.camera(s, [(0.0, (-7.8, 3.0, 103.6)), (0.5, (-8.2, 3.8, 108.4)), (1.0, (-7.0, 4.6, 112.6))],
             fov=45, target="ball", ease_kind="linear", fstop=5.6)


# ------------------------------------------------------------------ Stage 2: S07 - S11
RAFT_SPAWN = np.array([-16.5, 0.0, 12.0])
RAFT_V = 1.5


def stage2_pre(d):
    # S07 opens under the act-II card, and the "WAIT FOR THE RAFT" floor hint would cross its subtitle
    hint = d.node("Decor/Engraving WAIT FOR THE RAFT")
    d.set_active(hint, 0, False)
    d.set_active(hint, sh("S07")["end"] + 1, True)
    # rafts: Plate_2A was pressed before the act (HitAndClone every 5 s); each drifts +X until the sink at x = 17
    fire = 560
    d.fire(d.node("Puzzles/Plate_2A"), fire)
    d.rafts = []
    k = 0
    while fire + k * 120 < S.ACTS["forge"][1]:
        f0 = fire + k * 120
        life = int((17.0 - RAFT_SPAWN[0]) / RAFT_V * FPS)
        frames = list(range(f0, f0 + life + 1))
        pos = [RAFT_SPAWN + np.array([RAFT_V * (f - f0) / FPS, 0, 0]) for f in frames]
        d.spawn("Assets/_Ember/Prefabs/LavaRaft.prefab", f"Raft{k}", f0, frames, pos)
        d.rafts.append((f0, frames, pos))
        k += 1
    # boulders: the spawner fires when the ember enters the gallery (every 2.6 s from both lanes)
    d.fire(d.node("Puzzles/Boulder_Spawner"), 880)
    boulders(d, 880, 1180)
    # counterweight: Plate_2D was pressed just before S11, the weight lands on the scale on the "lamps" hit
    counterweight(d, S.HITS["lamps"])


def raft_pos(d, f, k):
    """Position of raft k (Unity) at frame f, clamped to its life."""
    f0, frames, pos = d.rafts[k]
    i = min(max(int(round(f)) - f0, 0), len(pos) - 1)
    return pos[i]


def track_fn(frames, pos, ahead=0.0, offset=(0.0, 0.0, 0.0)):
    """Camera target that follows a keyed prop (optionally looking `ahead` frames forward)."""
    fr = np.asarray(frames, dtype=float)
    pu = np.asarray(pos, dtype=float)
    off = np.asarray(offset, dtype=float)

    def fn(f):
        f = f + ahead
        i = int(np.clip(np.searchsorted(fr, f), 1, len(fr) - 1))
        a = float(np.clip((f - fr[i - 1]) / max(fr[i] - fr[i - 1], 1e-9), 0, 1))
        return pu[i - 1] + (pu[i] - pu[i - 1]) * a + off
    return fn


def boulders(d, f_fire, f_stop):
    """Boulder clones roll down the two gallery lanes (ramp 0 -> 4.5 m over z 60.5 -> 88.5) into the trench."""
    period = 2.6 * FPS
    r = 0.85
    k = 0
    f0 = float(f_fire)
    slope = math.atan2(4.5, 28.0)
    acc = 5.0 / 7.0 * A.G * math.sin(slope) * 1.6          # a bit livelier than the physics for the trailer
    while f0 < f_stop:
        for x in (2.6, 7.4):
            frames, pos = [], []
            z, v = 87.4, 0.0
            y_top = 4.5 * (z - 60.5) / 28.0 + r
            y = 7.2
            vy = 0.0
            f = int(round(f0))
            landed = False
            while True:
                frames.append(f)
                if not landed:
                    vy -= A.G / FPS
                    y += vy / FPS
                    if y <= y_top:
                        y = y_top
                        landed = True
                else:
                    v += acc / FPS
                    z -= v / FPS
                    if z > 60.5:
                        y = 4.5 * (z - 60.5) / 28.0 + r
                    else:
                        y -= 0.5 * v / FPS * 3                  # drops into the lava trench
                pos.append(np.array([x, y, z]))
                f += 1
                if z < 58.5 or f - frames[0] > 15 * FPS:
                    break
            quats = d.roll_quats(pos, r)
            d.spawn("Assets/_Ember/Prefabs/Boulder.prefab", f"Boulder{k}_{x}", frames[0], frames, pos, rot_quats_b=quats)
            d.cue(frames[-1] - 6, "sfx_impact", 0.35, "boulder", pos[-1])
        k += 1
        f0 += period


def counterweight(d, land_frame):
    """Plate_2D -> Counterweight clone -> down the weight chute -> drops onto the scale (fires Plate_Scale)."""
    w0 = np.array([-7.0, 5.9, 105.2])
    w1 = np.array([-7.0, 4.75, 117.6])
    r = 0.7
    acc = 4.6
    # roll time on the chute, then a 0.45 s drop into the basin
    L = np.linalg.norm(w1 - w0)
    t_roll = math.sqrt(2 * L / acc)
    drop = int(0.45 * FPS)
    start = land_frame - int(round(t_roll * FPS)) - drop
    d.fire(d.node("Puzzles/Plate_2D"), start - 8)           # the ember pressed the walkway plate (TriggerSound clone)
    frames, pos, f_end, v_end = chute_motion(w0, w1, r, acc, start)
    p_end = pos[-1]
    basin = np.array([-7.0, 3.5 + r, 120.5])
    for i in range(1, drop + 1):
        k = i / drop
        p = p_end + (basin - p_end) * k
        p[1] = p_end[1] + (basin[1] - p_end[1]) * k * k
        frames.append(f_end + i)
        pos.append(p)
    hold = list(range(frames[-1] + 1, S.ACTS["forge"][1] + 2))
    frames += hold
    pos += [pos[-1]] * len(hold)
    d.spawn("Assets/_Ember/Prefabs/Counterweight.prefab", "Counterweight", start, frames, pos, rot_quats_b=d.roll_quats(pos, r))
    land = f_end + drop
    d.fire(d.node("Puzzles/Plate_Scale"), land, who="Counterweight(Clone)")
    d.cue(land, "sfx_impact", 0.9, "counterweight", basin)
    d.cw_land = land


def S07_events(d):
    s = sh("S07")
    d.solve_ball(s, [("at", (-6.2, 8.9)), ("wait", 4.0)], triggers=False)


def S07_camera(d):
    s = sh("S07")
    d.camera(s, [(0.0, (-10.6, 4.6, 3.6), (4.0, -0.8, 13.4)), (1.0, (-8.8, 5.0, 4.6), (6.0, -0.6, 13.6))],
             fov=50, ease_kind="linear", fstop=7.0)


def S08_events(d):
    s = sh("S08")
    lift = np.array([0.0, A.R_BALL + 0.02, 0.0])
    dur = (s["end"] - s["start"] + 20) / FPS
    # the raft released at frame 680 (index 1) is at x ~ -9 when the shot starts
    d.solve_ball(s, [("ride", dur, lambda f: raft_pos(d, f, 1) + lift)], t_start=-0.4, triggers=False)


def S08_camera(d):
    s = sh("S08")
    p0 = raft_pos(d, s["start"], 1)
    p1 = raft_pos(d, s["end"], 1)
    d.camera(s, [(0.0, (p0[0] - 2.2, 2.2, 9.3)), (1.0, (p1[0] + 1.2, 2.5, 9.5))], fov=46, target="ball", ease_kind="linear",
             fstop=3.5)


def S09_events(d):
    s = sh("S09")
    smash = S.HITS["smash"]
    # the ember presses the balcony plate: the iron ball is cloned, rolls down the brass chute and hits the wall
    c0 = np.array([5.0, 3.3, 34.0])
    c1 = np.array([5.0, 0.75, 51.3])
    r = 0.7
    acc = 3.4
    L = np.linalg.norm(c1 - c0)
    t_roll = math.sqrt(2 * L / acc)
    start = smash - int(round(t_roll * FPS))
    d.solve_ball(s, [("at", (-3.2, 4.0, 32.0)), ("roll", max(0.2, (start - 6 - s["start"]) / FPS + 0.3), [(0.0, 32.0)], "out"),
                     ("wait", 6.0)], t_start=-0.3)
    frames, pos, f_end, v_end = chute_motion(c0, c1, r, acc, start)
    drop = [frames[0] - 6 + i for i in range(6)]
    spawn_p = np.array([5.0, 4.4, 34.5])
    pre_pos = [spawn_p + (pos[0] - spawn_p) * (i / 6) ** 2 for i in range(6)]
    d.spawn("Assets/_Ember/Prefabs/IronBall.prefab", "IronBall", drop[0], drop + frames, pre_pos + pos,
            rot_quats_b=d.roll_quats(pre_pos + pos, r), until=f_end)
    wall = d.node("Puzzles/CrackedWall")
    d.fire(wall, f_end, who="IronBall(Clone)")
    d.smash = f_end
    # camera target: the ember on the plate, then the iron ball all the way to the wall
    tf = [s["start"] - 2] + drop + frames + [f_end + 40]
    tp = [np.array([0.0, 4.0, 32.0])] + pre_pos + pos + [np.array([5.0, 1.6, 51.6])]
    d.iron_track = track_fn(tf, tp, ahead=8)


def S09_camera(d):
    s = sh("S09")
    # between the chute (x 5) and the wall gear (x 11.6), clear of the brazier at (10.6, 0, 36)
    d.camera(s, [(0.0, (8.2, 5.8, 29.8)), (0.5, (8.6, 5.2, 37.0)), (1.0, (8.3, 3.6, 44.6))],
             fov=50, target=d.iron_track, ease_kind="inout", fstop=5.0)


def S10_events(d):
    s = sh("S10")
    d.solve_ball(s, [("at", (5.0, 64.0)), ("roll", 3.9, [(5.0, 68.0), (5.0, 72.5)], "linear")], t_start=-0.3, triggers=False)


def S10_camera(d):
    s = sh("S10")
    d.camera(s, [(0.0, (5.0, 1.1, 61.2), (5.0, 3.2, 76.0)), (1.0, (5.0, 1.7, 63.4), (5.0, 3.6, 78.0))],
             fov=50, ease_kind="linear", fstop=4.0, handheld=0.02)


def S11_events(d):
    s = sh("S11")
    d.solve_ball(s, [("at", (-10.0, 6.5, 107.4)), ("wait", 5.0)], triggers=False)
    exposure_offset(d, s, 0.5)          # the forge is dark until its six lamps light


def S11_camera(d):
    s = sh("S11")
    # basin and scale on the left, the forge gate ahead, lamps high on both walls
    d.camera(s, [(0.0, (4.6, 8.6, 108.4), (-3.4, 5.4, 124.0)), (1.0, (3.2, 9.2, 111.0), (-2.0, 6.4, 128.0))],
             fov=54, ease_kind="inout", fstop=7.0)


# ------------------------------------------------------------------ Stage 3: S12 - S17
def stage3_pre(d):
    pre = sh("S12")["start"] - 10
    d.set_active(d.node("Puzzles/CrystalSeal"), pre, False)           # the sun-stone broke the seal earlier
    for i in (2, 3):
        d.set_active(d.node(f"Puzzles/EchoTile_{i}"), pre, True)
    set_transform(d, d.node("Puzzles/Drawbridge"), pre, (-12.0, 3.75, 98.6))
    # the turning bridge swings into line during S14 (180 deg; faster than the game's 24 deg/s)
    s14 = sh("S14")
    bridge = d.node("Puzzles/RotatingBridge")
    ob = d.env.obj[bridge]
    q0 = C.u_quat(d.ex.nodes[bridge]["lr"])
    f_a, f_b = s14["start"] - 34, s14["start"] + 38
    frames = list(range(f_a, f_b + 1))
    qs = [q0 @ Quaternion((0, 0, 1), -math.pi * A.ease((f - f_a) / (f_b - f_a), "inout")) for f in frames]
    d.key_vec(ob, "rotation_quaternion", frames, [tuple(q) for q in d.continuous_quats(qs)], hold_before=tuple(q0))
    d.cue(f_a + 6, "sfx_bridge", 0.6, "turning bridge")


def S12_events(d):
    s = sh("S12")
    d.solve_ball(s, [("at", (0.0, -2.2)), ("roll", 4.2, [(0.2, 0.6), (0.3, 3.0)], "inout"), ("wait", 2.0)], t_start=-0.2)


def S12_camera(d):
    s = sh("S12")
    d.camera(s, [(0.0, (1.3, 0.95, -5.2), (0.0, 0.8, 2.0)), (0.5, (0.6, 5.0, -11.0), (1.0, 1.2, 12.0)),
                 (1.0, (-6.8, 10.4, -17.0), (2.0, 2.2, 30.0))], fov=48, fov_end=55, ease_kind="inout", fstop=7.0)


def S13_events(d):
    s = sh("S13")
    tp = frame_at(s, 0.95)
    d.solve_ball(s, [("at", (0.0, 2.6)), ("roll", 0.95, [(0.0, 4.35)], "in"), ("teleport", (-10.5, 2.7, 22.0)),
                     ("roll", 2.0, [(-9.9, 23.2), (-9.2, 24.4)], "out"), ("wait", 2.0)], t_start=-0.2, bvh_frame=s["end"])
    flash(d, (0.0, 1.2, 5.2), tp, color=(1.0, 0.82, 0.5), energy=1400.0, dur=0.55)
    flash(d, (-10.5, 2.9, 22.0), tp, color=(1.0, 0.82, 0.5), energy=1400.0, dur=0.7)


def S13_camera(d):
    s = sh("S13")
    # from the side: the gold ring on the left, the destination island on the right
    d.camera(s, [(0.0, (13.0, 7.4, 13.0), (-4.6, 1.8, 13.2)), (1.0, (12.2, 7.0, 14.6), (-5.4, 2.0, 14.4))],
             fov=42, ease_kind="inout", fstop=10.0)


def S14_events(d):
    s = sh("S14")
    d.solve_ball(s, [("at", (7.0, 4.5, 47.8)), ("wait", 1.7), ("roll", 2.3, [(7.0, 52.0), (7.0, 56.4)], "in")], bvh_frame=s["end"])


def S14_camera(d):
    s = sh("S14")
    # behind the ember on the path: the bridge swings into line ahead, planets all around
    d.camera(s, [(0.0, (10.2, 6.8, 41.6), (6.6, 3.6, 57.0)), (1.0, (9.2, 6.4, 45.6), (6.8, 3.8, 60.0))],
             fov=52, ease_kind="inout", fstop=7.0)


def S15a_events(d):
    s = sh("S15a")
    d.solve_ball(s, [("at", (-13.2, 4.5, 99.2)), ("roll", 0.7, [(-13.2, 102.6)], "out"), ("wait", 2.0)], t_start=0.0)


def S15a_camera(d):
    s = sh("S15a")
    # east of the return ring, so the gold beam is seen from the side (end-on it fades out, like the game's shader)
    d.camera(s, [(0.0, (-7.0, 5.4, 104.0), (-12.4, 6.8, 106.4)), (1.0, (-7.4, 5.8, 103.4), (-10.8, 9.4, 108.6))],
             fov=50, ease_kind="out", fstop=5.0)


def S15b_events(d):
    s = sh("S15b")
    d.solve_ball(s, [("at", (29.0, 4.5, 90.8)), ("roll", 0.7, [(29.0, 94.2)], "out"), ("wait", 2.0)], t_start=0.0)


def S15b_camera(d):
    s = sh("S15b")
    d.camera(s, [(0.0, (33.0, 5.8, 90.6), (28.8, 6.6, 97.2)), (1.0, (33.4, 6.2, 90.0), (27.0, 9.4, 99.6))],
             fov=50, ease_kind="out", fstop=5.0)


def S15c_events(d):
    s = sh("S15c")
    d.solve_ball(s, [("at", (-14.0, 10.5, 81.2)), ("roll", 0.7, [(-14.0, 84.6)], "out"), ("wait", 2.0)], t_start=0.0)


def S15c_camera(d):
    s = sh("S15c")
    d.camera(s, [(0.0, (-10.2, 12.4, 81.0), (-14.0, 11.0, 88.6)), (1.0, (-23.0, 25.0, 69.0), (6.0, 12.5, 112.0))],
             fov=50, fov_end=56, ease_kind="in", fstop=8.0)


def S16_events(d):
    s = sh("S16")
    d.solve_ball(s, [("at", (8.0, 4.5, 97.6)), ("roll", 2.45, [(8.0, 104.0), (8.0, 112.2)], "linear"),
                     ("roll", 0.9, [(8.0, 116.1)], "out"), ("wait", 2.0)], t_start=-0.25, bvh_frame=s["end"])


def S16_camera(d):
    s = sh("S16")
    d.camera(s, [(0.0, (9.6, 5.3, 93.8), (8.0, 9.2, 118.0)), (1.0, (9.2, 9.0, 106.6), (8.0, 12.4, 124.0))],
             fov=50, ease_kind="inout", fstop=6.0)


def S17_events(d):
    s = sh("S17")
    d.solve_ball(s, [("at", (8.0, 7.5, 116.1)), ("roll", 0.7, [(8.0, 118.6)], "out"), ("wait", 12.0)], bvh_frame=s["start"] + 2)
    ign = next((f for f, txt in d.log if "Ignition" in txt), S.HITS["ignite"])
    # the beacon catches: a hot flash at the bowl and a short shake on top of the game's own fade-ins
    flash(d, (8.0, 19.2, 124.0), ign + 4, color=(1.0, 0.7, 0.35), energy=60000.0, dur=1.6, radius=1.0)
    d.shakes.append((ign + 4, 0.18, 0.9))


def S17_camera(d):
    s = sh("S17")
    d.camera(s, [(0.0, (11.6, 8.0, 112.8), (8.0, 14.2, 124.0)), (0.32, (6.2, 11.0, 104.0), (8.0, 13.2, 123.0)),
                 (1.0, (-12.4, 22.4, 85.6), (8.0, 12.2, 121.6))], fov=45, fov_end=55, ease_kind="inout", fstop=8.0)


# ------------------------------------------------------------------ registry
PRE = {"Title": title_pre, "Stage1": stage1_pre, "Stage2": stage2_pre, "Stage3": stage3_pre}
EVENTS = {k[:-7]: v for k, v in globals().items() if k.endswith("_events")}
CAMERAS = {k[:-7]: v for k, v in globals().items() if k.endswith("_camera")}


def direct(env):
    """Run the whole choreography of one environment and return the Director."""
    d = A.Director(env)
    d.ambient()
    if env.name in PRE:
        PRE[env.name](d)
    for s in d.shot_list:
        EVENTS[s["id"]](d)
    d.on_enable_effects()
    for s in d.shot_list:
        CAMERAS[s["id"]](d)
    d.finalize()
    d.write_reports()
    C.log(f"{env.name}: {len(d.shot_list)} shots, {len(d.sfx)} sound cues, {len(d.log)} events")
    return d
