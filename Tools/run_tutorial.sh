#!/usr/bin/env bash
# EMBER - the gameplay tutorial video, step by step (game side: EMBER/Assets/_Ember/Scripts/Tools/Tutorial,
# post-production: ArtSource/Tutorial). Steps that run Unity need the Unity Editor CLOSED.
#   bash Tools/run_tutorial.sh spike             # capture test in the built player -> Build/Tutorial/spike, gates -> reports/spike.json
#   bash Tools/run_tutorial.sh dry [FROM [TO]]   # the script in editor play mode (batch, nothing rendered) -> Build/Tutorial/dry_editor
#   bash Tools/run_tutorial.sh dryplayer         # the same in the built player, headless -> Build/Tutorial/dry_player
#   bash Tools/run_tutorial.sh build             # Windows player (= Tools/run_pipeline.sh build)
#   bash Tools/run_tutorial.sh preview           # 640x360 take in a window -> Build/Tutorial/preview
#   bash Tools/run_tutorial.sh capture           # 1920x1080 take, full screen, 10-15 min: do not touch mouse or keyboard -> Build/Tutorial/final
#   bash Tools/run_tutorial.sh captions          # caption PNGs (WPF, Thai shaping) -> Build/Tutorial/captions
#   bash Tools/run_tutorial.sh mix [TAKE]        # sound: game + music bed + voice/*.wav if any -> Build/Tutorial/audio/<take>_mix.wav
#   bash Tools/run_tutorial.sh edit [TAKE]       # Blender VSE -> Deliver/EMBER_Tutorial.mp4, .srt and the narration script (preview -> Build/Tutorial)
#   bash Tools/run_tutorial.sh check [TAKE]      # MP4 probe, contact sheet, caption stills, overlap and subtitle checks
#   bash Tools/run_tutorial.sh post [TAKE]       # captions + mix + edit + check
# TAKE = final (default) or preview.
set -u
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
ROOTW="$(cd "$ROOT" && pwd -W 2>/dev/null || echo "$ROOT")"
UNITY="/c/Program Files/Unity/Hub/Editor/6000.6.9f1/Editor/Unity.exe"
BLENDER="${BLENDER:-/c/Program Files (x86)/Steam/steamapps/common/Blender/blender.exe}"
PY="$(dirname "$BLENDER")/5.3/python/bin/python.exe"
EXE="$ROOT/Build/EMBER/EMBER.exe"
T="$ROOT/ArtSource/Tutorial"
OUT="$ROOTW/Build/Tutorial"
CAPTIONS="$ROOTW/ArtSource/Tutorial/captions.json"
LOGS="$ROOT/Logs"
mkdir -p "$LOGS" "$ROOT/Build/Tutorial"
step="${1:-post}"
take="${2:-final}"

bl() { "$BLENDER" -b --factory-startup -noaudio --python-exit-code 1 "$@"; }
summary() {  # summary <take dir>: the director's verdict from log.txt
  grep -E "segment .* (PASS|FAIL|ABORTED)|WARNING|FAIL  |finished:|failed segment" "$1/log.txt" | tail -60
}

case "$step" in
  spike)
    timeout 300 "$EXE" -tutorialSpike "$OUT/spike" -screen-width 1280 -screen-height 720 -screen-fullscreen 0 -logFile "$ROOTW/Logs/tutorial_spike_player.log"
    "$PY" -I "$T/tut_spike_check.py" --take "$OUT/spike" ;;
  dry)
    extra=()
    [ -n "${2:-}" ] && extra+=(-tutorialFrom "$2")
    [ -n "${3:-}" ] && extra+=(-tutorialTo "$3")
    timeout 1800 "$UNITY" -batchmode -projectPath "$ROOTW/EMBER" -executeMethod Ember.EditorTools.TutorialTools.BatchDryRun \
      -tutorialDry "$OUT/dry_editor" -tutorialCaptions "$CAPTIONS" "${extra[@]}" -logFile "$ROOTW/Logs/tutorial_dry_editor.log"
    code=$?
    grep -E "error CS|Exception|\[Tutorial\] ABORT" "$LOGS/tutorial_dry_editor.log" | grep -v "^  at " | head -30
    summary "$ROOT/Build/Tutorial/dry_editor"
    echo "   exit code $code"; exit $code ;;
  dryplayer)
    timeout 1800 "$EXE" -batchmode -nographics -tutorialDry "$OUT/dry_player" -tutorialCaptions "$CAPTIONS" -logFile "$ROOTW/Logs/tutorial_dry_player.log"
    code=$?
    summary "$ROOT/Build/Tutorial/dry_player"
    echo "   exit code $code"; exit $code ;;
  build)
    bash "$ROOT/Tools/run_pipeline.sh" build ;;
  preview)
    timeout 3600 "$EXE" -tutorialRecord "$OUT/preview" -tutorialCaptions "$CAPTIONS" -tutorialQuality 85 -tutorialMinFreeGB 3 \
      -screen-width 640 -screen-height 360 -screen-fullscreen 0 -logFile "$ROOTW/Logs/tutorial_preview_player.log"
    code=$?
    summary "$ROOT/Build/Tutorial/preview"
    echo "   exit code $code"; exit $code ;;
  capture)
    timeout 5400 "$EXE" -tutorialRecord "$OUT/final" -tutorialCaptions "$CAPTIONS" -tutorialQuality 92 -tutorialMinFreeGB 15 \
      -tutorialRequire 1920x1080 -screen-width 1920 -screen-height 1080 -screen-fullscreen 1 -window-mode borderless \
      -logFile "$ROOTW/Logs/tutorial_capture_player.log"
    code=$?
    summary "$ROOT/Build/Tutorial/final"
    echo "   exit code $code"; exit $code ;;
  captions)
    powershell -STA -NoProfile -ExecutionPolicy Bypass -File "$T/render_captions.ps1" || exit 1 ;;
  mix)
    "$PY" -I "$T/tut_mix.py" --take "$OUT/$take" || exit 1 ;;
  edit)
    bl -P "$T/tut_edit.py" -- --take "$OUT/$take" || exit 1 ;;
  check)
    bl -P "$T/tut_check.py" -- --take "$OUT/$take" || exit 1 ;;
  post)
    bash "$0" captions && bash "$0" mix "$take" && bash "$0" edit "$take" && bash "$0" check "$take" ;;
  *)
    echo "unknown step $step"; exit 2 ;;
esac
