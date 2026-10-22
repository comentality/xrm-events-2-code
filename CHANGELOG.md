# Changelog

All notable changes to Events2Code are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[semantic versioning](https://semver.org/).

## Unreleased

## [1.1.0] - 2026-10-22

### Fixed

- **Changing the connection now clears the old environment from the window, so no write can
  land in the wrong one.** Anything still loading from the old connection is thrown away instead
  of filling the new window. The last environment's tables, forms and handlers all go. The form
  left selected across a change was the dangerous part: its id is what Unregister and Generate
  write back to, so a write meant for one environment could land in another.
- **On a slow connection the forms list and handler grid always show what you clicked last, and
  the buttons wait for it.** Load Tables, Generate, Save and Unregister stay disabled until what
  they act on has arrived, so Unregister can no longer run twice on one form. They come back
  when it lands or fails. Clicking through tables or forms used to leave the slower answer on
  screen under a newer selection, so the grid described one form while everything acting on it
  pointed at another. Two presses of Unregister used to be two rewrites of the same form, the
  second taking as its "before" state a form the first had already stripped — so the backup
  meant to be the way back was a copy of the damage.
- **Loading tables, forms and a form's XML can now be cancelled from the Cancel button on the
  loading panel.** The tool never offered one before. Cancelling says so and leaves nothing
  stuck.
- **A failed load now stays on the status line, which also says what is still loading instead of
  counting too early.** A table list that could not be read no longer looks like an environment
  with no tables. A table whose forms could not be read no longer shows the previous table's
  forms, and the failure can still be read after the dialog is gone. The status line no longer
  counts handlers of a form that is still loading: "No handlers registered on this form" is now
  a statement about a form the tool has read.
- **Closing the tab mid-load no longer pops up an error later, and a save to a dead share no
  longer freezes the window.** Closing the tab mid-fetch used to put an error dialog up
  afterwards, over whatever you had moved on to. Saving the generated file now happens off the
  UI thread.

### Changed

- The tool's home is now [comentality.com/tools](https://comentality.com/tools/#events2code):
  the package page's project link goes there, and the README says so. The source and these
  notes stay on GitHub.

### Added

- `tests\slow.ps1`, a harness that drives the real control against a Dataverse that takes
  seconds to answer: eleven scenarios, a screenshot per gesture, and a line per finding. Every
  fix above but the save has one that failed before it.

## [1.0.0] - 2026-08-17

First public release.

### Added

- **Browse tables and their Main and Quick Create forms over an XrmToolBox connection, with a
  filter on display or logical name.**
- **List every event handler a form registers through the designer, with its event, target,
  function, library and enabled state.** Its extra parameters and its execution-context flag
  come across too.
- **Generate a single OnLoad bootstrap function that registers the checked handlers through
  the Client API.** It emits `addOnSave`, `addOnChange`, `addTabStateChange`, and `addOnLoad`
  for subgrids, with original form OnLoad handlers called directly. Lookups are guarded with `?.` so a target missing
  from the current form variant is skipped rather than throwing.
- **Designer quirks are preserved: extra parameters and the "pass execution context" flag are
  re-emitted through wrapper closures.** Disabled handlers are emitted commented out, and
  anything not convertible is listed as a comment at the bottom.
- **Copy the generated code to the clipboard or save it to a `.js` file.**
- **Unregister the checked handlers: they leave the form XML, the bootstrap is registered on
  form OnLoad, and the table is published.** Its web resource is added to the form libraries
  and the form is updated.
- **Back up the form XML to `Documents\Events2Code\backups` before any change, and warn on
  managed forms.** Handlers Dynamics owns (`<InternalHandlers>`) are never converted or removed.
- **The generated-code preview is syntax highlighted.**

[1.1.0]: https://github.com/comentality/xrm-events-2-code/releases/tag/v1.1.0
[1.0.0]: https://github.com/comentality/xrm-events-2-code/releases/tag/v1.0.0
