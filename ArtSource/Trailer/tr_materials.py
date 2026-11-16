"""EMBER trailer - materials rebuilt from the exported Unity materials, plus the shared fog/time node groups.

Per-scene values live in scene custom properties and are read with Attribute(VIEW_LAYER) nodes, so one
material can be shared by every environment scene and still be animated (verified in Blender 5.3):
    scene["t"]            seconds (keyed linear: frame / FPS)
    scene["fog_density"]  Unity exp2 fog density
    scene["fog_color"]    linear rgb
"""
import math
import os

import bpy

from tr_common import ASSETS, BUILD, ensure_dir, log

_images = {}
_mats = {}


# ------------------------------------------------------------------ node helpers
def node(nt, type_, x=0, y=0, **props):
    n = nt.nodes.new(type_)
    n.location = (x, y)
    for k, v in props.items():
        setattr(n, k, v)
    return n


def link(nt, a, b):
    nt.links.new(a, b)


def math_node(nt, op, a=None, b=None, x=0, y=0, clamp=False):
    n = node(nt, "ShaderNodeMath", x, y, operation=op, use_clamp=clamp)
    for i, v in enumerate((a, b)):
        if v is None:
            continue
        if isinstance(v, (int, float)):
            n.inputs[i].default_value = v
        else:
            link(nt, v, n.inputs[i])
    return n.outputs[0]


def vmath(nt, op, a=None, b=None, x=0, y=0):
    n = node(nt, "ShaderNodeVectorMath", x, y, operation=op)
    for i, v in enumerate((a, b)):
        if v is None:
            continue
        if isinstance(v, (tuple, list)):
            n.inputs[i].default_value = v
        else:
            link(nt, v, n.inputs[i])
    return n.outputs["Vector"] if op not in ("DOT_PRODUCT", "LENGTH", "DISTANCE") else n.outputs["Value"]


def mix_rgb_sockets(nt, blend, fac, a, b, x=0, y=0):
    """ShaderNodeMix in RGBA mode. Its sockets share names across data types, so pick them by identifier.
    a / b / fac may be sockets, numbers, colour tuples or None (left at default)."""
    n = node(nt, "ShaderNodeMix", x, y, data_type="RGBA", blend_type=blend)
    n.clamp_factor = True
    ins = {s.identifier: s for s in n.inputs}
    fac_s, a_s, b_s = ins["Factor_Float"], ins["A_Color"], ins["B_Color"]
    for sock, v in ((fac_s, fac), (a_s, a), (b_s, b)):
        if v is None:
            continue
        if isinstance(v, (int, float)):
            sock.default_value = v
        elif isinstance(v, (tuple, list)):
            sock.default_value = tuple(v) + ((1.0,) if len(v) == 3 else ())
        else:
            link(nt, v, sock)
    outs = {s.identifier: s for s in n.outputs}
    return outs["Result_Color"]


def attr(nt, name, kind="VIEW_LAYER", x=0, y=0):
    n = node(nt, "ShaderNodeAttribute", x, y, attribute_type=kind, attribute_name=name)
    return n


# ------------------------------------------------------------------ shared groups
def fog_group():
    """EMB_Fog: Factor = 1 - exp(-(density * viewZ)^2) (URP EXP2), Color = fog colour (linear)."""
    ng = bpy.data.node_groups.get("EMB_Fog")
    if ng:
        return ng
    ng = bpy.data.node_groups.new("EMB_Fog", "ShaderNodeTree")
    ng.interface.new_socket(name="Factor", in_out="OUTPUT", socket_type="NodeSocketFloat")
    ng.interface.new_socket(name="Color", in_out="OUTPUT", socket_type="NodeSocketColor")
    out = node(ng, "NodeGroupOutput", 800, 0)
    cam = node(ng, "ShaderNodeCameraData", 0, 0)
    dens = attr(ng, "fog_density", x=0, y=-200)
    col = attr(ng, "fog_color", x=0, y=-400)
    dz = math_node(ng, "MULTIPLY", cam.outputs["View Z Depth"], dens.outputs["Fac"], 200, 0)
    sq = math_node(ng, "MULTIPLY", dz, dz, 350, 0)
    ex = math_node(ng, "EXPONENT", math_node(ng, "MULTIPLY", sq, -1.0, 450, 0), None, 550, 0)
    f = math_node(ng, "SUBTRACT", 1.0, ex, 650, 0, clamp=True)
    link(ng, f, out.inputs["Factor"])
    link(ng, col.outputs["Color"], out.inputs["Color"])
    return ng


def time_group():
    """EMB_Time: seconds from scene['t']."""
    ng = bpy.data.node_groups.get("EMB_Time")
    if ng:
        return ng
    ng = bpy.data.node_groups.new("EMB_Time", "ShaderNodeTree")
    ng.interface.new_socket(name="Seconds", in_out="OUTPUT", socket_type="NodeSocketFloat")
    out = node(ng, "NodeGroupOutput", 300, 0)
    t = attr(ng, "t", x=0, y=0)
    link(ng, t.outputs["Fac"], out.inputs["Seconds"])
    return ng


def group_node(nt, ng, x=0, y=0):
    n = node(nt, "ShaderNodeGroup", x, y)
    n.node_tree = ng
    return n


def fogged_surface(nt, shader_socket, x=0, y=0):
    """Mix a surface shader towards the fog emission (Unity MixFog for lit opaque surfaces)."""
    fog = group_node(nt, fog_group(), x, y - 250)
    em = node(nt, "ShaderNodeEmission", x + 200, y - 250)
    link(nt, fog.outputs["Color"], em.inputs["Color"])
    mix = node(nt, "ShaderNodeMixShader", x + 400, y)
    link(nt, fog.outputs["Factor"], mix.inputs[0])
    link(nt, shader_socket, mix.inputs[1])
    link(nt, em.outputs[0], mix.inputs[2])
    return mix.outputs[0]


def fogged_color(nt, color_socket, x=0, y=0):
    fog = group_node(nt, fog_group(), x, y - 200)
    return mix_rgb_sockets(nt, "MIX", fog.outputs["Factor"], color_socket, fog.outputs["Color"], x + 200, y)


# ------------------------------------------------------------------ images
def image(ex, tex_id, non_color=False):
    if tex_id is None or tex_id < 0:
        return None
    t = ex.textures[tex_id]
    path = t.get("path")
    if not path or t.get("embedded"):
        return None
    imp = t.get("importer") or {}
    if imp.get("type") == "NormalMap" or imp.get("sRGB") is False:
        non_color = True
    key = (path, non_color)
    if key in _images:
        return _images[key]
    img = _load_image(path)
    if img is None:
        log("WARNING texture failed:", path)
        _images[key] = None
        return None
    img.colorspace_settings.name = "Non-Color" if non_color else "sRGB"
    img.alpha_mode = "STRAIGHT"
    _images[key] = img
    return img


def _load_image(path):
    try:
        img = bpy.data.images.load(path, check_existing=True)
        if img.size[0] > 0:
            return img
    except RuntimeError:
        pass
    # PSD / unusual TIF: convert with the bundled OpenImageIO
    try:
        import OpenImageIO as oiio
        cache = ensure_dir(os.path.join(BUILD, "cache"))
        dst = os.path.join(cache, os.path.splitext(os.path.basename(path))[0] + ".png")
        if not os.path.exists(dst):
            buf = oiio.ImageBuf(path)
            buf.write(dst)
        return bpy.data.images.load(dst, check_existing=True)
    except Exception as e:  # noqa
        log("OIIO convert failed", path, e)
        return None


def tex_node(nt, img, uv_socket=None, x=0, y=0, extension="REPEAT"):
    n = node(nt, "ShaderNodeTexImage", x, y)
    n.image = img
    n.extension = extension
    n.interpolation = "Linear"
    if uv_socket is not None:
        link(nt, uv_socket, n.inputs["Vector"])
    return n


def uv_mapping(nt, st, x=0, y=0, uv_map=None):
    """Unity uv * _ST.xy + _ST.zw."""
    uv = node(nt, "ShaderNodeUVMap", x, y)
    if uv_map:
        uv.uv_map = uv_map
    sx, sy, ox, oy = st if st else (1, 1, 0, 0)
    if (sx, sy, ox, oy) == (1, 1, 0, 0):
        return uv.outputs["UV"]
    mp = node(nt, "ShaderNodeMapping", x + 180, y, vector_type="POINT")
    mp.inputs["Scale"].default_value = (sx, sy, 1)
    mp.inputs["Location"].default_value = (ox, oy, 0)
    link(nt, uv.outputs["UV"], mp.inputs["Vector"])
    return mp.outputs["Vector"]


def P(m, name, default=None):
    p = m["props"].get(name)
    if p is None:
        return default
    t = p["t"]
    if t == "Color":
        return p
    if t == "Texture":
        return p
    return p["v"]


def lin(p, default=(1, 1, 1, 1)):
    """Shader-side value of an exported colour: [HDR] colours are used as stored, others gamma -> linear."""
    if p is None:
        return default
    if p.get("hdr"):
        return p.get("v")
    return p.get("lin") or p.get("v")


# ------------------------------------------------------------------ material builders
def material(ex, mat_id):
    """Blender material for an exported Unity material id (shared across scenes by asset path + name)."""
    if mat_id is None or mat_id < 0:
        return None
    m = ex.materials[mat_id]
    key = (m.get("path") or "") + "|" + m["name"]
    if key in _mats:
        return _mats[key]
    sh = m["shader"]
    if sh in ("Universal Render Pipeline/Lit", "Universal Render Pipeline/Simple Lit"):
        bm = build_lit(ex, m)
    elif sh == "Ember/ScrollingTexture":
        bm = build_scroll(ex, m)
    elif sh in ("Sprites/Default", "Universal Render Pipeline/2D/Sprite-Unlit-Default"):
        bm = build_sprite(ex, m)
    elif sh.startswith("TextMeshPro"):
        bm = build_text(ex, m)
    elif sh == "Universal Render Pipeline/Unlit":
        bm = build_unlit(ex, m)
    else:
        bm = None   # particles and sky are handled elsewhere
    _mats[key] = bm
    return bm


def new_material(name):
    bm = bpy.data.materials.new(name)
    nt = bm.node_tree
    nt.nodes.clear()
    return bm, nt


def cull(bm, m):
    c = P(m, "_Cull", 2.0)
    bm.use_backface_culling = (c == 2.0)
    bm.use_backface_culling_shadow = False


def build_lit(ex, m):
    bm, nt = new_material(m["name"])
    out = node(nt, "ShaderNodeOutputMaterial", 1400, 0)
    bsdf = node(nt, "ShaderNodeBsdfPrincipled", 700, 0)
    base = lin(P(m, "_BaseColor"))
    transparent = P(m, "_Surface", 0.0) == 1.0
    kw = set(m.get("keywords") or [])

    base_tex = P(m, "_BaseMap")
    img = image(ex, base_tex["tex"]) if base_tex else None
    alpha_sock = None
    if img is not None:
        tn = tex_node(nt, img, uv_mapping(nt, base_tex["st"], -600, 200), -300, 200)
        col = mix_rgb_sockets(nt, "MULTIPLY", 1.0, tn.outputs["Color"], tuple(base[:3]), 100, 200)
        link(nt, col, bsdf.inputs["Base Color"])
        alpha_sock = tn.outputs["Alpha"]
    else:
        bsdf.inputs["Base Color"].default_value = tuple(base[:3]) + (1.0,)

    smooth = P(m, "_Smoothness", 0.5)
    if "_SPECULAR_SETUP" in kw:
        bsdf.inputs["Metallic"].default_value = 0.0
        spec = lin(P(m, "_SpecColor"), (0.2, 0.2, 0.2, 1))
        bsdf.inputs["Specular IOR Level"].default_value = min(1.0, max(spec[:3]) * 2.5)
    else:
        bsdf.inputs["Metallic"].default_value = float(P(m, "_Metallic", 0.0))
    bsdf.inputs["Roughness"].default_value = max(0.04, 1.0 - float(smooth))

    nrm_tex = P(m, "_BumpMap")
    if nrm_tex and "_NORMALMAP" in kw:
        nimg = image(ex, nrm_tex["tex"], non_color=True)
        if nimg is not None:
            st = base_tex["st"] if base_tex else nrm_tex["st"]   # URP samples the normal map with _BaseMap_ST
            tn = tex_node(nt, nimg, uv_mapping(nt, st, -600, -300), -300, -300)
            nm = node(nt, "ShaderNodeNormalMap", 100, -300)
            nm.inputs["Strength"].default_value = float(P(m, "_BumpScale", 1.0))
            link(nt, tn.outputs["Color"], nm.inputs["Color"])
            link(nt, nm.outputs["Normal"], bsdf.inputs["Normal"])

    if "_EMISSION" in kw:
        ec = lin(P(m, "_EmissionColor"), (0, 0, 0, 1))
        em_tex = P(m, "_EmissionMap")
        eimg = image(ex, em_tex["tex"]) if em_tex else None
        if eimg is not None:
            st = base_tex["st"] if base_tex else em_tex["st"]
            tn = tex_node(nt, eimg, uv_mapping(nt, st, -600, -600), -300, -600)
            col = mix_rgb_sockets(nt, "MULTIPLY", 1.0, tn.outputs["Color"], tuple(ec[:3]), 100, -600)
            link(nt, col, bsdf.inputs["Emission Color"])
        else:
            bsdf.inputs["Emission Color"].default_value = tuple(ec[:3]) + (1.0,)
        bsdf.inputs["Emission Strength"].default_value = 1.0

    shader = fogged_surface(nt, bsdf.outputs[0], 950, 0)
    if transparent:
        a = base[3]
        if alpha_sock is not None:
            a_s = math_node(nt, "MULTIPLY", alpha_sock, a, 950, -300)
        else:
            a_s = a
        tr = node(nt, "ShaderNodeBsdfTransparent", 1100, 200)
        mix = node(nt, "ShaderNodeMixShader", 1250, 0)
        if isinstance(a_s, float):
            mix.inputs[0].default_value = a_s
        else:
            link(nt, a_s, mix.inputs[0])
        link(nt, tr.outputs[0], mix.inputs[1])
        link(nt, shader, mix.inputs[2])
        shader = mix.outputs[0]
        bm.surface_render_method = "DITHERED"
        bm.use_transparent_shadow = True
    elif P(m, "_AlphaClip", 0.0) == 1.0 and alpha_sock is not None:
        clip = math_node(nt, "GREATER_THAN", alpha_sock, float(P(m, "_Cutoff", 0.5)), 950, -300)
        tr = node(nt, "ShaderNodeBsdfTransparent", 1100, 200)
        mix = node(nt, "ShaderNodeMixShader", 1250, 0)
        link(nt, clip, mix.inputs[0])
        link(nt, tr.outputs[0], mix.inputs[1])
        link(nt, shader, mix.inputs[2])
        shader = mix.outputs[0]
    link(nt, shader, out.inputs["Surface"])
    cull(bm, m)
    return bm


def build_unlit(ex, m):
    bm, nt = new_material(m["name"])
    out = node(nt, "ShaderNodeOutputMaterial", 1000, 0)
    base = lin(P(m, "_BaseColor"))
    em = node(nt, "ShaderNodeEmission", 600, 0)
    em.inputs["Color"].default_value = tuple(base[:3]) + (1,)
    link(nt, em.outputs[0], out.inputs["Surface"])
    cull(bm, m)
    return bm


def build_scroll(ex, m):
    """Ember/ScrollingTexture, matching its fragment shader (see ScrollingTexture.shader)."""
    bm, nt = new_material(m["name"])
    out = node(nt, "ShaderNodeOutputMaterial", 2200, 0)
    tg = group_node(nt, time_group(), -1400, 0)
    t = tg.outputs["Seconds"]
    # main uv: uv * ST + ST.zw + object["uv_rate"] * t     (ScrollingUVs drives the _MainTex offset)
    main = P(m, "_MainTex")
    det = P(m, "_DetailTex")
    uv = node(nt, "ShaderNodeUVMap", -1400, 300)
    rate = attr(nt, "uv_rate", kind="OBJECT", x=-1400, y=150)
    off = vmath(nt, "SCALE", rate.outputs["Vector"], None, -1200, 150)
    sc_node = off.node
    link(nt, t, sc_node.inputs["Scale"])
    st = main["st"] if main else (1, 1, 0, 0)
    muv = vmath(nt, "MULTIPLY_ADD", uv.outputs["UV"], (st[0], st[1], 1.0), -1000, 300)
    muv.node.inputs[2].default_value = (st[2], st[3], 0.0)
    muv = vmath(nt, "ADD", muv, off, -850, 300)
    img = image(ex, main["tex"]) if main else None
    if img is None:
        a_rgb, a_a = (1.0, 1.0, 1.0), 1.0
    else:
        tn = tex_node(nt, img, muv, -650, 300)
        a_rgb, a_a = tn.outputs["Color"], tn.outputs["Alpha"]
    # detail: uv * ST + t * speed
    dst = det["st"] if det else (1, 1, 0, 0)
    speed = P(m, "_DetailSpeed", [0, 0, 0, 0])
    dimg = image(ex, det["tex"]) if det else None
    strength = float(P(m, "_DetailStrength", 0.0))
    if dimg is not None and strength > 0:
        duv = vmath(nt, "MULTIPLY_ADD", uv.outputs["UV"], (dst[0], dst[1], 1.0), -1000, -100)
        duv.node.inputs[2].default_value = (dst[2], dst[3], 0.0)
        dsp = vmath(nt, "SCALE", None, None, -1000, -250)
        dsp.node.inputs[0].default_value = (speed[0], speed[1], 0.0)
        link(nt, t, dsp.node.inputs["Scale"])
        duv = vmath(nt, "ADD", duv, dsp, -850, -100)
        dtn = tex_node(nt, dimg, duv, -650, -100)
        sep = node(nt, "ShaderNodeSeparateColor", -400, -100)
        link(nt, dtn.outputs["Color"], sep.inputs[0])
        dr = sep.outputs[0]
        # detail = lerp(1, 2 d.r, s);  alpha factor = lerp(1, 1.5 d.r, s/2)
        detail = math_node(nt, "MULTIPLY_ADD", dr, 2.0 * strength, -200, -100)
        detail.node.inputs[2].default_value = 1.0 - strength
        afac = math_node(nt, "MULTIPLY_ADD", dr, 1.5 * strength * 0.5, -200, -250)
        afac.node.inputs[2].default_value = 1.0 - strength * 0.5
    else:
        detail, afac = 1.0, 1.0
    color = lin(P(m, "_Color"))
    emission = float(P(m, "_Emission", 1.0))
    pulse_amt = float(P(m, "_Pulse", 0.0))
    pulse_speed = float(P(m, "_PulseSpeed", 1.5))
    # pulse = 1 + p * sin(t * speed * 2pi)
    ph = math_node(nt, "MULTIPLY", t, pulse_speed * 2 * math.pi, -400, -450)
    pulse = math_node(nt, "MULTIPLY_ADD", math_node(nt, "SINE", ph, None, -250, -450), pulse_amt, -100, -450)
    pulse.node.inputs[2].default_value = 1.0
    gain = math_node(nt, "MULTIPLY", pulse, emission, 50, -450)
    if not isinstance(detail, float):
        gain = math_node(nt, "MULTIPLY", gain, detail, 200, -300)
    rgb = mix_rgb_sockets(nt, "MULTIPLY", 1.0, a_rgb, tuple(color[:3]), 0, 300)
    rgb_scaled = vmath(nt, "SCALE", rgb, None, 350, 300)
    link(nt, gain, rgb_scaled.node.inputs["Scale"])
    # alpha = sat(a.a * color.a * afac)
    alpha = math_node(nt, "MULTIPLY", a_a, color[3], 200, 100)
    if not isinstance(afac, float):
        alpha = math_node(nt, "MULTIPLY", alpha, afac, 350, 100)
    alpha = math_node(nt, "MINIMUM", alpha, 1.0, 500, 100)
    # edge fade: lerp(1, ndv^p, f), ndv = |N.V|
    edge_f = float(P(m, "_EdgeFade", 0.0))
    if edge_f > 0:
        lw = node(nt, "ShaderNodeLayerWeight", 200, -600)
        lw.inputs["Blend"].default_value = 0.5
        ndv = math_node(nt, "SUBTRACT", 1.0, lw.outputs["Facing"], 400, -600, clamp=True)
        pw = math_node(nt, "POWER", ndv, float(P(m, "_EdgePower", 2.0)), 550, -600)
        edge = math_node(nt, "MULTIPLY_ADD", pw, edge_f, 700, -600)
        edge.node.inputs[2].default_value = 1.0 - edge_f
        alpha = math_node(nt, "MULTIPLY", alpha, edge, 850, 100)
        rgb_scaled = vmath(nt, "SCALE", rgb_scaled, None, 850, 300)
        link(nt, edge, rgb_scaled.node.inputs["Scale"])
    rgb_f = fogged_color(nt, rgb_scaled, 1050, 300)
    src = P(m, "_SrcBlend", 1.0)
    dst_b = P(m, "_DstBlend", 0.0)
    em = node(nt, "ShaderNodeEmission", 1400, 300)
    if src == 5.0 and dst_b == 1.0:            # additive: SrcAlpha One
        prem = vmath(nt, "SCALE", rgb_f, None, 1250, 300)
        link(nt, alpha, prem.node.inputs["Scale"])
        link(nt, prem, em.inputs["Color"])
        tr = node(nt, "ShaderNodeBsdfTransparent", 1400, 450)
        add = node(nt, "ShaderNodeAddShader", 1700, 300)
        link(nt, tr.outputs[0], add.inputs[0])
        link(nt, em.outputs[0], add.inputs[1])
        link(nt, add.outputs[0], out.inputs["Surface"])
        bm.surface_render_method = "BLENDED"
        bm.use_transparency_overlap = True
    elif src == 5.0 and dst_b == 10.0:         # alpha blend
        link(nt, rgb_f, em.inputs["Color"])
        tr = node(nt, "ShaderNodeBsdfTransparent", 1400, 450)
        mix = node(nt, "ShaderNodeMixShader", 1700, 300)
        link(nt, alpha, mix.inputs[0])
        link(nt, tr.outputs[0], mix.inputs[1])
        link(nt, em.outputs[0], mix.inputs[2])
        link(nt, mix.outputs[0], out.inputs["Surface"])
        bm.surface_render_method = "DITHERED"
    else:                                       # opaque
        link(nt, rgb_f, em.inputs["Color"])
        link(nt, em.outputs[0], out.inputs["Surface"])
    cull(bm, m)
    return bm


def build_sprite(ex, m, tex_id=None, color=(1, 1, 1, 1), name=None):
    """Sprites/Default (class candle flames): texture * colour, alpha blended, unlit."""
    bm, nt = new_material(name or m["name"])
    out = node(nt, "ShaderNodeOutputMaterial", 1000, 0)
    em = node(nt, "ShaderNodeEmission", 500, 100)
    tr = node(nt, "ShaderNodeBsdfTransparent", 500, 250)
    mix = node(nt, "ShaderNodeMixShader", 750, 100)
    img = image(ex, tex_id) if tex_id is not None else None
    if img is not None:
        tn = tex_node(nt, img, None, 0, 100, extension="CLIP")
        c = mix_rgb_sockets(nt, "MULTIPLY", 1.0, tn.outputs["Color"], tuple(color[:3]), 250, 100)
        link(nt, c, em.inputs["Color"])
        a = math_node(nt, "MULTIPLY", tn.outputs["Alpha"], color[3], 250, -100)
        link(nt, a, mix.inputs[0])
    else:
        em.inputs["Color"].default_value = tuple(color[:3]) + (1,)
        mix.inputs[0].default_value = color[3]
    link(nt, tr.outputs[0], mix.inputs[1])
    link(nt, em.outputs[0], mix.inputs[2])
    link(nt, mix.outputs[0], out.inputs["Surface"])
    bm.surface_render_method = "BLENDED"
    bm.use_backface_culling = False
    return bm


def build_text(ex, m):
    """Glowing engraving text (TMP face colour is HDR); fogged like the rest of the scene."""
    bm, nt = new_material(m["name"])
    out = node(nt, "ShaderNodeOutputMaterial", 900, 0)
    face = P(m, "_FaceColor")
    c = lin(face, (1, 1, 1, 1)) if face else (1, 1, 1, 1)
    em = node(nt, "ShaderNodeEmission", 600, 0)
    col = fogged_color(nt, tuple(c[:3]), 200, 0)
    link(nt, col, em.inputs["Color"])
    link(nt, em.outputs[0], out.inputs["Surface"])
    return bm


def particle_material(ex, mat_id, name):
    """URP Particles/Unlit -> emission quad: texture * vertex/instance colour; additive or alpha blended.
    Instance colour (rgba, linear) comes from the Geometry Nodes attribute 'pcol'."""
    m = ex.materials[mat_id] if mat_id is not None and mat_id >= 0 else None
    key = "particle|" + (m["name"] if m else name)
    if key in _mats:
        return _mats[key]
    bm, nt = new_material(key.replace("|", "_"))
    out = node(nt, "ShaderNodeOutputMaterial", 1400, 0)
    base = lin(P(m, "_BaseColor")) if m else (1, 1, 1, 1)
    blend = P(m, "_Blend", 0.0) if m else 0.0
    tex = P(m, "_BaseMap") if m else None
    img = image(ex, tex["tex"]) if tex else None
    pc = attr(nt, "pcol", kind="GEOMETRY", x=-400, y=-200)
    if img is not None:
        tn = tex_node(nt, img, None, -400, 200, extension="CLIP")
        rgb = mix_rgb_sockets(nt, "MULTIPLY", 1.0, tn.outputs["Color"], tuple(base[:3]), -150, 200)
        a = math_node(nt, "MULTIPLY", tn.outputs["Alpha"], base[3], -150, 0)
    else:
        rgb, a = tuple(base[:3]), base[3]
    rgb = mix_rgb_sockets(nt, "MULTIPLY", 1.0, rgb, pc.outputs["Color"], 100, 200)
    a = math_node(nt, "MULTIPLY", a, pc.outputs["Alpha"], 100, 0)
    rgb = fogged_color(nt, rgb, 300, 200)
    em = node(nt, "ShaderNodeEmission", 800, 200)
    tr = node(nt, "ShaderNodeBsdfTransparent", 800, 350)
    additive = blend == 2.0          # URP Particles/Unlit _Blend: 0 alpha, 1 premultiply, 2 additive, 3 multiply
    if additive:
        prem = vmath(nt, "SCALE", rgb, None, 600, 200)
        link(nt, a, prem.node.inputs["Scale"])
        link(nt, prem, em.inputs["Color"])
        add = node(nt, "ShaderNodeAddShader", 1100, 200)
        link(nt, tr.outputs[0], add.inputs[0])
        link(nt, em.outputs[0], add.inputs[1])
        link(nt, add.outputs[0], out.inputs["Surface"])
    else:
        link(nt, rgb, em.inputs["Color"])
        mix = node(nt, "ShaderNodeMixShader", 1100, 200)
        link(nt, a, mix.inputs[0])
        link(nt, tr.outputs[0], mix.inputs[1])
        link(nt, em.outputs[0], mix.inputs[2])
        link(nt, mix.outputs[0], out.inputs["Surface"])
    bm.surface_render_method = "BLENDED"
    bm.use_backface_culling = False
    bm.use_transparency_overlap = True
    _mats[key] = bm
    return bm


# ------------------------------------------------------------------ world (Ember/SkyGradient)
def build_world(ex, name):
    """Camera rays see the sky gradient; every other ray sees Unity's flat ambient (the raw colour, see GameManager)."""
    w = bpy.data.worlds.new("World_" + name)
    nt = w.node_tree
    nt.nodes.clear()
    out = node(nt, "ShaderNodeOutputWorld", 2600, 0)
    r = ex.render
    sky = ex.materials[r["skybox"]] if r.get("skybox", -1) >= 0 else None
    amb = r.get("ambientGame") or r.get("ambient")
    amb_rgb = node(nt, "ShaderNodeRGB", 1900, -300)
    amb_rgb.name = amb_rgb.label = "ambient"
    amb_rgb.outputs[0].default_value = (amb[0], amb[1], amb[2], 1)
    bg_amb = node(nt, "ShaderNodeBackground", 2150, -300)
    link(nt, amb_rgb.outputs[0], bg_amb.inputs["Color"])
    lp = node(nt, "ShaderNodeLightPath", 2150, 300)
    mix = node(nt, "ShaderNodeMixShader", 2400, 0)
    link(nt, lp.outputs["Is Camera Ray"], mix.inputs[0])
    link(nt, bg_amb.outputs[0], mix.inputs[1])
    bg_sky = node(nt, "ShaderNodeBackground", 2150, 0)
    link(nt, sky_color(nt, sky), bg_sky.inputs["Color"])
    link(nt, bg_sky.outputs[0], mix.inputs[2])
    link(nt, mix.outputs[0], out.inputs["Surface"])
    return w


def _val(nt, name, v, x, y):
    n = node(nt, "ShaderNodeValue", x, y)
    n.name = n.label = name
    n.outputs[0].default_value = v
    return n.outputs[0]


def _rgb(nt, name, c, x, y):
    n = node(nt, "ShaderNodeRGB", x, y)
    n.name = n.label = name
    n.outputs[0].default_value = (c[0], c[1], c[2], 1)
    return n.outputs[0]


def sky_color(nt, sky):
    """Node network of SkyGradient.shader. Named nodes (sky_blend, sun_dir, sun_visible...) can be keyed."""
    def c(name):
        return lin(P(sky, name)) if sky else (0, 0, 0, 1)

    def f(name, d):
        return float(P(sky, name, d)) if sky else d

    tc = node(nt, "ShaderNodeTexCoord", -800, 0)
    d = vmath(nt, "NORMALIZE", tc.outputs["Generated"], None, -600, 0)
    sep = node(nt, "ShaderNodeSeparateXYZ", -400, 0)
    link(nt, d, sep.inputs[0])
    h = sep.outputs["Z"]
    blend = _val(nt, "sky_blend", f("_Blend", 0.0), -400, 400)
    top = mix_rgb_sockets(nt, "MIX", blend, c("_TopA"), c("_TopB"), -200, 500)
    hor = mix_rgb_sockets(nt, "MIX", blend, c("_HorizonA"), c("_HorizonB"), -200, 350)
    bot = mix_rgb_sockets(nt, "MIX", blend, c("_BottomA"), c("_BottomB"), -200, 200)
    up = math_node(nt, "POWER", math_node(nt, "MAXIMUM", h, 0.0, -200, 0), 1.0 / f("_HorizonSharpness", 1.6), 0, 0)
    down = math_node(nt, "POWER", math_node(nt, "MAXIMUM", math_node(nt, "MULTIPLY", h, -1.0, -300, -150), 0.0, -200, -150), 0.6, 0, -150)
    upper = mix_rgb_sockets(nt, "MIX", up, hor, top, 200, 400)
    lower = mix_rgb_sockets(nt, "MIX", down, hor, bot, 200, 250)
    is_up = math_node(nt, "GREATER_THAN", h, 0.0, 200, 0)
    col = mix_rgb_sockets(nt, "MIX", is_up, lower, upper, 400, 300)
    # sun disc + glow
    sd = P(sky, "_SunDirection", [0.3, 0.08, 1, 0]) if sky else [0, 0.3, 1, 0]
    sdir = node(nt, "ShaderNodeCombineXYZ", -400, -400)
    sdir.name = sdir.label = "sun_dir"
    sdir.inputs[0].default_value, sdir.inputs[1].default_value, sdir.inputs[2].default_value = -sd[0], -sd[2], sd[1]
    sdn = vmath(nt, "NORMALIZE", sdir.outputs[0], None, -200, -400)
    s = vmath(nt, "DOT_PRODUCT", d, sdn, 0, -400)
    size = f("_SunSize", 0.012)
    disc = node(nt, "ShaderNodeMapRange", 200, -400, interpolation_type="SMOOTHSTEP")
    disc.inputs["From Min"].default_value = 1.0 - size
    disc.inputs["From Max"].default_value = 1.0 - size * 0.6
    link(nt, s, disc.inputs["Value"])
    ss = math_node(nt, "MAXIMUM", s, 0.0, 200, -600)
    g = math_node(nt, "ADD", math_node(nt, "MULTIPLY", math_node(nt, "POWER", ss, 6.0, 350, -550), 0.35, 500, -550),
                  math_node(nt, "MULTIPLY", math_node(nt, "POWER", ss, 48.0, 350, -700), 0.6, 500, -700), 650, -600)
    g = math_node(nt, "MULTIPLY", g, f("_SunGlow", 0.6), 800, -600)
    sun_amt = math_node(nt, "ADD", disc.outputs[0], g, 950, -450)
    vis = _val(nt, "sun_visible", f("_SunVisible", 0.0), 950, -650)
    sun_amt = math_node(nt, "MULTIPLY", sun_amt, vis, 1100, -450)
    sunc = lin(P(sky, "_SunColor"), (4, 2.6, 1.6, 1)) if sky else (4, 2.6, 1.6, 1)
    sun_rgb = vmath(nt, "SCALE", None, None, 1250, -450)
    sun_rgb.node.inputs[0].default_value = tuple(sunc[:3])
    link(nt, sun_amt, sun_rgb.node.inputs["Scale"])
    col = vmath(nt, "ADD", col, sun_rgb, 1450, 200)
    # stars: hash(floor(d_unity * 260)) > 1 - 0.0035*stars, * sat(5h) * (1 - blend)
    stars = f("_Stars", 0.0)
    if stars > 0:
        scaled = vmath(nt, "SCALE", d, None, -200, -900)
        scaled.node.inputs["Scale"].default_value = 260.0
        cell = vmath(nt, "FLOOR", scaled, None, 0, -900)
        wn = node(nt, "ShaderNodeTexWhiteNoise", 200, -900, noise_dimensions="3D")
        link(nt, cell, wn.inputs["Vector"])
        st_on = math_node(nt, "GREATER_THAN", wn.outputs["Value"], 1.0 - 0.0035 * stars, 400, -900)
        hor_fade = math_node(nt, "MINIMUM", math_node(nt, "MAXIMUM", math_node(nt, "MULTIPLY", h, 5.0, 400, -1050), 0.0, 550, -1050), 1.0, 700, -1050)
        inv_blend = math_node(nt, "SUBTRACT", 1.0, blend, 700, -1200)
        st_amt = math_node(nt, "MULTIPLY", math_node(nt, "MULTIPLY", st_on, hor_fade, 850, -950), inv_blend, 1000, -950)
        st_amt = math_node(nt, "MULTIPLY", st_amt, f("_StarBrightness", 2.0) * 0.8, 1150, -950)
        st_rgb = node(nt, "ShaderNodeCombineXYZ", 1300, -950)
        for i in range(3):
            link(nt, st_amt, st_rgb.inputs[i])
        col = vmath(nt, "ADD", col, st_rgb.outputs[0], 1600, 100)
    return col
