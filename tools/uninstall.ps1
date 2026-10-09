[CmdletBinding()]
param([switch] $RemoveStatusHistory)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}
if (-not (Test-IsAdministrator)) {
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"'
    if ($RemoveStatusHistory) { $arguments += ' -RemoveStatusHistory' }
    $child = Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $arguments -Wait -PassThru
    exit $child.ExitCode
}
$serviceName = 'ARFastSNTP'
$root = Join-Path $env:ProgramFiles 'ARFastSNTP'
$ownedExe = Join-Path $root 'worker\ARFastSNTP.exe'
$existing = Get-CimInstance -ClassName Win32_Service -Filter "Name='ARFastSNTP'" -ErrorAction SilentlyContinue
if ($null -ne $existing) {
    if (-not $existing.PathName.StartsWith('"' + $ownedExe + '"', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Service path does not match this installation. Refusing to remove unrelated service.'
    }
    if ($existing.State -ne 'Stopped') {
        Stop-Service -Name $serviceName
        $controller = Get-Service -Name $serviceName
        $controller.WaitForStatus([ServiceProcess.ServiceControllerStatus]::Stopped, [TimeSpan]::FromSeconds(20))
    }
    & sc.exe delete $serviceName | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Service deletion failed ($LASTEXITCODE)." }
}
if (Test-Path -LiteralPath $root) { Remove-Item -LiteralPath $root -Recurse -Force }
$shortcut = Join-Path ([Environment]::GetFolderPath('CommonDesktopDirectory')) 'AR Fast SNTP.lnk'
if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut -Force }
if ($RemoveStatusHistory) {
    $statusDirectory = Join-Path $env:ProgramData 'ARFastSNTP'
    if (Test-Path -LiteralPath $statusDirectory) {
        Remove-Item -LiteralPath $statusDirectory -Recurse -Force
    }
}
Write-Host 'AR Fast SNTP service and application removed.' -ForegroundColor Green
if (-not $RemoveStatusHistory) { Write-Host 'Status and correction history preserved in ProgramData.' }
