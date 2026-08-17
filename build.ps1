# Builds the tool and drops the DLL into an XrmToolBox Plugins folder.
#
#   .\build.ps1
#   .\build.ps1 -PluginDir D:\XrmToolBox\Plugins
#
# Without -PluginDir it uses $env:XRMTOOLBOX_PLUGINS, and failing that the folder the installed
# XrmToolBox reads. For a throwaway instance that cannot disturb your real one, use tests\xtb.ps1.

param(
    [string]$PluginDir
)

$project = Join-Path $PSScriptRoot "Events2Code\Events2Code.csproj"

dotnet build $project -c Debug
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

& (Join-Path $PSScriptRoot "deploy.ps1") -PluginDir $PluginDir
