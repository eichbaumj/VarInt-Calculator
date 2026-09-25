# Builds the Varint Calculator installer: tests, a fresh self-contained publish for
# win-x64, then Inno Setup. Output: Installer\VarintCalculator_<version>_x64.exe
#
#   powershell -ExecutionPolicy Bypass -File setup\Build-Installer.ps1 [-SkipTests]
#
# Only the win-x64 build output is cleared before publishing (an icon swap does not
# invalidate an incremental build, so a stale exe could keep the old icon). The
# Installer folder is never cleaned: it holds the released setup files.

param([switch]$SkipTests)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
if (-not (Test-Path $iscc)) { throw "Inno Setup 6 not found at $iscc" }

if (-not $SkipTests) {
    Write-Host '== Tests' -ForegroundColor Cyan
    dotnet run --project VarIntCalculator.Tests\VarIntCalculator.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed; no installer built.' }
}

Write-Host '== Publish (self-contained, win-x64)' -ForegroundColor Cyan
foreach ($dir in 'bin\Release\net9.0-windows\win-x64', 'obj\Release\net9.0-windows\win-x64') {
    if (Test-Path $dir) { Remove-Item -Recurse -Force $dir }
}
dotnet publish VarIntCalculator.csproj -c Release -r win-x64 --self-contained true -p:DebugType=none
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }

$publish = Join-Path $root 'bin\Release\net9.0-windows\win-x64\publish'
foreach ($required in 'VarIntCalculator.exe', 'Fonts\OFL-Saira.txt', 'Fonts\OFL-Inter.txt', 'Fonts\OFL-BeVietnamPro.txt') {
    if (-not (Test-Path (Join-Path $publish $required))) { throw "Publish is missing $required" }
}
$version = (Get-Item (Join-Path $publish 'VarIntCalculator.exe')).VersionInfo.ProductVersion
Write-Host "   VarIntCalculator.exe $version"

Write-Host '== Inno Setup' -ForegroundColor Cyan
& $iscc 'VarIntCalculator_Setup.iss' | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup failed.' }

$setup = Get-ChildItem (Join-Path $root 'Installer') -Filter 'VarintCalculator_*_x64.exe' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
$hash = (Get-FileHash $setup.FullName -Algorithm SHA256).Hash
Write-Host ''
Write-Host "Installer: $($setup.FullName)" -ForegroundColor Green
Write-Host ("Size:      {0:N1} MB" -f ($setup.Length / 1MB))
Write-Host "SHA-256:   $hash"
