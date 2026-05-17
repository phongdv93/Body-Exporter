# Run dev server (uses venv outside repo)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot "scripts\venv-path.ps1")

if (-not (Test-Path $script:VenvPython)) {
    Write-Host "No venv yet. Running install.ps1 ..."
    & (Join-Path $PSScriptRoot "install.ps1")
}

$old = $ErrorActionPreference
$ErrorActionPreference = "SilentlyContinue"
& $script:VenvPython -c "import uvicorn, itsdangerous" 2>$null
$ready = ($LASTEXITCODE -eq 0)
$ErrorActionPreference = $old

if (-not $ready) {
    Write-Host "Packages missing. Running install.ps1 ..."
    & (Join-Path $PSScriptRoot "install.ps1")
}

if (-not (Test-Path .env)) {
    Copy-Item .env.example .env
    Write-Host "Created .env - set SECRET_KEY and ADMIN_PASSWORD."
}

Write-Host "Python: $script:VenvPython"
Write-Host "Starting http://127.0.0.1:8080"
& $script:VenvPython -m uvicorn app.main:app --reload --host 127.0.0.1 --port 8080
