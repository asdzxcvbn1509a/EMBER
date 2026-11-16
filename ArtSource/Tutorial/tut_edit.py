"""EMBER tutorial video - the edit: the take's frames, the caption images and the sound mix in Blender's VSE -> MP4,
plus the Thai subtitles (.srt) and the voice-over script for the team.

  blender -b --factory-startup -noaudio --python-exit-code 1 -P ArtSource/Tutorial/tut_edit.py -- [--take Build/Tutorial/final]
      [--out file.mp4] [--audio mix.wav] [--encode no]

take "final"  -> Deliver/EMBER_Tutorial.mp4, Deliver/EMBER_Tutorial_TH.srt, Deliver/EMBER_Tutorial_Narration_TH.md
other takes   -> Build/Tutorial/EMBER_Tutorial_<take>.mp4 / .srt / _Narration_TH.md
Caption timing comes from the take (timeline.json: the frames each caption was on screen); the images from
render_captions.ps1 (Build/Tutorial/captions/<id>.png, full frame, straight alpha) fade in and out over 6 frames.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402

import tut_common as C  # noqa: E402

FADE = 6


def frames_in(folder):
    out = {}
    if os.path.isdir(folder):
        for f in os.listdir(folder):
            if f.startswith("f_") and f.endswith(".jpg") and os.path.getsize(os.path.join(folder, f)) > 0:
                try:
                    out[int(f[2:-4])] = f
                except ValueError:
                    pass
    return out


def image_sequence(se, name, folder, files, start, end, channel):
    """One image strip for VSE frames start..end; a missing file repeats the previous frame (logged)."""
    have = [f for f in range(start, end + 1) if f in files]
    if not have:
        raise RuntimeError("no frames in " + folder)
    names, last, missing = [], files[have[0]], 0
    for f in range(start, end + 1):
        if f in files:
            last = files[f]
        else:
            missing += 1
        names.append(last)
    st = se.strips.new_image(name, os.path.join(folder, names[0]), channel, start, fit_method="FIT")
    for n in names[1:]:
        st.elements.append(n)
    st.colorspace_settings.name = "sRGB"
    if missing:
        C.log(f"WARNING {name}: {missing} missing frame(s) held")
    return st


def key_alpha(st, keys):
    for f, a in keys:
        st.blend_alpha = a
        st.keyframe_insert("blend_alpha", frame=f)


def outputs(take_name):
    if take_name == "final":
        return (os.path.join(C.DELIVER, "EMBER_Tutorial.mp4"), os.path.join(C.DELIVER, "EMBER_Tutorial_TH.srt"),
                os.path.join(C.DELIVER, "EMBER_Tutorial_Narration_TH.md"))
    base = os.path.join(C.BUILD, "EMBER_Tutorial_" + take_name)
    return base + ".mp4", base + "_TH.srt", base + "_Narration_TH.md"


def build(take, tl, out, audio):
    n = tl["frames"]
    fps = tl.get("fps", 30)
    sc = bpy.context.scene
    sc.name = "Tutorial"
    sc.render.fps, sc.render.fps_base = fps, 1.0
    sc.render.resolution_x, sc.render.resolution_y = tl["width"], tl["height"]
    sc.render.resolution_percentage = 100
    sc.frame_start, sc.frame_end = 1, n
    sc.view_settings.view_transform = "Standard"
    sc.view_settings.look = "None"
    sc.sequencer_colorspace_settings.name = "sRGB"
    sc.render.use_sequencer = True
    sc.render.use_compositing = False
    for ob in list(bpy.data.objects):
        bpy.data.objects.remove(ob)
    se = sc.sequence_editor_create()
    bg = se.strips.new_effect("black", "COLOR", 1, frame_start=1, length=n)
    bg.color = (0.0, 0.0, 0.0)
    folder = os.path.join(take, "frames")
    image_sequence(se, "game", folder, frames_in(folder), 1, n, 2)
    # captions: VSE frame = take frame index + 1
    for i, c in enumerate(tl["captions"]):
        png = os.path.join(C.CAPTION_DIR, c["id"] + ".png")
        if not os.path.exists(png):
            C.log("WARNING caption image missing:", png)
            continue
        on, off = c["on"] + 1, (c["off"] if c["off"] >= 0 else n - 1) + 1
        if off - on < 2:
            continue
        st = se.strips.new_image(c["id"], png, 3 + i % 2, on, fit_method="STRETCH")
        st.duration = off - on
        st.blend_type = "ALPHA_OVER"
        st.colorspace_settings.name = "sRGB"
        f = min(FADE, (off - on) // 3)
        key_alpha(st, [(on, 0.0), (on + f, 1.0), (off - f, 1.0), (off, 0.0)])
    if audio and os.path.exists(audio):
        snd = se.strips.new_sound("mix", audio, 7, 1)
        snd.volume = 1.0
    else:
        C.log("WARNING no sound mix:", audio)
    r = sc.render
    r.image_settings.media_type = "VIDEO"
    r.image_settings.file_format = "FFMPEG"
    r.ffmpeg.format = "MPEG4"
    r.ffmpeg.codec = "H264"
    r.ffmpeg.constant_rate_factor = "HIGH" if tl["width"] >= 1920 else "MEDIUM"
    r.ffmpeg.ffmpeg_preset = "GOOD"
    r.ffmpeg.gopsize = fps
    r.ffmpeg.audio_codec = "AAC"
    r.ffmpeg.audio_bitrate = 256
    r.ffmpeg.audio_channels = "STEREO"
    r.ffmpeg.audio_mixrate = 48000
    r.filepath = out
    r.use_file_extension = False
    return sc


def write_srt(tl, caps, path):
    fps = tl.get("fps", 30)
    lines, k = [], 0
    for c in tl["captions"]:
        if c["id"] not in caps or c["off"] - c["on"] < 2:
            continue
        k += 1
        t0 = (c["on"] + 3) / fps
        t1 = c["off"] / fps
        lines += [str(k), f"{C.tc(t0, True)} --> {C.tc(t1, True)}", C.plain(caps[c["id"]]["text"]), ""]
    with open(path, "w", encoding="utf-8-sig", newline="\r\n") as fh:
        fh.write("\n".join(lines))
    return k


def write_narration(tl, caps, path, video_name):
    fps = tl.get("fps", 30)
    rows = []
    for i, c in enumerate(tl["captions"], 1):
        cap = caps.get(c["id"])
        if cap is None:
            continue
        t0, t1 = c["on"] / fps, c["off"] / fps
        say = (cap.get("say") or C.plain(cap["text"])).replace("\n", " ")
        screen = C.plain(cap["text"]).replace("\n", " / ")
        rows.append(f"| {i} | {C.tc(t0)} | {t1 - t0 - 0.15:.1f} | `voice/{c['id']}.wav` | {screen} | {say} |")
    md = [
        "# บทพากย์ วิดีโอ Tutorial EMBER",
        "",
        f"สร้างจาก take ของ {video_name} อัตโนมัติ (ArtSource/Tutorial/tut_edit.py) เวลาตรงกับวิดีโอฉบับนี้",
        "",
        "## วิธีอัดเสียง",
        "1. อัดทีละบรรทัดเป็นไฟล์ WAV (mono หรือ stereo, 44.1 หรือ 48 kHz) ตั้งชื่อตามคอลัมน์ \"ไฟล์\" แล้ววางไว้ที่ `ArtSource/Tutorial/voice/`",
        "2. พูดให้จบภายใน \"วินาทีที่มี\" ไม่ต้องตัดความเงียบหัวท้าย สคริปต์ตัดให้และปรับความดังให้เท่ากัน",
        "3. รัน `bash Tools/run_tutorial.sh mix` แล้ว `bash Tools/run_tutorial.sh edit` และ `bash Tools/run_tutorial.sh check`",
        "4. ถ้าเสียงยาวเกิน รายงาน `Build/Tutorial/reports/mix_final.json` จะเตือน ให้แก้ค่า `hold` ของบรรทัดนั้นใน `captions.json` แล้วอัดภาพใหม่ (`capture`)",
        "",
        "เสียงพากย์จะเริ่ม 0.15 วินาทีหลังข้อความขึ้นจอ ระหว่างนั้นเสียงเกมและเพลงจะเบาลงเอง",
        "",
        "| # | เวลาเริ่ม | วินาทีที่มี | ไฟล์ | ข้อความบนจอ | บทพากย์ |",
        "| --- | --- | --- | --- | --- | --- |",
    ] + rows
    with open(path, "w", encoding="utf-8") as fh:
        fh.write("\n".join(md) + "\n")
    return len(rows)


def main():
    a = C.args()
    take = C.take_dir(a.get("--take"))
    name = os.path.basename(take)
    tl = C.load_timeline(take)
    caps = C.load_captions()
    mp4, srt, md = outputs(name)
    if a.get("--out"):
        mp4 = os.path.abspath(a["--out"])
    audio = a.get("--audio", os.path.join(C.AUDIO_DIR, f"{name}_mix.wav"))
    C.ensure_dir(os.path.dirname(mp4))
    k = write_srt(tl, caps, srt)
    m = write_narration(tl, caps, md, os.path.basename(mp4))
    C.log(f"{k} subtitles -> {srt}; {m} narration lines -> {md}")
    sc = build(take, tl, mp4, audio)
    if a.get("--encode", "yes") == "no":
        return
    bpy.ops.render.render(animation=True, scene=sc.name)
    C.log("wrote", mp4)


if __name__ == "__main__":
    main()
