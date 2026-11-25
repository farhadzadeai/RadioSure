# Downloads and installs the .NET 10 Windows Desktop Runtime (x64) if it is missing.
# Exit code 0 = runtime present afterwards; anything else = failure.
$ErrorActionPreference = 'Stop'

function Test-DesktopRuntime {
    try {
        $list = & dotnet --list-runtimes 2>$null
        return ($list | Select-String 'Microsoft.WindowsDesktop.App 10\.') -ne $null
    } catch { return $false }
}

if (Test-DesktopRuntime) { exit 0 }

# Prefer winget when available (handles the download hash itself).
$winget = Get-Command winget.exe -ErrorAction SilentlyContinue
if ($winget) {
    & winget.exe install --id Microsoft.DotNet.DesktopRuntime.10 -e --silent --accept-source-agreements --accept-package-agreements | Out-Null
    if (Test-DesktopRuntime) { exit 0 }
}

# Fallback: direct download from Microsoft.
$url = 'https://builds.dotnet.microsoft.com/dotnet/WindowsDesktop/10.0.0/windowsdesktop-runtime-10.0.0-win-x64.exe'
$installer = Join-Path $env:TEMP 'windowsdesktop-runtime-10-win-x64.exe'
try {
    & curl.exe '-fSL' '-o' $installer $url | Out-Null
    Start-Process $installer -ArgumentList '/install', '/quiet', '/norestart' -Wait
} catch {
    exit 1
}

if (Test-DesktopRuntime) { exit 0 } else { exit 2 }
