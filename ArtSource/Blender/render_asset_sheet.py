"""Render a labelled contact sheet of the EMBER asset kit.

blender.exe -b <EmberKit.blend> -P render_asset_sheet.py -- --out <png> [--cols 8] [--only A,B] [--size 2400x1500]
"""
import bpy
import math
import os
import sys
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
def _arg(flag, default=None):
    return argv[argv.index(flag) + 1] if flag in argv else default

OUT = _arg("--out", "asset_sheet.png")
COLS = int(_arg("--cols", "8"))
ONLY = set(_arg("--only", "").split(",")) - {""}
W, H = (int(v) for v in _arg("--size", "2400x1500").split("x"))
CELL = 3.0

scene = bpy.context.scene
cols = [c for c in scene.collection.children if (not ONLY or c.name in ONLY)]
for c in scene.collection.children:
    if c not in cols:
        c.hide_render = True

label_mat = bpy.data.materials.new("Label")
bsdf = label_mat.node_tree.nodes.get("Principled BSDF")
bsdf.inputs["Base Color"].default_value = (0.9, 0.85, 0.75, 1)
bsdf.inputs["Emission Color"].default_value = (0.9, 0.85, 0.75, 1)
bsdf.inputs["Emission Strength"].default_value = 0.6

floor_mat = bpy.data.materials.new("Floor")
fb = floor_mat.node_tree.nodes.get("Principled BSDF")
fb.inputs["Base Color"].default_value = (0.05, 0.055, 0.07, 1)
fb.inputs["Roughness"].default_value = 0.9

for i, col in enumerate(cols):
    meshes = [o for o in col.all_objects if o.type == "MESH"]
    if not meshes:
        continue
    bpy.context.view_layer.update()
    pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    size = max((hi - lo).x, (hi - lo).y, (hi - lo).z)
    s = 2.0 / size if size > 0 else 1.0
    pivot = bpy.data.objects.new(col.name + "_Pivot", None)
    col.objects.link(pivot)
    pivot.location = Vector(((lo.x + hi.x) / 2, (lo.y + hi.y) / 2, lo.z))
    bpy.context.view_layer.update()
    for o in [o for o in col.objects if o.parent is None and o is not pivot]:
        mw = o.matrix_world.copy()
        o.parent = pivot
        o.matrix_parent_inverse = pivot.matrix_world.inverted()
        o.matrix_world = mw
    gx, gy = i % COLS, i // COLS
    pivot.location = Vector((gx * CELL, gy * -CELL * 1.25, 0))
    pivot.scale = (s, s, s)
    cu = bpy.data.curves.new(col.name + "_Label", "FONT")
    cu.body = col.name
    cu.size = 0.26
    cu.align_x = "CENTER"
    lab = bpy.data.objects.new(col.name + "_Label", cu)
    lab.data.materials.append(label_mat)
    lab.location = (gx * CELL, gy * -CELL * 1.25 - 1.25, 0.01)
    col.objects.link(lab)

rows = math.ceil(len(cols) / COLS)
cx = (COLS - 1) * CELL / 2
cy = -(rows - 1) * CELL * 1.25 / 2

bpy.ops.mesh.primitive_plane_add(size=1, location=(cx, cy, -0.005))
plane = bpy.context.active_object
plane.scale = (COLS * CELL + 6, rows * CELL * 1.25 + 6, 1)
plane.data.materials.append(floor_mat)

cam_data = bpy.data.cameras.new("SheetCam")
cam_data.type = "ORTHO"
cam_data.ortho_scale = max(COLS * CELL, rows * CELL * 1.25 * W / H) + 1.0
cam = bpy.data.objects.new("SheetCam", cam_data)
scene.collection.objects.link(cam)
tilt = math.radians(float(_arg("--tilt", "55")))
dist = 60
cam.location = (cx, cy - dist * math.cos(tilt), dist * math.sin(tilt))
cam.rotation_euler = (math.radians(90) - tilt, 0, 0)
scene.camera = cam

sun = bpy.data.lights.new("Key", "SUN")
sun.energy = 3.5
sun.angle = math.radians(8)
sun_ob = bpy.data.objects.new("Key", sun)
sun_ob.rotation_euler = (math.radians(50), math.radians(-20), math.radians(-35))
scene.collection.objects.link(sun_ob)
fill = bpy.data.lights.new("Fill", "SUN")
fill.energy = 1.0
fill.color = (0.55, 0.65, 1.0)
fill_ob = bpy.data.objects.new("Fill", fill)
fill_ob.rotation_euler = (math.radians(60), 0, math.radians(150))
scene.collection.objects.link(fill_ob)

world = bpy.data.worlds.new("SheetWorld")
scene.world = world
bg = world.node_tree.nodes.get("Background")
if bg:
    bg.inputs["Color"].default_value = (0.03, 0.035, 0.05, 1)
    bg.inputs["Strength"].default_value = 1.0

engines = [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items]
scene.render.engine = "BLENDER_EEVEE" if "BLENDER_EEVEE" in engines else ("BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in engines else "CYCLES")
if scene.render.engine == "CYCLES":
    scene.cycles.samples = 32
scene.render.resolution_x = W
scene.render.resolution_y = H
scene.render.film_transparent = False
scene.view_settings.view_transform = "AgX" if "AgX" in [v.identifier for v in bpy.types.ColorManagedViewSettings.bl_rna.properties["view_transform"].enum_items] else "Filmic"
scene.render.filepath = OUT
os.makedirs(os.path.dirname(OUT) or ".", exist_ok=True)
bpy.ops.render.render(write_still=True)
print("RENDERED", OUT, "engine", scene.render.engine)
