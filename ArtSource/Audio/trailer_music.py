"""EMBER trailer - 90 s score and final sound mix, synthesised with the game's own instruments (synth.py).

  "<Blender>/5.3/python/bin/python.exe" ArtSource/Audio/trailer_music.py

Writes Build/Trailer/audio/trailer_music.wav (score only, stereo) and trailer_mix.wav (score + the game's sound
effects at the frames the trailer director reported + ambience beds), both 44.1 kHz 16-bit stereo.
Timing comes from ArtSource/Trailer/shots.py (96 BPM, 1 bar = 2.5 s); frame F plays at (F - 1) / 24 s.
"""
import glob
import json
import os
import sys
import wave

import numpy as np

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.normpath(os.path.join(HERE, "..", ".."))
OUT = os.path.join(ROOT, "Build", "Trailer", "audio")
REPORTS = os.path.join(ROOT, "Build", "Trailer", "reports")
GAME_AUDIO = os.path.join(ROOT, "EMBER", "Assets", "_Ember", "Audio")
sys.path.insert(0, HERE)
sys.path.insert(0, os.path.join(ROOT, "ArtSource", "Trailer"))
os.makedirs(OUT, exist_ok=True)

# synth.py reads its output folder from argv and creates it on import: point it at the trailer folder
_argv = sys.argv
sys.argv = [_argv[0], OUT]
import synth as Y  # noqa: E402
sys.argv = _argv
import shots as S  # noqa: E402

SR = Y.SR
BEAT = 60.0 / S.BPM
BAR = 4 * BEAT
DUR = S.TOTAL / S.FPS
N = int(DUR * SR) + SR * 4


def T(frame):
    return (frame - 1) / S.FPS


def bar_t(bar, beat=0.0):
    return (bar - 1) * BAR + beat * BEAT


class Track:
    def __init__(self):
        self.L = np.zeros(N)
        self.R = np.zeros(N)

    def add(self, x, at, gain=1.0, pan=0.0):
        """Mono x at time `at` (s), constant-power pan -1..1."""
        i = int(at * SR)
        if i >= N:
            return
        x = np.asarray(x)[: N - max(i, 0)]
        if i < 0:
            x = x[-i:]
            i = 0
        a = (pan + 1) * np.pi / 4
        self.L[i:i + len(x)] += x * gain * np.cos(a)
        self.R[i:i + len(x)] += x * gain * np.sin(a)

    def add_stereo(self, l, r, at, gain=1.0):
        i = int(at * SR)
        n = min(len(l), N - i)
        if n <= 0:
            return
        self.L[i:i + n] += l[:n] * gain
        self.R[i:i + n] += r[:n] * gain


# ------------------------------------------------------------------ instruments
def env_ar(n, a, r):
    e = np.ones(n)
    na, nr = int(a * SR), int(r * SR)
    if na:
        e[:na] = np.linspace(0, 1, na)
    if nr:
        e[-nr:] *= np.linspace(1, 0, nr)
    return e


def pad_chord(notes, dur, bright=1600, gain=0.11, attack=0.8, release=1.2):
    n = int(dur * SR)
    x = np.zeros(n)
    for m in notes:
        x += Y.pad_voice(Y.note(m), dur, 0.004, bright)[:n] * gain
    return x * env_ar(n, attack, release)


def sub(freq, dur, gain=0.22):
    n = int(dur * SR)
    return Y.sine(freq, dur)[:n] * gain * env_ar(n, 0.3, 0.8)


def kick(gain=0.9):
    d = 0.5
    t = Y.t_axis(d)
    f = 48 + 90 * np.exp(-t / 0.035)
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * Y.env_exp(d, 0.16, 0.001)
    click = Y.highpass(Y.noise(d), 2500) * Y.env_exp(d, 0.004, 0.0005) * 0.25
    return (body + click) * gain


def tom(freq=95, gain=0.6):
    d = 0.7
    t = Y.t_axis(d)
    f = freq * (1 + 0.5 * np.exp(-t / 0.05))
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * Y.env_exp(d, 0.22, 0.002) * gain


def tick(gain=0.12):
    d = 0.08
    return Y.bandpass(Y.noise(d), 5000, 11000) * Y.env_exp(d, 0.012, 0.0005) * gain


def anvil(freq=880, gain=0.25):
    partials = ((1, 1.0), (2.41, 0.7), (3.93, 0.5), (5.71, 0.35), (8.2, 0.2))
    return Y.bell(freq, 1.6, 0.5, partials) * gain


def boom(gain=1.0, dur=3.0):
    t = Y.t_axis(dur)
    f = 30 + 55 * np.exp(-t / 0.25)
    body = np.sin(2 * np.pi * np.cumsum(f) / SR) * Y.env_exp(dur, 0.9, 0.003)
    rumble = Y.lowpass(Y.noise(dur), 180) * Y.env_exp(dur, 0.7, 0.01) * 0.6
    return (body + rumble) * gain


def crash(gain=0.5, dur=3.0):
    return Y.highpass(Y.noise(dur), 3000) * Y.env_exp(dur, 0.8, 0.002) * gain


def riser(dur, gain=0.35, lo=400, hi=6000):
    n = int(dur * SR)
    x = Y.noise(dur)[:n]
    k = np.linspace(0, 1, n)
    # crude sweep: blend a low and a high band with the ramp, rising level
    low = Y.bandpass(x, lo * 0.5, lo * 2)
    high = Y.bandpass(x, hi * 0.5, hi)
    return (low * (1 - k) + high * k) * (k ** 2) * gain


def reverse_swell(dur, notes, gain=0.25):
    x = sum(Y.bell(Y.note(m), dur, 1.6) for m in notes)
    x = Y.reverb(x, 2.0, 0.6)[: int(dur * SR)]
    return x[::-1] * gain


def saw(freq, dur, bright=900):
    t = Y.t_axis(dur)
    x = 2 * ((freq * t) % 1.0) - 1
    x += 2 * ((freq * 1.005 * t) % 1.0) - 1
    return Y.lowpass(x * 0.5, bright)


# ------------------------------------------------------------------ the score
def score():
    tr = Track()
    rng_pan = np.random.default_rng(7)
    # ---- cold open (bars 1-4): drone, wind, one bell, the landing
    d = bar_t(5)
    drone = (Y.pad_voice(Y.note(33), d + 2, 0.003, 500) * 0.20 + Y.pad_voice(Y.note(40), d + 2, 0.003, 600) * 0.12)
    drone *= env_ar(len(drone), 3.0, 1.5)
    tr.add(drone, 0.0, 1.0, -0.15)
    tr.add(drone * 0.8, 0.02, 1.0, 0.15)
    wind = Y.bandpass(Y.noise(d), 200, 1600) * 0.10 * env_ar(int(d * SR), 2.0, 2.0)
    tr.add(wind, 0.0, 1.0, 0.3)
    tr.add(Y.bell(Y.note(69), 4.0, 2.2) * 0.22, T(S.TITLES[0]["start"]), 1.0, -0.2)
    tr.add(riser(2.2, 0.18, 300, 4000), T(S.HITS["landing"]) - 2.2, 1.0, 0.0)
    land = T(S.HITS["landing"])
    tr.add(boom(0.9, 3.5), land)
    for i, m in enumerate((57, 60, 64, 69)):
        tr.add(Y.bell(Y.note(m), 5.0, 2.4) * 0.16, land + 0.02 * i, 1.0, -0.3 + 0.2 * i)
    # ---- crypt (bars 5-12): Am F Cmaj7 G, the title-theme melody, a growing pulse
    chords = [(57, 60, 64), (53, 57, 60), (48, 52, 55, 59), (55, 59, 62)]
    for k in range(8):
        b = 5 + k
        ch = chords[k % 4]
        x = pad_chord(ch, BAR + 1.6, 1500, 0.10)
        tr.add(x, bar_t(b), 1.0, -0.25)
        tr.add(x * 0.9, bar_t(b) + 0.013, 1.0, 0.25)
        tr.add(sub(Y.note(ch[0] - 24), BAR + 0.8, 0.20), bar_t(b))
    melody = [76, 72, 74, 69, 76, 79, 74, 72, 76, 72, 74, 69, 72, 71, 69, 64]
    for i, m in enumerate(melody):
        tr.add(Y.bell(Y.note(m), 3.0, 1.3) * 0.17, bar_t(5, 2 * i), 1.0, rng_pan.uniform(-0.4, 0.4))
    for b in range(7, 12):
        for bt in (0, 2):
            tr.add(tom(82, 0.45), bar_t(b, bt), 1.0, -0.1)
    for b in range(9, 12):
        for e in range(8):
            tr.add(tick(0.07 + 0.02 * (b - 9)), bar_t(b, e * 0.5), 1.0, 0.35 if e % 2 else -0.35)
    for key in ("bridge", "gate", "wardens"):
        f = T(S.HITS[key])
        tr.add(boom(0.45, 2.0), f)
        tr.add(Y.bell(Y.note(81), 3.0, 1.6) * 0.12, f, 1.0, 0.2)
    tr.add(riser(BAR, 0.22), bar_t(12))
    # ---- forge (bars 13-20): kick on every beat, anvils, a saw ostinato, the smash and the lamps
    for b in range(13, 21):
        for bt in range(4):
            tr.add(kick(0.75 if bt % 2 == 0 else 0.55), bar_t(b, bt))
            if bt in (1, 3):
                tr.add(anvil(880 if bt == 1 else 932, 0.16), bar_t(b, bt), 1.0, 0.45 if bt == 1 else -0.45)
        root = {13: 33, 14: 33, 15: 29, 16: 28, 17: 33, 18: 33, 19: 29, 20: 28}[b]
        for e in range(8):
            m = root + (12 if e in (3, 6) else 0) + (3 if e == 5 else 0)
            x = saw(Y.note(m), BEAT * 0.48, 700) * env_ar(int(BEAT * 0.48 * SR), 0.005, 0.06) * 0.20
            tr.add(x, bar_t(b, e * 0.5), 1.0, 0.0)
        stab = pad_chord([root + 24, root + 27, root + 31], BEAT * 1.5, 2600, 0.09, 0.02, 0.4)
        tr.add(stab, bar_t(b), 1.0, -0.2)
        tr.add(stab, bar_t(b) + 0.011, 1.0, 0.2)
    sm = T(S.HITS["smash"])
    tr.add(reverse_swell(1.0, (45, 52, 57)), sm - 1.0)
    tr.add(boom(1.1, 3.0), sm)
    tr.add(crash(0.35), sm, 1.0, 0.2)
    lp = T(S.HITS["lamps"])
    tr.add(boom(0.6, 2.5), lp)
    for i, m in enumerate((69, 72, 76, 81)):
        tr.add(Y.bell(Y.note(m), 3.5, 1.8) * 0.12, lp + 0.05 * i, 1.0, -0.3 + 0.2 * i)
    tr.add(riser(BAR * 1.0, 0.30, 500, 7000), bar_t(20))
    # ---- sky (bars 21-28): C G Am F, the ending-theme melody, bell arpeggios, a lift into the finale
    chords = [(48, 52, 55), (55, 59, 62), (57, 60, 64), (53, 57, 60)]
    for k in range(8):
        b = 21 + k
        ch = chords[k % 4]
        x = pad_chord(ch, BAR + 1.6, 2400, 0.10)
        tr.add(x, bar_t(b), 1.0, -0.3)
        tr.add(x * 0.9, bar_t(b) + 0.012, 1.0, 0.3)
        tr.add(sub(Y.note(ch[0] - 12), BAR + 0.8, 0.16), bar_t(b))
        arp = [ch[0] + 24, ch[1] + 24, ch[2] + 24, ch[1] + 24 + 12 * (k % 2)]
        for e in range(8):
            tr.add(Y.bell(Y.note(arp[e % 4]), 1.4, 0.55) * 0.07, bar_t(b, e * 0.5), 1.0, 0.5 if e % 2 else -0.5)
        if b >= 23:
            tr.add(kick(0.45), bar_t(b))
            tr.add(kick(0.35), bar_t(b, 2))
    melody = [72, 76, 79, 79, 81, 79, 76, 74, 72, 76, 72, 76, 79, 84, 83, 79]
    for i, m in enumerate(melody):
        tr.add(Y.bell(Y.note(m), 3.0, 1.4) * 0.15, bar_t(21, 2 * i), 1.0, rng_pan.uniform(-0.35, 0.35))
    tp = T(S.HITS["teleport"])
    tr.add(reverse_swell(0.8, (72, 76, 79), 0.2), tp - 0.8)
    for key, m in (("lantern1", 79), ("lantern2", 84), ("lantern3", 88)):
        f = T(S.HITS[key])
        tr.add(Y.bell(Y.note(m), 4.0, 2.0) * 0.18, f, 1.0, 0.0)
        tr.add(boom(0.35, 1.8), f)
    tr.add(riser(BAR * 2, 0.32, 400, 8000), bar_t(27))
    for e in range(8):
        tr.add(tom(110 - e * 4, 0.35 + e * 0.04), bar_t(28, 2 + e * 0.25), 1.0, 0.0)
    # ---- finale (bars 29-32): ignition, a major-key swell and bell cascade, rising into the flash
    ig = T(S.HITS["ignite"])
    tr.add(boom(1.2, 4.0), ig)
    tr.add(crash(0.3, 4.0), ig, 1.0, -0.2)
    big = (36, 43, 48, 52, 55, 60, 64, 67)
    swell = pad_chord(big, 11.0, 2600, 0.085, 0.4, 2.5)
    tr.add(swell, ig, 1.0, -0.25)
    tr.add(swell, ig + 0.015, 1.0, 0.25)
    for i, m in enumerate((72, 76, 79, 84, 88, 91, 96)):
        tr.add(Y.bell(Y.note(m), 4.0, 2.2) * 0.12, ig + 0.11 * i, 1.0, -0.6 + 0.2 * i)
    for b in range(30, 33):
        for e in range(8):
            tr.add(Y.bell(Y.note((84, 79, 76, 79)[e % 4]), 1.2, 0.5) * 0.05, bar_t(b, e * 0.5), 1.0, 0.4 if e % 2 else -0.4)
    fl = T(S.HITS["flash"])
    tr.add(riser(3.0, 0.35, 600, 9000), fl - 3.0)
    # ---- logo (bars 33-36): the white flash, a held breath, the logo hit and a long minor bell
    lg = T(S.HITS["logo"])
    tr.add(reverse_swell(1.6, (57, 64, 69, 72), 0.3), lg - 1.6)
    tr.add(boom(1.2, 5.0), lg)
    for i, m in enumerate((45, 52, 59, 60, 64, 69)):
        tr.add(Y.bell(Y.note(m), 7.0, 3.2) * 0.15, lg + 0.03 * i, 1.0, -0.4 + 0.16 * i)
    tail = pad_chord((45, 52, 60, 64), 8.0, 1200, 0.09, 1.0, 3.5)
    tr.add(tail, lg, 1.0, -0.2)
    tr.add(tail, lg + 0.02, 1.0, 0.2)
    return tr


# ------------------------------------------------------------------ sound effects and ambience
GAINS = {"sfx_smash": 0.95, "sfx_gate": 0.6, "sfx_bridge": 0.6, "sfx_ignite": 0.45, "sfx_plate": 0.5, "sfx_teleport": 0.7,
         "sfx_impact": 0.55, "sfx_checkpoint": 0.35, "sfx_portal": 0.35, "sfx_clone": 0.35, "sfx_hammer": 0.3,
         "sfx_finale": 0.8, "sfx_shard": 0.4}


def read_wav(path):
    with wave.open(path, "rb") as w:
        n = w.getnframes()
        ch = w.getnchannels()
        data = np.frombuffer(w.readframes(n), dtype=np.int16).astype(np.float64) / 32768.0
        if ch > 1:
            data = data.reshape(-1, ch).mean(axis=1)
        return data


def effects(tr):
    cues = []
    for p in sorted(glob.glob(os.path.join(REPORTS, "sfx_*.json"))):
        with open(p, encoding="utf-8") as fh:
            cues += json.load(fh)
    shots_by_env = {}
    for s in S.SHOTS:
        shots_by_env.setdefault(s["env"], []).append(s)
    in_shot = lambda f: any(s["start"] - 4 <= f <= s["end"] for s in S.SHOTS)  # noqa: E731
    used = {}
    n = 0
    for c in sorted(cues, key=lambda c: c["frame"]):
        f, clip = c["frame"], c["clip"]
        if not in_shot(f):
            continue
        k = (clip, f // 6)
        if used.get(k, 0) >= 1:                     # one copy of the same sound per quarter beat
            continue
        used[k] = used.get(k, 0) + 1
        path = os.path.join(GAME_AUDIO, clip + ".wav")
        if not os.path.exists(path):
            continue
        x = read_wav(path)
        g = GAINS.get(clip, 0.5) * min(1.2, c.get("gain", 1.0))
        tr.add(x, T(f), g * 0.8, 0.0)
        n += 1
    return n


def ambience(tr):
    beds = [("amb_crypt", 0.0, bar_t(13), 0.22), ("amb_forge", bar_t(13), bar_t(21), 0.28),
            ("amb_sky", bar_t(21), bar_t(33), 0.22), ("amb_crypt", bar_t(33), DUR, 0.16)]
    for name, a, b, g in beds:
        x = read_wav(os.path.join(GAME_AUDIO, name + ".wav"))
        need = int((b - a) * SR)
        reps = int(np.ceil(need / len(x))) + 1
        y = np.tile(x, reps)[:need] * env_ar(need, 0.4, 0.5)
        tr.add(y, a, g, -0.2)
        tr.add(np.roll(y, SR // 3), a, g * 0.9, 0.2)


def master(tr):
    L, R = tr.L[: int(DUR * SR)], tr.R[: int(DUR * SR)]
    # fade to silence over the last second
    k = int(1.5 * SR)
    L[-k:] *= np.linspace(1, 0, k)
    R[-k:] *= np.linspace(1, 0, k)
    peak = max(np.max(np.abs(L)), np.max(np.abs(R))) + 1e-9
    L, R = L / peak * 1.25, R / peak * 1.25
    L, R = np.tanh(L), np.tanh(R)                    # soft limiter
    peak = max(np.max(np.abs(L)), np.max(np.abs(R)))
    lim = 10 ** (-1.0 / 20)
    return L / peak * lim, R / peak * lim


def write(path, L, R):
    data = np.empty(2 * len(L), dtype=np.int16)
    data[0::2] = np.clip(L * 32767, -32768, 32767).astype(np.int16)
    data[1::2] = np.clip(R * 32767, -32768, 32767).astype(np.int16)
    with wave.open(path, "wb") as w:
        w.setnchannels(2)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())
    print(f"wrote {path}  ({len(L) / SR:.2f} s)")


def main():
    Y.rng = np.random.default_rng(1234)              # reverb and noise draw from synth's module generator
    tr = score()
    L, R = master(tr)
    write(os.path.join(OUT, "trailer_music.wav"), L, R)
    tr2 = score()
    n = effects(tr2)
    ambience(tr2)
    L, R = master(tr2)
    write(os.path.join(OUT, "trailer_mix.wav"), L, R)
    print(f"{n} sound effects placed")


if __name__ == "__main__":
    main()
