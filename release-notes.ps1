# Generates the nuspec releaseNotes from CHANGELOG.md.
#
#   .\release-notes.ps1              # show what would ship, without touching the nuspec
#   .\release-notes.ps1 -Preview     # the same, including the Unreleased section
#   .\release-notes.ps1 -Write       # write them into the nuspec
#   .\release-notes.ps1 -Check       # fail if the nuspec is not what the changelog says
#
# publish.ps1 runs -Check, so a release cannot ship notes that were never regenerated.
#
# The rule the changelog and the store agree on: a top-level bullet whose lead sentence is
# bolded is user-facing and ships; a bullet without a bolded lead stays in the repo. That is
# why the bolded lead has to say what changed and stand on its own — in the store there is no
# paragraph under it and no screenshot beside it.
#
# Everything that is XrmToolBox rather than Events2Code lives in the XtbSandbox module
# (github.com/comentality/xrmtoolbox-sandbox), shared with the other tools.

param(
    [switch]$Write,
    [switch]$Check,
    [switch]$Preview
)

$ErrorActionPreference = "Stop"

if (-not (Get-Module -ListAvailable XtbSandbox)) {
    throw "XtbSandbox is not installed. Run: Install-Module XtbSandbox -Scope CurrentUser`n(see https://github.com/comentality/xrmtoolbox-sandbox)"
}
Import-Module XtbSandbox

$nuspec    = Join-Path $PSScriptRoot "Events2Code\Events2Code.nuspec"
$changelog = Join-Path $PSScriptRoot "CHANGELOG.md"

if ($Check) {
    Test-XtbReleaseNotes -Nuspec $nuspec -Changelog $changelog
} elseif ($Write) {
    Update-XtbReleaseNotes -Nuspec $nuspec -Changelog $changelog | Out-Null
} else {
    ConvertTo-XtbReleaseNotes `
        -Path       $changelog `
        -ProjectUrl "https://github.com/comentality/xrm-events-2-code" `
        -IncludeUnreleased:$Preview
}
