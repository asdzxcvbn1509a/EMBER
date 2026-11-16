"""EMBER tutorial video - shared helpers for the post-production scripts in this folder.

They run with Blender's bundled Python (numpy, OpenImageIO), or inside blender.exe for the VSE edit:
    PY="C:/Program Files (x86)/Steam/steamapps/common/Blender/5.3/python/bin/python.exe"
A "take" is a folder written by the game's tutorial recorder (EMBER.exe -tutorialRecord DIR):
    frames/f_NNNNNN.jpg   one JPEG per video frame (NNNNNN = frame index + 1)
    game.wav              the game's own sound, 32-bit float, in sync with the frames
    timeline.json         segments, captions on/off, black holds, keys, events, checks (TutorialTimeline.cs)
    frames.tsv            per-frame state: fader, HUD alphas, keys, cursor, ball position
    log.txt
"""
import json
import os
import re
import struct
import sys
import time

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
BUILD = os.path.join(ROOT, "Build", "Tutorial")
REPORTS = os.path.join(BUILD, "reports")
CAPTIONS_JSON = os.path.join(HERE, "captions.json")
CAPTION_DIR = os.path.join(BUILD, "captions")
VOICE_DIR = os.path.join(HERE, "voice")
AUDIO_DIR = os.path.join(BUILD, "audio")
DELIVER = os.path.join(ROOT, "Deliver")
DOCS = os.path.join(ROOT, "Docs", "Tutorial")
GAME_AUDIO = os.path.join(ROOT, "EMBER", "Assets", "_Ember", "Audio")
TRAILER = os.path.join(ROOT, "ArtSource", "Trailer")

# HUD rectangles of the game in canvas pixels (1920x1080, origin TOP-left), from UIFactory.cs, for the overlap check.
# "when" names the frames.tsv column that tells whether the element is visible (alpha / 0-1 flag); None = scene-wide.
HUD = {
    "stage_card":    {"rect": (160, 627, 1760, 807), "scenes": ("Stage1", "Stage2", "Stage3"), "when": "card"},
    "controls_hint": {"rect": (48, 990, 1348, 1040), "scenes": ("Stage1",), "when": "hint"},
    "toast":         {"rect": (510, 60, 1410, 120), "scenes": ("Stage1", "Stage2", "Stage3"), "when": "toast"},
    "shard_counter": {"rect": (1652, 40, 1872, 100), "scenes": ("Stage1", "Stage2", "Stage3"), "when": None},
    "photo_hint":    {"rect": (260, 980, 1660, 1030), "scenes": ("Stage1", "Stage2", "Stage3"), "when": "photo"},
    "pause_menu":    {"rect": (680, 337, 1240, 772), "scenes": ("Stage1", "Stage2", "Stage3"), "when": "pause"},
    "title_menu":    {"rect": (110, 580, 600, 900), "scenes": ("Title",), "when": None},
    "title_footer":  {"rect": (180, 1006, 1880, 1046), "scenes": ("Title",), "when": None},
    "ending_stats":  {"rect": (50, 40, 950, 110), "scenes": ("Ending",), "when": None},
    "ending_buttons": {"rect": (1440, 860, 1860, 1020), "scenes": ("Ending",), "when": None},
}

_t0 = time.time()


def log(*a):
    print("[tutorial %6.1fs]" % (time.time() - _t0), *a, flush=True)


def args():
    """--key value pairs after "--" (inside blender.exe) or after the script name (plain Python)."""
    a = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else sys.argv[1:]
    out, key = {}, None
    for x in a:
        if x.startswith("--"):
            key = x
            out[key] = "yes"
        elif key:
            out[key] = x
            key = None
    return out


def ensure_dir(p):
    os.makedirs(p, exist_ok=True)
    return p


def tc(seconds, srt=False):
    """Time code: m:ss.s (or hh:mm:ss,mmm for SRT)."""
    seconds = max(0.0, seconds)
    if srt:
        ms = int(round(seconds * 1000))
        return "%02d:%02d:%02d,%03d" % (ms // 3600000, ms // 60000 % 60, ms // 1000 % 60, ms % 1000)
    return "%d:%04.1f" % (int(seconds // 60), seconds % 60)


# ------------------------------------------------------------------ take data
def take_dir(path=None):
    return os.path.abspath(path or os.path.join(BUILD, "final"))


def load_timeline(take):
    with open(os.path.join(take, "timeline.json"), encoding="utf-8") as fh:
        return json.load(fh)


def load_frames(take):
    """frames.tsv -> dict of numpy arrays (the scene column is a list of strings)."""
    with open(os.path.join(take, "frames.tsv"), encoding="utf-8") as fh:
        head = fh.readline().rstrip("\n").split("\t")
        rows = [ln.rstrip("\n").split("\t") for ln in fh if ln.strip()]
    cols = {}
    for i, name in enumerate(head):
        vals = [r[i] if i < len(r) else "0" for r in rows]
        cols[name] = vals if name == "scene" else np.array([float(v) for v in vals], dtype=np.float64)
    return cols


# ------------------------------------------------------------------ captions
MARKUP = re.compile(r"\[([^\]]+)\]|\{([^}]+)\}")
THAI_MARKS = set(chr(c) for c in [0x0E31] + list(range(0x0E34, 0x0E3B)) + list(range(0x0E47, 0x0E4F)))


def load_captions():
    with open(CAPTIONS_JSON, encoding="utf-8") as fh:
        data = json.load(fh)
    return {c["id"]: c for c in data["captions"]}


def plain(text):
    """Caption text without the [KEY] / {MENU} markup (subtitles, narration script)."""
    return MARKUP.sub(lambda m: m.group(1) or m.group(2), text)


def graphemes(text):
    """Rough reading length: characters without Thai combining marks, spaces and markup."""
    return sum(1 for ch in plain(text) if not ch.isspace() and ch not in THAI_MARKS)


# ------------------------------------------------------------------ audio
def read_wav(path):
    """PCM 8/16/24/32-bit or float 32/64-bit WAV -> (rate, float32 array (frames, channels))."""
    with open(path, "rb") as fh:
        data = fh.read()
    if data[:4] != b"RIFF" or data[8:12] != b"WAVE":
        raise ValueError("not a WAV file: " + path)
    pos, fmt, raw = 12, None, None
    while pos + 8 <= len(data):
        cid, size = data[pos:pos + 4], struct.unpack("<I", data[pos + 4:pos + 8])[0]
        if cid == b"fmt ":
            body = data[pos + 8:pos + 8 + size]
            fmt = struct.unpack("<HHIIHH", body[:16])
            if fmt[0] == 0xFFFE and len(body) >= 26:   # WAVE_FORMAT_EXTENSIBLE: the real tag starts the sub-format GUID
                fmt = (struct.unpack("<H", body[24:26])[0],) + fmt[1:]
        elif cid == b"data":
            # a recorder that was killed leaves size 0: take the rest of the file
            raw = data[pos + 8:] if size == 0 or pos + 8 + size > len(data) else data[pos + 8:pos + 8 + size]
            break
        pos += 8 + size + (size & 1)
    if fmt is None or raw is None:
        raise ValueError("no fmt/data chunk: " + path)
    tag, ch, rate, _, align, bits = fmt
    raw = raw[: len(raw) // align * align]
    if tag == 3 and bits == 32:
        x = np.frombuffer(raw, dtype="<f4").astype(np.float32)
    elif tag == 3 and bits == 64:
        x = np.frombuffer(raw, dtype="<f8").astype(np.float32)
    elif tag == 1 and bits == 16:
        x = np.frombuffer(raw, dtype="<i2").astype(np.float32) / 32768.0
    elif tag == 1 and bits == 32:
        x = np.frombuffer(raw, dtype="<i4").astype(np.float32) / 2147483648.0
    elif tag == 1 and bits == 24:
        b = np.frombuffer(raw, dtype=np.uint8).reshape(-1, 3).astype(np.int32)
        v = b[:, 0] | (b[:, 1] << 8) | (b[:, 2] << 16)
        v = np.where(v >= 1 << 23, v - (1 << 24), v)
        x = v.astype(np.float32) / 8388608.0
    elif tag == 1 and bits == 8:
        x = (np.frombuffer(raw, dtype=np.uint8).astype(np.float32) - 128.0) / 128.0
    else:
        raise ValueError(f"unsupported WAV format tag {tag} / {bits} bit: {path}")
    return rate, x.reshape(-1, ch)


def write_wav16(path, rate, x):
    """float array (frames, channels) in -1..1 -> 16-bit PCM WAV."""
    x = np.asarray(x, dtype=np.float32)
    if x.ndim == 1:
        x = x[:, None]
    pcm = (np.clip(x, -1.0, 1.0) * 32767.0).round().astype("<i2")
    ch = pcm.shape[1]
    body = pcm.tobytes()
    head = b"RIFF" + struct.pack("<I", 36 + len(body)) + b"WAVE" + b"fmt " + struct.pack("<IHHIIHH", 16, 1, ch, rate, rate * ch * 2, ch * 2, 16)
    with open(path, "wb") as fh:
        fh.write(head + b"data" + struct.pack("<I", len(body)) + body)


def resample(x, src_rate, dst_rate):
    """Linear-interpolation resample of (frames, channels) audio (fine for voice and the synthesized music)."""
    if src_rate == dst_rate or len(x) == 0:
        return x
    n = int(round(len(x) * dst_rate / src_rate))
    t = np.arange(n) * (src_rate / dst_rate)
    i = np.minimum(t.astype(np.int64), len(x) - 1)
    j = np.minimum(i + 1, len(x) - 1)
    f = (t - i)[:, None].astype(np.float32)
    return x[i] * (1 - f) + x[j] * f


def dbfs(v):
    return 20.0 * np.log10(max(float(v), 1e-12))


def rms(x):
    return float(np.sqrt(np.mean(np.square(x, dtype=np.float64)))) if len(x) else 0.0


# ------------------------------------------------------------------ images
def read_image(path):
    """JPEG/PNG -> float32 array (h, w, channels) in 0..1, top row first (OpenImageIO)."""
    import OpenImageIO as oiio
    buf = oiio.ImageBuf(path)
    px = buf.get_pixels(oiio.FLOAT)
    if px is None or buf.has_error:
        raise IOError(f"cannot read {path}: {buf.geterror()}")
    return np.asarray(px, dtype=np.float32)
