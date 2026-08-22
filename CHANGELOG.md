# Changelog

All notable changes to Events2Code are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[semantic versioning](https://semver.org/).

## Unreleased

### Fixed

- The forms list and the handler grid now belong to whatever was clicked last. Clicking through
  tables or forms on a slow connection used to leave the slower answer on screen under a newer
  selection, so the grid described one form while everything acting on it pointed at another.
- Load Tables, Generate, Save and Unregister go dead while the thing they act on is still
  arriving, and come back when it lands or fails. Two presses of Unregister used to be two
  rewrites of the same form, the second taking as its "before" state a form the first had
  already stripped — so the backup meant to be the way back was a copy of the damage.
- A fetch that fails now says so where it can still be read after the dialog is gone. A table
  list that could not be loaded no longer looks like an environment with no tables, and a table
  whose forms could not be read no longer shows the previous table's forms.
- The status line no longer counts handlers of a form that is still loading, and says what it is
  waiting for instead. "No handlers registered on this form" is now a statement about a form the
  tool has read.
- Loading tables, forms and a form's XML can be cancelled from the panel's Cancel button, which
  the tool never offered before. Cancelling says so and leaves nothing stuck.
- Closing the tab mid-fetch no longer puts an error dialog up afterwards, over whatever you
  moved on to.
- Saving the generated file happens off the UI thread, so a save to a share that is down no
  longer freezes the window.

### Added

- `tests\slow.ps1`, a harness that drives the real control against a Dataverse that takes
  seconds to answer: ten scenarios, a screenshot per gesture, and a line per finding. Every fix
  above has one that failed before it.

## [1.0.0] - 2026-08-17

First public release.

### Added

- Browse tables and their Main and Quick Create forms over an XrmToolBox connection, with a
  filter on display or logical name.
- List every event handler registered on a form through the form designer, with its event,
  target, function, library, extra parameters, execution-context flag, and enabled state.
- Generate a single OnLoad bootstrap function that registers the checked handlers through the
  Client API: `addOnSave`, `addOnChange`, `addTabStateChange`, and `addOnLoad` for subgrids, with
  original form OnLoad handlers called directly. Lookups are guarded with `?.` so a target missing
  from the current form variant is skipped rather than throwing.
- Preserve designer quirks in the generated code: extra parameters and the "pass execution
  context" flag are re-emitted through wrapper closures, disabled handlers are emitted commented
  out, and anything not convertible is listed as a comment at the bottom.
- Copy the generated code to the clipboard or save it to a `.js` file.
- Unregister the checked handlers: remove them from the form XML, register the bootstrap on form
  OnLoad, add its web resource to the form libraries, update the form, and publish the table.
- Back up the form XML to `Documents\Events2Code\backups` before any change, warn on managed
  forms, and refuse to convert or remove handlers Dynamics owns (`<InternalHandlers>`).
- Syntax highlighting in the generated-code preview.

[1.0.0]: https://github.com/comentality/xrm-events-2-code/releases/tag/v1.0.0
