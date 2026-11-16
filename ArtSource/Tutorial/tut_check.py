"""EMBER tutorial video - checks after the edit.

  blender -b --factory-startup -noaudio --python-exit-code 1 -P ArtSource/Tutorial/tut_check.py -- [--take Build/Tutorial/final]

  * MP4 probe (frames, fps, size, sound) with ArtSource/Trailer/tr_check.probe_mp4
  * contact sheet every 10 s and one still per caption at its midpoint (take frame + caption image, as in the edit)
      final take -> Docs/Tutorial/EMBER_Tutorial_contact_sheet.jpg, EMBER_Tutorial_captions.jpg; others -> Build/Tutorial/reports
  * overlap: caption boxes against the game HUD (visible frames only, from frames.tsv) and the key panel
  * captions shown over a black screen (unless they are cards), reading speed, subtitle order
Report: Build/Tutorial/reports/check_<take>.json; exits 1 when the video itself is wrong (frames, size, sound).
"""
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
sys.path.insert(1, os.path.join(HERE, "..", "Trailer"))

import numpy as np  # noqa: E402

import tut_common as C  # noqa: E402


def write_jpg(px, path):
    import OpenImageIO as oiio
    h, w = px.shape[:2]
    spec = oiio.ImageSpec(w, h, 3, oiio.UINT8)
    spec.attribute("Compression", "jpeg:90")
    o = oiio.ImageOutput.create(path)
    o.open(path, spec)
    o.write_image((np.clip(px[..., :3], 0, 1) * 255 + 0.5).astype(np.uint8))
    o.close()


def shrink(px, w):
    h0, w0 = px.shape[:2]
    f = max(1, w0 // w)
    return px[: h0 // f * f, : w0 // f * f].reshape(h0 // f, f, w0 // f, f, px.shape[2]).mean(axis=(1, 3))


class Compositor:
    """Take frame + the captions visible on it (with the edit's fades), at the take's resolution."""

    def __init__(self, take, tl):
        self.take, self.tl, self.cache = take, tl, {}

    def caption(self, cid, w, h):
        key = (cid, w)
        if key not in self.cache:
            px = C.read_image(os.path.join(C.CAPTION_DIR, cid + ".png"))
            if px.shape[1] != w:
                px = shrink(px, w)[:h, :w]
            self.cache[key] = px
        return self.cache[key]

    def frame(self, f):
        img = C.read_image(os.path.join(self.take, "frames", "f_%06d.jpg" % (f + 1)))[..., :3]
        h, w = img.shape[:2]
        for c in self.tl["captions"]:
            if c["on"] <= f < c["off"] and os.path.exists(os.path.join(C.CAPTION_DIR, c["id"] + ".png")):
                fade = min(1.0, (f - c["on"] + 1) / 6.0, (c["off"] - f) / 6.0)
                cap = self.caption(c["id"], w, h)
                a = cap[..., 3:4] * fade
                img = img * (1 - a) + cap[..., :3] * a
        return img


def grid(tiles, cols, gap=6):
    th, tw = tiles[0].shape[:2]
    rows = []
    for i in range(0, len(tiles), cols):
        row = tiles[i:i + cols] + [np.zeros_like(tiles[0])] * (cols - len(tiles[i:i + cols]))
        parts = []
        for t in row:
            parts += [t, np.full((th, gap, 3), 0.15, np.float32)]
        rows.append(np.concatenate(parts[:-1], axis=1))
        rows.append(np.full((gap, rows[-1].shape[1], 3), 0.15, np.float32))
    return np.concatenate(rows[:-1], axis=0)


def overlap(a, b):
    return not (a[2] <= b[0] or b[2] <= a[0] or a[3] <= b[1] or b[3] <= a[1])


def main():
    a = C.args()
    take = C.take_dir(a.get("--take"))
    name = os.path.basename(take)
    tl = C.load_timeline(take)
    rows = C.load_frames(take)
    caps = C.load_captions()
    fps, n = tl.get("fps", 30), tl["frames"]
    report = {"take": take, "errors": [], "warnings": []}
    final = name == "final"

    # ---- the MP4
    mp4 = os.path.join(C.DELIVER, "EMBER_Tutorial.mp4") if final else os.path.join(C.BUILD, f"EMBER_Tutorial_{name}.mp4")
    if os.path.exists(mp4):
        import tr_check
        info = tr_check.probe_mp4(os.path.abspath(mp4))
        report["mp4"] = info
        if info.get("frames") != n:
            report["errors"].append(f"MP4 has {info.get('frames')} frames, the take {n}")
        if abs(info.get("fps", 0) - fps) > 0.01:
            report["errors"].append(f"MP4 fps {info.get('fps')}")
        if tuple(info.get("size", (0, 0))) != (tl["width"], tl["height"]):
            report["errors"].append(f"MP4 size {info.get('size')}")
        if info.get("audio_channels") != 2 or abs(info.get("audio_seconds", 0) - n / fps) > 0.1:
            report["errors"].append(f"MP4 sound: {info.get('audio_channels')} ch, {info.get('audio_seconds')} s for {n / fps:.2f} s of video")
        if info.get("audio_peak_dbfs", 0) > -0.9:
            report["warnings"].append(f"sound peak {info.get('audio_peak_dbfs')} dBFS")
    else:
        report["errors"].append("MP4 missing: " + mp4)

    # ---- the take: segments, checks, warnings
    report["segments"] = [f'{s["id"]} {s["result"]} {(s["end"] - s["start"]) / fps:.1f}s' for s in tl["segments"]]
    bad = [s for s in tl["segments"] if s["result"] != "PASS"]
    if bad:
        report["errors"].append("take has failed segments: " + ", ".join(s["id"] for s in bad))
    report["failed_checks"] = [f'{c["seg"]} / {c["label"]}: {c["what"]}' for c in tl["checks"] if not c["pass"]]
    report["take_warnings"] = tl["warnings"]
    report["respawns_unplanned"] = sum(1 for e in tl["events"] if e["t"] == "respawn-unplanned")

    # ---- captions: overlap, black screen, reading speed
    meta_path = os.path.join(C.CAPTION_DIR, "captions_meta.json")
    meta = json.load(open(meta_path, encoding="utf-8")) if os.path.exists(meta_path) else {}
    kx0, ky0, kx1, ky1 = tl.get("keysRect") or [0, 0, 0, 0]
    keys_rect = (kx0, 1080 - ky1, kx1, 1080 - ky0)
    fade = rows["fade"]
    issues = []
    for c in tl["captions"]:
        cid, on, off = c["id"], c["on"], c["off"]
        m = meta.get(cid)
        if m is None:
            issues.append(f"{cid}: no caption image")
            continue
        rect = m["rect"]
        span = range(max(0, on + 6), max(on + 6, off - 6))
        seconds = (off - on) / fps
        cps = C.graphemes(caps[cid]["text"]) / max(seconds, 0.01) if cid in caps else 0
        if cps > 16.5:
            issues.append(f"{cid}: {cps:.1f} characters/s ({seconds:.1f} s on screen)")
        if m["kind"] != "card" and any(fade[f] > 0.5 for f in span if f < len(fade)):
            issues.append(f"{cid}: shown while the screen is faded out")
        scenes = set(rows["scene"][f] for f in span if f < len(rows["scene"]))
        for hud, h in C.HUD.items():
            if not scenes.intersection(h["scenes"]) or not overlap(rect, h["rect"]):
                continue
            col = h["when"]
            visible = [f for f in span if f < len(fade) and rows["scene"][f] in h["scenes"] and (col is None or rows[col][f] > 0.3)]
            if visible:
                issues.append(f"{cid}: overlaps {hud} for {len(visible) / fps:.1f} s")
        if scenes.intersection({"Stage1", "Stage2", "Stage3"}) and overlap(rect, keys_rect) and kx1 > 0:
            issues.append(f"{cid}: overlaps the key panel")
    report["caption_issues"] = issues

    # ---- subtitles
    srt = os.path.join(C.DELIVER, "EMBER_Tutorial_TH.srt") if final else os.path.join(C.BUILD, f"EMBER_Tutorial_{name}_TH.srt")
    if os.path.exists(srt):
        times = []
        for ln in open(srt, encoding="utf-8-sig").read().splitlines():
            if "-->" in ln:
                t = [x.strip().replace(",", ".").split(":") for x in ln.split("-->")]
                times.append(tuple(float(h) * 3600 + float(mi) * 60 + float(s) for h, mi, s in t))
        ok = all(t0 < t1 for t0, t1 in times) and all(times[i][1] <= times[i + 1][0] + 1e-6 for i in range(len(times) - 1))
        ok = ok and (not times or times[-1][1] <= n / fps + 0.05)
        report["srt"] = {"entries": len(times), "ordered_non_overlapping": ok}
        if not ok:
            report["errors"].append("subtitles overlap or run past the end")

    # ---- stills
    comp = Compositor(take, tl)
    out_dir = C.ensure_dir(C.DOCS if final else C.REPORTS)
    tag = "" if final else "_" + name
    step = 10 * fps
    tiles = [shrink(comp.frame(f), 480) for f in range(step // 2, n, step)]
    sheet = os.path.join(out_dir, f"EMBER_Tutorial_contact_sheet{tag}.jpg")
    write_jpg(grid(tiles, 5), sheet)
    tiles = []
    for c in tl["captions"]:
        f = (c["on"] + c["off"]) // 2
        if 0 <= f < n:
            tiles.append(shrink(comp.frame(f), 640))
    capsheet = os.path.join(out_dir, f"EMBER_Tutorial_captions{tag}.jpg")
    if tiles:
        write_jpg(grid(tiles, 4), capsheet)
    report["sheets"] = [sheet, capsheet]

    with open(os.path.join(C.ensure_dir(C.REPORTS), f"check_{name}.json"), "w", encoding="utf-8") as fh:
        json.dump(report, fh, indent=1, ensure_ascii=False)
    print("SEGMENTS", " | ".join(report["segments"]))
    for k in ("errors", "warnings", "caption_issues", "failed_checks", "take_warnings"):
        for x in report.get(k, []):
            print(k.upper()[:-1] if k.endswith("s") else k.upper(), x)
    if "mp4" in report:
        print("PROBE", json.dumps(report["mp4"]))
    print("SHEETS", sheet, capsheet)
    print("RESULT", "FAIL" if report["errors"] else "PASS")
    if report["errors"]:
        sys.exit(1)


if __name__ == "__main__":
    main()
