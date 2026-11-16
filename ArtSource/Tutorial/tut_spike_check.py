"""EMBER tutorial video - checks the recorder's capture test (EMBER.exe -tutorialSpike DIR, see TutorialSpike.cs).

  "<Blender>/5.3/python/bin/python.exe" ArtSource/Tutorial/tut_spike_check.py [--take Build/Tutorial/spike]

Gates (all must pass before the real takes are recorded):
  G1  150 JPEG frames, contiguous, upright (the red marker is top-left)
  G2  game.wav holds exactly frames x rate / 30 sample frames (+-1 frame of audio)
  G3  the sound is not silent (title music before the test quiets it)
  G4  the click starts at 2.000 s +-1 frame and frame 60 is the white one
  G5  the worker-thread JPEG of frame 10 is byte-identical to the main-thread one
  G6  the 0.9 s fade lasts 27 +-1 frames although frames 30..59 took 80 ms longer each
Writes Build/Tutorial/reports/spike.json.
"""
import glob
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np  # noqa: E402

import tut_common as C  # noqa: E402

FRAMES, FADE_FRAME, FLASH_FRAME, MAIN_FRAME = 150, 30, 60, 10


def main():
    a = C.args()
    take = os.path.abspath(a.get("--take", os.path.join(C.BUILD, "spike")))
    tl = C.load_timeline(take)
    rows = C.load_frames(take)
    gates, info = {}, {"take": take, "timeline_frames": tl["frames"], "wall_seconds": tl["wallSeconds"],
                       "capture_fps": round(tl["captureFps"], 2), "size": [tl["width"], tl["height"]]}

    # G1 frames and orientation
    files = sorted(glob.glob(os.path.join(take, "frames", "f_*.jpg")))
    idx = [int(os.path.basename(f)[2:8]) for f in files]
    contiguous = idx == list(range(1, len(idx) + 1))
    first = C.read_image(files[0]) if files else None
    upright = False
    if first is not None:
        h, w = first.shape[:2]
        s = max(4, int(round(48 * h / 1080 * 0.5)))
        tl_px = first[s // 2:s, s // 2:s, :3].reshape(-1, 3).mean(0)
        bl_px = first[h - s:h - s // 2, s // 2:s, :3].reshape(-1, 3).mean(0)
        upright = bool(tl_px[0] > 0.7 and tl_px[1] < 0.3 and tl_px[2] < 0.3 and not (bl_px[0] > 0.7 and bl_px[1] < 0.3))
        info["corner_top_left_rgb"] = [round(float(v), 3) for v in tl_px]
        info["corner_bottom_left_rgb"] = [round(float(v), 3) for v in bl_px]
    gates["G1_frames_upright"] = bool(len(files) == FRAMES and contiguous and upright)
    info["jpeg_count"] = len(files)
    info["jpeg_mb_each"] = round(sum(os.path.getsize(f) for f in files) / max(1, len(files)) / 1e6, 3)

    # G2..G4 audio
    wav = os.path.join(take, "game.wav")
    if os.path.exists(wav):
        rate, x = C.read_wav(wav)
        mono = x.mean(axis=1)
        per_frame = rate / 30.0
        expected = FRAMES * per_frame
        info.update(audio_rate=rate, audio_channels=x.shape[1], audio_frames=len(x), audio_expected=expected,
                    audio_empty_frames=tl.get("audioEmptyFrames", 0))
        gates["G2_audio_length"] = bool(abs(len(x) - expected) <= per_frame)
        level = C.dbfs(C.rms(mono[: int(0.9 * rate)]))
        info["music_rms_dbfs_first_0.9s"] = round(level, 1)
        gates["G3_not_silent"] = bool(level > -45.0)
        win = mono[int(1.5 * rate): int(2.6 * rate)]
        loud = np.nonzero(np.abs(win) > 0.5)[0]
        onset = (1.5 + loud[0] / rate) if len(loud) else -1.0
        info["click_onset_s"] = round(onset, 4)
        white = C.read_image(files[FLASH_FRAME])[..., :3].mean() if len(files) > FLASH_FRAME else 0.0
        before = C.read_image(files[FLASH_FRAME - 1])[..., :3].mean() if len(files) > FLASH_FRAME else 1.0
        info["flash_frame_mean"], info["frame_before_mean"] = round(float(white), 3), round(float(before), 3)
        gates["G4_sync"] = bool(onset >= 0 and abs(onset - FLASH_FRAME / 30.0) <= 1.0 / 30.0 and white > 0.85 and before < 0.5)
    else:
        gates["G2_audio_length"] = gates["G3_not_silent"] = gates["G4_sync"] = False
        info["audio"] = "game.wav missing"

    # G5 thread-safe encoder
    main_jpg = os.path.join(take, "main_%06d.jpg" % (MAIN_FRAME + 1))
    work_jpg = os.path.join(take, "frames", "f_%06d.jpg" % (MAIN_FRAME + 1))
    same = os.path.exists(main_jpg) and os.path.exists(work_jpg) and open(main_jpg, "rb").read() == open(work_jpg, "rb").read()
    gates["G5_worker_jpeg_identical"] = bool(same)

    # G6 fade length in frames under slow frames
    fade = rows["fade"]
    f = np.asarray(rows["f"], dtype=int)
    after = [int(fr) for fr, al in zip(f, fade) if fr >= FADE_FRAME and al >= 0.999]
    length = (after[0] - FADE_FRAME) if after else -1
    info["fade_frames"] = length
    gates["G6_fade_frames"] = bool(after and abs(length - 27) <= 1)

    report = {"gates": gates, "pass": all(gates.values()), "info": info}
    C.ensure_dir(C.REPORTS)
    with open(os.path.join(C.REPORTS, "spike.json"), "w", encoding="utf-8") as fh:
        json.dump(report, fh, indent=1)
    for k, v in gates.items():
        print(("PASS  " if v else "FAIL  ") + k)
    print("INFO", json.dumps(info))
    print("RESULT", "PASS" if report["pass"] else "FAIL")
    sys.exit(0 if report["pass"] else 1)


if __name__ == "__main__":
    main()
