"""EMBER tutorial video - the soundtrack: the game's own sound from the take, a quiet bed of the title music, and the
team's voice-over when its files exist.

  "<Blender>/5.3/python/bin/python.exe" ArtSource/Tutorial/tut_mix.py [--take Build/Tutorial/final]

  * game.wav (recorded in sync by the game) is muted inside black holds (timeline "holds", 0.15 s ramps)
  * mus_title.wav loops underneath in phase with the title screen's own music: silent while the title and ending
    play their music, -20 dB during gameplay, -12 dB over black holds
  * voice/<caption id>.wav (any WAV: mono/stereo, 44.1/48 kHz, 16/24-bit/float) starts 0.15 s after its caption
    appears; game sound and music duck by 9 dB under it; a voice longer than its caption is reported
  * level towards -19 dBFS RMS (at most +6 dB), soft limiter, peak -1.5 dBFS, 16-bit stereo at the take's sample rate -> Build/Tutorial/audio/<take>_mix.wav
Report: Build/Tutorial/reports/mix_<take>.json
"""
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np  # noqa: E402

import tut_common as C  # noqa: E402

BED_GAME_DB, BED_HOLD_DB, DUCK_DB = -20.0, -12.0, -9.0
TARGET_RMS_DB, MAX_GAIN_DB = -19.0, 6.0
VOICE_RMS_DB, VOICE_DELAY = -19.0, 0.15


def db(v):
    return 10.0 ** (v / 20.0)


def ramp_envelope(per_frame, rate, fps, ramp_s):
    """Per-video-frame gains -> per-sample gains, smoothed with a moving average of ramp_s seconds."""
    spf = rate / fps
    n = int(round(len(per_frame) * spf))
    idx = np.minimum((np.arange(n) / spf).astype(np.int64), len(per_frame) - 1)
    g = per_frame[idx].astype(np.float64)
    k = max(1, int(ramp_s * rate))
    c = np.cumsum(np.concatenate([[0.0], g]))
    sm = (c[k:] - c[:-k]) / k
    out = np.concatenate([np.full(k // 2, g[0]), sm, np.full(n - len(sm) - k // 2, g[-1])])[:n]
    return out.astype(np.float32)


def main():
    a = C.args()
    take = C.take_dir(a.get("--take"))
    name = os.path.basename(take)
    tl = C.load_timeline(take)
    rows = C.load_frames(take)
    caps = C.load_captions()
    fps = tl.get("fps", 30)
    frames = tl["frames"]
    report = {"take": take, "frames": frames, "warnings": []}

    wav = os.path.join(take, "game.wav")
    if os.path.exists(wav):
        rate, game = C.read_wav(wav)
        if game.shape[1] == 1:
            game = np.repeat(game, 2, axis=1)
        game = game[:, :2]
    else:
        rate, game = 48000, np.zeros((0, 2), np.float32)
        report["warnings"].append("no game.wav (dry run?): game sound is silent")
    n = int(round(frames * rate / fps))
    report["game_samples"], report["expected_samples"] = len(game), n
    if abs(len(game) - n) > rate / fps and len(game):
        report["warnings"].append(f"game.wav has {len(game)} sample frames, the picture needs {n}")
    game = np.concatenate([game, np.zeros((max(0, n - len(game)), 2), np.float32)])[:n]

    # ---- per video frame: hold flags and the music bed level
    hold = np.zeros(frames, np.float32)
    for h in tl["holds"]:
        hold[max(0, h["start"]):max(0, h["end"] if h["end"] >= 0 else frames)] = 1.0
    scene = rows["scene"][:frames] + [rows["scene"][-1]] * max(0, frames - len(rows["scene"]))
    own_music = np.array([s in ("Title", "Ending") for s in scene], np.float32)
    bed_gain = np.where(hold > 0, db(BED_HOLD_DB), np.where(own_music > 0, 0.0, db(BED_GAME_DB))).astype(np.float32)
    game_env = ramp_envelope(1.0 - hold, rate, fps, 0.15)
    bed_env = ramp_envelope(bed_gain, rate, fps, 0.8)

    # ---- music bed: the title theme looped from the first frame (the title screen starts it at the same moment)
    sr, mus = C.read_wav(os.path.join(C.GAME_AUDIO, "mus_title.wav"))
    mus = C.resample(mus.mean(axis=1, keepdims=True), sr, rate)
    reps = int(np.ceil(n / max(1, len(mus))))
    bed = np.tile(mus, (reps, 2))[:n] * 0.55   # the title scene plays it at volume 0.55

    # ---- voice-over
    voice = np.zeros((n, 2), np.float32)
    active = np.zeros(n, np.float32)
    placed = []
    script_caps = {c["id"]: c for c in tl["captions"]}
    if os.path.isdir(C.VOICE_DIR):
        for f in sorted(os.listdir(C.VOICE_DIR)):
            if not f.lower().endswith(".wav"):
                continue
            cid = f[:-4]
            if cid not in script_caps:
                report["warnings"].append(f"voice/{f}: no caption '{cid}' in this take")
                continue
            vr, v = C.read_wav(os.path.join(C.VOICE_DIR, f))
            v = C.resample(v.mean(axis=1, keepdims=True), vr, rate)[:, 0]
            # trim silence at both ends, then level to the voice target
            loud = np.nonzero(np.abs(v) > 0.02)[0]
            if len(loud) == 0:
                report["warnings"].append(f"voice/{f}: silent")
                continue
            v = v[max(0, loud[0] - int(0.03 * rate)): loud[-1] + int(0.08 * rate)]
            v *= db(VOICE_RMS_DB) / max(C.rms(v), 1e-6)
            c = script_caps[cid]
            start = int((c["on"] / fps + VOICE_DELAY) * rate)
            slot = (c["off"] - c["on"]) / fps - VOICE_DELAY
            seconds = len(v) / rate
            if seconds > slot:
                report["warnings"].append(f"voice/{f}: {seconds:.1f} s but the caption is on screen for {slot:.1f} s")
            end = min(n, start + len(v))
            voice[start:end] += v[: end - start, None]
            active[start:end] = 1.0
            placed.append({"id": cid, "start_s": round(start / rate, 2), "seconds": round(seconds, 2), "slot_s": round(slot, 2)})
    report["voices"] = placed

    # ducking under the voice: per video frame (a frame with any voice ducks), smoothed over 0.3 s
    duck = np.ones(n, np.float32)
    if placed:
        spf = rate / fps
        per_frame = np.ones(frames, np.float32)
        for i in range(frames):
            a0, a1 = int(i * spf), int((i + 1) * spf)
            if active[a0:a1].any():
                per_frame[max(0, i - 3):i + 4] = db(DUCK_DB)   # a little ahead and after the words
        duck = ramp_envelope(per_frame, rate, fps, 0.3)

    mix = game * (game_env * duck)[:, None] + bed * (bed_env * duck)[:, None] + voice
    # fade in / out at the ends of the video
    fi, fo = int(0.25 * rate), int(1.2 * rate)
    mix[:fi] *= np.linspace(0, 1, fi, dtype=np.float32)[:, None]
    mix[-fo:] *= np.linspace(1, 0, fo, dtype=np.float32)[:, None]

    # loudness: bring the programme up towards -19 dBFS RMS (at most +6 dB); the limiter takes the peaks
    level = C.rms(mix)
    gain = min(db(MAX_GAIN_DB), db(TARGET_RMS_DB) / max(level, 1e-6))
    if gain > 1.0:
        mix *= gain
    report["gain_db"] = round(C.dbfs(max(gain, 1.0)), 2)
    # soft limiter above 0.8, then peak at -1.5 dBFS at most (AAC encoding adds a few tenths of a dB)
    x = np.abs(mix)
    over = x > 0.8
    mix[over] = np.sign(mix[over]) * (0.8 + 0.19 * np.tanh((x[over] - 0.8) / 0.19))
    peak = float(np.max(np.abs(mix))) if n else 0.0
    if peak > db(-1.5):
        mix *= db(-1.5) / peak
    report["peak_dbfs"] = round(C.dbfs(float(np.max(np.abs(mix))) if n else 0.0), 2)
    report["rms_dbfs"] = round(C.dbfs(C.rms(mix)), 2)
    report["game_rms_dbfs"] = round(C.dbfs(C.rms(game)), 2)
    report["rate"], report["seconds"] = rate, round(n / rate, 3)

    out = os.path.join(C.ensure_dir(C.AUDIO_DIR), f"{name}_mix.wav")
    C.write_wav16(out, rate, mix)
    report["out"] = out
    with open(os.path.join(C.ensure_dir(C.REPORTS), f"mix_{name}.json"), "w", encoding="utf-8") as fh:
        json.dump(report, fh, indent=1, ensure_ascii=False)
    for w in report["warnings"]:
        print("WARNING", w)
    C.log(f"mix {out}: {report['seconds']} s, peak {report['peak_dbfs']} dBFS, rms {report['rms_dbfs']} dBFS, {len(placed)} voice file(s)")


if __name__ == "__main__":
    main()
