# Reset admin password from .env (stop uvicorn first recommended)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
. (Join-Path $PSScriptRoot "scripts\venv-path.ps1")

if (-not (Test-Path $script:VenvPython)) {
    Write-Error "Chua co venv. Chay: .\install.ps1"
}

& $script:VenvPython (Join-Path $PSScriptRoot "reset_admin.py")
