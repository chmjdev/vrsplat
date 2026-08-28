#!/usr/bin/env bash
# Reconstruct a room on a remote NVIDIA GPU and bring the result home.
#
#   ./remote-train.sh <frames-dir> <sceneId>
#
# Provider-agnostic: RunPod, Vast.ai, an EC2 g5, or a workstation under
# someone's desk are all just an SSH target. See README.md for the licence
# boundary (LichtFeld is GPLv3 and stays a remote process) and the budget.
set -euo pipefail

FRAMES="${1:-}"
SCENE_ID="${2:-}"
if [ -z "$FRAMES" ] || [ -z "$SCENE_ID" ]; then
  echo "usage: $0 <frames-dir> <sceneId>" >&2
  exit 2
fi
[ -d "$FRAMES" ] || { echo "ERROR: no such frames directory: $FRAMES" >&2; exit 2; }

: "${VRSPLAT_REMOTE_HOST:?set VRSPLAT_REMOTE_HOST=user@gpu-box (see README.md)}"
REMOTE_WORK="${VRSPLAT_REMOTE_WORKDIR:-\$HOME/vrsplat-work}"
SSH_OPTS=()
[ -n "${VRSPLAT_REMOTE_KEY:-}" ] && SSH_OPTS=(-i "$VRSPLAT_REMOTE_KEY")

# The trainer's exact flags are deliberately overridable. LichtFeld's public
# docs describe a headless workflow but do not pin the CLI surface, so this
# default is a STARTING POINT to confirm on first run rather than something
# verified here — override it instead of editing this script:
#
#   VRSPLAT_TRAIN_CMD='lichtfeld-studio --headless --data {DATA} --output {OUT}'
TRAIN_CMD="${VRSPLAT_TRAIN_CMD:-lichtfeld-studio --headless --data {DATA} --output {OUT}}"

# Where the finished capture belongs — exactly what CapturedRoomLoader
# already reads, so the pipeline ends where the runtime begins.
SUITE_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
DEST="$SUITE_ROOT/unityvrlabs/Assets/StreamingAssets/captures/$SCENE_ID"

echo "=== 1/4  uploading frames -> $VRSPLAT_REMOTE_HOST ==="
# Frames stay remote; only the reconstruction travels back.
ssh "${SSH_OPTS[@]}" "$VRSPLAT_REMOTE_HOST" "mkdir -p $REMOTE_WORK/$SCENE_ID/frames"
rsync -az --info=progress2 ${VRSPLAT_REMOTE_KEY:+-e "ssh -i $VRSPLAT_REMOTE_KEY"} \
  "$FRAMES"/ "$VRSPLAT_REMOTE_HOST:$REMOTE_WORK/$SCENE_ID/frames/"

echo "=== 2/4  COLMAP poses (required: LichtFeld trains from a COLMAP dataset) ==="
ssh "${SSH_OPTS[@]}" "$VRSPLAT_REMOTE_HOST" bash -s <<REMOTE
set -euo pipefail
cd "$REMOTE_WORK/$SCENE_ID"
if [ ! -d sparse ]; then
  colmap automatic_reconstructor --workspace_path . --image_path frames --dense 0
else
  echo "sparse/ already present — reusing existing poses"
fi
REMOTE

echo "=== 3/4  training on the remote GPU ==="
TRAIN_RESOLVED="${TRAIN_CMD//\{DATA\}/$REMOTE_WORK/$SCENE_ID}"
TRAIN_RESOLVED="${TRAIN_RESOLVED//\{OUT\}/$REMOTE_WORK/$SCENE_ID/out}"
echo "    $TRAIN_RESOLVED"
ssh "${SSH_OPTS[@]}" "$VRSPLAT_REMOTE_HOST" \
  "mkdir -p $REMOTE_WORK/$SCENE_ID/out && $TRAIN_RESOLVED"

echo "=== 4/4  retrieving room.ply ==="
# PLY specifically: the Unity importer reads PLY. LichtFeld can also emit
# SOG/SPZ, which are far smaller but not importable yet (ROADMAP.md item 2).
mkdir -p "$DEST"
rsync -az ${VRSPLAT_REMOTE_KEY:+-e "ssh -i $VRSPLAT_REMOTE_KEY"} \
  "$VRSPLAT_REMOTE_HOST:$REMOTE_WORK/$SCENE_ID/out/"*.ply "$DEST/room.ply"

if [ ! -f "$DEST/capture.json" ]; then
  cat > "$DEST/capture.json" <<JSON
{
  "asset": "room.ply",
  "position": {"x": 0, "y": 0, "z": 0},
  "rotation": {"x": 0, "y": 0, "z": 0},
  "scale": 1.0,
  "notes": "$SCENE_ID captured $(date +%Y-%m-%d), trained remotely",
  "enabled": true
}
JSON
  echo "    wrote a starting capture.json — alignment WILL need adjusting"
fi

SIZE=$(du -h "$DEST/room.ply" | cut -f1)
cat <<DONE

Done. $DEST/room.ply ($SIZE)

Next, and none of it is skippable:
  1. Convert in Unity: Tools > Gaussian Splats > Create GaussianSplatAsset
     (pick a quality preset with the ~400k Gaussian Quest budget in mind).
  2. Align: nudge capture.json until the floor sits at y=0 and the room
     faces -Z. A reconstruction never lands aligned by luck.
  3. Check it in the headset against the equipment positions.

The GPU box is still running and still billing. Shutting it down is your
call to make on the provider's console — this script will not destroy an
instance that may still hold the only copy of a capture.
DONE
