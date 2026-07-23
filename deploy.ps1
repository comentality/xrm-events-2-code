$pluginDir = "C:\Users\kk\Downloads\XrmToolbox\Plugins"
if (-not (Test-Path $pluginDir)) { New-Item -ItemType Directory -Path $pluginDir | Out-Null }

$source = Join-Path $PSScriptRoot "Events2Code\bin\Debug\net48\Events2Code.dll"
Copy-Item $source -Destination $pluginDir -Force

Write-Host "Deployed Events2Code.dll to $pluginDir"
