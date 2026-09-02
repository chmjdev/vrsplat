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

1e. **Single Pass Instanced stereo. — UNVERIFIED, currently Multi-pass**
   vrsimulator renders Multi-pass (OpenXR `m_renderMode: 0`). An earlier
   draft of this entry asserted the shaders "produce NOTHING" under Single
   Pass Instanced because the composite reads a `Texture2D` and the splat
   vertex shader carries no stereo macros. That reasoning is plausible and
   the shaders really do lack the macros — but the invisible room it was
   explaining turned out to be the spawn position (below), and SPI has NOT
   been re-tested since. Treat it as an open measurement, not a known
   defect: switch back to SPI, capture the screen, compare frame time with
   the telemetry in vr-session-result. Multi-pass costs a second sort per
   frame unless `m_CenterEyeOnly` is set.

1d. **Vulkan/Quest draw bindings. — RETRACTED 2026-09-01**
   The 2026-08-31 entry claimed MaterialPropertyBlock buffer bindings were
   dropped on Vulkan and "fixed" it by also binding everything as
   command-buffer globals. Measured on a Quest 3S, both halves were wrong:
   the globals never reach this shader on that backend, and the property
   block is what actually delivers the buffers. Every variant of the
   globals (with the block, without it, null-guarded, with substituted
   buffers) ended in a SIGSEGV milliseconds after the draw; drawing without
   the block reads unbound buffers, and substituting a different-typed
   buffer for a missing layer reads out of bounds. The experiment is
   removed; the draw is upstream's property-block-only path again.

   The room really was invisible — but because the trainee's HEAD sat at
   the rig plus the tracked pose, i.e. wherever the headset physically was
   relative to the guardian origin, which put them inside a wall.
   vrsimulator now recentres the rig on the authored spawn
   (`XRRigController.RecentreOnSpawn`). Measured splat screen extent went
   from a mean of 269 px (fog) to 48 px (a room); desktop baseline 65 px.

   What survives from the investigation, both harmless where they are
   no-ops and correct where they are not: `CalcViewData` uses the eye's
   view/projection matrices when `cam.stereoEnabled`, and the draw shader
   divides by the same `_VecScreenParams` the compute used (passed through
   the property block) instead of trusting `_ScreenParams` to match.

   The lesson, kept here because it cost a day: the driver going quiet is
   not evidence that a binding arrived. Read the buffer back.

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
