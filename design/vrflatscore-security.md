# VR Flats Ref Code (vrflatscore) — Security

**Scanned:** 2026-09-08 · **Commit:** `963e1c2`

> Source-level review. No Unity build was run.
>
> **Superseded 2026-09-09 by `SECURITY.md` at the repository root**, which this
> review's F-6 called for. That file is the living document and is authoritative
> for current posture and finding status; this one stays as the dated scan that
> produced the findings, annotated below rather than rewritten.

---

## 1. Posture

A Unity rendering package. No network surface, no data store, no credentials, no
service. Its manifest declares no ports and no domains, and marks it as not
supervised.

Its security profile is almost entirely **supply chain and licensing**, plus one
privacy consideration inherited from what it renders.

## 2. Licensing — the primary compliance surface

- **MIT throughout.** A fork of `ninjamode/Unity-VR-Gaussian-Splatting`, itself
  built on `aras-p/UnityGaussianSplatting` by Aras Pranckevičius.
- **Original copyright and licence preserved unchanged** (`LICENSE.md`); *"this
  fork adds no licence terms."*
- The package ships inside consuming Unity applications, so the MIT attribution
  travels with any distributed build. Anything shipped to the Meta Horizon Store
  carries it.
- Upstream originates in a published paper on displaying CT scans as Gaussian
  splats in VR; the academic citation is part of the provenance.

Handling an MIT fork this way — preserve, attribute, add nothing — is correct and
is the low-risk path.

## 3. Supply chain

| Fact | Detail |
| --- | --- |
| Consumed as | `"com.binteca.vrflatscore": "file:../../vrflatscore/package"` — **a relative file path**, not a registry package |
| Version pinning | **None.** The consumer gets whatever is in the sibling checkout |
| Upstream remote | **Not configured** — verified 2026-09-08, `origin` only |
| Divergence | Since the rename the package identity is this fork's own (`com.binteca.vrflatscore`, assembly `VRFlatsCore`), so pulling upstream fixes means adding the remote and resolving renames by hand |

**The unpinned file dependency is the notable item.** A consuming build takes
whatever is on disk beside it: no version, no checksum, no lockfile entry that
would show a change. In exchange, the fork is fully controlled and auditable —
which was the stated reason for forking rather than depending on an unmaintained
upstream.

## 4. Code-level changes worth knowing

Two fork additions (2026-08-31), both benign and both fixing real build breakage:

1. **`GaussianSplatAsset.SetRuntimeData`** — allows loading a PLY **in a
   player**. It widens what a shipped build will parse at runtime: a malformed or
   hostile PLY now reaches parsing code in a released application rather than
   only in the Editor. For the suite's own captures this is not a threat; if
   runtime loading ever accepts a file from outside the suite, the parser becomes
   an input-handling surface.
2. **`#if UNITY_EDITOR` guards on four bare `using UnityEditor;` lines** in the
   Runtime assembly, *"which previously broke every player build that included
   this package."* A correctness fix, not a security one, but it is the
   difference between a shipping build and none.

## 5. Findings — status as at 2026-09-09

`SECURITY.md` is authoritative; this table is the scan's own record of what
became of what it found.

| # | As found 2026-09-08 | Status |
| --- | --- | --- |
| F-1 | The 2026-09-08 rename was unverified — every compatibility claim predated it, including the type string `VRFlatsCore.Runtime.GaussianSplatRenderer, VRFlatsCore` | **Closed.** Built on Unity 6000.3.22f1 / URP 17.3.0: zero compile errors, and that exact type string resolved by `Type.GetType` at runtime. Re-runnable as `tools/verify/run.sh` |
| F-2 | No upstream remote, so upstream security fixes were untracked | **Closed.** `upstream` and `upstream-aras` added; relationship measured (ninjamode a true ancestor, aras-p sharing no history at all). `docs/upstream.md` |
| F-3 | Unpinned relative file dependency | **Open, by construction.** Not closeable from this repository; mitigated with a real `version` and `CHANGELOG.md` |
| F-4 | Runtime loading widens the parsing surface in shipped builds | **Closed, and narrower than stated.** The scan implied this package parses PLY at runtime; it does not — it takes packed buffers, and parsing lives in the caller. Those buffers are now validated against the declared formats before reaching the GPU, with six assertions covering it |
| F-5 | Captures are photographs of real rooms | **Open, and not closeable here.** An obligation on whoever captures and publishes. Restated in `SECURITY.md` |
| F-6 | No `SECURITY.md` | **Closed** — written 2026-09-09 |

**One finding this scan did not make**, recorded because it is the more
dangerous shape: the URP feature **drew nothing at all on Unity 6**, because URP
runs RenderGraph by default and the pass implemented only the legacy `Execute`.
Not a security defect, but the same failure mode as several of the above — a
thing that is silently absent, in code that reads correctly and compiles clean.
Fixed the same day; ROADMAP 1c.

## 6. Which policy areas actually apply

Most of the standard checklist is inapplicable here, and saying so explicitly is
the point — a package is not a service:

| Area | Application |
| --- | --- |
| Network exposure / loopback isolation | Not applicable — no network surface |
| Payment, card or identity rails | Not applicable — none exist |
| Execution model | It is a package, not a daemon; declared as such |
| Clean install of native app builds | Applies to **consumers** that build a player, not to this package |
| Build output must not be search-indexed | No build output in this repository; applies to consuming projects |
| Production teardown by a person | Not applicable — nothing here is deployed |

## 7. Consolidated sources

No `SECURITY.md` existed at scan time — writing one was F-6, and it now exists
and supersedes this file for current posture. Absorbs the licensing,
fork-rationale and upstream-relationship content of `Readme.md`, and
`ROADMAP.md` item 1b for the fork additions. `LICENSE.md` remains the unchanged
upstream MIT licence and is authoritative for licensing.
