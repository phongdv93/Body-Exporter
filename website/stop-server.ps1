# Stop processes listening on port 8080 (local dev server)
$ErrorActionPreference = "SilentlyContinue"
$killed = 0
foreach ($port in 8080, 8765) {
    $conns = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
    foreach ($c in $conns) {
        $pid = $c.OwningProcess
        if ($pid -gt 0) {
            Write-Host "Stopping PID $pid (port $port)..."
            Stop-Process -Id $pid -Force
            $killed++
        }
    }
}
if ($killed -eq 0) {
    Write-Host "No listener on 8080/8765."
} else {
    Write-Host "Done. Wait 2s then run install.ps1 or run.ps1"
    Start-Sleep -Seconds 2
}
