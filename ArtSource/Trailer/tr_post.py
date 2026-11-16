"""EMBER trailer - per-environment compositor that reproduces the game's URP post-processing.

Order follows URP (Uber post + colour grading LUT), all in scene-linear before Blender's ACES 1.3 view:
    bloom -> vignette -> post exposure -> white balance (LMS) -> contrast (log, mid grey 0.18)
    -> split toning (gamma space, soft light) -> saturation -> [view transform ACES 1.3]
Every parameter is a named Value/RGB node ("p_*"), so the Stage 3 finale can key Night -> Dawn.
"""
import math
import os

import bpy
import numpy as np

import tr_common as C

LUMA = (0.2126729, 0.7151522, 0.0721750)
LIN_2_LMS = np.array([[3.90405e-1, 5.49941e-1, 8.92632e-3],
                      [7.08416e-2, 9.63172e-1, 1.35775e-3],
                      [2.31082e-2, 1.28021e-1, 9.36245e-1]])
LMS_2_LIN = np.array([[2.85847e+0, -1.62879e+0, -2.48910e-2],
                      [-2.10182e-1, 1.15820e+0, 3.24281e-4],
                      [-4.18120e-2, -1.18169e-1, 1.06867e+0]])


# ------------------------------------------------------------------ profile -> parameters
def profile_params(vol):
    """URP Volume (exported) -> flat dict of grading parameters."""
    comps = {c["type"]: c["params"] for c in vol["components"] if c.get("active", True)}

    def g(comp, name, default):
        p = comps.get(comp, {}).get(name)
        if p is None or not p.get("o", False):
            return default
        v = p["v"]
        if isinstance(v, dict):
            v = v.get("raw")
        return v

    ca, st, vg, wb, bl, dof = "ColorAdjustments", "SplitToning", "Vignette", "WhiteBalance", "Bloom", "DepthOfField"
    return {
        "exposure": float(g(ca, "postExposure", 0.0)),
        "contrast": float(g(ca, "contrast", 0.0)),
        "saturation": float(g(ca, "saturation", 0.0)),
        "split_shadows": list(g(st, "shadows", [0.5, 0.5, 0.5, 1]))[:3],
        "split_highlights": list(g(st, "highlights", [0.5, 0.5, 0.5, 1]))[:3],
        "split_balance": float(g(st, "balance", 0.0)),
        "vignette": float(g(vg, "intensity", 0.0)),
        "vignette_smooth": float(g(vg, "smoothness", 0.2)),
        "vignette_color": list(g(vg, "color", [0, 0, 0, 1]))[:3],
        "temperature": float(g(wb, "temperature", 0.0)),
        "tint": float(g(wb, "tint", 0.0)),
        "bloom": float(g(bl, "intensity", 0.0)),
        "bloom_threshold": float(g(bl, "threshold", 0.9)),
        "bloom_scatter": float(g(bl, "scatter", 0.7)),
        "bloom_tint": list(g(bl, "tint", [1, 1, 1, 1]))[:3],
        "dof_focus": float(g(dof, "focusDistance", 10.0)),
        "dof_aperture": float(g(dof, "aperture", 5.6)),
        "dof_focal": float(g(dof, "focalLength", 50.0)),
        "grain": float(g("FilmGrain", "intensity", 0.0)),
    }


def wb_matrix(temperature, tint):
    """URP ColorUtils.ColorBalanceToLMSCoeffs + the LMS round trip as one 3x3 matrix (linear rgb)."""
    t1, t2 = temperature / 65.0, tint / 65.0
    x = 0.31271 - t1 * (0.1 if t1 < 0 else 0.05)
    y = 2.87 * x - 3.0 * x * x - 0.27509507 + t2 * 0.05
    X, Y, Z = x / y, 1.0, (1.0 - x - y) / y
    L = 0.7328 * X + 0.4296 * Y - 0.1624 * Z
    Mv = -0.7036 * X + 1.6975 * Y + 0.0061 * Z
    S = 0.0030 * X + 0.0136 * Y + 0.9834 * Z
    w1 = np.array([0.949237, 1.03542, 1.08728])
    coeff = w1 / np.array([L, Mv, S])
    return LMS_2_LIN @ np.diag(coeff) @ LIN_2_LMS


# ------------------------------------------------------------------ graph builder
class G:
    def __init__(self, ng):
        self.ng = ng
        self.x = 0

    def n(self, t, **kw):
        nd = self.ng.nodes.new(t)
        nd.location = (self.x, 0)
        self.x += 40
        for k, v in kw.items():
            setattr(nd, k, v)
        return nd

    def val(self, name, v):
        nd = self.n("ShaderNodeValue")
        nd.name = nd.label = name
        nd.outputs[0].default_value = float(v)
        return nd.outputs[0]

    def m(self, op, a, b=None, c=None, clamp=False):
        nd = self.n("ShaderNodeMath", operation=op, use_clamp=clamp)
        for i, v in enumerate((a, b, c)):
            if v is None:
                continue
            if isinstance(v, (int, float)):
                nd.inputs[i].default_value = float(v)
            else:
                self.ng.links.new(v, nd.inputs[i])
        return nd.outputs[0]

    def lin3(self, ws, xs):
        """ws[0]*xs[0] + ws[1]*xs[1] + ws[2]*xs[2] (ws: sockets or floats)."""
        acc = self.m("MULTIPLY", xs[0], ws[0])
        acc = self.m("MULTIPLY_ADD", xs[1], ws[1], acc)
        return self.m("MULTIPLY_ADD", xs[2], ws[2], acc)

    def soft_light(self, base, blend):
        # URP Colors.hlsl SoftLight
        two_bl = self.m("MULTIPLY", blend, 2.0)
        r1 = self.m("MULTIPLY", self.m("MULTIPLY", base, base), self.m("SUBTRACT", 1.0, two_bl))
        r1 = self.m("MULTIPLY_ADD", two_bl, base, r1)
        r2 = self.m("MULTIPLY", self.m("SQRT", self.m("MAXIMUM", base, 0.0)), self.m("SUBTRACT", two_bl, 1.0))
        r2 = self.m("MULTIPLY_ADD", self.m("MULTIPLY", base, 2.0), self.m("SUBTRACT", 1.0, blend), r2)
        t = self.m("GREATER_THAN", blend, 0.5)
        return self.m("MULTIPLY_ADD", t, self.m("SUBTRACT", r2, r1), r1)


def vignette_image(env, intensity, smooth, color):
    """Multiplier image lerp(colour, 1, pow(sat(1 - |uv - .5|^2 * (3i)^2), 5s)) (URP UberPost)."""
    w, h = 960, 540
    cache = C.ensure_dir(os.path.join(C.BUILD, "cache"))
    path = os.path.join(cache, f"vignette_{env}.exr")
    u = (np.arange(w) + 0.5) / w
    v = (np.arange(h) + 0.5) / h
    uu, vv = np.meshgrid(u, v)
    dx = np.abs(uu - 0.5) * intensity * 3.0
    dy = np.abs(vv - 0.5) * intensity * 3.0
    vf = np.clip(1.0 - (dx * dx + dy * dy), 0.0, 1.0) ** (smooth * 5.0)
    px = np.ones((h, w, 4), dtype=np.float32)
    for i in range(3):
        px[:, :, i] = color[i] + (1.0 - color[i]) * vf
    img = bpy.data.images.get("VIG_" + env) or bpy.data.images.new("VIG_" + env, w, h, float_buffer=True, alpha=False)
    img.pixels.foreach_set(px.ravel())
    img.filepath_raw = path
    img.file_format = "OPEN_EXR"
    img.save()
    img.colorspace_settings.name = "Non-Color"
    return img


def build(env, params, params_b=None):
    """Compositor for an Env. params_b: optional second profile (keys can blend to it, Stage 3 finale)."""
    sc = env.scene
    ng = bpy.data.node_groups.new("CMP_" + env.name, "CompositorNodeTree")
    sc.compositing_node_group = ng
    try:
        sc.render.compositor_device = "GPU"
    except Exception:
        pass
    ng.interface.new_socket(name="Image", in_out="OUTPUT", socket_type="NodeSocketColor")
    g = G(ng)
    p = params
    rl = g.n("CompositorNodeRLayers")
    rl.scene = sc
    img = rl.outputs["Image"]

    # ---- bloom (URP threshold is gamma-space)
    glare = g.n("CompositorNodeGlare")
    glare.inputs["Type"].default_value = "Bloom"
    glare.inputs["Quality"].default_value = "High"
    ng.links.new(img, glare.inputs["Image"])
    thr = C.srgb_to_lin([p["bloom_threshold"]])[0]
    ng.links.new(g.val("p_bloom_threshold", thr), glare.inputs["Threshold"])
    glare.inputs["Smoothness"].default_value = 0.5
    ng.links.new(g.val("p_bloom_strength", p["bloom"] * BLOOM_GAIN), glare.inputs["Strength"])
    glare.inputs["Size"].default_value = 0.55 + 0.4 * p["bloom_scatter"]
    tint = g.n("CompositorNodeRGB")
    tint.name = tint.label = "p_bloom_tint"
    tint.outputs[0].default_value = tuple(p["bloom_tint"]) + (1.0,)
    ng.links.new(tint.outputs[0], glare.inputs["Tint"])
    img = glare.outputs["Image"]

    # ---- vignette
    if p["vignette"] > 0:
        vimg = g.n("CompositorNodeImage")
        vimg.image = vignette_image(env.name, p["vignette"], p["vignette_smooth"], p["vignette_color"])
        sc_n = g.n("CompositorNodeScale")
        for k, v in (("Type", "Render Size"), ("Frame Type", "Stretch")):
            try:
                sc_n.inputs[k].default_value = v
            except Exception as e:
                C.log("scale node", k, e)
        ng.links.new(vimg.outputs["Image"], sc_n.inputs["Image"])
        mixv = g.n("ShaderNodeMix", data_type="RGBA", blend_type="MIX")
        ins = {s.identifier: s for s in mixv.inputs}
        ng.links.new(g.val("p_vignette", 1.0), ins["Factor_Float"])
        ins["A_Color"].default_value = (1, 1, 1, 1)
        ng.links.new(sc_n.outputs["Image"], ins["B_Color"])
        mult = g.n("ShaderNodeMix", data_type="RGBA", blend_type="MULTIPLY")
        ins2 = {s.identifier: s for s in mult.inputs}
        ins2["Factor_Float"].default_value = 1.0
        ng.links.new(img, ins2["A_Color"])
        ng.links.new([s for s in mixv.outputs if s.identifier == "Result_Color"][0], ins2["B_Color"])
        img = [s for s in mult.outputs if s.identifier == "Result_Color"][0]

    # ---- exposure
    ex = g.n("CompositorNodeExposure")
    ng.links.new(img, ex.inputs["Image"])
    ng.links.new(g.val("p_exposure", p["exposure"]), ex.inputs["Exposure"])
    sep = g.n("CompositorNodeSeparateColor")
    ng.links.new(ex.outputs["Image"], sep.inputs["Image"])
    rgb = [sep.outputs["Red"], sep.outputs["Green"], sep.outputs["Blue"]]

    # ---- white balance (3x3)
    W = wb_matrix(p["temperature"], p["tint"])
    wv = [[g.val(f"p_wb{i}{j}", W[i, j]) for j in range(3)] for i in range(3)]
    rgb = [g.lin3(wv[i], rgb) for i in range(3)]

    # ---- contrast around 0.18 in log space: x' = 0.18 (x / 0.18)^k
    k = 1.0 + p["contrast"] / 100.0
    kk = g.val("p_contrast_k", k)
    km = g.val("p_contrast_mul", 0.18 ** (1.0 - k))
    rgb = [g.m("MULTIPLY", g.m("POWER", g.m("MAXIMUM", c, 0.0), kk), km) for c in rgb]

    # ---- split toning (gamma space)
    gam = [g.m("POWER", c, 1.0 / 2.2) for c in rgb]
    sat_g = [g.m("MINIMUM", c, 1.0) for c in gam]
    luma = g.m("ADD", g.lin3(LUMA, sat_g), g.val("p_split_balance", p["split_balance"] / 100.0), clamp=True)
    inv = g.m("SUBTRACT", 1.0, luma)
    out = []
    for i, ch in enumerate("rgb"):
        sh = g.val(f"p_split_sh_{ch}", p["split_shadows"][i])
        hi = g.val(f"p_split_hi_{ch}", p["split_highlights"][i])
        s_blend = g.m("MULTIPLY_ADD", g.m("SUBTRACT", sh, 0.5), inv, 0.5)
        h_blend = g.m("MULTIPLY_ADD", g.m("SUBTRACT", hi, 0.5), luma, 0.5)
        x = g.soft_light(gam[i], s_blend)
        x = g.soft_light(x, h_blend)
        out.append(g.m("POWER", g.m("MAXIMUM", x, 0.0), 2.2))
    rgb = out

    # ---- saturation (luma based)
    s = g.val("p_saturation", 1.0 + p["saturation"] / 100.0)
    L = g.lin3(LUMA, rgb)
    rgb = [g.m("MULTIPLY_ADD", g.m("SUBTRACT", c, L), s, L) for c in rgb]

    if TONEMAP[0] == "narkowicz":
        # per-channel ACES fit (Narkowicz 2015): highlights run to white like URP's ACES; view transform = Standard
        out = []
        for c in rgb:
            x = g.m("MULTIPLY", g.m("MAXIMUM", c, 0.0), 0.6)
            num = g.m("MULTIPLY", x, g.m("MULTIPLY_ADD", x, 2.51, 0.03))
            den = g.m("MULTIPLY_ADD", x, g.m("MULTIPLY_ADD", x, 2.43, 0.59), 0.14)
            out.append(g.m("DIVIDE", num, den, clamp=True))
        rgb = out
        sc.view_settings.view_transform = "Standard"
    comb = g.n("CompositorNodeCombineColor")
    for i, c in enumerate(rgb):
        ng.links.new(c, comb.inputs[i])
    comb.inputs["Alpha"].default_value = 1.0
    go = g.n("NodeGroupOutput")
    ng.links.new(comb.outputs["Image"], go.inputs[0])
    env.post = params
    env.post_b = params_b
    return ng


BLOOM_GAIN = 0.3   # URP bloom intensity -> Blender Glare strength (calibrated against Docs/Screenshots)
TONEMAP = ["ocio"]  # "ocio" (scene view transform, ACES 1.3) or "narkowicz" (compositor curve + Standard view)


def param_values(p):
    """Flat {node name: value} for keying a blend between two profiles."""
    W = wb_matrix(p["temperature"], p["tint"])
    k = 1.0 + p["contrast"] / 100.0
    d = {
        "p_bloom_threshold": C.srgb_to_lin([p["bloom_threshold"]])[0],
        "p_bloom_strength": p["bloom"] * BLOOM_GAIN,
        "p_exposure": p["exposure"],
        "p_contrast_k": k,
        "p_contrast_mul": 0.18 ** (1.0 - k),
        "p_split_balance": p["split_balance"] / 100.0,
        "p_saturation": 1.0 + p["saturation"] / 100.0,
    }
    for i in range(3):
        for j in range(3):
            d[f"p_wb{i}{j}"] = W[i, j]
    for i, ch in enumerate("rgb"):
        d[f"p_split_sh_{ch}"] = p["split_shadows"][i]
        d[f"p_split_hi_{ch}"] = p["split_highlights"][i]
    return d
