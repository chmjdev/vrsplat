# vrflatscore — maintenance roadmap

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

   **Scoped, not attempted, 2026-09-09.** Read `RenderGaussianSplats.shader`,
   `GaussianComposite.shader`, `GaussianSplatURPFeature.cs`: the change needs
   stereo-instancing macros on the vertex/compute stages, `_GaussianSplatRT`
   as a `Texture2DArray` under SPI, and `CalcViewData` dispatching per-eye
   (doubling the view buffer, splitting `SV_InstanceID` into eye+splat
   index) — that last part is where a confident-looking mistake is easiest.
   Not written: no Unity process was available to compile or run it this
   session (0.11.0, `CHANGELOG.md`), and this file's own item 1d is a record
   of what shipping an unverified VR stereo "fix" here has cost before.

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

1c. **RenderGraph port of `GaussianSplatURPFeature`. — DONE 2026-09-09**
   Unity 6 URP runs RenderGraph by default and the feature's `GSRenderPass`
   implemented only the legacy `Execute` path, so it silently drew nothing
   there — the player log said exactly this (observed in vrsimulator's
   first smoke run, 2026-08-31), and Compatibility Mode (RenderGraph
   disabled) was the workaround every consuming project had to carry.

   `GSRenderPass.RecordRenderGraph` now exists, ported from
   `upstream-aras/main` — and the remote to port it from is itself new
   (`docs/upstream.md`; before 2026-09-09 this repository had `origin`
   only). It is an **unsafe pass**, which is what hands back a real
   `CommandBuffer` for the sort dispatches and the `DrawProcedural`, and it
   follows upstream on every point where upstream ships a working choice:
   `CreateRenderGraphTexture`, `AllowPassCulling(false)` (nothing downstream
   reads the target by handle — the composite reaches it through the
   `_GaussianSplatRT` global — so the graph would otherwise cull the pass,
   which looks identical to not implementing it), the `SetGlobalTexture`,
   and the camera depth declared **read-only** because both splat shaders
   are ZWrite Off. What is ours and must survive any future port: the XR
   per-eye matrix overrides, which upstream's version does not have, and the
   retained legacy `Execute`/`OnCameraSetup` overrides for URP 14 and for
   Compatibility Mode projects.

   **Two traps, both silent, both cost a run to find:**
   - The new code is behind a `GS_URP_RENDERGRAPH` version define on URP
     `17.0.0`. The first attempt wrote the range as `[17.0.0,)`, which Unity
     rejects with `ExpressionNotValidException` — its syntax has no
     unbounded-range form, a bare version means "that version or newer". The
     result was a clean build with the RenderGraph code silently compiled
     out. The source was right and the manifest was wrong, and nothing
     failed.
   - Overriding an `[Obsolete]` member raises **CS0672**, not CS0618. The
     pragma named the wrong warning and suppressed nothing.

   Neither would have been caught by reading. Both were caught by
   `tools/verify/run.sh`, which checks the compiled type by reflection —
   `RecordRenderGraph` present, `PassData` present, `Execute` still present.
   **Still unmeasured:** nothing here renders a frame. That the pass is
   recorded is verified; that it draws correctly on a Quest 3 is not.

2. **SOG / compressed-format import. — SCAFFOLDING ADDED 2026-09-09; COMPILES 2026-09-10, still no end-to-end read**
   PlayCanvas' SOG format reports 15–20× smaller than PLY. It is
   web-oriented, so this is real importer work rather than a flag, but the
   size win matters: captures ship inside the APK's StreamingAssets, and
   load time and download size are both real UAT costs.

   `package/Editor/Utils/SogFileReader.cs`: `meta.json` schema (transcribed
   from the published spec, then **cross-checked 2026-09-09 against a real
   generated file** — see item 4 below for how `@playcanvas/splat-transform`
   got installed and run this session) plus the position/scale/SH0/quat
   dequantization formulas. The quaternion formula (`DequantizeQuat`,
   "smallest-three" packing) was read directly out of that npm package's own
   shipped source once it was actually available, closing what was
   initially an unresolved gap in the published spec. Still not
   implemented, and now correctly scoped as solvable rather than unknown:
   **WebP pixel decoding in the C# importer itself** — Unity has no native
   WebP path, and this machine still has no `dwebp`/`cwebp`/libwebp/
   ImageMagick to shell out to, but `@playcanvas/splat-transform` does ship
   a working WebP codec, proven to run this session, so the concrete next
   step is an Editor-only external-process call into it (or an equivalent
   .NET libwebp binding), not an unsolved problem. `ISogTextureDecoder` is
   the seam. Not wired into `GaussianSplatAssetCreator`'s UI.

   **Compiled and partly exercised 2026-09-10**, superseding "none of it has
   been compiled": `SogFileReader` and the `ISogTextureDecoder` seam are in the
   assembly, every dequantization method is declared, and `DequantizeQuat`
   returns a unit quaternion on all four `maxComp` branches — the first actual
   execution of the port. **No `.sog` file has been read end to end**, because
   WebP decoding is still absent; that is what remains of this item.

3. **An LOD ladder. — ADDED 2026-09-09; COMPILES 2026-09-10, never run**
   So a room can hold detail near the trainee and shed it at distance
   instead of being decimated uniformly. This is what would let a capture be
   larger than the flat budget allows.

   Note, 2026-09-09: an earlier draft of this item named a specific "AURA"
   method as the model to follow. That reference could not be verified — a
   search for it returned no matching paper, only an unrelated web-search
   summary that named implementation details (a `pip install aura-splat`
   package) not backed by any of the actual pages the search returned, which
   reads as the search tool fabricating rather than finding something. What
   was built instead (`GaussianSplatRenderer.m_LodEnabled` /
   `m_LayerLodDistances`) is a distance-based ladder in the general spirit
   this item's own opening line describes, reusing the existing multi-layer
   asset format (one layer = one LOD rung, given a max camera distance) —
   not a reproduction of any specific external method.

   **Compiles as of 2026-09-10** — the three symbols are in the assembly,
   checked by reflection. It has still **never been run**, against a real
   capture or otherwise, so whether the ladder behaves is untested.

4. **Author-facing decimation guidance. — REAL INVOCATION RUN 2026-09-09**
   `SplatTransform` (converts formats, emits LOD) is the practical tool for
   getting a raw capture down to budget. Document the exact invocation we
   use rather than telling authors to "decimate".

   Corrected, 2026-09-09: it is **PlayCanvas'** tool
   (`@playcanvas/splat-transform` on npm), not upstream's — verified absent
   from `upstream-aras/main`, so adding the remote does not bring it.

   **First real run, same date, later session.** This machine had no
   Node.js; a portable Node v24.21.0 LTS (darwin-arm64, checksum verified
   against `nodejs.org`'s published `SHASUMS256.txt`) was installed into the
   *notebook's own sandboxed workspace* — **not into this repository, and
   not onto whatever machine actually builds `VR-URP/`** — followed by
   `npm install @playcanvas/splat-transform`, which resolved v3.4.2
   (commit `0cb47cd`), 8 packages, 0 vulnerabilities. It runs and its
   `--help`/`--info`/`--stats` output is real, captured output — not
   documentation guesswork. No real estate capture was available to this
   session to decimate (no `.ply` exists anywhere in this checkout — the
   bundled `VR-URP/Assets/GS Assets/` are already Unity's post-import
   `.bytes` format), so the actual invocation below was run against a
   **synthetic** 20,000-splat PLY (random positions/colours, degenerate
   identity rotations, generated in Python for this test) — every number is
   real, but none of it is a statement about how a real capture decimates:

   ```
   $ splat-transform test_input.ply -d 50% test_output_50.ply -w
   ▸ [2/2] Output test_output_50.ply
     ▸ [1/1] Decimate generation
       · 10K gaussians · 0 SH bands
   done in 0.399s  [peak cpu=182.7MB gpu=9.2MB]
   ```
   20,000 → 10,000 gaussians exactly (`-d 50%`), file size 1,360,415 →
   680,415 bytes exactly halved (uniform decimation, no SH compression in
   this synthetic file). `-d` (`--decimate`) is uniform-rate and cheaper;
   `--decimate-adaptive` allocates removal by local error and the tool's own
   `--help` recommends it for mixed-scale content ("skies") over `-d`'s
   recommended use ("uniform texture, single objects, snow") — a real
   capture's own scale mix would decide which one an author should actually
   use, which this session cannot state without a real capture to test
   against.

   The same install also fully closed the ROADMAP item 2 SOG gap on the
   quaternion formula — see `CHANGELOG.md` and `SogFileReader.cs`.

   What the guard already gives authors, independent of the above: the
   verdict and the route (`QuestBudget.Describe`): crop with cutouts, trim
   with the edit tools, export modified PLY, re-import at `VeryLow`.

## Ground rules for this fork

- **Stay mergeable.** Prefer additive changes; keep upstream's file layout
  so `upstream` can still be pulled.
- **MIT preserved.** Original copyright and attribution stay untouched.
- **Measured, not assumed.** Performance claims in this repo carry the
  device and the method, or they are labelled as upstream's numbers.
- **Compatibility claims are re-runnable.** `./tools/verify/run.sh` compiles
  the package in a throwaway Unity project and checks the declared identities
  and the runtime input handling by reflection; `docs/verification.md` says
  what a pass does and does not mean. A claim that survives a change without
  being re-run is evidence about the state before the change.
