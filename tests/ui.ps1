# Screenshots the tool's UI at a few window sizes, without XrmToolBox and without a connection.
#
# The layout is built in code and most of it only misbehaves at a size you did not try, so this
# hosts Events2CodeControl in a bare form, fills it with sample rows and grabs a PNG per size.
# Shots land in tests\.ui and are overwritten each run.
#
#   .\ui.ps1                          # default sizes
#   .\ui.ps1 -Size 1600x1000          # one size
#   .\ui.ps1 -Size 1280x900,820x620   # several
#   .\ui.ps1 -NoBuild                 # reuse the last build
#
# The screenshots in docs\images come from here too, with the bigger sample form and a name of
# their own - regenerate them with:
#
#   .\ui.ps1 -Size 1280x820 -Scene showcase -Name overview -Title Events2Code -OutputDir ..\docs\images
#
# It drives the real control, so it screenshots real bugs: a splitter that throws while being set
# up shows here as a failed run rather than as a broken tab in XrmToolBox. Note that it captures
# the screen, so the desktop must be unlocked and the window is briefly topmost.

param(
    [string[]]$Size = @("1280x900", "820x620"),
    [string]$OutputDir,
    # Which sample form to load: "layout" is the small one, "showcase" carries one of every
    # convertible event and is what the documentation screenshots use.
    [ValidateSet("layout", "showcase")]
    [string]$Scene = "layout",
    # File name (without extension) for the shot; only valid for a single size, since several
    # sizes would all write to it in turn. Defaults to the size, e.g. 1280x900.png.
    [string]$Name,
    [string]$Title,
    [switch]$NoBuild
)

if ($Name -and $Size.Count -ne 1) { throw "-Name takes a single -Size, got $($Size.Count)" }

$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "Events2Code.UiHarness\Events2Code.UiHarness.csproj"
$exe     = Join-Path $PSScriptRoot "Events2Code.UiHarness\bin\Debug\net48\Events2Code.UiHarness.exe"
if (-not $OutputDir) { $OutputDir = Join-Path $PSScriptRoot ".ui" }

if (-not $NoBuild) {
    dotnet build $project -c Debug -v q --nologo
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

$failed = $false
foreach ($s in $Size) {
    if ($s -notmatch '^\s*(\d+)\s*[x×]\s*(\d+)\s*$') { throw "Size must look like 1280x900, got '$s'" }
    $width  = [int]$Matches[1]
    $height = [int]$Matches[2]

    $file = if ($Name) { $Name } else { "$($width)x$($height)" }
    $path = Join-Path $OutputDir "$file.png"
    Remove-Item "$path*" -ErrorAction SilentlyContinue

    # Out of process with a timeout: a layout that deadlocks the message loop should fail the run
    # rather than hang it.
    $p = Start-Process $exe -ArgumentList $width, $height, "`"$path`"", $Scene, "`"$Title`"" -PassThru
    if (-not $p.WaitForExit(30000)) {
        $p.Kill()
        Write-Host "TIMED OUT  $($width)x$($height)" -ForegroundColor Red
        $failed = $true
        continue
    }

    if ($p.ExitCode -ne 0) {
        Write-Host "FAILED     $($width)x$($height)" -ForegroundColor Red
        if (Test-Path "$path.error.txt") { Get-Content "$path.error.txt" | Select-Object -First 12 }
        $failed = $true
        continue
    }

    Write-Host "$($width)x$($height)  ->  $path"
}

if ($failed) { exit 1 }
