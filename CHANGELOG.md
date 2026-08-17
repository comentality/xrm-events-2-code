# Changelog

All notable changes to Events2Code are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[semantic versioning](https://semver.org/).

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
