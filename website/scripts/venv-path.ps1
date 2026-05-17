# Shared venv location (outside repo — avoids locked website\.venv)
$script:VenvRoot = if ($env:BODYEXPORTER_VENV) {
    $env:BODYEXPORTER_VENV
} else {
    Join-Path $env:USERPROFILE ".venvs\bodyexporter-web"
}
$script:VenvPython = Join-Path $script:VenvRoot "Scripts\python.exe"
