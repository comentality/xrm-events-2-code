# The tool on a slow link, without XrmToolBox and without an environment.
#
# ui.ps1 asks what the window looks like. This asks what it is doing while somebody waits: which
# buttons are still live, what the status line is claiming, and whether the answer that lands last
# belongs to the question asked last.
#
#   .\slow.ps1                          # every scenario
#   .\slow.ps1 -Scenario cancel         # one
#   .\slow.ps1 -Scenario cancel,write-guarded
#   .\slow.ps1 -NoBuild                 # reuse the last build
#
# It drives the real control against a fake Dataverse that takes seconds to answer, handed in
# through the connection property XrmToolBox would have set - so the query code runs for real and
# a form that is three seconds away here is three seconds away the way a bad link makes it. Each
# scenario gets a window and a screenshot per beat of its own under tests\.slow, and the exit code
# is the number of scenarios with findings.
#
# The windows are real windows, but they open past the edge of the desktop and never take the
# keyboard: the shots are the window's own pixels and the gestures are performed on the controls
# rather than typed at them, so the machine stays usable while a suite runs. Set
# E2C_HARNESS_ONSCREEN=1 to watch a scenario play out instead.
#
# One scenario presses Unregister, so the tool really does write a form backup to
# Documents\Events2Code\backups. The harness counts what appeared there - one press, one backup -
# and takes away only the files it made.

param(
    [string[]]$Scenario,
    [string]$OutputDir,
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "Events2Code.SlowHarness\Events2Code.SlowHarness.csproj"
$exe     = Join-Path $PSScriptRoot "Events2Code.SlowHarness\bin\Debug\net48\Events2Code.SlowHarness.exe"
if (-not $OutputDir) { $OutputDir = Join-Path $PSScriptRoot ".slow" }

if (-not $NoBuild) {
    dotnet build $project -c Debug -v q --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$arguments = @("`"$OutputDir`"") + $Scenario

# Out of process with a timeout: a scenario that deadlocks the message loop - which a dialog
# nobody answers would - should fail the run rather than hang it. The budget is the whole suite's,
# and the suite is a little over a minute of deliberate waiting.
$p = Start-Process $exe -ArgumentList $arguments -NoNewWindow -PassThru
if (-not $p.WaitForExit(300000)) {
    $p.Kill()
    Write-Host "TIMED OUT" -ForegroundColor Red
    exit 1
}

exit $p.ExitCode
