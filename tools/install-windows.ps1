# AR Fast SNTP: Windows installer from the official GitHub release.
# Verify the downloaded executable against the published SHA-256 first.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$base = 'https://github.com/masarray/arfast-sntp/releases/latest/download'
$file = 'ARFastSNTP-Setup-Windows-x64.exe'
$dir = Join-Path ([IO.Path]::GetTempPath()) ('ARFastSNTP-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $dir | Out-Null
$exe = Join-Path $dir $file
$checksum = "$exe.sha256"
try {
    Write-Host 'Downloading AR Fast SNTP from GitHub...'
    Invoke-WebRequest "$base/$file" -OutFile $exe
    Invoke-WebRequest "$base/$file.sha256" -OutFile $checksum
    $line = (Get-Content $checksum | Select-Object -First 1).Trim()
    if ($line -notmatch '^([a-fA-F0-9]{64})(\s|$)') {
        throw 'Invalid official release checksum.'
    }
    $expected = $Matches[1].ToUpperInvariant()
    $actual = (Get-FileHash $exe -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($actual -ne $expected) {
        throw 'The installer checksum does not match the official release.'
    }
    Write-Host 'Checksum verified. Enter your SNTP server IP in the setup wizard.'
    $process = Start-Process -FilePath $exe -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Setup returned exit code $($process.ExitCode)."
    }
    Write-Host 'AR Fast SNTP is ready. Open it from the Start menu.'
}
finally {
    Remove-Item -LiteralPath $dir -Recurse -Force -ErrorAction SilentlyContinue
}
