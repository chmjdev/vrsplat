#!/usr/bin/env bash
# Pull a room scan off a Quest and hand it to remote-train.sh.
#
#   ./pull-scan.sh                 # newest scan, list if ambiguous
#   ./pull-scan.sh <sceneId>       # a specific scan
#   ./pull-scan.sh --list          # what is on the headset
#
# RoomScanner.cs writes to the app's own persistentDataPath, which adb can read
# without root because it is app-scoped external storage.
set -euo pipefail

PKG="${VRFLATSCORE_SCAN_PACKAGE:-com.binteca.vrflatscore.demo}"
DEST_ROOT="${VRFLATSCORE_SCAN_DIR:-$HOME/vrflatscore-scans}"
REMOTE_ROOT="/sdcard/Android/data/$PKG/files/scans"

ADB="${VRFLATSCORE_ADB:-adb}"
command -v "$ADB" >/dev/null || { echo "ERROR: adb not found (set VRFLATSCORE_ADB)" >&2; exit 2; }

DEV_ARGS=()
[ -n "${VRFLATSCORE_DEVICE:-}" ] && DEV_ARGS=(-s "$VRFLATSCORE_DEVICE")
# bash 3.2 (macOS default) errors on "${DEV_ARGS[@]}" when the array is empty
# under `set -u`, so every use below takes the ${arr[@]+"${arr[@]}"} form.

devices=$("$ADB" devices | awk 'NR>1 && $2=="device" {print $1}')
if [ -z "$devices" ]; then
    echo "ERROR: no headset visible to adb." >&2
    echo "  If it is plugged in, it is usually asleep or USB debugging was revoked:" >&2
    echo "  wear it, accept the 'Allow USB debugging' prompt, then retry." >&2
    exit 2
fi

list_scans() { "$ADB" ${DEV_ARGS[@]+"${DEV_ARGS[@]}"} shell ls "$REMOTE_ROOT" 2>/dev/null | tr -d '\r' | grep -v '^$' || true; }

if [ "${1:-}" = "--list" ]; then
    echo "scans in $REMOTE_ROOT:"; list_scans | sed 's/^/  /'; exit 0
fi

SCENE_ID="${1:-}"
if [ -z "$SCENE_ID" ]; then
    # Names are scan-YYYYMMDD-HHMMSS, so lexical sort is chronological.
    SCENE_ID=$(list_scans | sort | tail -1)
    [ -n "$SCENE_ID" ] || { echo "ERROR: no scans found in $REMOTE_ROOT" >&2; exit 2; }
    echo "newest scan: $SCENE_ID"
fi

DEST="$DEST_ROOT/$SCENE_ID"
mkdir -p "$DEST"
echo "=== pulling $SCENE_ID -> $DEST ==="
"$ADB" ${DEV_ARGS[@]+"${DEV_ARGS[@]}"} pull "$REMOTE_ROOT/$SCENE_ID/." "$DEST" >/dev/null

FRAMES="$DEST/frames"
[ -d "$FRAMES" ] || { echo "ERROR: no frames/ in the pulled scan" >&2; exit 1; }
COUNT=$(find "$FRAMES" -name '*.jpg' | wc -l | tr -d ' ')

echo
echo "frames:   $COUNT"
[ -f "$DEST/scan.json" ] && { echo "manifest:"; sed 's/^/  /' "$DEST/scan.json"; } \
                         || echo "manifest: ABSENT - the scan did not stop cleanly; frames may still be usable"
[ -f "$DEST/poses.jsonl" ] && echo "poses:    $(wc -l < "$DEST/poses.jsonl" | tr -d ' ') head poses (priors only)"

# COLMAP needs overlapping views from a moving camera. Too few frames is the
# most common reason reconstruction fails, and it fails slowly and remotely.
if [ "$COUNT" -lt 40 ]; then
    echo
    echo "WARNING: $COUNT frames is probably too few for COLMAP to solve."
    echo "  Walk the room again, slower, with generous overlap and several heights."
fi

echo
echo "next:"
echo "  VRFLATSCORE_REMOTE_HOST=user@gpu-box ./tools/capture/remote-train.sh \"$FRAMES\" \"$SCENE_ID\""
