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

Do not link, vendor, or copy LichtFeld source into `vrsplat` or into
`unityvrlabs` — that would pull GPLv3 across an MIT package and a shipped
application. Anything the pipeline needs from it goes over SSH, never over
a compiler.

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

    frames  →  COLMAP poses  →  LichtFeld training  →  room.ply  →  Unity asset  →  captures/<sceneId>/

1. **Shoot.** Walk the room slowly, overlapping generously, several heights.
   Avoid motion blur — blurry frames poison pose estimation.
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

## Budget

Upstream reports ~72fps up to roughly **400k Gaussians** on Quest 3 (their
figure, not yet measured on ours). Pick a quality preset with that in mind:
the importer already ships `VeryLow` (~18.6× smaller) and `Medium` (~5.1×).
A room fits; a building does not.

## Configuration

Estate convention: every variable is project-prefixed, and none are
committed.

    VRSPLAT_REMOTE_HOST     user@host of the GPU box
    VRSPLAT_REMOTE_KEY      ssh key path            (optional)
    VRSPLAT_REMOTE_WORKDIR  remote scratch dir      (default ~/vrsplat-work)

The GPU box is **rented, ephemeral compute — not estate infrastructure**.
It gets no `jcds.config` entry and no DNS name; JCDS supervises services,
and this is a batch job that should not outlive its run.

`remote-train.sh` never destroys the instance. Rented GPUs bill by the
minute, so shutting one down is worth doing — but it is a
money-and-data-destroying action, so it stays a decision a person makes
deliberately, on their provider's console, after the result is safely home.
