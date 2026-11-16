#!/usr/bin/env bash
# EMBER - full batch pipeline (Unity Editor must be CLOSED, Unity license must be active).
#   bash Tools/run_pipeline.sh            # everything
#   bash Tools/run_pipeline.sh build      # only the Windows build
set -u
UNITY="/c/Program Files/Unity/Hub/Editor/6000.6.9f1/Editor/Unity.exe"
ROOT="$(cd "$(dirname "$0")/.." && pwd -W 2>/dev/null || pwd)"
PROJ="$ROOT/EMBER"
LOGS="$ROOT/Logs"
mkdir -p "$LOGS"

run() {  # run <log name> <method> [extra args...]
  local name="$1" method="$2"; shift 2
  echo "== $name ($method)"
  # TIMEOUT (seconds, 0 = none) guards runs without -quit, such as the AutoPilot play-mode run
  timeout "${TIMEOUT:-0}" "$UNITY" -batchmode -projectPath "$PROJ" -accept-apiupdate -executeMethod "Ember.EditorTools.Pipeline.$method" -logFile "$LOGS/$name.log" "$@"
  local code=$?
  grep -E "error CS|Shader error|Exception|Compilation failed|compiler errors|\[Ember\]|\[Validator\]|\[AutoPilot\]|No valid Unity Editor license" "$LOGS/$name.log" | grep -v "^  at " | tail -60
  echo "   exit code $code"
  return $code
}

step="${1:-all}"
if [ "$step" = "all" ] || [ "$step" = "assets" ]; then run 10_assets_scenes BatchAll -quit || exit 1; fi
# scenes only (no asset regeneration), then the validator; both opt-in, not part of "all"
if [ "$step" = "scenes" ]; then run 14_scenes BatchScenes -quit || exit 1; run 15_validate BatchValidate -quit || exit 1; fi
if [ "$step" = "validate" ]; then run 15_validate BatchValidate -quit || exit 1; fi
if [ "$step" = "all" ] || [ "$step" = "capture" ]; then run 11_capture BatchCapture -quit; fi
if [ "$step" = "all" ] || [ "$step" = "autopilot" ]; then TIMEOUT=1500 run 12_autopilot BatchAutoPilot; fi
if [ "$step" = "all" ] || [ "$step" = "build" ]; then run 13_build BatchBuild -quit; fi
# scene data for the Blender trailer (ArtSource/Trailer); opt-in, not part of "all"
if [ "$step" = "trailer" ]; then run 16_trailer_export BatchTrailerExport -quit; fi
