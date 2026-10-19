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

## Slow-link harness

The UI harness asks what the window looks like. This one asks what it is doing while somebody
waits, which is where the interesting bugs are: on a slow connection the panel XrmToolBox draws
over a tab disables nothing, so every button stays live for the seconds an answer takes.

```powershell
.\tests\slow.ps1                          # every scenario into tests\.slow
.\tests\slow.ps1 -Scenario cancel         # one
.\tests\slow.ps1 -NoBuild                 # reuse the last build
```

It hosts the same control the UI harness photographs, but hands it a fake Dataverse through the
connection property XrmToolBox would have set, with per-call latency and failures a scenario
scripts. Everything is driven on the UI thread from a timer, because that is where `WorkAsync`
marshals its callbacks back to; a scenario driven from anywhere else would be testing something
nobody does. Each scenario gets a window, a screenshot per beat, and a line per finding, and the
exit code is the number of scenarios with findings.

The windows open past the edge of the desktop and never take the keyboard, so a run does not take
over the machine — set `E2C_HARNESS_ONSCREEN=1` to watch one play out. One scenario presses
Unregister, so the tool really does write a form backup to `Documents\Events2Code\backups`; the
harness counts what appeared there and takes away only what it made.

The sample environment is shared with the UI harness (`tests\harness`), so the two cannot
disagree about what "the sample" is: the forms carry real form XML built from the same handler
lists the screenshots use, and the tool parses it back with its own parser.

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

`BackgroundColor` and `PrimaryFontColor` colour the tile XrmToolBox draws on its Tools list; they
come from the kit's `tint` surface. `SecondaryFontColor` is set for completeness but has no
observable effect there — the list draws every line, description included, in the primary colour.

**Changing any of this and seeing no change means the metadata cache.** XrmToolBox keeps
`Plugins\manifest.json` beside the assemblies and keys it on assembly path plus `Version`, not on
file hash or timestamp. Rebuilding without bumping `<Version>` therefore leaves the old icon and
colours on screen no matter how many times you relaunch. Delete the cache to force a rescan:

```powershell
Remove-Item tests\.xtb\Plugins\manifest.json
```

Releases are unaffected, since step 1 below bumps the version anyway.

## Releasing

1. Set `<Version>` in `Events2Code\Events2Code.csproj` and `<version>` in
   `Events2Code\Events2Code.nuspec` to the release, and stamp `## Unreleased` in `CHANGELOG.md`
   as `## [X.Y.Z] - date`, with its link at the bottom.
2. `.\release-notes.ps1 -Write` regenerates `<releaseNotes>` in the nuspec from the bolded leads
   of the changelog, and commit the result. `.\release-notes.ps1 -Preview` shows beforehand what
   the store will get; only a bullet whose lead is bolded ships.
3. `dotnet build Events2Code\Events2Code.csproj -c Release` and check the tool once in the
   sandbox instance.
4. Tag `vX.Y.Z`, push, and create the GitHub release with `Events2Code.dll` attached. The store
   notes end with a link to that release, so it has to exist before the package does.
5. `.\publish.ps1` pushes the package to nuget.org, which is where the XrmToolBox Tool Library
   installs it from. It runs `.\release-notes.ps1 -Check` first and refuses notes that were never
   regenerated. The API key comes from `-ApiKey` or from `.nuget-apikey` (gitignored).
6. Open the next development version: the next patch plus `-dev` (1.1.0 ships, main becomes
   `1.1.1-dev`) in both files, and a fresh `## Unreleased` heading.

A package version can be unlisted on nuget.org but never replaced, so publish last, after the
release is tagged and the DLL has been tried.
