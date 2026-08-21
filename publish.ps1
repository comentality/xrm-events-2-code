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

$nuspec = Join-Path $PSScriptRoot "Events2Code\Events2Code.nuspec"
[xml]$spec = Get-Content $nuspec
$version = $spec.package.metadata.version
if ($spec.package.metadata.releaseNotes -notmatch [regex]::Escape("v$version")) {
    Write-Host "releaseNotes in $(Split-Path $nuspec -Leaf) do not mention v$version. The store shows whatever ships; update the notes first." -ForegroundColor Red
    exit 1
}
$changelog = Join-Path $PSScriptRoot "CHANGELOG.md"
if ((Test-Path $changelog) -and ((Get-Content $changelog -Raw) -notmatch "## \[?$([regex]::Escape($version))")) {
    Write-Host "CHANGELOG.md has no section for $version. Add one before publishing." -ForegroundColor Red
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
