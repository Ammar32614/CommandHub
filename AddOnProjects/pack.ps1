# Builds every add-on in AddOnProjects into <Out>\<id>-<version>.zip and writes <Out>\catalog.json.
# Command hub reads that catalogue from the "addons" GitHub release (or, for testing, from the folder
# named in COMMANDHUB_ADDON_SOURCE). Each zip holds only manifest.json and the add-on's DLL.
param([string]$Out = "addons-dist", [string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
New-Item -ItemType Directory -Force $Out | Out-Null
$Out = (Resolve-Path $Out).Path
$entries = @()
foreach ($dir in Get-ChildItem $root -Directory) {
    $manifestPath = Join-Path $dir.FullName "manifest.json"
    $proj = Get-ChildItem $dir.FullName -Filter *.csproj | Select-Object -First 1
    if (-not (Test-Path $manifestPath) -or -not $proj) { continue }
    $m = Get-Content $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    Write-Host "Building $($m.id) $($m.version)"
    $build = Join-Path ([IO.Path]::GetTempPath()) ("addon-" + $m.id + "-" + [guid]::NewGuid().ToString("N"))
    dotnet build $proj.FullName -c $Configuration -p:Version=$($m.version) -o $build | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $($m.id)" }
    $stage = Join-Path $build "stage"
    New-Item -ItemType Directory -Force $stage | Out-Null
    Copy-Item $manifestPath $stage
    Copy-Item (Join-Path $build $m.entry) $stage
    $zip = Join-Path $Out "$($m.id)-$($m.version).zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip
    Remove-Item $build -Recurse -Force
    $entries += [ordered]@{
        id = $m.id; name = $m.name; version = $m.version; api = $m.api; minHost = $m.minHost
        blurb = $m.blurb; description = $m.description; icon = $m.icon
        url = (Split-Path $zip -Leaf); sha256 = (Get-FileHash $zip -Algorithm SHA256).Hash; size = (Get-Item $zip).Length
    }
}
$catalog = [ordered]@{ updated = (Get-Date).ToUniversalTime().ToString("o"); addons = $entries }
$json = $catalog | ConvertTo-Json -Depth 5
[IO.File]::WriteAllText((Join-Path $Out "catalog.json"), $json, (New-Object Text.UTF8Encoding $false))
Write-Host "Wrote $($entries.Count) add-on(s) to $Out"
