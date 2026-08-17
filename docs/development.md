# Development

## Layout

```
Events2Code/            the tool itself
  Events2CodePlugin.cs    XrmToolBox plugin metadata (name, description, icons)
  Events2CodeControl.cs   the whole UI, plus the Dataverse calls it makes
  JsSyntaxHighlighter.cs  colouring for the generated-code preview
  Logic/                  everything worth testing, with no UI and no SDK dependency
    FormXmlParser.cs        form XML -> handler list
    JsCodeGenerator.cs      handler list -> bootstrap JavaScript
    FormXmlRewriter.cs      form XML + handlers to drop -> new form XML
tests/
  Events2Code.E2ETests/   the live pipeline suite
  Events2Code.UiHarness/  hosts the control outside XrmToolBox for screenshots
  e2e-fixtures/           the solution deployed to the test environment
```

The split matters: `Logic` holds the three transformations that can go wrong quietly, and it
compiles without XrmToolBox or a connection, so the end-to-end suite exercises exactly the code
the tool runs rather than a reimplementation of it.

## Build

```powershell
dotnet build Events2Code\Events2Code.csproj -c Release
.\build.ps1                # Debug build, copied into an XrmToolBox Plugins folder
.\deploy.ps1               # copy the last Debug build without rebuilding
```

Both scripts take `-PluginDir`, and fall back to `$env:XRMTOOLBOX_PLUGINS` and then to
`%AppData%\MscrmTools\XrmToolBox\Plugins`.

## Running it against a real environment

```powershell
.\tests\xtb.ps1            # build, wire up a private XrmToolBox, launch it
.\tests\xtb.ps1 -Reset     # throw that instance away and rebuild it
```

The instance lives in `tests\.xtb` with its own plugins, settings, and connection list, so it
cannot disturb the XrmToolBox you use for real work. It needs the
[XtbSandbox](https://github.com/comentality/xrmtoolbox-sandbox) module and a `pac auth` profile
pointing at the environment you want.

## UI harness

The layout is built in code, and most of it only misbehaves at a size nobody tried. The harness
hosts the real control in a bare form, fills it with sample rows, and screenshots it:

```powershell
.\tests\ui.ps1                                  # default sizes into tests\.ui
.\tests\ui.ps1 -Size 1600x1000                  # one size
.\tests\ui.ps1 -NoBuild                         # reuse the last build
```

Because it drives the real control, a splitter that throws while being set up fails the run
instead of showing up later as a broken tab in XrmToolBox.

The documentation screenshots come from the same harness, using the bigger `showcase` sample form
and the real code generator for the preview pane — a screenshot cannot drift away from what the
tool actually emits:

```powershell
cd tests
.\ui.ps1 -Size 1280x820 -Scene showcase -Name overview -Title Events2Code -OutputDir ..\docs\images
.\ui.ps1 -Size 900x620  -Scene showcase -Name compact  -Title Events2Code -OutputDir ..\docs\images -NoBuild
```

Regenerating them rewrites the date in the generated-code header, which is the only churn.

## End-to-end tests

```powershell
dotnet test tests\Events2Code.E2ETests
```

The suite runs the real `Logic` pipeline against a live Dataverse environment: it resets the
environment to fixture state, parses the fixture form, asserts the generated JavaScript line by
line, then rewrites, updates, publishes, and re-reads the form to confirm only the internal and
bootstrap OnLoad handlers remain. Teardown restores the fixture, so runs repeat cleanly.

It needs `tests\e2e-fixtures\.env` with service principal credentials and the fixture solution
imported into the environment; without the `.env` the suite skips itself with a message. See
[tests/e2e-fixtures/README.md](../tests/e2e-fixtures/README.md) for deployment and for the form
XML schema gotchas that fixture ran into.

## The icon

Three copies of the same artwork have to stay in step: `SmallImageBase64` (32×32) and
`BigImageBase64` (80×80) in `Events2CodePlugin.cs`, and `Events2Code\icon.png` (80×80), which the
nuspec ships. They are generated, not drawn by hand — the palette and the generator live in the
[Comentality brand kit](https://github.com/comentality/comentality-brand), where `brand.py`
renders the PNGs and prints the base64 to paste in.

## Releasing

1. Bump `<Version>` in `Events2Code\Events2Code.csproj` and `<version>` in
   `Events2Code\Events2Code.nuspec`, and add the release to `CHANGELOG.md`.
2. Update `<releaseNotes>` in the nuspec.
3. `dotnet build Events2Code\Events2Code.csproj -c Release` and check the tool once in the
   sandbox instance.
4. Tag `vX.Y.Z`, push, and create the GitHub release with `Events2Code.dll` attached.
5. `.\publish.ps1` pushes the package to nuget.org, which is where the XrmToolBox Tool Library
   installs it from. The API key comes from `-ApiKey` or from `.nuget-apikey` (gitignored).

A package version can be unlisted on nuget.org but never replaced, so publish last, after the
release is tagged and the DLL has been tried.
