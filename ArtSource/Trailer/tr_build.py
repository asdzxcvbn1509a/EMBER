"""EMBER trailer - build entry point.

  blender -b --factory-startup -noaudio --python-exit-code 1 -P ArtSource/Trailer/tr_build.py -- [options]

Options:
  --envs Title,Stage1,...   environments to build (default: all)
  --lookdev DIR             render a still from every LevelBeat hero camera (+ the Title/Ending main camera)
  --scale PCT               render scale for --lookdev (default 50)
  --samples N               EEVEE samples for --lookdev (default 32)
  --time SEC                scene time of the look-dev stills (default 2.0)
  --save                    save ArtSource/Trailer/EMBER_Trailer.blend
  --shots                   add the trailer shots (tr_anim) and the Titles / Edit scenes
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402

import tr_common as C  # noqa: E402
import tr_env  # noqa: E402
import tr_fx  # noqa: E402
import tr_post  # noqa: E402


def main_volume(env):
    """Global volumes sorted by (weight > 0, priority): first = active look, second = blend target (Stage 3 dawn)."""
    vols = [v for (n, v) in env.volumes if v.get("global", True)]
    vols.sort(key=lambda v: (v.get("weight", 1.0) > 0, v.get("priority", 0)), reverse=True)
    return vols


def make_current(scene):
    """frame_set only evaluates animation for the window's scene (background mode still has one window)."""
    if bpy.context.window is not None:
        bpy.context.window.scene = scene


def build_env(name, fx_window=(0.0, 12.0), shots=False):
    env = tr_env.Env(name)
    make_current(env.scene)
    env.build()
    vols = main_volume(env)
    a = tr_post.profile_params(vols[0]) if vols else None
    b = tr_post.profile_params(vols[1]) if len(vols) > 1 else None
    if a:
        tr_post.build(env, a, b)
    env.validate()
    if shots:
        import tr_shots
        env.director = tr_shots.direct(env)       # animation, triggers, cameras, particles
    else:
        tr_fx.build_static(env, fx_window[0], fx_window[1])
    return env


def dof_for(cam, focus, params):
    """Unity bokeh DOF (focal length mm, f-number) -> Blender camera with its own lens: same blur size."""
    cd = cam.data
    cd.dof.use_dof = True
    cd.dof.focus_distance = focus
    if params:
        cd.dof.aperture_fstop = max(0.5, params["dof_aperture"] * (cd.lens / params["dof_focal"]) ** 2)
    else:
        cd.dof.aperture_fstop = 4.0


def lookdev(env, out_dir, scale, samples, t):
    sc = env.scene
    sc.render.resolution_percentage = scale
    sc.eevee.taa_render_samples = samples
    frame = int(round(t * C.FPS)) + 1
    sc.frame_set(frame)
    proxy = env.camera_proxy()
    for name, cam in sorted(env.cameras.items()):
        sc.camera = cam
        proxy.location = cam.matrix_world.to_translation()
        # LevelBeat hero shots focus where the shot looks (pos + shotLookOffset)
        dof_for(cam, float(cam.get("look_dist", 13.3)), getattr(env, "post", None))
        sc.render.filepath = os.path.join(out_dir, f"{env.name}_{name.replace('CAM_beat_', '').replace('CAM_', '')}.png")
        bpy.ops.render.render(write_still=True, scene=sc.name)
        C.log("rendered", sc.render.filepath)


def main():
    args = C.args_after_dashes()
    opts = {"--envs": ",".join(C.ENVS), "--lookdev": None, "--scale": "50", "--samples": "32", "--time": "2.0"}
    flags = set()
    i = 0
    while i < len(args):
        a = args[i]
        if a in ("--save", "--shots"):
            flags.add(a)
            i += 1
        else:
            opts[a] = args[i + 1]
            i += 2
    # start from an empty file (factory startup has a cube, a light and a camera)
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob)
    envs = {}
    for name in opts["--envs"].split(","):
        envs[name] = build_env(name, shots="--shots" in flags and name != "Ending")
    if "--shots" in flags:
        import tr_titles
        tr_titles.build()
    if opts["--lookdev"]:
        out = C.ensure_dir(os.path.abspath(opts["--lookdev"]))    # Blender resolves relative render paths oddly
        for env in envs.values():
            lookdev(env, out, int(opts["--scale"]), int(opts["--samples"]), float(opts["--time"]))
    if "--save" in flags:
        default = bpy.data.scenes.get("Scene")
        if default and len(bpy.data.scenes) > 1:
            bpy.data.scenes.remove(default)
        bpy.ops.wm.save_as_mainfile(filepath=C.BLEND, compress=True)
        C.log("saved", C.BLEND)


if __name__ == "__main__":
    main()
