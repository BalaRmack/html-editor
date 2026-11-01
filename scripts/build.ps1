[CmdletBinding()]
param(
    [ValidateSet('restore','build','test','publish','installer','all')]
    [string]$Task = 'all',
    [ValidatePattern('^\d+\.\d+\.\d+$')][string]$Version = '1.2.0',
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$InnoCompiler,
    [switch]$SkipInstaller
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root 'artifacts'
$publish = Join-Path $artifacts "publish/$Runtime"
$installerOut = Join-Path $artifacts 'installer'
$app = Join-Path $root 'src/LocalVoice.App/LocalVoice.App.csproj'
$solution = Join-Path $root 'LocalVoice.sln'
function Invoke-DotNet {
    param([string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) { throw "dotnet failed with exit code $LASTEXITCODE" }
}
function Find-Inno {
    param([string]$Explicit)
    $candidates = @($Explicit, $env:LOCALVOICE_ISCC)
    $candidates += @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) { return (Resolve-Path -LiteralPath $candidate).Path }
    }
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    throw 'Inno Setup 6 not found. Install https://jrsoftware.org/isinfo.php or pass -InnoCompiler <path-to-ISCC.exe>.'
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install .NET SDK 8.0.4xx first. See README.md.' }
$sdk = & dotnet --version
if ($LASTEXITCODE -ne 0 -or $sdk -notmatch '^8\.0\.4') { throw "This project requires .NET SDK 8.0.4xx; found $sdk." }
if ($Task -in @('restore','all')) { Invoke-DotNet @('restore', $solution, '-p:EnableWindowsTargeting=true', '--use-lock-file') }
if ($Task -in @('build','all')) { Invoke-DotNet @('build', $solution, '-c', $Configuration, '--no-restore', "-p:Version=$Version") }
if ($Task -in @('test','all')) {
    Invoke-DotNet @('test', (Join-Path $root 'tests/LocalVoice.Core.Tests/LocalVoice.Core.Tests.csproj'), '-c', $Configuration, '--no-build', '--logger', 'trx', '--results-directory', (Join-Path $artifacts 'tests'))
}
if ($Task -in @('publish','all')) {
    if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
    Invoke-DotNet @('publish', $app, '-c', $Configuration, '-r', $Runtime, '--self-contained', 'true', '-o', $publish, "-p:Version=$Version", '-p:PublishSingleFile=false', '-p:PublishReadyToRun=false', '-p:DebugType=none', '-p:DebugSymbols=false')
    $exe = Join-Path $publish 'LocalVoice.exe'
    if (-not (Test-Path -LiteralPath $exe)) { throw "Publish did not create $exe" }
    $native = Get-ChildItem -LiteralPath $publish -Recurse -File | Where-Object { $_.Name -eq 'whisper.dll' }
    if (-not $native) { throw 'The native whisper.dll runtime is missing from publish output.' }
    $noAvxDir = Join-Path $publish 'runtimes/noavx/win-x64'
    foreach ($dll in @('whisper.dll','ggml-whisper.dll','ggml-base-whisper.dll','ggml-cpu-whisper.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $noAvxDir $dll))) { throw "NoAvx runtime missing: $dll" }
    }
    & (Join-Path $PSScriptRoot 'collect-licenses.ps1') -PublishDir $publish
    Get-FileHash -Algorithm SHA256 -LiteralPath $exe | ForEach-Object { "$($_.Hash.ToLowerInvariant())  LocalVoice.exe" } | Set-Content -Encoding ascii (Join-Path $publish 'LocalVoice.exe.sha256')
    Write-Host "Published: $exe"
}
if (($Task -in @('installer','all')) -and -not $SkipInstaller) {
    if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'Inno Setup requires Windows. Publish can be cross-built; compile the installer on Windows or CI.' }
    if (-not (Test-Path -LiteralPath (Join-Path $publish 'LocalVoice.exe'))) { throw 'Publish the application first: scripts/build.ps1 -Task publish' }
    $iscc = Find-Inno $InnoCompiler
    New-Item -ItemType Directory -Force -Path $installerOut | Out-Null
    & $iscc "/DAppVersion=$Version" "/DPublishDir=$publish" "/O$installerOut" (Join-Path $root 'installer/LocalVoice.iss')
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE" }
    $setup = Join-Path $installerOut "LocalVoice-$Version-win-x64-setup.exe"
    if (-not (Test-Path -LiteralPath $setup)) { throw "Installer not found: $setup" }
    Get-FileHash -Algorithm SHA256 -LiteralPath $setup | ForEach-Object { "$($_.Hash.ToLowerInvariant())  $(Split-Path $setup -Leaf)" } | Set-Content -Encoding ascii "$setup.sha256"
    Write-Host "Installer: $setup"
}
