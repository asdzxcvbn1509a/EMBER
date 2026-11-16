"""EMBER trailer - the cut. Pure data (no bpy): shared by the Blender scripts and the music script.

Grid: 24 fps, 96 BPM -> 1 beat = 15 frames, 1 bar = 60 frames; 36 bars = 2160 frames = 90 s.
Frames are global trailer frames (1-based) and are used unchanged in every environment scene.
"""
FPS = 24
BPM = 96
BEAT = 15
BAR = 60
TOTAL = 2160


def B(bar, beat=0):
    """First frame of `bar` (1-based) plus `beat` beats."""
    return 1 + (bar - 1) * BAR + int(round(beat * BEAT))


def sec(frame):
    """Scene time (seconds) of a frame; GN Scene Time and scene['t'] both use frame / FPS."""
    return frame / FPS


# acts: (first frame, last frame)
ACTS = {
    "cold": (B(1), B(5) - 1),
    "crypt": (B(5), B(13) - 1),
    "forge": (B(13), B(21) - 1),
    "sky": (B(21), B(29) - 1),
    "finale": (B(29), B(33) - 1),
    "logo": (B(33), TOTAL),
}


def shot(id_, env, start, beats, desc):
    return {"id": id_, "env": env, "start": start, "end": start + int(round(beats * BEAT)) - 1, "desc": desc}


SHOTS = [
    # cold open: text card C1 is title-only (no 3D render) 1-75
    shot("S01", "Title", B(2, 1), 11, "a lone spark falls down the moonbeam onto the altar; the braziers wake"),
    # Act I - the Sunken Crypt (32 beats)
    shot("S02", "Stage1", B(5), 5, "low tracking shot down the candle corridor"),
    shot("S03", "Stage1", B(6, 1), 8, "plate, lantern, the plank bridge rises from the mist"),
    shot("S04", "Stage1", B(8, 1), 6, "jump onto the moonlit ledge, the gate rises"),
    shot("S05", "Stage1", B(9, 3), 7, "Hall of Wardens: last seal bursts, braziers, eyes turn amber"),
    shot("S06", "Stage1", B(11, 2), 6, "Moonwell orbit under the moonbeam"),
    # Act II - the Ember Forge
    shot("S07", "Stage2", B(13), 5, "lava river and lavafall"),
    shot("S08", "Stage2", B(14, 1), 7, "the ember rides a basalt raft"),
    shot("S09", "Stage2", B(16), 8, "iron ball down the brass chute smashes the cracked wall"),
    shot("S10", "Stage2", B(18), 6, "flaming boulders roll past the ember"),
    shot("S11", "Stage2", B(19, 2), 6, "counterweight on the scale, forge doors open, six fires ignite"),
    # Act III - the Sky Sanctum
    shot("S12", "Stage3", B(21), 8, "reveal: floating island over the cloud sea at night"),
    shot("S13", "Stage3", B(23), 5, "through the gold ring, out on the far island"),
    shot("S14", "Stage3", B(24, 1), 6, "the turning bridge swings into place among the orrery"),
    shot("S15a", "Stage3", B(25, 3), 8 / 3, "lantern of echoes"),
    shot("S15b", "Stage3", B(25, 3) + 40, 8 / 3, "lantern of the forge"),
    shot("S15c", "Stage3", B(25, 3) + 80, 8 / 3, "lantern of winds, three beams meet at the beacon"),
    shot("S16", "Stage3", B(27, 3), 5, "up the risen stairs to the beacon"),
    # finale and logo
    shot("S17", "Stage3", B(29), 16, "ignition: night turns to dawn, pull back to the golden vista"),
    shot("S18", "Title", B(33), 16, "the EMBER logo above the altar"),
]

SHOT = {s["id"]: s for s in SHOTS}

# on-screen text (English, the game's own strings where they exist). style: "card" (centre),
# "stage" (stage title card: amber title + pale subtitle), "logo" (end card, Thai line is an image)
TITLES = [
    dict(id="T_cold1", style="card", lines=["THE BEACON OF DAWN HAS GONE OUT"], start=B(1, 0.5), end=B(2, 1) - 4),
    dict(id="T_cold2", style="card", lines=["ONLY ONE EMBER REMAINS"], start=B(4, 1), end=B(5) - 4),
    dict(id="T_act1", style="stage", lines=["I  -  THE SUNKEN CRYPT", "WHERE THE LAST EMBER AWAKENS"], start=B(5, 1), end=B(6, 3)),
    dict(id="T_light", style="card", lines=["LIGHT OPENS THE WAY"], start=B(12, 0), end=B(13) - 4),
    dict(id="T_act2", style="stage", lines=["II  -  THE EMBER FORGE", "FIRE MOVES WHAT THE EMBER CANNOT"], start=B(13, 0.5), end=B(14, 3)),
    dict(id="T_act3", style="stage", lines=["III  -  THE SKY SANCTUM", "LIGHT THE THREE LANTERNS OF DAWN"], start=B(21, 1), end=B(22, 3)),
    dict(id="T_dawn", style="card", lines=["REKINDLE THE DAWN"], start=B(31, 1), end=B(32, 3)),
    dict(id="T_logo", style="logo", lines=["THE LAST EMBER"], start=B(34, 1), end=TOTAL),
]

# musical hit points (frames) the score lines up with
HITS = {
    "landing": B(4),            # the spark lands on the altar
    "crypt": B(5),
    "bridge": B(7),
    "gate": B(9, 1),
    "wardens": B(10, 2),
    "forge": B(13),
    "smash": B(17, 2),
    "lamps": B(20, 1),
    "sky": B(21),
    "teleport": B(23, 2),
    "lantern1": B(25, 3) + 8,
    "lantern2": B(25, 3) + 48,
    "lantern3": B(25, 3) + 88,
    "ignite": B(29) + 8,
    "flash": B(33) - 18,
    "logo": B(34),
}

# fades in the edit: (frame, frames, colour) - dips between acts, white flash into the logo, fade out at the end
FADES = [
    dict(kind="in", start=1, length=1, color=(0, 0, 0)),
    dict(kind="dip", at=B(5), length=6, color=(0, 0, 0)),
    dict(kind="dip", at=B(13), length=6, color=(0, 0, 0)),
    dict(kind="dip", at=B(21), length=8, color=(0, 0, 0)),
    dict(kind="flash", at=B(33), length=36, color=(1.0, 0.95, 0.88)),
    dict(kind="out", start=TOTAL - 30, length=30, color=(0, 0, 0)),
]

# poster frames of the finished edit -> Docs/Trailer/<name>.png (one per act, the dawn card, the end card)
POSTERS = [("poster_crypt", 600), ("poster_forge", 1060), ("poster_sky", 1290), ("poster_dawn", 1890), ("poster_logo", 2110)]


def check():
    """Shots must tile their acts without gaps or overlaps (title-only cold open excepted)."""
    errs = []
    prev = None
    for s in SHOTS:
        if prev is not None and s["start"] != prev["end"] + 1:
            errs.append(f"{prev['id']} -> {s['id']}: gap/overlap {prev['end']} .. {s['start']}")
        prev = s
    if SHOTS[-1]["end"] != TOTAL:
        errs.append(f"last shot ends at {SHOTS[-1]['end']}, expected {TOTAL}")
    return errs


if __name__ == "__main__":
    for s in SHOTS:
        print(f"{s['id']:5s} {s['env']:7s} {s['start']:5d}-{s['end']:5d} ({(s['end'] - s['start'] + 1) / FPS:4.2f}s)  {s['desc']}")
    print("check:", check() or "ok")
