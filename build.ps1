# disk-cleaner build script
# Usage: powershell -ExecutionPolicy Bypass -File build.ps1
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

Write-Host 'Publishing disk-cleaner (win-x64, single-file, self-contained)...'
dotnet publish src\disk-cleaner.csproj -c Release -o dist
if ($LASTEXITCODE -ne 0) { throw "publish failed with exit code $LASTEXITCODE" }

$exe = Join-Path $PSScriptRoot 'dist\disk-cleaner.exe'
if (-not (Test-Path $exe)) { throw "output not found: $exe" }

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host "Done: $exe ($sizeMb MB)"
Write-Host 'Smoke test (dry-run):'
& $exe --help | Select-Object -First 3
