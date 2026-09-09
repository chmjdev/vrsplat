# Changelog — vrflatscore

A `file:` dependency cannot be version-pinned: a consuming project takes
whatever is in the sibling checkout, with no lockfile entry that would show a
change. This file and the `version` field in `package.json` are what make
"which vrflatscore was that built against?" an answerable question from this
side. Bump the version with any change that a consumer could notice.

Dates are the estate's, and every "verified" below names how.

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
