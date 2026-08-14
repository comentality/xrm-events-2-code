# E2E test fixtures

Creates the test data Events2Code needs for end-to-end testing in a Dataverse environment:

- **`e2e_test_handlers.js`** web resource — dummy `E2ETest.*` handler functions that log every invocation to the console and to `window._e2eCalls`.
- **"E2E Events Test"** — an unmanaged copy of the Contact main form (formid `0e2e0001-aaaa-4bbb-8ccc-000000000001`) with UI-registered event handlers covering every event kind the tool supports:

| Event | Target | Function | Notes |
|---|---|---|---|
| Form OnLoad | — | `E2ETest.onFormLoad` | passExecutionContext |
| Form OnLoad | — | `E2ETest.onFormLoadExtra` | parameters `"hello", 42`, no exec context |
| Form OnSave | — | `E2ETest.onSave` | passExecutionContext |
| OnChange | `firstname` | `E2ETest.onFirstNameChange` | passExecutionContext |
| OnChange | `lastname` | `E2ETest.onLastNameChange` | **disabled**, parameters `'test'` |
| TabStateChange | `DETAILS_TAB` | `E2ETest.onDetailsTabStateChange` | stored nested inside the `<tab>` element |
| Subgrid OnLoad | `Subgrid_e2e` | `E2ETest.onSubgridLoad` | subgrid added to DETAILS_TAB |

The form also keeps the stock internal `AppCommon.Contact.Instance.form_onload` handler (`application="true"`), so tests see a realistic mix.

Everything ships as unmanaged solution **E2EEventsTest** (publisher `e2epublisher`, prefix `e2e`), so removing the fixtures is just deleting that solution + its components.

## Deploy

```powershell
# auth once: pac auth create --environment <test env url>
.\build_solution.ps1                                  # builds E2EEventsTest.zip next to the script
pac solution import --path .\E2EEventsTest.zip --publish-changes
```

## Running the e2e tests

```powershell
dotnet test tests/Events2Code.E2ETests
```

The suite (`tests/Events2Code.E2ETests/E2EPipelineTests.cs`) authenticates with the service principal from `.env` (it self-skips with a message if `.env` is missing), resets the environment to fixture state (re-imports this solution, deletes the test-created `e2e_bootstrap.js`), then runs the tool's real pipeline against the live env:

1. **Parse** — fetch the fixture form, `FormXmlParser.Parse`, assert all 8 registrations exactly.
2. **Generate** — `JsCodeGenerator.Generate`, assert the bootstrap JS line by line.
3. **Unregister cycle** — upload the bootstrap web resource, `FormXmlRewriter.Rewrite`, PATCH the form, publish, re-fetch, and assert only the internal + bootstrap OnLoad handlers remain.

Teardown restores fixture state again, so runs are repeatable back-to-back.

## Service principal auth (for test code)

`.env` in this folder (gitignored — never commit) holds `DATAVERSE_URL`, `TENANT_ID`, `CLIENT_ID`, `CLIENT_SECRET` for the **Events2Code-E2E-SP** app registration, whose app user has System Administrator in the test env. Tests get a Web API token non-interactively via client credentials:

```powershell
$body = @{ grant_type='client_credentials'; client_id=$env:CLIENT_ID; client_secret=$env:CLIENT_SECRET; scope="$env:DATAVERSE_URL/.default" }
$token = (Invoke-RestMethod -Method Post -Uri "https://login.microsoftonline.com/$env:TENANT_ID/oauth2/v2.0/token" -Body $body).access_token
```

The secret expires 2027-08-14; rotate in Entra → App registrations → Events2Code-E2E-SP → Certificates & secrets.

## Files

- `contact_form.xml` — base Contact main form XML fetched from the env (formid `1fed44d1-ae68-4a41-bd2b-f13acac4acfa`); the build script injects the subgrid, events, and `formLibraries` into it
- `solution/` — solution.xml, [Content_Types].xml, the web resource, and the generated customizations.xml
- `build_solution.ps1` — assembles customizations.xml and zips the solution

## Form XML schema gotchas (learned the hard way)

- `<event control="...">` is schema-valid at form level; `<event tab="...">` is **not** — tab events must be nested inside the `<tab>` element as `<events><event name="tabstatechange">…`.
- Root components: contact needs `type="1" schemaName="contact" behavior="2"`; the web resource needs both `id` and `schemaName` on its `type="61"` entry.
- The web resource file must keep its `.js` extension in the zip and in `<FileName>`.
