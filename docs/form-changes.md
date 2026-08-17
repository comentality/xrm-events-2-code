# What unregistering changes

**Unregister UI Handlers** is the only thing the tool writes to your environment. Everything
before it — loading tables, forms, generating code — is read-only.

## The confirmation

Before anything happens you get a dialog naming the form, the number of handlers to remove, the
bootstrap function and library that will be registered, and the backup folder. It ends with a
reminder that the web resource must already contain the generated code. Nothing is written if
you answer no.

## The edits, in order

1. **Backup.** The current `formxml` is written verbatim to
   `Documents\Events2Code\backups\<entity>_<form>_<yyyyMMdd_HHmmss>.xml`. This happens before the
   form XML is touched, and the resulting path is shown when the run finishes.

2. **Remove the checked handlers.** Each `<Handler>` element is matched by `handlerUniqueId`, or,
   for handlers that have none, by function name plus library name. Handlers inside
   `<InternalHandlers>` are skipped no matter what was passed in — those are registrations
   Dynamics owns, and removing one would silently drop platform form logic.

3. **Tidy up.** `<event>` elements left with no handlers are removed, and `<events>` containers
   left with no events go with them.

4. **Register the bootstrap.** The form-level OnLoad event — an `<event name="onload">` with no
   `attribute`, `control`, or `tab` — is created if the form has none, and a `<Handler>` for the
   bootstrap function is added with `passExecutionContext="true"` and a fresh
   `handlerUniqueId`. If a handler for the same function and library is already there, nothing is
   added; running the tool twice does not produce a duplicate registration.

5. **Register the library.** The bootstrap's web resource is appended to `<formLibraries>` unless
   it is already listed (compared case-insensitively).

6. **Update and publish.** The rewritten XML goes back through an `Update` on `systemform`,
   followed by a `PublishXmlRequest` for that one table.

7. **Reload.** The form XML is fetched again and the grid refills, so what you see afterwards is
   the environment's state, not an optimistic local guess.

## Rolling back

The backup is the complete form XML from immediately before the change, so restoring is a matter
of writing it back to the form's `formxml` column and republishing it. Through the Web API, with
`$headers` holding a bearer token for the environment and `$formId` taken from the form's URL in
the maker portal:

```powershell
$xml  = Get-Content "$env:USERPROFILE\Documents\Events2Code\backups\contact_Contact_20260817_143210.xml" -Raw
$body = @{ formxml = $xml } | ConvertTo-Json

Invoke-RestMethod -Method Patch -Uri "$url/api/data/v9.2/systemforms($formId)" `
    -Headers $headers -ContentType "application/json" -Body $body

$publish = @{ ParameterXml = "<importexportxml><entities><entity>contact</entity></entities></importexportxml>" } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri "$url/api/data/v9.2/PublishXml" `
    -Headers $headers -ContentType "application/json" -Body $publish
```

Backups accumulate — the folder is never pruned — so the pre-migration state stays available long
after the run.

If the environment has solution history you would rather use, exporting the unmanaged solution
containing the form before migrating gives you the same rollback through a re-import.

## Managed forms

Selecting a managed form warns you up front. The tool can still rewrite it, but the result is an
unmanaged customization layered over the managed form, which will shadow future updates shipped
by the managed solution's publisher. Prefer migrating in the environment the form is authored in,
and shipping the change through your normal solution pipeline.

## What it never touches

- Handlers in `<InternalHandlers>`
- Handlers you left unchecked in the grid
- Web resources — creating or updating the JavaScript file is yours to do
- Any table other than the one being published
