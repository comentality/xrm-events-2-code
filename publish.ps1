param(
    [string]$ApiKey
)

if (-not $ApiKey) {
    $keyFile = Join-Path $PSScriptRoot ".nuget-apikey"
    if (Test-Path $keyFile) {
        $ApiKey = (Get-Content $keyFile -First 1).Trim()
    } else {
        Write-Host "No API key provided and .nuget-apikey file not found." -ForegroundColor Red
        exit 1
    }
}

$project = Join-Path $PSScriptRoot "Events2Code\Events2Code.csproj"

# The store shows whatever ships, and a nupkg can never be replaced or deleted. So the notes
# are generated from CHANGELOG.md rather than kept in step by hand: this fails the publish if
# the nuspec is not what the changelog says, or if the version has no section behind it at
# all. Run .\release-notes.ps1 -Write and commit the result.
try {
    & (Join-Path $PSScriptRoot "release-notes.ps1") -Check | Out-Null
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}

dotnet build $project -c Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet pack $project -c Release --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$nupkg = Get-ChildItem (Join-Path $PSScriptRoot "Events2Code\bin\Release\*.nupkg") | Sort-Object LastWriteTime -Descending | Select-Object -First 1

Write-Host "Publishing $($nupkg.Name)..."
dotnet nuget push $nupkg.FullName --api-key $ApiKey --source https://api.nuget.org/v3/index.json
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Published $($nupkg.Name) to NuGet.org"
