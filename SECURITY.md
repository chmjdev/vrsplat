# Security — vrflatscore

**Last verified: 2026-09-09**, against commit `963e1c2` plus the changes in this
one. Everything below is either something checked on that date or is labelled as
unverified. There is no third category.

## What this is, and what that bounds

A Unity **rendering package** — `com.binteca.vrflatscore`, assemblies
`VRFlatsCore` and `VRFlatsCoreEditor`. It is not a service. `jcds.config`
declares ports 0, empty domains, `disabled: true` and `disable_automation: true`,
and it is excluded from JCDS supervision.

Verified by reading the package source on 2026-09-09:

| Surface | Finding |
| --- | --- |
| Network | **None.** No `UnityWebRequest`, `HttpClient`, `WebClient`, `Socket` or `System.Net` anywhere in `package/`. |
| Credentials | **None.** No key, token or connection string is read, stored or emitted. |
| Process execution | **None at runtime.** `System.Diagnostics` appears twice: `Runtime/Timer.cs` (`Stopwatch`) and `Editor/Utils/Screenshotter.cs`. Neither starts a process. |
| Runtime file writes | `Runtime/Timer.cs` writes profiling CSVs into `Application.persistentDataPath` — app-private, benign, and off unless timing is enabled. |
| Editor file access | The importer and the editing tools read PLY and write asset blobs, as expected of an Editor importer. |

So the security profile is **supply chain, licensing, and one input-handling
surface** — plus a privacy consideration inherited from what the package renders
rather than from the package itself.

## Reporting

**This repository is public** — verified 2026-09-09 (`gh repo view`:
`visibility: PUBLIC`). An earlier draft of this file said "private"; that was
wrong, and it matters, because it changes who can read a report and who can read
the code.

Raise anything security-relevant with the operator directly rather than as a
public issue, and please do not open a public issue describing an unfixed
weakness. There is no formal disclosure timetable: this is a rendering package
maintained for one suite's use, with no external release channel.

Being public also bounds what belongs in this repository at all: it carries no
credentials, no capture data, and no customer material, and it must stay that
way.

## Input handling — runtime splat data

`GaussianSplatAsset.SetRuntimeData` is the one path in this package that accepts
data in a **shipped player** rather than only in the Editor. It exists so a
capture made after the build can still be rendered (ROADMAP 1b). The buffers it
takes go straight to the GPU as raw and structured buffers and as a colour
texture, carrying no bounds information of their own.

**Since 2026-09-09 those buffers are checked against the asset's declared
formats and per-layer splat count before they are accepted**
(`ValidateRuntimeLayer`), and a mismatch throws with the buffer named and both
numbers given. Before that, a buffer of the wrong length was an out-of-bounds
GPU read in a released application — which on Quest's Vulkan backend arrives as
a SIGSEGV in `libunity` milliseconds after the draw, with nothing in the log to
say why. The checks are exercised by six assertions in the verification harness
(short buffer, packed-vs-raw colour size, missing `Initialize`, undeclared
layer, clustered SH, and the well-formed case), all passing on
Unity 6000.3.22f1.

**What this is not.** It is a structural check, not a PLY parser hardening
exercise: **this package does not parse PLY at runtime at all.** Parsing lives
in the calling project, which packs the buffers and hands them over. If runtime
loading is ever pointed at a file from outside the suite, that caller's parser
is the surface to review — this validation only guarantees that whatever it
produces is self-consistent before it reaches the GPU.

## Supply chain

| Fact | Detail |
| --- | --- |
| Consumed as | `"com.binteca.vrflatscore": "file:../../vrflatscore/package"` — a **relative file path**, not a registry package |
| Version pinning | **Not possible with a `file:` dependency.** The consumer gets whatever is in the sibling checkout |
| Version identity | `package.json` version + `CHANGELOG.md`, so a consumer can at least *state* which version it was built against |
| Upstream remotes | `upstream` and `upstream-aras`, added 2026-09-09 — see `docs/upstream.md` |
| Third-party code | Upstream's own: `zanders3/json` (MIT) and the DeviceRadixSort GPU sort. No package dependencies beyond Burst, Collections and Mathematics |

**The unpinned file dependency remains open, and is a property of the layout
rather than a defect to fix here.** A consuming build takes whatever is on disk
beside it: no version, no checksum, no lockfile entry that would show a change.
Closing it properly means publishing to a registry or pinning a git SHA, which
is a decision about how the suite is assembled, not a change this repository can
make on its own. `CHANGELOG.md` and the `version` field are the mitigation
available from this side: they make "which vrflatscore was that built against?"
an answerable question.

## Licensing

- **MIT throughout.** A fork of `ninjamode/Unity-VR-Gaussian-Splatting`, itself
  built on `aras-p/UnityGaussianSplatting` by Aras Pranckevičius.
- **Original copyright and licence preserved unchanged** (`LICENSE.md`); this
  fork adds no licence terms.
- The package ships inside consuming Unity applications, so the MIT attribution
  travels with any distributed build — anything shipped to the Meta Horizon
  Store carries it.
- Upstream originates in a published paper on displaying CT scans as Gaussian
  splats in VR (IEEE TVCG, `10.1109/TVCG.2025.3549882`); the citation is part of
  the provenance and is reproduced in `Readme.md`.
- **Separate from this package's licence:** the original paper implementation's
  licence makes the *training* software academic / non-commercial, with
  commercial use requiring a licence from INRIA. That governs how a PLY was
  produced, not how it is rendered. Anyone shipping captures commercially has to
  answer for the pipeline that made them.

## Privacy — what a capture is

**A Gaussian splat capture is a photographic reconstruction of a real space.**
For the Interactive suite those are real training rooms, and a capture may
contain whiteboards, documents, screens, name badges and — depending on when it
was taken — people.

Nothing in this package addresses that, and nothing in it can: it is a renderer,
it sanitises nothing, and it cannot tell a wall from a payslip. **The obligation
sits with whoever captures and whoever publishes.** It is stated here because
"point cloud" reads as abstract data and a splat capture is not.

The editing tools (`docs/splat-editing.md`) and cutouts are the practical means
of removing something from a capture before it ships — and removal there is
removal from the exported PLY, not a mask over it.

## Which policy areas actually apply

Most of the standard checklist is inapplicable here, and saying so explicitly is
the point — a package is not a service:

| Area | Application |
| --- | --- |
| Network exposure / loopback isolation | Not applicable — no network surface (verified above) |
| Payment, card or identity rails | Not applicable — no such path exists |
| Execution model | A package, not a daemon; its manifest declares it unsupervised |
| Clean install of native app builds | Applies to **consumers** that build a player from this package, not to the package |
| Build output must not be search-indexed | No build output in this repository; applies to consuming projects |
| Teardown of anything deployed | Not applicable — nothing here is deployed |

## Findings, and where they stand

| # | Finding | Status |
| --- | --- | --- |
| F-1 | The 2026-09-08 rename had never been compiled | **Closed 2026-09-09.** Zero compile errors on Unity 6000.3.22f1 / URP 17.3.0; the type string, both assembly names, all namespaces and the package id verified by reflection in a batchmode run |
| F-2 | No upstream remote, so upstream security fixes were untracked | **Closed 2026-09-09.** Both remotes added and the rename map documented in `docs/upstream.md` |
| F-3 | Unpinned relative file dependency | **Open, by construction.** Mitigated with a version and a changelog; see Supply chain above |
| F-4 | Runtime splat data widened the parsing surface in shipped builds | **Closed 2026-09-09.** Buffers validated at the boundary; see Input handling above |
| F-5 | Captures are photographs of real rooms | **Open, and not closeable here.** It is an obligation on capture and publication; see Privacy above |
| F-6 | No `SECURITY.md` | **Closed 2026-09-09** — this file |
