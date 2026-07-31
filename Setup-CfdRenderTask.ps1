param(
    [string]$TaskName       = "AxialFanCfdRender",
    [string]$PythonExe      = "C:\Users\Admin\AppData\Local\Python\pythoncore-3.14-64\python.exe",
    [string]$DispatchScript = "D:\Office\AxialFanMVC.Business\Cfd\Render\render_dispatch.py",
    [string]$IpcDirectory   = "D:\Office\CfdIpc",
    [string]$RunAsUser      = "$env:USERDOMAIN\$env:USERNAME"
)

$ErrorActionPreference = "Stop"

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Run this script from an elevated (Administrator) PowerShell prompt."
}

foreach ($path in @($PythonExe, $DispatchScript)) {
    if (-not (Test-Path $path)) {
        Write-Warning "Not found on this machine: $path — double-check the value matches appsettings.json before continuing."
    }
}

if (-not (Test-Path $IpcDirectory)) {
    Write-Host "Creating IPC directory: $IpcDirectory"
    New-Item -ItemType Directory -Path $IpcDirectory -Force | Out-Null
}

$taskRun = "`"$PythonExe`" `"$DispatchScript`" `"$IpcDirectory`""

$schtasksArgs = @(
    "/Create",
    "/TN", $TaskName,
    "/TR", $taskRun,
    "/SC", "ONLOGON",
    "/RU", $RunAsUser,
    "/IT",
    "/RL", "LIMITED",
    "/F"
)

Write-Host "Creating scheduled task '$TaskName' ..."
Write-Host "  Action:  $taskRun"
Write-Host "  Run as:  $RunAsUser (interactive session only)"

& schtasks.exe @schtasksArgs

if ($LASTEXITCODE -ne 0) {
    throw "schtasks /create failed with exit code $LASTEXITCODE."
}

Write-Host ""
Write-Host "Done. Verify with:  schtasks /query /tn `"$TaskName`" /v /fo LIST"
Write-Host "Test a manual run:  schtasks /run /tn `"$TaskName`""
