# Capture pipeline: room → walkable splat

Turns a 360 walkthrough of a real room into a capture the Interactive suite
can render on a Quest. Reconstruction runs on a **remote NVIDIA GPU**; only
the finished file comes home.

## Why remote

[LichtFeld Studio](https://github.com/MrNeRF/LichtFeld-Studio) needs an
NVIDIA GPU of compute capability 7.5+ and runs on Windows/Linux only. Apple
Silicon cannot train locally at all, so a rented GPU box is not a
convenience here — it is the only option.

## Licence boundary — read before changing this

**LichtFeld Studio is GPLv3. This repository is MIT.** The pipeline runs it
as a **separate process on a remote machine and consumes the files it
writes**. Program output is not a derivative work, so that arrangement is
clean.

Do not link, vendor, or copy LichtFeld source into `vrflatscore` or into
any application that ships it — that would pull GPLv3 across an MIT
package and a shipped application. Anything the pipeline needs from it
goes over SSH, never over a compiler.

## Getting LichtFeld onto the box

Prebuilt **Windows** binaries are behind a paid portal; **Linux** is a
source build needing CUDA 12.8+, driver 570+ and a C++23 toolchain. On a
rented Linux GPU that build is the slow part of first setup — do it once and
snapshot the image, rather than rebuilding per session.

It exports **PLY, SOG, SPZ** and a standalone HTML viewer. Export **PLY**:
SOG/SPZ are far smaller but our importer cannot read them yet
(`ROADMAP.md` item 2), so choosing them now means a file we cannot ship.

It also exposes **Python plugins and an MCP interface**, which is the
cleaner long-term automation path than driving a CLI over SSH — worth
revisiting once the manual pipeline has actually produced a room.

## The steps

    scan  →  frames  →  COLMAP poses  →  LichtFeld training  →  room.ply  →  Unity asset  →  captures/<sceneId>/

1. **Shoot — now has a tool.** `unity/RoomScanner.cs` captures posed frames
   from the Quest's own passthrough cameras; `pull-scan.sh` brings them home
   in the shape step 2 wants. See [Scanning on the headset](#scanning-on-the-headset).
   Any camera still works — the rest of this pipeline only needs a directory
   of overlapping JPEGs — but the headset is the one you are already wearing.

   However you shoot: walk the room slowly, overlap generously, cover several
   heights, and avoid motion blur. Blurry frames poison pose estimation.
2. **Poses.** COLMAP (on the same remote box). LichtFeld trains *from a
   COLMAP dataset*, so this step is required, not optional.
3. **Train.** LichtFeld Studio, headless, on the remote GPU.
4. **Bring home `room.ply`.** Only the result travels; frames stay remote.
5. **Convert.** Unity: `Tools ▸ Gaussian Splats ▸ Create GaussianSplatAsset`.
   **The importer reads PLY** (`Input PLY File`) — `.spz` is not supported
   yet; see `ROADMAP.md` item 2. Export PLY from LichtFeld for now.
6. **Place and align.** Drop into
   `unityvrlabs/Assets/StreamingAssets/captures/<sceneId>/` with a
   `capture.json`. Alignment always needs work — see that folder's README.


## Scanning on the headset

`unity/RoomScanner.cs` is a drop-in MonoBehaviour, deliberately **not** part of
the `package/` — this is capture tooling, and the rendering package stays a
rendering package with no camera permissions in it.

    right trigger  →  start scan
    right trigger  →  stop scan

Frames are written at 3 fps to the app's own `persistentDataPath`, alongside
`poses.jsonl` (one head pose per frame) and a `scan.json` manifest written
last, so its presence means the scan finished cleanly.

Then:

    ./pull-scan.sh --list                    # what is on the headset
    ./pull-scan.sh                           # newest scan
    ./pull-scan.sh scan-20260910-021500      # a specific one

It reports the frame count and warns below 40 frames, which is the usual
reason a reconstruction fails — slowly, remotely, and after you have paid for
the GPU time.

### What it needs, and what it deliberately does not use

| Requirement | Why |
| :---------- | :-- |
| Horizon OS **v74+**, Quest **3 / 3S** | Passthrough Camera Access exists nowhere else. Quest Pro and earlier are out |
| **Passthrough feature enabled** | PCA is gated on it |
| `horizonos.permission.HEADSET_CAMERA` | Passthrough cameras only. `android.permission.CAMERA` would also grant the avatar camera, which this has no business touching |
| `com.unity.modules.audio` | **Not a typo.** `WebCamTexture` is forwarded to `UnityEngine.AudioModule`; without it the file fails to compile with `CS1069` |
| A physical headset | PCA does not work in XR Simulator |

**It uses `WebCamTexture`, not Meta's `PassthroughCameraAccess`.** The current
Meta component ships in MRUK v81+, which is Meta XR SDK; this repository is an
MIT package on pure Unity OpenXR and the licence boundary above exists to keep
vendor code out of the compiler. The component Meta replaced was itself a
wrapper around stock `WebCamTexture`, so the SDK-free path is to use it
directly.

**What that costs:** no camera intrinsics or extrinsics from the API. It does
not block this pipeline, because `remote-train.sh` runs
`colmap automatic_reconstructor`, which solves intrinsics from the images. The
head poses in `poses.jsonl` are recorded for later metric-scale and gravity
alignment — as **priors, never as fixed truth**, which is the same stance the
suite's posed-capture work already takes.

## Budget

Upstream reports ~72fps up to roughly **400k Gaussians** on Quest 3 (their
figure, not yet measured on ours). Pick a quality preset with that in mind:
the importer already ships `VeryLow` (~18.6× smaller) and `Medium` (~5.1×).
A room fits; a building does not.

## Configuration

Estate convention: every variable is project-prefixed, and none are
committed.

    VRFLATSCORE_REMOTE_HOST     user@host of the GPU box
    VRFLATSCORE_REMOTE_KEY      ssh key path            (optional)
    VRFLATSCORE_REMOTE_WORKDIR  remote scratch dir      (default ~/vrflatscore-work)
    VRFLATSCORE_CAPTURE_DIR     where room.ply lands    (default <repo>/captures)
    VRFLATSCORE_SCAN_DIR        where pulled scans land (default ~/vrflatscore-scans)
    VRFLATSCORE_SCAN_PACKAGE    scanning app's package  (default com.binteca.vrflatscore.demo)
    VRFLATSCORE_DEVICE          adb serial              (optional, for >1 headset)
    VRFLATSCORE_ADB             adb binary              (optional)

**`VRFLATSCORE_CAPTURE_DIR` replaced a hardcoded sibling path.** Until
2026-09-10 `remote-train.sh` wrote its result into
`unityvrlabs/Assets/StreamingAssets/captures/`, a project that is not present
beside this one — so the final step of a paid remote training run landed in a
directory that did not exist. The default is now a `captures/` directory inside
this repository, which always does. Point the variable at a consuming
project's StreamingAssets when there is one.

The GPU box is **rented, ephemeral compute — not estate infrastructure**.
It gets no `jcds.config` entry and no DNS name; JCDS supervises services,
and this is a batch job that should not outlive its run.

`remote-train.sh` never destroys the instance. Rented GPUs bill by the
minute, so shutting one down is worth doing — but it is a
money-and-data-destroying action, so it stays a decision a person makes
deliberately, on their provider's console, after the result is safely home.
