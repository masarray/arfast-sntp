[CmdletBinding()]
param(
    [string] $Server = '192.16.1.175',
    [ValidateRange(2020, 2090)] [int] $ServerYear = 2026,
    [switch] $CertificationStep,
    [switch] $AcceptLabRisk
)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$parsedIp = [System.Net.IPAddress]::None
if (-not [System.Net.IPAddress]::TryParse($Server, [ref] $parsedIp) -or
    $parsedIp.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) {
    throw 'Server must be a valid IPv4 address.'
}
$Server = $parsedIp.ToString()
if ($CertificationStep -ne $AcceptLabRisk) {
    throw 'Clock correction requires BOTH -CertificationStep and -AcceptLabRisk. Otherwise omit both for monitor only.'
}
function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
if (-not (Test-IsAdministrator)) {
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -Server ' +
        $Server + ' -ServerYear ' + $ServerYear
    if ($CertificationStep) { $arguments += ' -CertificationStep -AcceptLabRisk' }
    $child = Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    exit $child.ExitCode
}
$worker = Join-Path $PSScriptRoot 'worker\ARFastSNTP.exe'
$dashboard = Join-Path $PSScriptRoot 'dashboard\ARFastSNTP.Dashboard.exe'
if (-not (Test-Path -LiteralPath $worker -PathType Leaf) -or
    -not (Test-Path -LiteralPath $dashboard -PathType Leaf)) {
    throw 'Install from the extracted GitHub Actions artifact folder (worker and dashboard must exist).'
}
$serviceName = 'ARFastSNTP'
$root = Join-Path $env:ProgramFiles 'ARFastSNTP'
$installedWorker = Join-Path $root 'worker\ARFastSNTP.exe'
$installedDashboard = Join-Path $root 'dashboard\ARFastSNTP.Dashboard.exe'
if (Get-Service -Name $serviceName -ErrorAction SilentlyContinue) {
    throw 'ARFastSNTP service already exists. Uninstall first; existing configuration is unchanged.'
}
if (Test-Path -LiteralPath $root) {
    throw "Installation directory already exists: $root. No files overwritten."
}
if ($CertificationStep) {
    $conflicts = @('W32Time', 'NTP', 'ntpd', 'ntpdssl-sag')
    $running = @($conflicts | ForEach-Object {
        Get-Service -Name $_ -ErrorAction SilentlyContinue
    } | Where-Object { $_.Status -eq 'Running' })
    if ($running.Count -gt 0) {
        $names = ($running | ForEach-Object { $_.Name }) -join ', '
        throw "Other time-setting services are running: $names. Resolve manually before lab stepping."
    }
}
$registered = $false
try {
    $workerDest = Join-Path $root 'worker'
    $dashboardDest = Join-Path $root 'dashboard'
    New-Item -Path $workerDest -ItemType Directory -Force | Out-Null
    New-Item -Path $dashboardDest -ItemType Directory -Force | Out-Null
    Copy-Item -Path (Join-Path $PSScriptRoot 'worker\*') -Destination $workerDest -Recurse -Force
    Copy-Item -Path (Join-Path $PSScriptRoot 'dashboard\*') -Destination $dashboardDest -Recurse -Force
    $flags = '--server ' + $Server + ' --server-year ' + $ServerYear
    if ($CertificationStep) { $flags += ' --certification-mode --allow-step' }
    & $installedWorker --server $Server --server-year $ServerYear --check-config
    if ($LASTEXITCODE -ne 0) { throw "Worker preflight failed ($LASTEXITCODE)." }
    $binaryPath = '"' + $installedWorker + '" ' + $flags
    New-Service -Name $serviceName -DisplayName 'AR Fast SNTP' -BinaryPathName $binaryPath -StartupType Automatic | Out-Null
    $registered = $true
    Start-Service -Name $serviceName
    Start-Sleep -Seconds 2
    if ((Get-Service -Name $serviceName).Status -ne 'Running') {
        throw 'Windows Service stopped unexpectedly.'
    }
}
catch {
    if ($registered) {
        Stop-Service -Name $serviceName -ErrorAction SilentlyContinue
        & sc.exe delete $serviceName | Out-Null
    }
    if (Test-Path -LiteralPath $root) {
        Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
    }
    throw
}
try {
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'AR Fast SNTP.lnk'
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $installedDashboard
    $shortcut.WorkingDirectory = Split-Path -Parent $installedDashboard
    $shortcut.Description = 'Read-only SNTP status dashboard'
    $shortcut.Save()
}
catch { Write-Warning 'Service installed, but desktop shortcut could not be created.' }
Write-Host 'AR Fast SNTP service installed and started.' -ForegroundColor Green
Write-Host "Server: $Server | Reference year: $ServerYear"
if ($CertificationStep) { Write-Host 'Mode: ISOLATED LAB CLOCK STEP' }
else { Write-Host 'Mode: MONITOR ONLY (no Windows time adjustment)' }
Write-Host "Read-only dashboard: $installedDashboard"
Write-Host 'No other Windows time service was changed.'
