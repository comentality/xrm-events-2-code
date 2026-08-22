---
name: xtb-slow-network
description: Events2Code's own slow-link suite and the specifics the general procedure needs
---

The general procedure is the user-level `xtb-slow-network` skill. This is what it needs to
know about this repo.

## Run it

From `tests`, in pwsh:

```
.\slow.ps1                          # every scenario
.\slow.ps1 -Scenario cancel         # one, or a comma separated list
.\slow.ps1 -NoBuild                 # reuse the last build
```

Give it a generous timeout — the suite is about a minute of deliberate waiting, and
`slow.ps1` kills the run at five. The exit code is the number of scenarios with findings.
Windows open past the edge of the desktop and take no focus; `E2C_HARNESS_ONSCREEN=1` puts
them in front to watch one play out.

Output lands in `tests\.slow`: a folder per scenario holding a screenshot per gesture, plus
`report.txt` with the same lines the console printed. When a scenario fails, look at the shot
named for the beat before the failing check.

## The parts

| | |
|---|---|
| `tests\Events2Code.SlowHarness\Scenarios.cs` | The ten scenarios. One per failure mode; each says in its summary which one. |
| `tests\Events2Code.SlowHarness\SlowService.cs` | The fake environment. Answers the four things the tool asks, with per-call latency, scripted failures and a call log. |
| `tests\Events2Code.SlowHarness\Probe.cs` | The control from the outside. Every private field name the suite depends on is here, so a rename breaks one file. |
| `tests\Events2Code.SlowHarness\Bench.cs` | Generic: the timeline, the dialog sweeper, the connection setter. Destined for XtbSandbox. |
| `tests\harness\` | `Sample.cs`, `Capture.cs`, `Quiet.cs` — shared with the UI harness. |

## Specifics

- The control is `Events2Code.Events2CodeControl`.
- What a fake must answer, and the name each is logged under: `RetrieveAllEntitiesRequest`
  (`tables`), `RetrieveMultiple` on `systemform` (`forms`), `Retrieve` of one form's `formxml`
  (`formxml`), `Update` of a form (`update`), and `PublishXmlRequest` (`publish`). Each is a
  single round trip, which is why the races here are between fetches rather than inside one.
- `SlowService.Latency` takes the call and its per-kind index, which is how "the second fetch
  answers first" is arranged: `Slow("forms", 3000, 300)`.
- `SlowService.Fails` is the same shape and throws instead of answering, after waiting just as
  long — a link that is going to time out takes as long about it as one that works.
- The fixture is `tests\harness\Sample.cs`, shared with the UI harness. Forms carry real form
  XML built from the same handler lists the screenshots use, so what a scenario asserts on is
  what the tool's own parser read back. `MyOrg.Contact.onSave` is on the showcase form only and
  `mspp.subgrid_filtering.filterAssociatedRecords` on the plain ones — that is how a scenario
  tells which form's answer landed in the grid.
- The dialog sweeper answers a Yes/No confirmation by clicking its Yes button. A confirmation
  has no close box, so posting it `WM_CLOSE` would be ignored and hang the run.
- `write-guarded` presses Unregister for real, so the tool writes a form backup to
  `Documents\Events2Code\backups`. `Run.NewBackups()` counts what appeared there — one press,
  one backup — and the bench deletes only the files the scenario made.

## Where the answers live

`docs\development.md`, section **Slow-link harness**. The CHANGELOG entry under Unreleased
describes the same failure modes as user-visible behaviour. The control carries the reasoning
in the doc comments on `_generation`, `_trouble`, `_handlerSummary` and `FormBusy`, and in
`StartFetch`, `Landed`, `Blame` and `UpdateState`.

## Do not

- Write the status label from anywhere but `UpdateState`. It is the one place that knows
  whether what a sentence claims has actually arrived; `FillHandlersGrid` computes the sentence
  and leaves the saying of it alone.
- Make the write path cancelable. A form update followed by a publish cannot be taken back
  halfway through; only the three read fetches set `IsCancelable`.
- Put a filesystem call back on the UI thread. Save writes through `WorkAsync` because a file
  dialog is as likely to point at a share as at the local disk.
- Add a test hook to the control. `Probe` reaches private state by reflection on purpose.
