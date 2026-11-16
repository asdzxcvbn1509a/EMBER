"""EMBER trailer - rebuild one exported Unity scene as a Blender scene (static state at time 0).

Every Unity GameObject becomes an object (mesh or empty) in the same hierarchy, with the converted local TRS.
Lights, text, sprites and particle emitters hang off their node as extra children. Animation is added later
by tr_anim; particles by tr_fx.
"""
import math

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

import tr_common as C
import tr_materials as M

FOUR_PI2 = 4.0 * math.pi * math.pi
RENDER_W, RENDER_H = 1920, 1080


def futura():
    f = bpy.data.fonts.get("Futura-Bold Regular")
    return f or bpy.data.fonts.load(C.FONT_FUTURA, check_existing=True)


class Env:
    """One environment scene (Title, Stage1, Stage2, Stage3, Ending)."""

    def __init__(self, name):
        self.name = name
        self.ex = C.Export(name)
        self.scene = bpy.data.scenes.new(name)
        self.coll = bpy.data.collections.new("ENV_" + name)
        self.scene.collection.children.link(self.coll)
        self.obj = {}          # node id -> object
        self.light_obj = {}    # node id -> light object
        self.text_obj = {}     # node id -> [text objects]
        self.sprite_obj = {}   # node id -> sprite object
        self.ps_nodes = []     # node ids with particle systems
        self.mesh_cache = {}
        self.eff = self.ex.effective_active()
        self.volumes = []      # (node, volume dict)
        self.cameras = {}      # name -> camera object (beats + main)
        self.base_energy = {}  # light object name -> energy at full intensity

    # -------------------------------------------------------------- scene settings
    def setup_scene(self):
        sc = self.scene
        sc.render.engine = "BLENDER_EEVEE"
        sc.render.fps = C.FPS
        sc.render.fps_base = 1.0
        sc.render.resolution_x = RENDER_W
        sc.render.resolution_y = RENDER_H
        sc.render.resolution_percentage = 100
        sc.render.film_transparent = False
        sc.render.use_motion_blur = False
        sc.render.motion_blur_shutter = 0.5
        sc.render.image_settings.file_format = "PNG"
        sc.render.image_settings.color_mode = "RGB"
        sc.render.image_settings.color_depth = "8"
        sc.render.image_settings.compression = 15
        ee = sc.eevee
        ee.taa_render_samples = 32
        ee.taa_samples = 8
        ee.use_shadows = True
        ee.shadow_ray_count = 1
        ee.shadow_step_count = 6
        ee.use_raytracing = False
        ee.use_fast_gi = False
        try:
            ee.shadow_pool_size = "1024"      # MB; the default 512 overflows in the sky/forge scenes
        except TypeError:
            pass
        ee.shadow_resolution_scale = 0.75     # the 1024 MB pool still overflowed at full scale (Stage 3, Title)
        ee.clamp_surface_indirect = 10.0
        sc.view_settings.view_transform = "ACES 1.3"
        sc.view_settings.look = "None"
        sc.view_settings.exposure = 0.0
        sc.view_settings.gamma = 1.0
        sc.sequencer_colorspace_settings.name = "sRGB"
        r = self.ex.render
        sc["t"] = 0.0
        sc["fog_density"] = float(r["fogDensity"]) if r.get("fog") else 0.0
        sc["fog_color"] = [float(x) for x in r["fogColorLin"][:3]]
        sc["env"] = self.name
        # scene time: t = frame / FPS (same clock as Geometry Nodes' Scene Time), linear forever
        sc.frame_start, sc.frame_end = 1, 2160
        C.set_keys(sc, '["t"]', [0.0, float(C.FPS)], [0.0, 1.0], interp="LINEAR", extrapolate="LINEAR")
        sc.world = M.build_world(self.ex, self.name)
        vl = sc.view_layers[0]
        vl.use_pass_z = True

    # -------------------------------------------------------------- nodes
    def build(self):
        self.setup_scene()
        for n in self.ex.nodes:
            self.make_node(n)
        for n in self.ex.nodes:
            self.decorate(n)
        self.apply_visibility()
        C.log(f"{self.name}: {len(self.obj)} nodes, {len(self.light_obj)} lights, {len(self.ps_nodes)} emitters, "
              f"{sum(len(v) for v in self.text_obj.values())} text lines, {len(self.sprite_obj)} sprites")
        return self

    def oname(self, n, prefix=""):
        base = n["name"][:40]
        return f"{prefix}{base}#{self.name[:2]}{n['id']}"

    def make_node(self, n):
        mr = n.get("mr")
        data = None
        if mr and mr.get("mesh", -1) >= 0 and mr.get("enabled", True):
            data = self.mesh(mr["mesh"])
        ob = bpy.data.objects.new(self.oname(n), data)
        if data is None:
            ob.empty_display_size = 0.25
        self.coll.objects.link(ob)
        ob.rotation_mode = "QUATERNION"
        p = n["parent"]
        if p >= 0:
            ob.parent = self.obj[p]
        ob.location = C.u_pos(n["lp"])
        ob.rotation_quaternion = C.u_quat(n["lr"])
        ob.scale = C.u_scale(n["ls"])
        ob["uid"] = n["id"]
        ob["upath"] = n["path"]
        self.obj[n["id"]] = ob
        if data is not None:
            mats = mr["mats"]
            for i, slot in enumerate(ob.material_slots):
                slot.link = "OBJECT"
                if i < len(mats):
                    slot.material = M.material(self.ex, mats[i])
            ob.visible_shadow = mr.get("cast", "On") != "Off"
        return ob

    def mesh(self, mid):
        if mid in self.mesh_cache:
            return self.mesh_cache[mid]
        ex = self.ex
        m = ex.meshes[mid]
        me = None
        if m.get("pos") and "error" not in m:
            pos = C.np_u_pos(ex.f32(m["pos"], 3))
            tris, mats = [], []
            for si, sub in enumerate(m.get("sub") or []):
                if not sub:
                    continue
                idx = ex.u32(sub).reshape(-1, 3)[:, [0, 2, 1]]
                tris.append(idx)
                mats.append(np.full(len(idx), si, dtype=np.int32))
            if tris:
                tris = np.concatenate(tris).astype(np.int32)
                mats = np.concatenate(mats)
                me = bpy.data.meshes.new(m["name"] or f"mesh{mid}")
                nv, ntri = len(pos), len(tris)
                me.vertices.add(nv)
                me.vertices.foreach_set("co", pos.astype(np.float32).ravel())
                me.loops.add(ntri * 3)
                me.loops.foreach_set("vertex_index", tris.ravel())
                me.polygons.add(ntri)
                me.polygons.foreach_set("loop_start", np.arange(0, ntri * 3, 3, dtype=np.int32))
                me.polygons.foreach_set("material_index", mats)
                uv = ex.f32(m.get("uv0"), 2)
                if uv is not None:
                    uvl = me.uv_layers.new(name="UVMap")
                    uvl.data.foreach_set("uv", uv[tris.ravel()].astype(np.float32).ravel())
                col = ex.f32(m.get("col"), 4)
                if col is not None and not np.allclose(col, 1.0):
                    ca = me.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
                    ca.data.foreach_set("color", col.astype(np.float32).ravel())
                me.update(calc_edges=True)
                me.shade_smooth()
                nrm = ex.f32(m.get("nrm"), 3)
                if nrm is not None:
                    me.normals_split_custom_set_from_vertices(C.np_u_pos(nrm).tolist())
                for _ in m.get("sub") or []:
                    me.materials.append(None)
        self.mesh_cache[mid] = me
        return me

    # -------------------------------------------------------------- lights, text, sprites, probes, cameras
    def decorate(self, n):
        ob = self.obj[n["id"]]
        if "light" in n:
            self.make_light(n, ob)
        if "tmp" in n:
            self.make_text(n, ob)
        if "sprite" in n and n["sprite"].get("sprite"):
            self.make_sprite(n, ob)
        if "ps" in n:
            self.ps_nodes.append(n["id"])
        if "probe" in n:
            self.make_probe(n, ob)
        if "volume" in n:
            self.volumes.append((n, n["volume"]))
        for b in n.get("beh", []):
            if b["type"] == "ScrollingUVs":
                r = b["f"].get("uvAnimationRate", [0, 0])
                ob["uv_rate"] = (float(r[0]), float(r[1]), 0.0)
            elif b["type"] == "LevelBeat":
                self.make_beat_camera(n, b)
        if "camera" in n and n["camera"].get("main"):
            self.make_main_camera(n)

    def make_light(self, n, ob):
        L = n["light"]
        t = L["type"]
        name = self.oname(n, "L:")
        I = float(L["intensity"])
        if t == "Directional":
            ld = bpy.data.lights.new(name, "SUN")
            ld.energy = math.pi * I
            ld.angle = math.radians(1.5)
            try:
                ld.shadow_maximum_resolution = 0.02   # coarser sun shadow pages: big skies otherwise fill the pool
            except AttributeError:
                pass
        elif t == "Spot":
            ld = bpy.data.lights.new(name, "SPOT")
            ld.energy = FOUR_PI2 * I
            ld.spot_size = math.radians(float(L["spotAngle"]))
            outer = float(L["spotAngle"])
            inner = float(L.get("innerSpotAngle", outer * 0.6))
            ld.spot_blend = min(1.0, max(0.0, 1.0 - inner / max(outer, 1e-3)))
            ld.shadow_soft_size = 0.1
        else:
            ld = bpy.data.lights.new(name, "POINT")
            ld.energy = FOUR_PI2 * I
            ld.shadow_soft_size = 0.08
        ld.color = tuple(L["colorLin"][:3])
        ld.use_shadow = L["shadows"] != "None"
        if t != "Directional":
            ld.use_soft_falloff = False      # inverse-square like URP (Blender softens near the light by default)
            ld.use_custom_distance = True
            ld.cutoff_distance = float(L["range"]) * 1.2
        lo = bpy.data.objects.new(name, ld)
        self.coll.objects.link(lo)
        lo.parent = ob
        lo.rotation_mode = "QUATERNION"
        lo.rotation_quaternion = C.FIX_VIEW
        lo["uid"] = n["id"]
        if not L.get("enabled", True):
            ld.energy = 0.0
        self.base_energy[lo.name] = ld.energy
        self.light_obj[n["id"]] = lo

    def make_text(self, n, ob):
        t = n["tmp"]
        chars = t["chars"]
        mat = M.material(self.ex, t["mat"])
        objs = []
        for li, line in enumerate(t["lines"]):
            cs = [c for c in chars[line["first"]: line["last"] + 1] if c["ch"] not in (10, 13)]
            vis = [c for c in cs if c["vis"]]
            if not vis:
                continue
            body = "".join(chr(c["ch"]) for c in cs).strip()
            first = next(c for c in cs if chr(c["ch"]) != " ")
            pt = float(first["pt"])
            cu = bpy.data.curves.new(self.oname(n, f"T{li}:"), "FONT")
            cu.body = body
            cu.font = futura()
            cu.size = pt * 0.1
            cu.space_character = 1.0 + 2.0 * float(t.get("spacing", 0.0)) / 100.0
            cu.align_x = "LEFT"
            cu.align_y = "BOTTOM_BASELINE"
            to = bpy.data.objects.new(cu.name, cu)
            self.coll.objects.link(to)
            to.parent = ob
            to.location = C.u_pos((first["origin"], first["base"], 0.0))
            to.rotation_mode = "QUATERNION"
            to.rotation_quaternion = C.FIX_VIEW
            if mat:
                cu.materials.append(mat)
            to.visible_shadow = False
            objs.append(to)
        self.text_obj[n["id"]] = objs

    def make_sprite(self, n, ob):
        """Class candle flames: a camera-facing quad that plays the flipbook in the shader."""
        s = n["sprite"]
        sp = s["sprite"]
        ex = self.ex
        tex = ex.textures[sp["tex"]]
        tw, th = float(tex["w"]), float(tex["h"])
        rx, ry, rw, rh = sp["rect"]
        cell_w, cell_h = 128.0, 256.0
        ppu = float(sp["ppu"])
        px, py = sp["pivot"]
        x0, x1 = -px / ppu, (cell_w - px) / ppu
        y0, y1 = -py / ppu, (cell_h - py) / ppu
        # quad in Unity sprite space (x right, y up, z=0) -> Blender node space
        corners = [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]
        verts = [tuple(C.u_pos((x, y, 0.0))) for x, y in corners]
        me = bpy.data.meshes.new("flame_quad")
        me.from_pydata(verts, [], [(0, 3, 2, 1)])
        uvl = me.uv_layers.new(name="UVMap")
        cu, cv = cell_w / tw, cell_h / th
        v_top = 1.0 - cv
        uv = [(0, v_top), (cu, v_top), (cu, 1.0), (0, 1.0)]
        loop_uv = [uv[i] for i in (0, 3, 2, 1)]
        uvl.data.foreach_set("uv", [c for u in loop_uv for c in u])
        clip = None
        anim = n.get("animator")
        if anim and anim.get("clips"):
            clip = ex.clips[anim["clips"][0]]
        frames = len(clip["pptr"][0]["keys"]) if clip and clip["pptr"] else 1
        dur = float(clip["length"]) if clip else 1.0
        me.materials.append(flame_material(ex, sp["tex"], s["color"], int(tw // cell_w), frames, dur, cu, cv))
        so = bpy.data.objects.new(self.oname(n, "S:"), me)
        self.coll.objects.link(so)
        so.parent = ob
        so["phase"] = float(C.rng(C.stable_seed(self.name, n["id"])).random())
        so.visible_shadow = False
        con = so.constraints.new("LOCKED_TRACK")
        con.lock_axis = "LOCK_Z"
        con.track_axis = "TRACK_Y"
        con.target = self.camera_proxy()
        self.sprite_obj[n["id"]] = so

    def camera_proxy(self):
        """Empty that follows the active shot camera (sprites and look-at constraints aim at it)."""
        name = "CAMERA_PROXY_" + self.name
        pr = bpy.data.objects.get(name)
        if pr is None:
            pr = bpy.data.objects.new(name, None)
            self.coll.objects.link(pr)
        return pr

    def make_probe(self, n, ob):
        p = n["probe"]
        size = C.u_scale(p["size"])
        pd = bpy.data.lightprobes.new(self.oname(n, "P:"), "SPHERE")
        pd.influence_distance = max(size) * 0.5
        po = bpy.data.objects.new(pd.name, pd)
        self.coll.objects.link(po)
        po.parent = ob
        po.location = C.u_pos(p["center"])

    def make_beat_camera(self, n, b):
        f = b["f"]
        pos = np.array(n["wm"]).reshape(4, 4)[:3, 3]
        off = f.get("shotOffset", [0, 6, -9])
        look = f.get("shotLookOffset", [0, 0, 0])
        cam_u = pos + np.array(off)
        tgt_u = pos + np.array(look)
        cam = self.camera("CAM_beat_" + f.get("code", str(n["id"])), C.u_pos(cam_u), C.u_pos(tgt_u), float(f.get("shotFov", 50)))
        cam["beat"] = f.get("code")
        cam["look_dist"] = (C.u_pos(cam_u) - C.u_pos(tgt_u)).length

    def make_main_camera(self, n):
        m = C.u_matrix(n["wm"])
        loc = m.to_translation()
        fwd_u = np.array(n["wm"]).reshape(4, 4)[:3, 2]          # Unity forward = third column
        tgt = loc + C.u_dir(fwd_u).normalized() * 10.0
        self.camera("CAM_main", loc, tgt, float(n["camera"]["fov"]))

    def camera(self, name, loc, target, vfov, near=0.3, far=800.0):
        cd = bpy.data.cameras.new(name + "_" + self.name)
        cd.sensor_fit = "VERTICAL"
        cd.sensor_height = 24.0
        cd.lens = C.lens_from_vfov(vfov)
        cd.clip_start = near
        cd.clip_end = far
        co = bpy.data.objects.new(name + "_" + self.name, cd)
        self.coll.objects.link(co)
        co.location = loc
        d = (Vector(target) - Vector(loc)).normalized()
        co.rotation_mode = "QUATERNION"
        co.rotation_quaternion = d.to_track_quat("-Z", "Y")
        self.cameras[name] = co
        return co

    # -------------------------------------------------------------- visibility (Unity activeInHierarchy at start)
    def apply_visibility(self):
        hide_names = ("Player", "CameraRig")
        for n in self.ex.nodes:
            ob = self.obj[n["id"]]
            vis = self.eff[n["id"]]
            top = n["path"].split("/")[0]
            if top in hide_names:
                vis = False
            if n.get("mr") and not n["mr"].get("enabled", True):
                vis = False
            self.set_visible(n["id"], vis)

    def set_visible(self, nid, vis):
        """Static (frame-independent) visibility of a node's own render objects."""
        ob = self.obj[nid]
        ob.hide_render = not vis
        for extra in self.extras(nid):
            extra.hide_render = not vis

    def extras(self, nid):
        out = []
        if nid in self.light_obj:
            out.append(self.light_obj[nid])
        out.extend(self.text_obj.get(nid, []))
        if nid in self.sprite_obj:
            out.append(self.sprite_obj[nid])
        fx = bpy.data.objects.get("FX:" + self.oname(self.ex.nodes[nid]))
        if fx:
            out.append(fx)
        return out

    def render_objects(self, nid):
        """Every object that renders for a node and its descendants (used by visibility keys)."""
        out = []
        stack = [nid]
        while stack:
            k = stack.pop()
            out.append(self.obj[k])
            out.extend(self.extras(k))
            stack.extend(self.ex.children.get(k, []))
        return out

    # -------------------------------------------------------------- checks
    def validate(self):
        """World matrix of every node from the converted local TRS chain vs the exported Unity world matrix."""
        worst = 0.0
        worst_name = ""
        wm = {}
        for n in self.ex.nodes:                      # parents first
            ob = self.obj[n["id"]]
            local = Matrix.LocRotScale(ob.location, ob.rotation_quaternion, ob.scale)
            p = n["parent"]
            wm[n["id"]] = (wm[p] @ local) if p >= 0 else local
            mu = C.u_matrix(n["wm"])
            d = max(abs(a - b) for ra, rb in zip(wm[n["id"]], mu) for a, b in zip(ra, rb))
            if d > worst:
                worst, worst_name = d, n["path"]
        C.log(f"{self.name}: matrix self-test max error {worst:.2e} ({worst_name})")
        return worst


def flame_material(ex, tex_id, color, cols, frames, dur, cu, cv):
    key = f"flame|{tex_id}|{frames}"
    if key in M._mats:
        return M._mats[key]
    bm, nt = M.new_material("M_Flame_" + ex.textures[tex_id]["name"])
    out = M.node(nt, "ShaderNodeOutputMaterial", 1400, 0)
    tg = M.group_node(nt, M.time_group(), -1200, 0)
    ph = M.attr(nt, "phase", kind="OBJECT", x=-1200, y=-200)
    # idx = floor(fract(t / dur + phase) * frames)
    k = M.math_node(nt, "MULTIPLY_ADD", tg.outputs["Seconds"], 1.0 / dur, -1000, 0)
    M.link(nt, ph.outputs["Fac"], k.node.inputs[2])
    idx = M.math_node(nt, "FLOOR", M.math_node(nt, "MULTIPLY", M.math_node(nt, "FRACT", k, None, -850, 0), float(frames), -700, 0), None, -550, 0)
    col_i = M.math_node(nt, "FLOORED_MODULO", idx, float(cols), -400, 100)
    row_i = M.math_node(nt, "FLOOR", M.math_node(nt, "DIVIDE", idx, float(cols), -400, -100), None, -250, -100)
    du = M.math_node(nt, "MULTIPLY", col_i, cu, -100, 100)
    dv = M.math_node(nt, "MULTIPLY", row_i, -cv, -100, -100)
    off = M.node(nt, "ShaderNodeCombineXYZ", 50, 0)
    M.link(nt, du, off.inputs[0])
    M.link(nt, dv, off.inputs[1])
    uv = M.node(nt, "ShaderNodeUVMap", 50, 200)
    uvo = M.vmath(nt, "ADD", uv.outputs["UV"], off.outputs[0], 250, 100)
    img = M.image(ex, tex_id)
    tn = M.tex_node(nt, img, uvo, 450, 100, extension="CLIP")
    c = tuple(color[:3])
    rgb = M.mix_rgb_sockets(nt, "MULTIPLY", 1.0, tn.outputs["Color"], c, 700, 150)
    rgb = M.fogged_color(nt, rgb, 850, 150)
    em = M.node(nt, "ShaderNodeEmission", 1100, 150)
    M.link(nt, rgb, em.inputs["Color"])
    em.inputs["Strength"].default_value = 1.6
    tr = M.node(nt, "ShaderNodeBsdfTransparent", 1100, 300)
    mix = M.node(nt, "ShaderNodeMixShader", 1250, 150)
    M.link(nt, tn.outputs["Alpha"], mix.inputs[0])
    M.link(nt, tr.outputs[0], mix.inputs[1])
    M.link(nt, em.outputs[0], mix.inputs[2])
    M.link(nt, mix.outputs[0], out.inputs["Surface"])
    bm.surface_render_method = "BLENDED"
    bm.use_backface_culling = False
    M._mats[key] = bm
    return bm
