# Limitations and troubleshooting

## What it will not convert

**Internal handlers.** Registrations that live in `<InternalHandlers>` belong to Dynamics, not to
whoever built the form. They show greyed out, cannot be checked, and are never removed — even if
something upstream asked for it.

**Events with no registration API.** Anything the Client API cannot register at runtime — form
`setadditionalparams`, business process flow events, and other event names the tool does not
classify — is listed for completeness and left on the form. The generated file records each one
in the `// NOT converted` block at the bottom.

**Form OnLoad.** There is no `formContext.data.addOnLoad` for the form itself, so the migration
always ends with exactly one designer registration: the bootstrap. Original OnLoad handlers are
called from inside it.

## Known gaps

**Subgrids on collapsed or secondary tabs.** `formContext.getControl("grid")` returns `null` for
a grid that has not been instantiated yet, which is the case at form OnLoad for grids on a
collapsed tab or a tab other than the default one. The `?.` guard means the registration is
silently skipped rather than throwing — the handler simply never runs. If a subgrid handler stops
firing after migration, move its registration into the owning tab's `addTabStateChange` callback
by hand. This is the one case where the generated code needs editing rather than just deploying.

**Handlers without a `handlerUniqueId`.** Removal falls back to matching on function name plus
library name for those, so a handler registered on two different events from the same function
and library is removed from both when either is checked. Forms written by current versions of the
designer always carry unique ids; hand-edited or very old form XML may not.

**Main and Quick Create forms only.** Card, dialog, and other form types are not listed.

**ES2020 output.** Optional chaining is not ES5. If your web resource pipeline minifies or lints
against an older target, adjust the target rather than the generated code.

## Errors you might see

| Message | What it means |
|---|---|
| *Not connected to Dynamics. Please connect first.* | The XrmToolBox tab has no connection — pick one from the connection bar. |
| *Error loading tables / forms / form* | The request to Dataverse failed; the underlying message is shown. Usually privileges or a dropped connection. |
| *Error parsing form XML* | The form's XML could not be read as XML. Nothing has been changed; the message names the parse failure. |
| *This form is managed.* | A warning, not an error — see [managed forms](form-changes.md#managed-forms). |
| *Check at least one handler to convert.* | Nothing is ticked in the grid. |
| *Enter the bootstrap function name and its web resource name first.* | Both fields are required for unregistering; only the function name is required for generating. |
| *Error updating form* | The update or the publish failed. The backup was already written, and the form is unchanged unless the update itself succeeded and only the publish failed — in which case rerunning the publish from the maker portal finishes the job. |

## Behaviour that surprises people

**Disabled handlers start unchecked.** Converting one would bring code the form had switched off
back to life. Check it deliberately if that is what you want; it is emitted commented out.

**The Generate button needs a form, not a table.** It enables once a form with at least one
convertible handler is loaded.

**Unregister needs a generate first.** The button enables after code has been generated, which is
deliberate: the code has to exist somewhere before the registrations that depend on it are
removed.

**Running it twice is safe.** The bootstrap is only added if a handler for the same function and
library is not already registered, and handlers already removed simply do not match anything.

## Getting help

Open an issue at
[github.com/comentality/xrm-events-2-code/issues](https://github.com/comentality/xrm-events-2-code/issues).
The form XML from `Documents\Events2Code\backups` — with anything sensitive stripped — is the
single most useful attachment for a parsing or rewriting bug.
