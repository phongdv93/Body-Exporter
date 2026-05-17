# One-time setup - venv at %USERPROFILE%\.venvs\bodyexporter-web (not website\.venv)
param([switch]$NoWait)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot "scripts\venv-path.ps1")

Write-Host "Venv: $script:VenvRoot"
Write-Host "If port 8080 is in use, run: .\stop-server.ps1"
if (-not $NoWait) {
    $null = Read-Host "Press Enter to continue"
}

if (Test-Path $script:VenvPython) {
    Write-Host "Venv already exists - upgrading packages..."
} else {
    New-Item -ItemType Directory -Force -Path (Split-Path $script:VenvRoot) | Out-Null
    Write-Host "Creating venv..."
    python -m venv $script:VenvRoot
    if (-not (Test-Path $script:VenvPython)) {
        py -3.12 -m venv $script:VenvRoot
    }
}

if (-not (Test-Path $script:VenvPython)) {
    Write-Error "Could not create venv at $script:VenvRoot"
}

& $script:VenvPython -m ensurepip --upgrade
& $script:VenvPython -m pip install --upgrade pip
& $script:VenvPython -m pip install -r requirements.txt
if ($LASTEXITCODE -ne 0) {
    Write-Error "pip install failed. Run .\stop-server.ps1 and retry."
}

if (-not (Test-Path .env)) {
    Copy-Item .env.example .env
}

Write-Host ""
Write-Host "OK. Run: .\run.ps1"
Write-Host "Optional: delete old locked folder: Remove-Item -Recurse -Force .venv"
Write-Host "  (only after .\stop-server.ps1 and closing Cursor terminals)"
