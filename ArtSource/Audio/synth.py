"""EMBER - procedural sound design. Every sound in the game is synthesised here (no samples).

Run with a Python that has numpy (Blender ships one):
  "<Blender>/5.3/python/bin/python.exe" synth.py <output dir>
"""
import math
import os
import sys
import wave

import numpy as np

SR = 44100
OUT = sys.argv[1] if len(sys.argv) > 1 else "out"
os.makedirs(OUT, exist_ok=True)
rng = np.random.default_rng(1234)


# ------------------------------------------------------------------ helpers
def t_axis(dur):
    return np.arange(int(dur * SR)) / SR


def env_exp(dur, tau, attack=0.005):
    t = t_axis(dur)
    a = np.clip(t / max(attack, 1e-4), 0, 1)
    return a * np.exp(-t / tau)


def noise(dur):
    return rng.standard_normal(int(dur * SR))


def spectral(x, fn):
    X = np.fft.rfft(x)
    f = np.fft.rfftfreq(len(x), 1 / SR)
    return np.fft.irfft(X * fn(f), n=len(x))


def lowpass(x, fc, order=2):
    return spectral(x, lambda f: 1 / np.sqrt(1 + (f / fc) ** (2 * order)))


def highpass(x, fc, order=2):
    return spectral(x, lambda f: 1 / np.sqrt(1 + (fc / np.maximum(f, 1e-3)) ** (2 * order)))


def bandpass(x, lo, hi):
    return highpass(lowpass(x, hi), lo)


def sine(freq, dur, phase=0.0):
    t = t_axis(dur)
    if callable(freq):
        ph = 2 * np.pi * np.cumsum(freq(t)) / SR
        return np.sin(ph + phase)
    return np.sin(2 * np.pi * freq * t + phase)


def pad(x, dur):
    n = int(dur * SR)
    out = np.zeros(n)
    out[:min(n, len(x))] = x[:n]
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = np.zeros(n)
    for p in parts:
        out[:len(p)] += p
    return out


def place(buf, x, at):
    i = int(at * SR)
    if i >= len(buf):
        return buf
    j = min(len(buf), i + len(x))
    buf[i:j] += x[:j - i]
    return buf


def reverb(x, decay=2.0, wet=0.3, pre=0.02, bright=6000):
    n = int(decay * SR)
    t = np.arange(n) / SR
    ir = rng.standard_normal(n) * np.exp(-t / (decay / 6.9))
    ir = lowpass(ir, bright)
    ir[: int(pre * SR)] = 0
    ir /= np.sqrt(np.sum(ir ** 2)) + 1e-9
    L = len(x) + n
    y = np.fft.irfft(np.fft.rfft(x, L) * np.fft.rfft(ir, L), n=L)
    out = np.concatenate([x, np.zeros(n)]) * (1 - wet) + y * wet * 1.5
    return out


def bell(freq, dur, decay=1.2, partials=((1, 1.0), (2.76, 0.5), (5.4, 0.25), (8.93, 0.12))):
    out = np.zeros(int(dur * SR))
    for ratio, amp in partials:
        out += amp * sine(freq * ratio, dur) * env_exp(dur, decay / ratio ** 0.6, 0.002)
    return out


def fade_edges(x, fin=0.005, fout=0.05):
    n_in, n_out = int(fin * SR), int(fout * SR)
    if n_in > 0:
        x[:n_in] *= np.linspace(0, 1, n_in)
    if n_out > 0:
        x[-n_out:] *= np.linspace(1, 0, n_out)
    return x


def loopify(x, xfade=2.0):
    n = int(xfade * SR)
    head, body = x[:n], x[n:]
    ramp = np.linspace(0, 1, n)
    body[-n:] = body[-n:] * (1 - ramp) + head * ramp
    return body


def save(name, x, peak=0.89):
    x = np.asarray(x, dtype=np.float64)
    m = np.max(np.abs(x)) + 1e-9
    x = x / m * peak
    data = (np.clip(x, -1, 1) * 32767).astype(np.int16)
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())
    print(f"{name:14s} {len(x) / SR:6.2f}s")


def note(n):  # MIDI note -> Hz
    return 440.0 * 2 ** ((n - 69) / 12)


def pad_voice(freq, dur, detune=0.004, bright=1800):
    v = np.zeros(int(dur * SR))
    for d in (-detune, 0, detune):
        f = freq * (1 + d)
        saw = 2 * ((t_axis(dur) * f) % 1.0) - 1
        v += saw
    return lowpass(v / 3, bright, order=2)


# ------------------------------------------------------------------ one-shots
def sfx_plate():
    d = 0.45
    thump = sine(lambda t: 110 * np.exp(-t * 6) + 55, d) * env_exp(d, 0.09)
    click = bandpass(noise(d), 1500, 6000) * env_exp(d, 0.012, 0.0005) * 0.5
    grind = bandpass(noise(d), 200, 900) * env_exp(d, 0.15) * 0.25
    return reverb(fade_edges(mix(thump, click, grind)), 1.4, 0.22)


def sfx_ignite():
    d = 1.4
    t = t_axis(d)
    whoosh = noise(d)
    whoosh = spectral(whoosh, lambda f: np.exp(-((f - 1200) / 1500) ** 2) + 0.2)
    whoosh *= np.clip(t / 0.25, 0, 1) * np.exp(-np.maximum(t - 0.25, 0) / 0.35)
    crackle = np.zeros(int(d * SR))
    for _ in range(40):
        at = rng.uniform(0.1, d - 0.05)
        c = highpass(noise(0.01), 2500) * env_exp(0.01, 0.002, 0.0002) * rng.uniform(0.2, 0.7)
        place(crackle, c, at)
    body = sine(lambda tt: 70 + 50 * np.clip(tt / 0.3, 0, 1), d) * env_exp(d, 0.4, 0.05) * 0.4
    return reverb(fade_edges(mix(whoosh * 0.8, crackle, body)), 1.8, 0.3)


def sfx_chime():
    d = 3.0
    x = mix(bell(note(81), d, 1.6), place(np.zeros(int(d * SR)), bell(note(88), 2.5, 1.4) * 0.6, 0.12))
    return reverb(fade_edges(x), 2.6, 0.38)


def sfx_gate():
    d = 2.2
    t = t_axis(d)
    rumble = lowpass(noise(d), 140) * 2.5
    grind = bandpass(noise(d), 300, 1400) * (0.5 + 0.5 * np.sin(2 * np.pi * 9 * t)) * 0.35
    chain = np.zeros(int(d * SR))
    for k in range(14):
        place(chain, bandpass(noise(0.04), 2000, 7000) * env_exp(0.04, 0.008) * 0.4, 0.1 + k * 0.13)
    env = np.clip(t / 0.15, 0, 1) * np.clip((d - t) / 0.4, 0, 1)
    end = sine(60, 0.6) * env_exp(0.6, 0.12) * 0.9
    x = mix((rumble + grind + chain) * env, place(np.zeros(int(d * SR)), end, d - 0.6))
    return reverb(fade_edges(x), 2.2, 0.3)


def sfx_bridge():
    d = 2.2
    x = np.zeros(int(d * SR))
    for k in range(4):
        th = sine(lambda t: 90 * np.exp(-t * 8) + 50, 0.4) * env_exp(0.4, 0.08)
        th += bandpass(noise(0.4), 300, 2500) * env_exp(0.4, 0.03) * 0.4
        place(x, th * (0.8 + 0.1 * k), 0.15 + k * 0.28)
    return reverb(fade_edges(x), 1.8, 0.3)


def sfx_smash():
    d = 2.6
    boom = sine(lambda t: 70 * np.exp(-t * 3) + 32, d) * env_exp(d, 0.35, 0.002) * 1.2
    crash = lowpass(noise(d), 4000) * env_exp(d, 0.25, 0.001)
    debris = np.zeros(int(d * SR))
    for _ in range(60):
        at = rng.uniform(0.05, 2.0)
        p = bandpass(noise(0.05), rng.uniform(400, 1500), 5000) * env_exp(0.05, 0.01) * rng.uniform(0.1, 0.5) * np.exp(-at)
        place(debris, p, at)
    return reverb(fade_edges(mix(boom, crash, debris)), 2.5, 0.3)


def sfx_clone():
    d = 0.9
    clunk = sine(lambda t: 160 * np.exp(-t * 10) + 80, d) * env_exp(d, 0.07)
    ting = bell(note(76), d, 0.35) * 0.35
    latch = bandpass(noise(d), 1000, 5000) * env_exp(d, 0.02) * 0.4
    return reverb(fade_edges(mix(clunk, ting, latch)), 1.5, 0.25)


def sfx_teleport():
    d = 1.8
    x = np.zeros(int(d * SR))
    for k, n in enumerate((69, 76, 81, 88)):
        place(x, bell(note(n), 1.2, 0.5) * 0.5, k * 0.07)
    sweep = sine(lambda t: 300 + 1500 * t, d) * env_exp(d, 0.25, 0.05) * 0.15
    return reverb(fade_edges(mix(x, sweep)), 2.4, 0.45)


def sfx_checkpoint():
    d = 2.5
    x = mix(bell(note(72), d, 1.4) * 0.7, place(np.zeros(int(d * SR)), bell(note(79), 2.0, 1.2) * 0.6, 0.18))
    return reverb(fade_edges(x), 2.2, 0.4)


def sfx_shard():
    d = 2.0
    x = np.zeros(int(d * SR))
    for k, n in enumerate((84, 88, 91, 96)):
        place(x, bell(note(n), 1.2, 0.6) * (0.6 - k * 0.08), k * 0.06)
    return reverb(fade_edges(x), 2.2, 0.45)


def sfx_jump():
    d = 0.3
    t = t_axis(d)
    x = bandpass(noise(d), 400, 3000) * np.exp(-((t - 0.06) / 0.05) ** 2)
    return fade_edges(x)


def sfx_land():
    d = 0.4
    x = sine(lambda t: 85 * np.exp(-t * 12) + 45, d) * env_exp(d, 0.06) + bandpass(noise(d), 200, 1500) * env_exp(d, 0.02) * 0.3
    return fade_edges(x)


def sfx_impact():
    d = 0.6
    x = sine(lambda t: 70 * np.exp(-t * 9) + 38, d) * env_exp(d, 0.12) + lowpass(noise(d), 2500) * env_exp(d, 0.04) * 0.5
    return reverb(fade_edges(x), 1.0, 0.2)


def sfx_hammer():
    d = 1.8
    clang = bell(note(57), d, 0.6, ((1, 1), (2.4, 0.6), (3.9, 0.4), (6.7, 0.25))) * 0.6
    boom = sine(lambda t: 60 * np.exp(-t * 4) + 35, d) * env_exp(d, 0.25)
    return reverb(fade_edges(mix(clang, boom)), 2.4, 0.35)


def sfx_portal():
    d = 2.6
    t = t_axis(d)
    swell = bandpass(noise(d), 300, 4000) * np.clip(t / 1.5, 0, 1) ** 2 * 0.5
    chord = sum(sine(note(n), d) * 0.2 for n in (57, 64, 69, 73)) * np.clip(t / 1.2, 0, 1)
    return reverb(fade_edges(mix(swell, chord), 0.01, 0.6), 2.5, 0.45)


def sfx_ui():
    d = 0.12
    return fade_edges(bell(note(91), d, 0.04) * 0.6 + bandpass(noise(d), 3000, 8000) * env_exp(d, 0.004) * 0.2)


def sfx_finale():
    d = 9.0
    t = t_axis(d)
    chord = np.zeros(int(d * SR))
    for n in (48, 55, 60, 64, 67, 72):
        chord += pad_voice(note(n), d, 0.003, 2500) * 0.25
    chord *= np.clip(t / 2.5, 0, 1) * np.clip((d - t) / 3.0, 0, 1)
    bells = np.zeros(int(d * SR))
    for k, n in enumerate((72, 76, 79, 84, 88)):
        place(bells, bell(note(n), 3.0, 1.6) * 0.4, 1.5 + k * 0.35)
    boom = sine(lambda tt: 50 * np.exp(-tt * 2) + 30, d) * env_exp(d, 1.2, 0.01) * 0.8
    return reverb(fade_edges(mix(chord, bells, boom)), 3.5, 0.45)


# ------------------------------------------------------------------ loops
def loop_roll():
    d = 4.0
    t = t_axis(d + 1.0)
    x = lowpass(noise(d + 1.0), 380, 2) * (0.75 + 0.25 * np.sin(2 * np.pi * 3.1 * t))
    x += bandpass(noise(d + 1.0), 800, 2500) * 0.08
    return loopify(x, 1.0)


def amb_crypt():
    d = 46.0
    t = t_axis(d)
    drone = (sine(note(33), d) * 0.5 + sine(note(40) * 1.003, d) * 0.3) * (0.7 + 0.3 * np.sin(2 * np.pi * t / 13))
    wind = bandpass(noise(d), 150, 900) * (0.5 + 0.5 * np.sin(2 * np.pi * t / 9.0) ** 2) * 0.45
    drips = np.zeros(len(t))
    for _ in range(26):
        at = rng.uniform(0.5, d - 2)
        f = rng.uniform(1100, 2200)
        drip = sine(lambda tt: f * (1 + 0.6 * np.exp(-tt * 40)), 0.25) * env_exp(0.25, 0.03, 0.001) * rng.uniform(0.15, 0.4)
        place(drips, drip, at)
    x = mix(drone * 0.6, wind, reverb(drips, 3.0, 0.6)[: len(t)])
    return loopify(x, 4.0)


def amb_forge():
    d = 46.0
    t = t_axis(d)
    rumble = lowpass(noise(d), 90, 2) * 3.0 * (0.8 + 0.2 * np.sin(2 * np.pi * t / 7))
    roar = bandpass(noise(d), 200, 1200) * 0.3 * (0.6 + 0.4 * np.sin(2 * np.pi * t / 11) ** 2)
    bubbles = np.zeros(len(t))
    for _ in range(90):
        at = rng.uniform(0.2, d - 1)
        f0 = rng.uniform(60, 140)
        b = sine(lambda tt: f0 * (1 + 2 * tt), 0.18) * env_exp(0.18, 0.05) * rng.uniform(0.2, 0.6)
        place(bubbles, b, at)
    crackle = np.zeros(len(t))
    for _ in range(400):
        at = rng.uniform(0, d - 0.05)
        place(crackle, highpass(noise(0.008), 3000) * rng.uniform(0.02, 0.15), at)
    x = mix(rumble, roar, bubbles * 0.6, crackle)
    return loopify(x, 4.0)


def amb_sky():
    d = 46.0
    t = t_axis(d)
    wind = bandpass(noise(d), 250, 2200) * (0.4 + 0.6 * np.sin(2 * np.pi * t / 12.0) ** 2) * 0.5
    padv = np.zeros(len(t))
    for n in (57, 64, 69, 71):
        padv += pad_voice(note(n), d, 0.003, 1200) * 0.12 * (0.6 + 0.4 * np.sin(2 * np.pi * t / (15 + n % 5)))
    x = mix(wind, padv)
    return loopify(x, 4.0)


def music(chords, melody, bar=4.0, bars=16, bright=1600, root_shift=0):
    d = bar * bars
    out = np.zeros(int((d + 6) * SR))
    for i in range(bars):
        ch = chords[i % len(chords)]
        seg = np.zeros(int((bar + 3) * SR))
        tt = t_axis(bar + 3)
        env = np.clip(tt / 1.2, 0, 1) * np.clip((bar + 3 - tt) / 2.5, 0, 1)
        for n in ch:
            seg += pad_voice(note(n + root_shift), bar + 3, 0.004, bright) * 0.12 * env
        seg += sine(note(ch[0] - 12 + root_shift), bar + 3) * 0.18 * env
        place(out, seg, i * bar)
    for k, (beat, n) in enumerate(melody):
        place(out, bell(note(n + root_shift), 3.0, 1.3) * 0.22, beat * bar / 4)
    out = reverb(out, 3.5, 0.45)
    return loopify(out[: int((d + 4) * SR)], 4.0)


def mus_title():
    chords = [(57, 60, 64), (53, 57, 60), (48, 52, 55, 59), (55, 59, 62)]
    melody = [(1, 76), (3, 72), (5, 74), (7, 69), (9, 76), (11, 79), (13, 74), (15, 72),
              (17, 76), (19, 72), (21, 74), (23, 69), (25, 72), (27, 71), (29, 69), (31, 64)]
    return music(chords, melody)


def mus_ending():
    chords = [(48, 52, 55), (55, 59, 62), (57, 60, 64), (53, 57, 60)]
    melody = [(1, 72), (2, 76), (3, 79), (5, 79), (6, 81), (7, 79), (9, 76), (11, 74),
              (13, 72), (15, 76), (17, 72), (18, 76), (19, 79), (21, 84), (23, 83), (25, 79),
              (27, 76), (29, 74), (31, 72)]
    return music(chords, melody, bright=2200)


SOUNDS = {
    "sfx_plate": sfx_plate, "sfx_ignite": sfx_ignite, "sfx_chime": sfx_chime, "sfx_gate": sfx_gate,
    "sfx_bridge": sfx_bridge, "sfx_smash": sfx_smash, "sfx_clone": sfx_clone, "sfx_teleport": sfx_teleport,
    "sfx_checkpoint": sfx_checkpoint, "sfx_shard": sfx_shard, "sfx_jump": sfx_jump, "sfx_land": sfx_land,
    "sfx_impact": sfx_impact, "sfx_hammer": sfx_hammer, "sfx_portal": sfx_portal, "sfx_ui": sfx_ui,
    "sfx_finale": sfx_finale, "loop_roll": loop_roll, "amb_crypt": amb_crypt, "amb_forge": amb_forge,
    "amb_sky": amb_sky, "mus_title": mus_title, "mus_ending": mus_ending,
}

if __name__ == "__main__":
    only = set(sys.argv[2].split(",")) if len(sys.argv) > 2 else None
    for name, fn in SOUNDS.items():
        if only and name not in only:
            continue
        peak = 0.7 if name.startswith(("amb_", "mus_", "loop_")) else 0.89
        save(name, fn(), peak)
