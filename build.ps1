$project = Join-Path $PSScriptRoot "Events2Code\Events2Code.csproj"

dotnet build $project -c Debug
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$pluginDir = "C:\Users\kk\Downloads\XrmToolbox\Plugins"
if (-not (Test-Path $pluginDir)) { New-Item -ItemType Directory -Path $pluginDir | Out-Null }

$source = Join-Path $PSScriptRoot "Events2Code\bin\Debug\net48\Events2Code.dll"
Copy-Item $source -Destination $pluginDir -Force

Write-Host "Built and deployed Events2Code.dll to $pluginDir"
