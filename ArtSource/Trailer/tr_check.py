"""EMBER trailer - checks: image statistics, A/B contact sheets against the game's screenshots, MP4 probe.

  blender -b --factory-startup -noaudio -P ArtSource/Trailer/tr_check.py -- --mp4 Deliver/EMBER_Trailer.mp4 [--sheet DIR [--cols 5]]
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import bpy  # noqa: E402
import numpy as np  # noqa: E402

import tr_common as C  # noqa: E402


def load_pixels(path, max_w=None):
    """PNG -> float numpy (h, w, 3) display values 0..1 (top row first)."""
    img = bpy.data.images.load(path, check_existing=False)
    img.colorspace_settings.name = "Non-Color"     # raw stored values (already display-referred)
    w, h = img.size
    px = np.empty(w * h * 4, dtype=np.float32)
    img.pixels.foreach_get(px)
    bpy.data.images.remove(img)
    px = px.reshape(h, w, 4)[::-1, :, :3]
    if max_w and w > max_w:
        f = w // max_w
        px = px[: (h // f) * f, : (w // f) * f].reshape(h // f, f, w // f, f, 3).mean(axis=(1, 3))
    return px


def stats(px):
    luma = px @ np.array([0.2126, 0.7152, 0.0722])
    return {"mean": float(luma.mean()), "p50": float(np.median(luma)), "p95": float(np.percentile(luma, 95)),
            "rgb": [round(float(x), 3) for x in px.reshape(-1, 3).mean(0)]}


def save_pixels(px, path):
    h, w, _ = px.shape
    img = bpy.data.images.new(os.path.basename(path), w, h, alpha=False)
    img.colorspace_settings.name = "Non-Color"     # before the pixels: changing it regenerates a generated image
    rgba = np.ones((h, w, 4), dtype=np.float32)
    rgba[:, :, :3] = np.clip(px[::-1], 0, 1)
    img.pixels.foreach_set(rgba.ravel())
    img.update()
    img.filepath_raw = path
    img.file_format = "PNG"
    img.save()
    bpy.data.images.remove(img)


def probe_mp4(path):
    """Frames / fps / size from the movie, sample rate / channels / length / peak from its audio."""
    info = {"path": path, "bytes": os.path.getsize(path)}
    clip = bpy.data.movieclips.load(path)
    info["frames"] = clip.frame_duration
    info["fps"] = round(clip.fps, 3)
    info["size"] = tuple(clip.size)
    try:
        import aud
        snd = aud.Sound(path)
        rate, ch = snd.specs
        data = snd.data()
        info["audio_rate"] = rate
        info["audio_channels"] = ch
        info["audio_seconds"] = round(len(data) / rate, 3)
        pk = float(np.max(np.abs(data))) if len(data) else 0.0
        info["audio_peak_dbfs"] = round(20 * np.log10(pk + 1e-12), 2)
        win = int(rate * 0.5)
        if len(data) > win:
            rms = np.sqrt(np.mean(np.asarray(data, dtype=np.float64)[: len(data) // win * win].reshape(-1, win, ch) ** 2, axis=(1, 2)))
            info["audio_silent_halfsecs"] = int(np.sum(rms < 1e-4))
    except Exception as e:  # noqa
        info["audio_error"] = str(e)
    return info


def main():
    import glob
    import json
    args = C.args_after_dashes()
    opts = {"--mp4": os.path.join(C.ROOT, "Deliver", "EMBER_Trailer.mp4"), "--sheet": "", "--cols": "5"}
    for i in range(0, len(args), 2):
        opts[args[i]] = args[i + 1]
    info = probe_mp4(os.path.abspath(opts["--mp4"]))  # movieclips.load does not resolve cwd-relative paths
    print("PROBE", json.dumps(info))
    with open(os.path.join(C.ensure_dir(C.REPORTS), "mp4_probe.json"), "w", encoding="utf-8") as fh:
        json.dump(info, fh, indent=1)
    if opts["--sheet"]:
        files = sorted(glob.glob(os.path.join(os.path.abspath(opts["--sheet"]), "*.png")))
        n = int(opts["--cols"])
        rows = [files[i:i + n] + [None] * (n - len(files[i:i + n])) for i in range(0, len(files), n)]
        out = os.path.join(C.ensure_dir(C.DOCS_TRAILER), "EMBER_Trailer_contact_sheet.png")
        contact_sheet(rows, out, cell_w=480)
        print("SHEET", out)


def contact_sheet(pairs, path, cell_w=640, label_rows=None):
    """pairs: list of [left_png, right_png] rows (either may be None). Saves one PNG."""
    rows = []
    for row in pairs:
        cells = []
        for p in row:
            if p and os.path.exists(p):
                px = load_pixels(p)
                h, w, _ = px.shape
                f = max(1, w // cell_w)
                px = px[: (h // f) * f, : (w // f) * f].reshape(h // f, f, w // f, f, 3).mean(axis=(1, 3))
                px = px[: cell_w * 9 // 16, :cell_w]
                if px.shape[0] < cell_w * 9 // 16 or px.shape[1] < cell_w:
                    pad = np.zeros((cell_w * 9 // 16, cell_w, 3), dtype=np.float32)
                    pad[: px.shape[0], : px.shape[1]] = px
                    px = pad
            else:
                px = np.zeros((cell_w * 9 // 16, cell_w, 3), dtype=np.float32)
            cells.append(px)
        gap = np.full((cells[0].shape[0], 6, 3), 0.15, dtype=np.float32)
        rows.append(np.concatenate([x for c in cells for x in (c, gap)][:-1], axis=1))
    vgap = np.full((6, rows[0].shape[1], 3), 0.15, dtype=np.float32)
    sheet = np.concatenate([x for r in rows for x in (r, vgap)][:-1], axis=0)
    save_pixels(sheet, path)
    return path


if __name__ == "__main__":
    import sys
    sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
    main()
