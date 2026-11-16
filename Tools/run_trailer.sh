#!/usr/bin/env bash
# EMBER - the Blender trailer, step by step (see ArtSource/Trailer/*.py).
#   bash Tools/run_trailer.sh all          # thai -> build -> stills -> music -> final render -> edit -> check -> docs (not export)
#   bash Tools/run_trailer.sh export       # Unity scene data (Unity must be CLOSED) -> ArtSource/Trailer/export
#   bash Tools/run_trailer.sh thai         # Thai title PNG (WPF shaping)
#   bash Tools/run_trailer.sh build        # ArtSource/Trailer/EMBER_Trailer.blend (scenes, animation, titles)
#   bash Tools/run_trailer.sh lookdev      # stills from every room's hero camera next to the game screenshots
#   bash Tools/run_trailer.sh stills       # 3 stills per shot -> Build/Trailer/stills
#   bash Tools/run_trailer.sh animatic     # quarter-size, low-sample full-length cut -> Build/Trailer/EMBER_Trailer_animatic.mp4
#   bash Tools/run_trailer.sh music        # score + sound mix -> Build/Trailer/audio
#   bash Tools/run_trailer.sh final        # 1920x1080 frames (resumable) -> Build/Trailer/final
#   bash Tools/run_trailer.sh edit         # final edit -> Deliver/EMBER_Trailer.mp4
#   bash Tools/run_trailer.sh check        # probe the MP4 (frames, fps, size, audio) -> Build/Trailer/reports/mp4_probe.json
#   bash Tools/run_trailer.sh docs         # posters and a contact sheet of the edit -> Docs/Trailer
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BLENDER="${BLENDER:-/c/Program Files (x86)/Steam/steamapps/common/Blender/blender.exe}"
PY="$(dirname "$BLENDER")/5.3/python/bin/python.exe"
T="$ROOT/ArtSource/Trailer"
BLEND="$T/EMBER_Trailer.blend"
step="${1:-all}"

bl() { "$BLENDER" -b --factory-startup -noaudio --python-exit-code 1 "$@"; }
blf() { "$BLENDER" -b "$BLEND" -noaudio --python-exit-code 1 "$@"; }

if [ "$step" = "export" ]; then bash "$ROOT/Tools/run_pipeline.sh" trailer || exit 1; fi
if [ "$step" = "all" ] || [ "$step" = "thai" ]; then
  powershell -STA -NoProfile -ExecutionPolicy Bypass -File "$T/render_thai_title.ps1" || exit 1
fi
if [ "$step" = "all" ] || [ "$step" = "build" ]; then
  bl -P "$T/tr_build.py" -- --envs Title,Stage1,Stage2,Stage3 --shots --save || exit 1
fi
if [ "$step" = "lookdev" ]; then
  bl -P "$T/tr_build.py" -- --lookdev "$ROOT/Build/Trailer/lookdev" --scale 50 --samples 32 || exit 1
fi
if [ "$step" = "all" ] || [ "$step" = "stills" ]; then
  blf -P "$T/tr_render.py" -- --profile stills --sample 3 || exit 1
fi
if [ "$step" = "all" ] || [ "$step" = "music" ] || [ "$step" = "animatic" ]; then
  "$PY" "$ROOT/ArtSource/Audio/trailer_music.py" || exit 1
fi
if [ "$step" = "animatic" ]; then
  blf -P "$T/tr_render.py" -- --profile animatic --titles yes || exit 1
  bl -P "$T/tr_edit.py" -- --profile animatic || exit 1
fi
if [ "$step" = "all" ] || [ "$step" = "final" ]; then
  blf -P "$T/tr_render.py" -- --profile final --titles yes || exit 1
fi
if [ "$step" = "all" ] || [ "$step" = "edit" ]; then
  bl -P "$T/tr_edit.py" -- --profile final --out "$ROOT/Deliver/EMBER_Trailer.mp4" || exit 1
fi
if [ "$step" = "all" ] || [ "$step" = "check" ]; then
  bl -P "$T/tr_check.py" -- --mp4 "$ROOT/Deliver/EMBER_Trailer.mp4" || exit 1
fi
if [ "$step" = "all" ] || [ "$step" = "docs" ]; then
  rm -rf "$ROOT/Build/Trailer/edit_stills_final"
  bl -P "$T/tr_edit.py" -- --profile final --stills 48 --posters yes || exit 1
  bl -P "$T/tr_check.py" -- --mp4 "$ROOT/Deliver/EMBER_Trailer.mp4" --sheet "$ROOT/Build/Trailer/edit_stills_final" || exit 1
fi
