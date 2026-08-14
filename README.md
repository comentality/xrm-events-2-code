# Events2Code

An [XrmToolBox](https://www.xrmtoolbox.com/) tool that converts Dynamics 365 form event handler registrations made in the form designer UI into equivalent JavaScript code — and unregisters them from the form.

Registering event handlers through the form designer (Form Properties → Event Handlers) is tedious and hides your form logic in form XML. The modern approach is to register everything in code from a single OnLoad bootstrap using the Client API (`addOnChange`, `addOnSave`, `addTabStateChange`, …). Events2Code automates the migration.

## Features

- **Browse forms** — connect via XrmToolBox, pick a table, and see all Main and Quick Create forms
- **List registrations** — every UI-registered handler with event type, target, function, library, parameters, and enabled state
- **Generate JavaScript** — produces a single OnLoad bootstrap function that registers the checked handlers programmatically:
  - Form OnSave → `formContext.data.entity.addOnSave(...)`
  - Attribute OnChange → `formContext.getAttribute(...)?.addOnChange(...)`
  - Tab state change → `formContext.ui.tabs.get(...)?.addTabStateChange(...)`
  - Subgrid OnLoad → `formContext.getControl(...)?.addOnLoad(...)`
  - Original form OnLoad handlers → called directly from the bootstrap

  Lookups are guarded with `?.` so a field, tab, or grid missing from the current form variant is skipped instead of throwing and killing every registration after it. This makes the output ES2020 — fine for Unified Interface, but check any minifier or ESLint config pinned to ES5.
- **Handles designer quirks** — extra handler parameters and the "pass execution context" flag are preserved via wrapper closures; disabled handlers are emitted commented out
- **Unregister UI handlers** — removes the checked handlers from the form XML, registers your bootstrap function on form OnLoad, adds its web resource to the form libraries, updates the form, and publishes
- **Safety first** — the original form XML is backed up to `Documents\Events2Code\backups` before any change, and managed forms trigger a warning

## Workflow

1. Load tables, pick a table, pick a form.
2. Review the handler grid; uncheck anything you want to leave as is.
3. Set the bootstrap function name (e.g. `MyOrg.FormEvents.onLoad`) and the web resource name it will live in.
4. **Generate Code**, then save the JavaScript into that web resource (create/update it in your solution) and publish it.
5. **Unregister UI Handlers** — the tool strips the old registrations, wires up the bootstrap OnLoad, and publishes the entity.

The order matters: upload the web resource *before* unregistering, or the form will briefly have no working scripts.

## Notes

- Form OnLoad itself cannot be registered from code, so exactly one UI registration remains: the bootstrap.
- Events with no programmatic registration API (e.g. business process flow events in v1) are listed but left untouched on the form.

## License

MIT
