# VR Flats Ref Code (vrflatscore) — Architecture

**Scanned:** 2026-09-08 · **Commit:** `963e1c2` (2026-09-08, "Rename identity to VR Flats Ref Code (vrflatscore) (#1)") · **Remote:** `chmjdev/vrflatscore`

> Read from this repository on the scan date. No Unity build was run. See §9.
>
> **Superseded in part, 2026-09-09.** The gaps in §8 were worked; §8 now carries
> the status of each. The living documents are `Readme.md` (verified facts),
> `SECURITY.md` (posture and findings), `CHANGELOG.md`, `docs/verification.md`
> (how the claims are re-run) and `docs/upstream.md`. This file remains the
> 2026-09-08 scan, annotated — not rewritten as though it had always been right.

---

## 1. What it is

A **Unity Gaussian-splat rendering package** — not an application. A maintained
**MIT fork** of [`ninjamode/Unity-VR-Gaussian-Splatting`], itself built on
[`aras-p/UnityGaussianSplatting`] by Aras Pranckevičius. Original copyright and
licence preserved unchanged (`LICENSE.md`); **this fork adds no licence terms**.

Upstream adds two capabilities over Aras's package: **multi-layer Gaussian
splat point clouds** and **VR rendering support**. Upstream reports it working
with HTC Vive series, Varjo Aero, Quest Pro and Quest 3, and it originates in a
publication on displaying CT scans as a GS cloud in VR.

**Renamed from `vrsplat` on 2026-09-08.**

## 2. Why the fork exists

> *"The Interactive suite renders real captured training rooms as Gaussian splats
> on Quest 3 standalone. Both upstreams describe themselves as experimental, and
> the canonical project states it is 'not tested at all on mobile/web'. Rather
> than depend on that unmaintained, we maintain this fork and carry the fixes
> ourselves."*

That is the correct reason to fork, stated plainly.

## 3. How it is consumed

| Property | Value |
| --- | --- |
| Kind | Unity package — `asset` / `static` |
| Supervised as a service | **No.** Declared `disabled`, ports 0, no domains |

Not a port-bound web service; nothing runs it as a daemon.

**Consumed by a sibling Unity checkout** via
`"com.binteca.vrflatscore": "file:../../vrflatscore/package"` — **a relative
file path, with the repositories kept side by side**. Moving or renaming a
sibling directory breaks the consumer's package resolution.

A separate consumer takes the **capture tooling under `tools/capture/`** rather
than the Unity package. The two halves of this repository serve different
callers.

## 4. Structure

| Path | Role |
| --- | --- |
| `package/` | The Unity package — `Runtime/`, `Editor/`, `Shaders/`, `Materials/`, `package.json`, `LICENSE.md` |
| `VR-URP/` | The URP integration |
| `tools/` | Capture tooling (consumed by `vrscanner`) |
| `docs/` | `render-pipeline-integration.md`, `splat-editing.md`, `upstream.md`, `verification.md`, `Images/`, `RefImages/` |
| `tools/verify/` | `run.sh` + `PackageVerify.cs` — compile and behaviour check in a throwaway Unity project (added 2026-09-09) |
| `ROADMAP.md` | Fork work items |
| `CHANGELOG.md`, `SECURITY.md` | Added 2026-09-09 |
| `design/` | This scan — architecture, data design, security (2026-09-08) |

## 5. Verified compatibility

**As scanned 2026-09-08**, these were quoted from `Readme.md` as dated evidence
and all of them predated the rename — which is what §8.1 was about.

**Re-verified 2026-09-09 by running it** (`./tools/verify/run.sh`, Unity
6000.3.22f1 + URP 17.3.0):

- **Zero compile errors**, against the renamed source itself rather than the
  state before it.
- Renderer type resolves as
  `VRFlatsCore.Runtime.GaussianSplatRenderer, VRFlatsCore` — **the assembly is
  `VRFlatsCore`, not `…Runtime`** — confirmed by `Type.GetType` on that exact
  string, not by reading the asmdef. Both assembly names, every runtime
  namespace and the package id `com.binteca.vrflatscore` likewise.
- **Not covered, and still not:** no frame is rendered and no APK is built, so
  this is not a device test. And `package.json` declares Unity `2022.3` — a
  minimum inherited from upstream that **has never been built in this fork**.
- **Budget on device: still upstream's number** — ~72 fps to roughly **400k
  Gaussians** on Quest 3. *"Room-sized captures are realistic; a whole building
  is not."* `QuestBudget.MeasuredOnOurDevice` remains `false`.

## 6. Fork additions (2026-08-31, for `vrsimulator`)

1. **Runtime-created splat data** — `GaussianSplatAsset.SetRuntimeData`, so a PLY
   can be loaded **in a player**, with no Editor importer needed. That is what
   makes a captured room loadable at runtime rather than only at author time.
2. **`#if UNITY_EDITOR` guards on the Runtime assembly's four bare
   `using UnityEditor;` lines**, *"which previously broke every player build that
   included this package"*.

See `ROADMAP.md` item 1b.

## 7. Upstream relationship

**No `upstream` remote was configured** — verified 2026-09-08, `origin` only.
**Two were added 2026-09-09** (`upstream`, `upstream-aras`); see §8.3 and
`docs/upstream.md`.

> *"Since the 2026-09-08 rename the package identity is this fork's own
> (`com.binteca.vrflatscore`, assembly `VRFlatsCore`), so pulling fixes from
> either upstream means adding the remote and resolving those renames by hand."*

The rename bought a clean identity at the cost of a harder merge. Both halves are
recorded, which is what makes the trade reviewable.

## 8. Gaps and risks — with 2026-09-09 status

**8.1 — the rename was unverified against a build. — CLOSED 2026-09-09.**
As scanned, `Readme.md` said it directly: *"the 2026-09-08 rename … has had no
Unity build run against it in this checkout."* It has now. Zero compile errors
and every declared identity resolved by reflection; see §5. The check is
committed as `tools/verify/run.sh`, so this closes as something re-runnable
rather than as a one-off assertion.

**8.2 — the consumer dependency is a relative file path. — OPEN, by
construction.** `file:` dependencies cannot be version-pinned; a consumer takes
whatever is in the checkout beside it. Closing it properly means a registry or a
pinned git SHA, which is a decision about how the suite is assembled, not one
this repository can make alone. Mitigated from this side: `package.json`
`version` is now meaningful and `CHANGELOG.md` exists, so "which vrflatscore was
that built against?" is at least answerable.

**8.3 — divergence from upstream was manual and untracked. — CLOSED
2026-09-09.** Both remotes added, and the relationship measured rather than
assumed: `upstream` (ninjamode) is a true git ancestor and we are **0 behind** it
(ahead by this fork's own history — 17 when this was written, 19 on 2026-09-10);
`upstream-aras` (aras-p, where fixes actually land) shares **no history at all**
— `git merge-base` exits 1 — so porting from it can never be a merge. The
procedure, the rename map, and what a careless port would erase are in
`docs/upstream.md`. **The first port is already done:** `RecordRenderGraph`
(ROADMAP 1c).

**8.4 — the device budget is upstream's figure. — OPEN.** ~72 fps to ~400k
Gaussians on Quest 3, unmeasured here. Closing it needs a capture, an APK and a
headset, none of which this change touches. A Quest 3 was attached to this
machine on 2026-09-09, so the obstacle is the work, not the hardware.

**8.5 — no sibling checkout currently consumes the Unity package. — CONFIRMED
2026-09-09**, by checking which sibling directories exist. The consumer of
`tools/capture/` is present; the Unity-package consumers named in older notes
are not. So the fork additions originally made for one of them remain useful to
any player build, but nothing beside this repository resolves the package today.
`VR-URP/` here does consume it, and is pinned to Unity 2022.3.51f1 — not the
editor version any of this was built with.

**8.6 — a silent-failure class this scan did not look for. — FOUND AND CLOSED
2026-09-09.** Worth recording because it is the shape of defect this project
keeps producing: **the URP feature drew nothing at all on Unity 6.** URP runs
RenderGraph by default and never calls the legacy `Execute`; the pass
implemented only that. A scan that reads source cannot see it, and a build that
compiles clean does not report it. See ROADMAP 1c.

## 9. Consolidated sources

Absorbs the fork rationale, verification record, additions and upstream-relationship
content of `Readme.md`. Left in place: `ROADMAP.md` (fork work items),
`docs/render-pipeline-integration.md` and `docs/splat-editing.md` (usage
guides), `LICENSE.md` (unchanged upstream MIT).

## 10. What this document does not say

**As scanned 2026-09-08:** no Unity build or test run was performed, every
compatibility and performance figure was quoted from `Readme.md` as dated
evidence, and the project's own note that the rename was unverified stood.

**As annotated 2026-09-09:** the compile and identity claims in §5 are now
measured, by a run that can be repeated (`docs/verification.md`). What remains
unmeasured is stated as such and has not quietly become a fact: **no frame has
been rendered, no APK built, no device timing taken**, and the Quest budget is
still upstream's number. A build that compiles is not a build that draws — §8.6
is exactly that distinction costing the project a working renderer on Unity 6.
