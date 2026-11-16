"""EMBER trailer - the edit: assemble rendered shots, title cards, dips/flash and the sound mix in the VSE, encode MP4.

  blender -b --factory-startup -noaudio --python-exit-code 1 -P ArtSource/Trailer/tr_edit.py -- --profile animatic|preview|final [--out file.mp4]
      [--stills N]       PNG of every Nth frame of the edit -> Build/Trailer/edit_stills_<profile> (for the contact sheet)
      [--posters yes]    the shots.POSTERS frames -> Docs/Trailer/<name>.png
  With --stills or --posters nothing is encoded.

Shot frames come from Build/Trailer/<profile>/<shot>/, title frames from Build/Trailer/<profile>/titles/<id>/.
The shot PNGs are already display-referred, so this scene uses the Standard view transform (no second tonemap).
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402

import shots as S  # noqa: E402
import tr_common as C  # noqa: E402
import tr_titles  # noqa: E402

SCALE = {"animatic": 25, "preview": 50, "stills": 50, "final": 100}


def frames_in(folder, prefix):
    if not os.path.isdir(folder):
        return {}
    out = {}
    for f in os.listdir(folder):
        if f.startswith(prefix) and f.endswith(".png") and os.path.getsize(os.path.join(folder, f)) > 0:
            try:
                out[int(f[len(prefix):-4])] = f
            except ValueError:
                pass
    return out


def image_strip(se, name, folder, files_by_frame, start, end, channel):
    """One image strip covering start..end; missing frames repeat the previous one (logged)."""
    have = [f for f in range(start, end + 1) if f in files_by_frame]
    if not have:
        C.log(f"MISSING all frames of {name}")
        return None
    names = []
    last = files_by_frame[have[0]]
    missing = 0
    for f in range(start, end + 1):
        if f in files_by_frame:
            last = files_by_frame[f]
        else:
            missing += 1
        names.append(last)
    st = se.strips.new_image(name, os.path.join(folder, names[0]), channel, start, fit_method="FIT")
    for n in names[1:]:
        st.elements.append(n)
    st.colorspace_settings.name = "sRGB"
    if missing:
        C.log(f"{name}: {missing} missing frame(s) held")
    return st


def key_alpha(st, keys):
    for f, a in keys:
        st.blend_alpha = a
        st.keyframe_insert("blend_alpha", frame=f)


def build(profile, out_path, audio):
    sc = bpy.context.scene
    sc.name = "Edit"
    sc.render.fps = S.FPS
    sc.render.resolution_x, sc.render.resolution_y = 1920, 1080
    sc.render.resolution_percentage = SCALE[profile]
    sc.frame_start, sc.frame_end = 1, S.TOTAL
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    sc.sequencer_colorspace_settings.name = "sRGB"
    sc.render.use_sequencer = True
    sc.render.use_compositing = False
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob)
    se = sc.sequence_editor_create()
    base = os.path.join(C.BUILD, profile)
    # black base (channel 1): blend modes above need something to blend with (the cold-open card sits on black)
    bg = se.strips.new_effect("black", "COLOR", 1, frame_start=1, length=S.TOTAL)
    bg.color = (0.0, 0.0, 0.0)
    # shots (channel 2)
    for s in S.SHOTS:
        folder = os.path.join(base, s["id"])
        st = image_strip(se, s["id"], folder, frames_in(folder, s["id"] + "_"), s["start"], s["end"], 2)
        if st is not None:
            st.blend_type = "ALPHA_OVER"
    # title shade (channel 3, multiply) and title cards (channel 4, screen)
    for t in S.TITLES:
        folder = os.path.join(base, "titles", t["id"])
        end = min(t["end"] + (30 if t["style"] == "logo" else 0), S.TOTAL)
        where = tr_titles.LAYOUT.get(t["id"], {}).get("shade")
        if where:
            sh = se.strips.new_image(t["id"] + "_shade", tr_titles.shade_image(where), 3, t["start"], fit_method="STRETCH")
            sh.duration = end - t["start"] + 1
            sh.blend_type = "MULTIPLY"
            key_alpha(sh, [(t["start"], 0.0), (t["start"] + 12, 1.0), (end - 12, 1.0), (end, 0.0)])
        st = image_strip(se, t["id"], folder, frames_in(folder, t["id"] + "_"), t["start"], end, 4)
        if st is not None:
            st.blend_type = "SCREEN"
    # dips, flash, fade out (channel 6, colour strips over everything)
    for i, fd in enumerate(S.FADES):
        color = fd["color"]
        if fd["kind"] == "dip":
            a, L = fd["at"], fd["length"]
            st = se.strips.new_effect(f"dip{i}", "COLOR", 6, frame_start=a - L, length=2 * L + 1)
            keys = [(a - L, 0.0), (a, 1.0), (a + L, 0.0)]
        elif fd["kind"] == "flash":
            a, L = fd["at"], fd["length"]
            st = se.strips.new_effect(f"flash{i}", "COLOR", 6, frame_start=a - L // 2, length=L + 1)
            keys = [(a - L // 2, 0.0), (a - 2, 1.0), (a + 2, 1.0), (a + L // 2, 0.0)]
        elif fd["kind"] == "out":
            st = se.strips.new_effect(f"out{i}", "COLOR", 6, frame_start=fd["start"], length=fd["length"] + 1)
            keys = [(fd["start"], 0.0), (fd["start"] + fd["length"], 1.0)]
        else:
            continue
        st.color = color
        st.blend_type = "ALPHA_OVER"
        key_alpha(st, keys)
    # sound
    if audio and os.path.exists(audio):
        snd = se.strips.new_sound("mix", audio, 7, 1)
        snd.volume = 1.0
    else:
        C.log("WARNING no audio", audio)
    # output
    r = sc.render
    r.image_settings.media_type = "VIDEO"
    r.image_settings.file_format = "FFMPEG"
    r.ffmpeg.format = "MPEG4"
    r.ffmpeg.codec = "H264"
    r.ffmpeg.constant_rate_factor = "HIGH" if profile == "final" else "MEDIUM"
    r.ffmpeg.ffmpeg_preset = "GOOD"
    r.ffmpeg.gopsize = 12
    r.ffmpeg.audio_codec = "AAC"
    r.ffmpeg.audio_bitrate = 320
    r.ffmpeg.audio_channels = "STEREO"
    r.ffmpeg.audio_mixrate = 48000
    r.filepath = out_path
    r.use_file_extension = False
    return sc


def main():
    args = C.args_after_dashes()
    opts = {"--profile": "animatic", "--out": "", "--audio": os.path.join(C.BUILD, "audio", "trailer_mix.wav"), "--stills": "",
            "--posters": ""}
    for i in range(0, len(args), 2):
        opts[args[i]] = args[i + 1]
    profile = opts["--profile"]
    out = opts["--out"] or os.path.join(C.BUILD, f"EMBER_Trailer_{profile}.mp4")
    out = os.path.abspath(out)
    C.ensure_dir(os.path.dirname(out))
    sc = build(profile, out, opts["--audio"])
    if opts["--stills"] or opts["--posters"]:
        # frames of the finished edit (titles, dips and flash included)
        stills = []
        if opts["--stills"]:
            step = int(opts["--stills"])
            folder = C.ensure_dir(os.path.join(C.BUILD, f"edit_stills_{profile}"))
            stills += [(os.path.join(folder, f"edit_{f:04d}.png"), f) for f in range(step // 2, S.TOTAL + 1, step)]
        if opts["--posters"]:
            folder = C.ensure_dir(C.DOCS_TRAILER)
            stills += [(os.path.join(folder, name + ".png"), f) for name, f in S.POSTERS]
        sc.render.image_settings.media_type = "IMAGE"
        sc.render.image_settings.file_format = "PNG"
        for path, f in stills:
            sc.frame_set(f)
            sc.render.filepath = path
            bpy.ops.render.render(write_still=True, scene=sc.name)
        C.log(f"{len(stills)} still(s) of the edit")
        return
    bpy.ops.render.render(animation=True, scene=sc.name)
    C.log("wrote", out)


if __name__ == "__main__":
    main()
