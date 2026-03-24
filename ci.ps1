function Start-EliteBridgePlannerServer {
    Param (
        [Parameter(Mandatory = $false)]
        [switch]$newWindow
    )   

    Stop-EliteBridgePlannerServer
    $env:ASPNETCORE_ENVIRONMENT = "Development"

    if ($newWindow) {        
        Start-Process ".\EliteBridgePlanner.Server\bin\Debug\net10.0\EliteBridgePlanner.Server.exe" -WorkingDirectory ".\EliteBridgePlanner.Server\bin\Debug\net10.0" -ArgumentList '--urls "https://localhost:7293;http://localhost:5293"'
    }
    else {
        Start-Process -NoNewWindow ".\EliteBridgePlanner.Server\bin\Debug\net10.0\EliteBridgePlanner.Server.exe" -WorkingDirectory ".\EliteBridgePlanner.Server\bin\Debug\net10.0" -ArgumentList '--urls "https://localhost:7293;http://localhost:5293"'
    }
}

function Stop-EliteBridgePlannerServer {
    $eliteBridgePlannerProcess = Get-Process EliteBridgePlanner.Server -ErrorAction SilentlyContinue
    $i = 0    
    while ($eliteBridgePlannerProcess) {
        Write-Host "EliteBridgePlanner.Server($i) is allready in use" -ForegroundColor Yellow                
        $eliteBridgePlannerProcess | Stop-Process -Force -ErrorAction Continue
        Start-Sleep 5
        Write-Host "EliteBridgePlanner.Server($i) process has been kill" -ForegroundColor Green
        $i++
        $eliteBridgePlannerProcess = Get-Process EliteBridgePlanner.Server -ErrorAction SilentlyContinue
    }
    Remove-Variable eliteBridgePlannerProcess
}