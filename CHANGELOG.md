# Changelog — vrflatscore

A `file:` dependency cannot be version-pinned: a consuming project takes
whatever is in the sibling checkout, with no lockfile entry that would show a
change. This file and the `version` field in `package.json` are what make
"which vrflatscore was that built against?" an answerable question from this
side. Bump the version with any change that a consumer could notice.

Dates are the estate's, and every "verified" below names how.

## 0.11.0 — 2026-09-09

**Everything in this release is source-only and UNVERIFIED.** The session that
wrote it had no working Unity process at all: `tools/verify/run.sh` needs the
same Unity Editor binary this session tried and failed to launch (see
"Environment finding" below), so none of it has been compiled, let alone run.
This breaks from every earlier entry in this file, which only records
`./tools/verify/run.sh` results. Treat every item below as a diff to review
and verify, not as a working feature.

### Added, unverified

- **Distance-based LOD ladder** (ROADMAP item 3), `GaussianSplatRenderer.m_LodEnabled`
  / `m_LayerLodDistances` / `LayerLodEntry`. Reuses the existing multi-layer
  asset format rather than a new one: a layer becomes one LOD rung by giving
  it a maximum camera distance, and `UpdateLod()` only calls
  `UpdateRessources()` (which re-uploads every active layer's GPU buffers) on
  an actual transition between rungs, not every frame. Only behaves as a real
  LOD ladder if the source capture's layers were authored as nested detail
  levels; this is author responsibility, same as the layer format always was.
  `tools/verify/PackageVerify.cs` now checks the three symbols are compiled
  in, by reflection, same pattern as every other identity check there.
- **SOG import scaffolding** (ROADMAP item 2), `package/Editor/Utils/SogFileReader.cs`.
  `meta.json` schema and validation, plus the three dequantization formulas
  the published SOG spec (developer.playcanvas.com, fetched 2026-09-09)
  actually states: position log-transform (`Unlog`), scale
  (`exp(codebook[pixel])`), and SH0 base colour/opacity. **Two things are
  deliberately not implemented, not guessed at:**
  - **WebP pixel decoding.** Unity's `ImageConversion.LoadImage` supports only
    JPEG/PNG, no native WebP. This machine has no `dwebp`/`cwebp`/libwebp/
    ImageMagick to shell out to either (checked). `ISogTextureDecoder` is the
    seam a real decoder plugs into; `DefaultSogTextureDecoder` throws with
    that explanation rather than returning wrong pixels.
  - **`quats.webp` channel-to-quaternion mapping.** The fetched schema states
    formulas for means/scales/sh0 but not for quats. `DequantizeQuat` throws
    `NotImplementedException` naming exactly this gap rather than inventing an
    encoding.
  - No `.meta` file was created for the new `.cs` file — Unity generates that
    on first import in a real Editor; writing one by hand risked a GUID
    collision I have no way to check for.

### Added later the same date, after Node.js became available

- **`SogFileReader.DequantizeQuat` implemented for real**, superseding the
  `NotImplementedException` stub above. This machine had no Node.js; a
  portable Node v24.21.0 LTS was installed into *the notebook's own sandboxed
  workspace* (not this repository, not the machine that actually builds
  `VR-URP/`) and used to `npm install @playcanvas/splat-transform` (resolved
  v3.4.2, commit `0cb47cd`). Running it for real against a synthetic PLY
  produced a genuine `meta.json` that matches `SogMeta`'s transcribed schema
  exactly, and its own shipped source
  (`node_modules/@playcanvas/splat-transform/dist/index.mjs`, function
  `unpackQuat`) gave the "smallest-three" quaternion decode formula the
  published spec page didn't state — ported into `DequantizeQuat` with that
  provenance, not guessed. `PackageVerify.cs` now asserts the result is a
  unit quaternion across all four `maxComp` branches (still uncompiled,
  same as everything else in this entry).
- **ROADMAP item 4 (decimation guidance) has a real, run invocation** for the
  first time: `splat-transform test_input.ply -d 50% test_output_50.ply -w`
  against a synthetic 20,000-splat PLY (no real capture exists in this
  checkout), 20,000 → 10,000 gaussians exactly, file size exactly halved.
  See `ROADMAP.md` item 4 for the full transcript and the caveat that
  synthetic-file numbers say nothing about a real capture.

### Investigated, not implemented

- **Single Pass Instanced stereo** (ROADMAP item 1e). Read `RenderGaussianSplats.shader`,
  `GaussianComposite.shader` and the XR matrix handoff in
  `GaussianSplatURPFeature.cs` to scope the change: it needs the vertex/compute
  stages to carry Unity's stereo-instancing macros, `GaussianComposite.shader`'s
  `Texture2D _GaussianSplatRT` to become a `Texture2DArray` under SPI, and —
  the part with the most room to get subtly wrong — `CalcViewData` to dispatch
  per-eye (doubling the view buffer and the `DrawProcedural` instance count,
  splitting `SV_InstanceID` into eye index and splat index). Given this
  repository's own history with exactly this class of "looks fixed, is not"
  VR bug (ROADMAP item 1d), and zero ability to compile or run anything this
  session, writing that change blind was judged worse than not writing it.
  Not attempted. Still Multi-pass, still `ROADMAP.md` item 1e's "open
  measurement, not a known defect."
- **Author-facing decimation guidance** (ROADMAP item 4). Needs a real
  `SplatTransform`/`splat-transform` invocation to document honestly — this
  repository's own rule against inventing one. This machine has no Node.js/npm
  (`node: command not found`), so no invocation was run. Still nothing to
  document here.

### Environment finding, not a package fact

The session that wrote this entry could not run the Unity Editor **at all**,
against any project, in any directory — not a project-specific issue. Batch
launches failed acquiring the licensing client's IPC mutex
(`System.IO.IOException` on `Global\Unity.Licensing.Client.Pipe...`), traced
to the sandboxed tool environment blocking writes to the fixed per-user macOS
temp directory (`/var/folders/.../T`, read via `confstr()`, not the `$TMPDIR`
env var) that `.NET`'s named-mutex implementation and `xcrun` both depend on.
Tested against the real project path and again against a full copy in a
confirmed-writable directory; same failure both times. No remote compute host
was available as a fallback (`host.compute.listHosts()` returned empty this
session). Recorded here because it explains why this entry has no
`tools/verify/run.sh` result attached, not because it is a fact about the
package.

## 0.10.0 — 2026-09-09

### Added

- **`RecordRenderGraph` on `GaussianSplatURPFeature`** (ROADMAP 1c). Unity 6 URP
  runs RenderGraph by default and never calls the legacy `Execute`, so the
  feature **silently drew nothing** there — the player log said exactly that,
  and the workaround was ticking Compatibility Mode in every consuming project.
  Ported from `upstream-aras/main`, keeping this fork's XR per-eye matrix
  handoff, which upstream's version does not have. Compiled in behind a new
  `GS_URP_RENDERGRAPH` version define (URP 17.0.0+); the legacy
  `Execute`/`OnCameraSetup` overrides are retained for URP 14 and for URP 17
  projects that choose Compatibility Mode.
- **`GaussianSplatAsset.ValidateRuntimeLayer`**, called by `SetRuntimeData`.
  Runtime splat buffers are now checked against the asset's declared formats and
  per-layer splat count, and a mismatch throws with the buffer named and both
  byte counts given. These buffers go straight to the GPU carrying no bounds
  information: previously a wrong length was an out-of-bounds read in a shipped
  player, arriving on Quest's Vulkan backend as a SIGSEGV in `libunity` with
  nothing in the log. Also refuses a call before `Initialize`, an undeclared
  layer, and a clustered SH palette (which has no runtime path).
- **`GaussianSplatAsset.kRuntimeColorStride`** — states that a runtime colour
  buffer is raw `float4` per splat, not the packed `colorFormat`.
  `CalcColorDataSize` describes the *converted* texture and is the wrong thing
  to size a runtime buffer with; that mistake is now refused by name.
- **`tools/verify/run.sh` + `tools/verify/PackageVerify.cs`** — compile and
  behaviour verification in a throwaway Unity project. See
  `docs/verification.md`.
- **`SECURITY.md`**, **`docs/upstream.md`**, **`docs/verification.md`**, and
  this file.
- **`upstream` and `upstream-aras` git remotes.** Until now the repository had
  `origin` only, so no upstream fix — security or otherwise — would ever have
  surfaced here.

### Changed

- A named `ProfilingScope` around the RenderGraph pass, so it is identifiable in
  a Perfetto or RenderDoc capture. That is the route by which `QuestBudget`'s
  figure eventually stops being upstream's and becomes ours.
- The XR per-eye matrix handoff is now one shared helper used by both recording
  paths, rather than living only in `Execute`. A stereo fix in one path only is
  a fix on one Unity version only.
- `CS0618`/`CS0672` suppressed around the deliberately-retained legacy overrides.

### Verified

- **The 2026-09-08 rename now has a build behind it.** Zero compile errors on
  Unity 6000.3.22f1 with URP 17.3.0, and by reflection: the type string
  `VRFlatsCore.Runtime.GaussianSplatRenderer, VRFlatsCore` resolves, both
  assemblies carry their documented names, every runtime namespace is
  `VRFlatsCore.*`, and the package registers as `com.binteca.vrflatscore@0.9.0`
  (the version this change supersedes). Until now every compatibility claim in
  `Readme.md` predated the rename and said so.
- **`upstream/main` (ninjamode) is a true git ancestor** — 17 ahead, 0 behind.
  **`upstream-aras/main` shares no history at all** (`git merge-base` exits 1),
  so porting from it is manual by construction, never a merge.

### Still open, and labelled

- **The `file:` dependency is unpinned** — a property of the suite's layout, not
  something this repository can close alone.
- **The Quest budget is still upstream's number.** `QuestBudget.MeasuredOnOurDevice`
  stays `false`. Nothing here renders a frame or builds an APK.
- **Single Pass Instanced stereo is unmeasured** (ROADMAP 1e).
- **`package.json` declares Unity `2022.3` and that minimum has never been
  verified in this fork**, including the legacy path kept for it.

## 0.9.0 — 2026-09-08

- Renamed from `vrsplat` to `vrflatscore`: package id
  `org.nesnausk.gaussian-splatting` → `com.binteca.vrflatscore`, assemblies
  `GaussianSplatting`/`GaussianSplattingEditor` →
  `VRFlatsCore`/`VRFlatsCoreEditor`, namespaces `GaussianSplatting.*` →
  `VRFlatsCore.*`, env vars `VRSPLAT_*` → `VRFLATSCORE_*`. The class
  vocabulary, `GaussianSplatting.hlsl` and the `nesnausk.*` EditorPrefs keys
  deliberately keep upstream's names. No Unity build was run against it at the
  time — see 0.10.0.

## Earlier

Before 0.9.0 this fork carried no changelog. The fork's own additions to that
point, from `ROADMAP.md`:

- **1b (2026-08-31)** — runtime-created splat data (`SetRuntimeData`), and
  `#if UNITY_EDITOR` guards on the Runtime assembly's four bare
  `using UnityEditor;` lines, which had been failing every player build of a
  consuming project.
- **1 (2026-08-30)** — `QuestBudget`, with the budget stated once and its
  provenance attached, warned about in the creator window and the renderer
  inspector.
- **1d (2026-09-01, retracted)** — a command-buffer-globals experiment on
  Quest/Vulkan, removed. The draw is upstream's property-block-only path again.
