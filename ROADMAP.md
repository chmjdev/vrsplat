# vrsplat — maintenance roadmap

Why this fork exists, and what we intend to carry in it. See `Readme.md`
for the attribution and licence position (MIT, unchanged).

The driving constraint is **Quest 3 standalone**: the Interactive suite
renders real captured training rooms in a headset with no PC attached.
Upstream reports ~72fps up to roughly **400k Gaussians** on that hardware.
That number is upstream's, not ours — it is the working assumption until we
measure a real capture on our own device, at which point this file gets the
measured figure instead.

## Already upstream (verified present — do not rebuild)

Reviewing https://github.com/MrNeRF/awesome-3D-gaussian-splatting against
this package, most of the "compression" techniques listed there are already
here, in `GaussianSplatAssetCreator` / `GaussianSplatAsset`:

- **Quality presets** with measured trade-offs, e.g. `VeryLow` ≈ 18.6×
  smaller at 32.3 PSNR, `Medium` ≈ 5.1× smaller at 47.5 PSNR.
- **Vector quantisation**: `Norm11` (4 bytes), `Norm6` (2 bytes).
- **Palette clustering**: `Cluster64k`, `Cluster32k`.

So the first job is not to add compression — it is to pick and document the
right preset for the Quest budget.

## Wanted, in priority order

1. **A documented Quest preset, and a guard. — DONE 2026-08-30**
   `Runtime/QuestBudget.cs` states the budget once, with its provenance
   attached (`MeasuredOnOurDevice` stays false until a real device run flips
   it). The creator window warns when the input PLY exceeds it, and the
   renderer inspector repeats the warning against the assigned asset, so an
   asset that arrives via version control cannot skip the guard. The preset
   guidance is in the warning text: crop with cutouts, trim with the edit
   tools, export modified PLY, re-import at `VeryLow`.

1b. **Runtime PLY loading + player-build fitness. — DONE 2026-08-31**
   Two additions, made for `Interactive/vrsimulator` (whose studio and
   Quest player must load a capture with no Editor in sight), both additive:
   - `GaussianSplatAsset.SetRuntimeData(...)` / `DisposeRuntimeData()` —
     NativeArray-backed layers parallel to the serialized TextAsset ones;
     `GaussianSplatRenderer.UpdateRessources` prefers them when present.
     The size properties (`posDataSize` …) account for both sources, so
     `HasValidAsset` holds for runtime-created assets. Packing rules the
     caller must follow are documented in vrsimulator's `SplatPly.cs`
     (Float32 pos/scale, Norm10 quat, raw float4 colour, Float16 SH table).
   - The four bare `using UnityEditor;` lines in Runtime files are now
     `#if UNITY_EDITOR`-guarded. They compiled in the editor and in
     EditMode suites while making every **player** build of a consuming
     project fail — the actual Editor API *usages* were already guarded,
     only the usings were not.

1e. **Single Pass Instanced stereo support. — OPEN**
   The render path is not stereo-aware, so it produces NOTHING in a headset
   configured for Single Pass Instanced (the Quest default, and Unity's):
   `GaussianComposite.shader` declares `Texture2D _GaussianSplatRT` and
   loads `int3(xy, 0)`, but under SPI the camera target — and therefore the
   RT allocated from `cameraTargetDescriptor` in `GaussianSplatURPFeature.
   OnCameraSetup` — is a **Texture2DArray with one slice per eye**.
   `RenderGaussianSplats.shader` likewise carries no stereo macros
   (`UNITY_VERTEX_OUTPUT_STEREO`, `UNITY_SETUP_INSTANCE_ID`), so it cannot
   route to the right slice and resolves `UNITY_MATRIX_VP` at eye 0.
   Everything else in the scene (URP lit/unlit, TMP) draws correctly, which
   is what makes this look like "the splats are broken" rather than
   "stereo is unsupported".

   **vrsimulator works around it by rendering Multi-pass** (OpenXR
   `m_renderMode: 0`), where each eye is its own pass with a plain 2D
   target and these shaders behave exactly as they do on a flat desktop.
   That costs a second pass — and a second sort per frame unless
   `m_CenterEyeOnly` is set — so proper SPI support is a real optimisation,
   to be done with the frame-rate telemetry now shipping in
   vr-session-result rather than by assumption.

   Observed on a Quest 3S, 2026-09-01: room invisible, guidance panel and
   item highlights correct.

1d. **Vulkan/Quest draw bindings. — DONE 2026-08-31**
   On Quest's Vulkan backend (the splat shader compiles through DXC), the
   structured/byte-address buffers the DRAW shader reads never arrived when
   bound via MaterialPropertyBlock — the driver logged `Shader requires a
   compute buffer "_SplatPos", but none provided. Skipping draw calls` for
   every source buffer and the room rendered as nothing, while Metal
   tolerated the identical path (first observed on a Quest 3S the day the
   first consumer APK ran). Mirroring the bindings onto the per-renderer
   material did not help either. The fix: `BindDrawGlobals` — every
   parameter the draw reads is also bound as a command-buffer GLOBAL, in
   draw order, immediately before its `DrawProcedural` (the same mechanism
   the compute side has always relied on). The MPB stays and wins wherever
   the backend honours it, with identical values.

1c. **RenderGraph port of `GaussianSplatURPFeature`.**
   Unity 6 URP runs RenderGraph by default and the feature's `GSRenderPass`
   only implements the legacy `Execute` path, so it silently draws nothing
   there — the player log says exactly this (observed in vrsimulator's
   first smoke run, 2026-08-31). vrsimulator ships with URP **Compatibility
   Mode (RenderGraph disabled)** as the workaround; the real fix is
   implementing `RecordRenderGraph` (upstream aras-p has since done this —
   candidate for pulling down via the `upstream` remote).

2. **SOG / compressed-format import.**
   PlayCanvas' SOG format reports 15–20× smaller than PLY. It is
   web-oriented, so this is real importer work rather than a flag, but the
   size win matters: captures ship inside the APK's StreamingAssets, and
   load time and download size are both real UAT costs.

3. **An LOD ladder.**
   AURA-style progressive levels, so a room can hold detail near the
   trainee and shed it at distance instead of being decimated uniformly.
   This is what would let a capture be larger than the flat budget allows.

4. **Author-facing decimation guidance.**
   `SplatTransform` (converts formats, emits LOD) is the practical tool for
   getting a raw capture down to budget. Document the exact invocation we
   use rather than telling authors to "decimate".

## Ground rules for this fork

- **Stay mergeable.** Prefer additive changes; keep upstream's file layout
  so `upstream` can still be pulled.
- **MIT preserved.** Original copyright and attribution stay untouched.
- **Measured, not assumed.** Performance claims in this repo carry the
  device and the method, or they are labelled as upstream's numbers.
