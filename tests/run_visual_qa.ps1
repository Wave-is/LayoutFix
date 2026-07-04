param (
    [string]$Scenario = "LayoutFix"
)

$Query = ""
if ($Scenario -eq "LayoutFix") {
    $Query = "Open D:\Antigravity\LayoutFix\Output\LayoutFix_Setup.exe, install the program, open settings and visually check if the interface uses a dark theme."
}
elseif ($Scenario -eq "Premiere") {
    $Query = "Open Adobe Premiere Pro, check that a sequence from pymiere was created on the timeline, and take a screenshot of the program window."
}
else {
    $Query = $Scenario
}

$repoPath = "D:\Antigravity\Tools\OS-Copilot"

Write-Output "========================================"
Write-Output "OS-Copilot Orchestration Wrapper (OLLAMA)"
Write-Output "========================================"
Write-Output "Scenario: $Scenario"
Write-Output "Task: $Query"

Write-Output "`n[ORCHESTRATOR] Launching OS-Copilot via quick_start.py (Local Engine)..."

Push-Location $repoPath
try {
    # Activate conda env and run Python using cmd.exe
    $condaActivate = "C:\Users\iswav\miniconda3\Scripts\activate.bat"
    $cmdArgs = "/c `"$condaActivate oscopilot && python quick_start.py --query `"$Query`"`""
    Start-Process -FilePath "cmd.exe" -ArgumentList $cmdArgs -Wait -NoNewWindow
}
finally {
    Pop-Location
}

Write-Output "`n[ORCHESTRATOR] Run completed."
