"""EMBER trailer - render shots from ArtSource/Trailer/EMBER_Trailer.blend.

  blender -b ArtSource/Trailer/EMBER_Trailer.blend -noaudio --python-exit-code 1 -P ArtSource/Trailer/tr_render.py -- \
      --profile animatic|preview|final|stills [--shots S01,S02 | --envs Stage1] [--frames 330,400] [--every N]

Frames go to Build/Trailer/<profile>/<shot>/<shot>_####.png (#### = global trailer frame). Existing frames are kept
(use_overwrite off, placeholders on), so an interrupted run resumes; zero-byte placeholders left by a crash are removed first.
"""
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402

import shots as S  # noqa: E402
import tr_common as C  # noqa: E402

PROFILES = {
    #            scale  samples  motion blur  dof
    "animatic": (25, 2, False, False),
    "preview": (50, 16, True, True),
    "stills": (50, 32, True, True),
    "final": (100, 64, True, True),       # 64: dithered mist / cloud sea converge (benchmark ~1-2 s/frame at 32)
}


def configure(sc, profile):
    scale, samples, mb, dof = PROFILES[profile]
    sc.render.resolution_percentage = scale
    sc.eevee.taa_render_samples = samples
    sc.render.use_motion_blur = mb
    sc.render.motion_blur_shutter = 0.5
    sc.render.use_overwrite = False
    sc.render.use_placeholder = True
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGB"
    sc.render.image_settings.color_depth = "8"
    sc.render.use_persistent_data = True
    for ob in sc.objects:
        if ob.type == "CAMERA":
            ob.data.dof.use_dof = ob.data.dof.use_dof and dof


def clean_placeholders(folder):
    if not os.path.isdir(folder):
        return 0
    n = 0
    for f in os.listdir(folder):
        p = os.path.join(folder, f)
        if f.endswith(".png") and os.path.getsize(p) == 0:
            os.remove(p)
            n += 1
    return n


def render_shot(shot, profile, frames=None):
    sc = bpy.data.scenes[shot["env"]]
    cam = bpy.data.objects.get("CAM_" + shot["id"])
    if cam is None:
        C.log("no camera for", shot["id"])
        return
    if bpy.context.window is not None:
        bpy.context.window.scene = sc        # frame_set only evaluates animation for the window's scene
    configure(sc, profile)
    sc.camera = cam
    out = C.ensure_dir(os.path.join(C.BUILD, profile, shot["id"]))
    removed = clean_placeholders(out)
    if removed:
        C.log(f"{shot['id']}: removed {removed} placeholder(s)")
    t0 = time.time()
    if frames:
        for f in frames:
            sc.frame_set(f)
            sc.render.filepath = os.path.join(out, f"{shot['id']}_{f:04d}.png")
            bpy.ops.render.render(write_still=True, scene=sc.name)
        n = len(frames)
    else:
        sc.frame_start, sc.frame_end = shot["start"], shot["end"]
        sc.render.filepath = os.path.join(out, f"{shot['id']}_")
        bpy.ops.render.render(animation=True, scene=sc.name)
        n = shot["end"] - shot["start"] + 1
    dt = time.time() - t0
    C.log(f"{shot['id']} [{profile}] {n} frames in {dt:.1f}s ({dt / max(n, 1):.2f}s/frame)")
    with open(os.path.join(C.ensure_dir(C.REPORTS), "render_times.csv"), "a", encoding="utf-8") as fh:
        fh.write(f"{time.strftime('%Y-%m-%d %H:%M')},{profile},{shot['id']},{n},{dt:.1f}\n")


def render_titles(profile):
    """Title cards from the Titles scene, each over its own frame range."""
    sc = bpy.data.scenes.get("Titles")
    if sc is None:
        C.log("no Titles scene")
        return
    if bpy.context.window is not None:
        bpy.context.window.scene = sc
    scale = PROFILES[profile][0]
    sc.render.resolution_percentage = scale
    sc.eevee.taa_render_samples = 8 if profile == "animatic" else 32
    sc.render.use_overwrite = False
    sc.render.use_placeholder = True
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGB"
    t0 = time.time()
    for t in S.TITLES:
        out = C.ensure_dir(os.path.join(C.BUILD, profile, "titles", t["id"]))
        clean_placeholders(out)
        sc.frame_start = t["start"]
        sc.frame_end = min(t["end"] + (30 if t["style"] == "logo" else 0), S.TOTAL)
        sc.render.filepath = os.path.join(out, t["id"] + "_")
        bpy.ops.render.render(animation=True, scene=sc.name)
    C.log(f"titles [{profile}] in {time.time() - t0:.1f}s")


def main():
    args = C.args_after_dashes()
    opts = {"--profile": "animatic", "--shots": "", "--envs": "", "--frames": "", "--every": "", "--sample": "", "--titles": ""}
    for i in range(0, len(args), 2):
        opts[args[i]] = args[i + 1]
    profile = opts["--profile"]
    if opts["--titles"]:
        render_titles(profile)
        if opts["--titles"] == "only":
            return
    shots = S.SHOTS
    if opts["--shots"]:
        want = opts["--shots"].split(",")
        shots = [s for s in S.SHOTS if s["id"] in want]
    elif opts["--envs"]:
        want = opts["--envs"].split(",")
        shots = [s for s in S.SHOTS if s["env"] in want]
    for s in shots:
        frames = None
        if opts["--frames"]:
            frames = [int(f) for f in opts["--frames"].split(",") if s["start"] <= int(f) <= s["end"]]
            if not frames:
                continue
        elif opts["--every"]:
            k = int(opts["--every"])
            frames = list(range(s["start"], s["end"] + 1, k))
        elif opts.get("--sample"):
            k = int(opts["--sample"])
            span = s["end"] - s["start"]
            frames = sorted({s["start"] + int(round(span * (i + 0.5) / k)) for i in range(k)})
        render_shot(s, profile, frames)


if __name__ == "__main__":
    main()
