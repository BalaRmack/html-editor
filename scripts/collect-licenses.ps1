param([Parameter(Mandatory=$true)][string]$PublishDir)
$ErrorActionPreference = 'Stop'
trap { throw "collect-licenses failed near line $($_.InvocationInfo.ScriptLineNumber): $($_.Exception.Message)" }
$root = Split-Path $PSScriptRoot -Parent
$assets = Join-Path $root 'src/LocalVoice.App/obj/project.assets.json'
if (-not (Test-Path $assets)) { throw 'Restore the app before collecting licenses.' }
$cacheLine = (& dotnet nuget locals global-packages --list | Select-Object -First 1)
Write-Host "NuGet cache line: $cacheLine"
$cache = ($cacheLine -replace '^.*global-packages:\s*','').Trim()
if (-not (Test-Path $cache)) { throw "NuGet cache not found: $cache" }
$json = Get-Content $assets -Raw | ConvertFrom-Json
$out = Join-Path $PublishDir 'licenses'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$manifest = [System.Collections.Generic.List[object]]::new()
foreach ($lib in $json.libraries.PSObject.Properties) {
    if ($lib.Value.type -ne 'package') { continue }
    $parts = $lib.Name -split '/'
    $id = $parts[0]; $version = $parts[1]
    $pkg = Join-Path $cache (Join-Path $id.ToLowerInvariant() $version)
    if (-not (Test-Path $pkg)) { Write-Host "Skip missing package folder: $pkg"; continue }
    $files = @(Get-ChildItem $pkg -File | Where-Object { $_.Name -match '^(license|licence|copying|notice|third-party)' })
    if ($files.Count -eq 0) {
        $nuspec = Get-ChildItem $pkg -Filter *.nuspec -File | Select-Object -First 1
        if ($nuspec) { $files = @($nuspec) }
    }
    if ($files.Count -eq 0) { continue }
    $dest = Join-Path $out (Join-Path $id $version)
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    foreach ($file in $files) { Copy-Item $file.FullName (Join-Path $dest $file.Name) -Force }
    $manifest.Add([pscustomobject]@{ package = $id; version = $version; files = @($files.Name) })
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -Encoding utf8 (Join-Path $out 'manifest.json')
Write-Host "Collected $($manifest.Count) package license folders."
