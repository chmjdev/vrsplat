#!/usr/bin/env bash
# Compile-verify the vrflatscore package, and check the behaviour that reading
# the source cannot settle. See docs/verification.md.
#
# Builds a THROWAWAY Unity project in a temp directory, points it at this
# package by file: path, and runs tools/verify/PackageVerify.cs in batchmode.
# Nothing in this repository is modified.
#
#   ./tools/verify/run.sh
#   VRFLATSCORE_UNITY=/path/to/Unity ./tools/verify/run.sh
#   VRFLATSCORE_VERIFY_DIR=/tmp/keepme ./tools/verify/run.sh   # keep the project
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PACKAGE="$(cd "$HERE/../.." && pwd)/package"
[ -f "$PACKAGE/package.json" ] || { echo "no package at $PACKAGE" >&2; exit 2; }

# --- the editor -------------------------------------------------------------
# Pinned to what the estate builds with. Override to check another version;
# what it says about THAT version is all the result then means.
UNITY_VERSION="${VRFLATSCORE_UNITY_VERSION:-6000.3.22f1}"
UNITY="${VRFLATSCORE_UNITY:-/Applications/Unity/Hub/Editor/$UNITY_VERSION/Unity.app/Contents/MacOS/Unity}"
if [ ! -x "$UNITY" ]; then
    echo "Unity not found at: $UNITY" >&2
    echo "Installed:" >&2
    ls /Applications/Unity/Hub/Editor 2>/dev/null | sed 's/^/  /' >&2 || echo "  (none)" >&2
    echo "Set VRFLATSCORE_UNITY to an editor binary." >&2
    exit 2
fi

# --- the throwaway project --------------------------------------------------
KEEP=1
if [ -z "${VRFLATSCORE_VERIFY_DIR:-}" ]; then
    VRFLATSCORE_VERIFY_DIR="$(mktemp -d -t vrflatscore-verify)"
    KEEP=0
fi
PROJ="$VRFLATSCORE_VERIFY_DIR/VerifyProject"
LOG="$VRFLATSCORE_VERIFY_DIR/verify.log"
mkdir -p "$PROJ/Assets/Editor" "$PROJ/Packages" "$PROJ/ProjectSettings"
# Must return 0. Bash lets a failing last command in an EXIT trap overwrite the
# script's exit status -- measured 2026-09-09: a script ending `exit 0` with a
# trap whose last test was false exited 1. A verifier that reports failure on
# success is worse than none.
cleanup() {
    if [ "$KEEP" = 0 ]; then rm -rf "$VRFLATSCORE_VERIFY_DIR"; fi
    return 0
}
trap cleanup EXIT

cat > "$PROJ/ProjectSettings/ProjectVersion.txt" <<EOF
m_EditorVersion: $UNITY_VERSION
m_EditorVersionWithRevision: $UNITY_VERSION
EOF

# URP is what makes GS_ENABLE_URP and GS_URP_RENDERGRAPH fire; without it the
# whole URP feature compiles out and the check would pass while proving nothing.
cat > "$PROJ/Packages/manifest.json" <<EOF
{
  "dependencies": {
    "com.unity.render-pipelines.universal": "${VRFLATSCORE_URP_VERSION:-17.3.0}",
    "com.unity.burst": "1.8.24",
    "com.unity.collections": "2.5.7",
    "com.unity.mathematics": "1.3.2",
    "com.binteca.vrflatscore": "file:$PACKAGE",
    "com.unity.modules.jsonserialize": "1.0.0",
    "com.unity.modules.imageconversion": "1.0.0",
    "com.unity.modules.ui": "1.0.0",
    "com.unity.modules.imgui": "1.0.0",
    "com.unity.modules.uielements": "1.0.0",
    "com.unity.modules.physics": "1.0.0",
    "com.unity.modules.vr": "1.0.0",
    "com.unity.modules.xr": "1.0.0"
  }
}
EOF

cp "$HERE/PackageVerify.cs" "$PROJ/Assets/Editor/PackageVerify.cs"

echo "editor:  $UNITY"
echo "package: $PACKAGE"
echo "project: $PROJ"
echo "Resolving packages and compiling; first run downloads URP and takes several minutes."

set +e
"$UNITY" -batchmode -nographics -projectPath "$PROJ" \
         -executeMethod PackageVerify.Run -logFile "$LOG"
UNITY_EXIT=$?
set -e

# --- read the log, not the exit code ---------------------------------------
# Measured 2026-09-09 on 6000.3.22f1: a run whose harness called
# EditorApplication.Exit(1) still returned 0 to the shell. The exit code is
# reported below for information and is deliberately NOT what decides this.
ERRORS=$(grep -c "error CS" "$LOG" || true)
EXPR=$(grep -c "ExpressionNotValidException" "$LOG" || true)

echo
grep "\[verify\]" "$LOG" || true
echo
echo "unity exit code: $UNITY_EXIT (informational)"
echo "compile errors:  $ERRORS"
echo "asmdef version-define parse errors: $EXPR"
if [ "$KEEP" = 1 ]; then echo "log kept at: $LOG"; fi

FAIL=0
if [ "$ERRORS" -ne 0 ]; then FAIL=1; fi
if [ "$EXPR"   -ne 0 ]; then FAIL=1; fi
if ! grep -q "\[verify\] RESULT ALL PASS" "$LOG"; then FAIL=1; fi
# An absent RESULT line means the harness never ran -- compilation failed, or
# -executeMethod could not resolve. Either way it is not a pass.

if [ "$FAIL" = 0 ]; then
    echo "VERIFY: PASS"
else
    echo "VERIFY: FAIL"
    grep -n "error CS\|ExpressionNotValidException" "$LOG" | head -20 || true
fi
exit "$FAIL"
