"""EMBER trailer - title cards (scene "Titles"), rendered on black and laid over the footage in the VSE (SCREEN).

English lines use the game's Futura Bold with wide tracking like the in-game title cards (amber title, pale
subtitle). The Thai title is the WPF-shaped PNG from render_thai_title.ps1 on an emission plane.
"""
import os

import bpy

import shots as S
import tr_common as C
import tr_env

PX = 100.0                       # pixels per Blender unit (orthographic camera, 1920 px = 19.2 units)
IVORY = (1.0, 0.9, 0.76)
AMBER = (1.0, 0.6, 0.26)
PALE = (0.82, 0.83, 0.9)
GOLD = (1.0, 0.78, 0.46)
THAI_PNG = os.path.join(C.HERE, "assets", "thai_title.png")

# per-title placement (screen units from the centre; +y is up) and an optional dark band behind the text
# ("top" / "bottom") so the light letters still read over bright footage
LAYOUT = {
    "T_cold1": dict(y=0.0),
    "T_cold2": dict(y=3.7, shade="top"),
    "T_act1": dict(shade="bottom"),
    "T_act2": dict(shade="bottom"),
    "T_act3": dict(shade="bottom"),
    "T_light": dict(y=-3.3, shade="bottom"),
    "T_dawn": dict(y=-3.3, shade="bottom"),
    "T_logo": dict(shade="bottom"),
}


def emission_material(name, color, strength=1.6):
    m = bpy.data.materials.new(name)
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Color"].default_value = tuple(color) + (1.0,)
    em.inputs["Strength"].default_value = strength
    nt.links.new(em.outputs[0], out.inputs["Surface"])
    return m, em


def text(sc, coll, name, body, size, spacing, color, x=0.0, y=0.0):
    cu = bpy.data.curves.new(name, "FONT")
    cu.body = body
    cu.font = tr_env.futura()
    cu.size = size
    cu.space_character = spacing
    cu.align_x = "CENTER"
    cu.align_y = "CENTER"
    ob = bpy.data.objects.new(name, cu)
    coll.objects.link(ob)
    ob.location = (x, y, 0.0)
    m, em = emission_material("M_" + name, color)
    cu.materials.append(m)
    return ob, m, em


def thai_plane(coll, name, width, color, x=0.0, y=0.0):
    img = bpy.data.images.load(THAI_PNG, check_existing=True)
    img.alpha_mode = "STRAIGHT"
    w, h = img.size
    height = width * h / w
    me = bpy.data.meshes.new(name)
    me.from_pydata([(-width / 2, -height / 2, 0), (width / 2, -height / 2, 0), (width / 2, height / 2, 0), (-width / 2, height / 2, 0)],
                   [], [(0, 1, 2, 3)])
    uv = me.uv_layers.new(name="UVMap")
    uv.data.foreach_set("uv", [0, 0, 1, 0, 1, 1, 0, 1])
    m = bpy.data.materials.new("M_" + name)
    nt = m.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.extension = "CLIP"
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Color"].default_value = tuple(color) + (1.0,)
    em.inputs["Strength"].default_value = 1.6
    tr = nt.nodes.new("ShaderNodeBsdfTransparent")
    mix = nt.nodes.new("ShaderNodeMixShader")
    nt.links.new(tex.outputs["Alpha"], mix.inputs[0])
    nt.links.new(tr.outputs[0], mix.inputs[1])
    nt.links.new(em.outputs[0], mix.inputs[2])
    nt.links.new(mix.outputs[0], out.inputs["Surface"])
    m.surface_render_method = "BLENDED"
    me.materials.append(m)
    ob = bpy.data.objects.new(name, me)
    coll.objects.link(ob)
    ob.location = (x, y, 0.0)
    return ob, m, em


def fade(m, em, start, end, delay=0, length=10, peak=1.6):
    """Emission strength: 0 -> peak -> 0, keyed on the material's node tree."""
    a = start + delay
    frames = [start - 1, a, a + length, end - length, end]
    vals = [0.0, 0.0, peak, peak, 0.0]
    if end - length <= a + length:
        mid = (a + end) // 2
        frames = [start - 1, a, mid, end]
        vals = [0.0, 0.0, peak, 0.0]
    C.set_keys(m.node_tree, f'nodes["{em.name}"].inputs["Strength"].default_value', frames, vals, interp="BEZIER")


def drift(ob, start, end, scale0=1.0, scale1=1.04):
    C.set_vec_keys(ob, "scale", [start, end], [(scale0,) * 3, (scale1,) * 3])


def only_during(obs, first, last):
    """Render a card's objects only while the card is up. Every card sits on the same z = 0 plane, and a faded-out
    card (emission 0, so opaque black letters) would otherwise cut into the live card wherever their letters overlap."""
    for ob in obs:
        for f, hide in ((0, True), (first - 1, False), (last + 1, True)):
            ob.hide_render = hide
            ob.keyframe_insert("hide_render", frame=f)
        for k in C.fcurve(ob, "hide_render").keyframe_points:
            k.interpolation = "CONSTANT"


def build():
    sc = bpy.data.scenes.new("Titles")
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.fps = C.FPS
    sc.render.resolution_x, sc.render.resolution_y = 1920, 1080
    sc.render.resolution_percentage = 100
    sc.render.film_transparent = False
    sc.eevee.taa_render_samples = 16
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    sc.frame_start, sc.frame_end = 1, S.TOTAL
    w = bpy.data.worlds.new("World_Titles")
    w.node_tree.nodes["Background"].inputs["Color"].default_value = (0, 0, 0, 1)
    sc.world = w
    coll = bpy.data.collections.new("TITLES")
    sc.collection.children.link(coll)
    cd = bpy.data.cameras.new("CAM_Titles")
    cd.type = "ORTHO"
    cd.ortho_scale = 1920 / PX
    cam = bpy.data.objects.new("CAM_Titles", cd)
    coll.objects.link(cam)
    cam.location = (0, 0, 10)
    sc.camera = cam
    for t in S.TITLES:
        lay = LAYOUT.get(t["id"], {})
        start, end = t["start"], t["end"]
        if t["style"] == "card":
            y = lay.get("y", -3.3)
            ob, m, em = text(sc, coll, t["id"], t["lines"][0], 0.62, 1.55, IVORY, y=y)
            fade(m, em, start, end, length=12)
            drift(ob, start, end)
            obs = [ob]
        elif t["style"] == "stage":
            ob, m, em = text(sc, coll, t["id"] + "_a", t["lines"][0], 0.6, 1.7, AMBER, y=-2.85)
            fade(m, em, start, end, length=10)
            drift(ob, start, end, 1.0, 1.03)
            ob2, m2, em2 = text(sc, coll, t["id"] + "_b", t["lines"][1], 0.3, 2.0, PALE, y=-3.6)
            fade(m2, em2, start, end, delay=7, length=10, peak=1.3)
            drift(ob2, start, end, 1.0, 1.03)
            obs = [ob, ob2]
        elif t["style"] == "logo":
            end += 30                                   # the end card runs on under the fade to black
            ob, m, em = text(sc, coll, t["id"] + "_a", t["lines"][0], 0.42, 2.4, IVORY, y=-3.05)
            fade(m, em, start, end, length=14)
            ob2, m2, em2 = thai_plane(coll, t["id"] + "_th", 3.2, GOLD, y=-3.95)
            fade(m2, em2, start, end, delay=10, length=16)
            obs = [ob, ob2]
        else:
            continue
        only_during(obs, start, end)
    # soft bloom so the letters glow a little, like the game's UI
    ng = bpy.data.node_groups.new("CMP_Titles", "CompositorNodeTree")
    sc.compositing_node_group = ng
    ng.interface.new_socket(name="Image", in_out="OUTPUT", socket_type="NodeSocketColor")
    rl = ng.nodes.new("CompositorNodeRLayers")
    rl.scene = sc
    gl = ng.nodes.new("CompositorNodeGlare")
    gl.inputs["Type"].default_value = "Bloom"
    gl.inputs["Quality"].default_value = "High"
    gl.inputs["Threshold"].default_value = 0.7
    gl.inputs["Strength"].default_value = 0.45
    gl.inputs["Size"].default_value = 0.6
    go = ng.nodes.new("NodeGroupOutput")
    ng.links.new(rl.outputs["Image"], gl.inputs["Image"])
    ng.links.new(gl.outputs["Image"], go.inputs[0])
    C.log(f"Titles: {len(S.TITLES)} cards")
    return sc


def shade_image(where="bottom"):
    """Multiply mask that darkens the band behind a card over bright footage (VSE MULTIPLY strip)."""
    import numpy as np
    path = os.path.join(C.ensure_dir(os.path.join(C.BUILD, "cache")), f"title_shade_{where}.png")
    w, h = 960, 540
    y = (np.arange(h) + 0.5) / h
    x = (np.arange(w) + 0.5) / w
    yy, xx = np.meshgrid(y, x, indexing="ij")
    # dark ellipse on the text line; Blender stores pixel rows bottom -> top, so the lower third sits at 0.19
    cy = 0.19 if where == "bottom" else 0.84
    d = ((xx - 0.5) / 0.42) ** 2 + ((yy - cy) / 0.13) ** 2
    m = 1.0 - 0.55 * np.exp(-d * 1.6)
    img = bpy.data.images.new("title_shade_" + where, w, h, alpha=False)
    img.colorspace_settings.name = "sRGB"
    px = np.ones((h, w, 4), dtype=np.float32)
    px[:, :, 0] = px[:, :, 1] = px[:, :, 2] = m
    img.pixels.foreach_set(px.ravel())
    img.update()
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    return path
