# RadioSure build/publish script.
# Produces in .\dist:
#   publish-fd\        framework-dependent build (needs .NET 10 Desktop Runtime) - for the online installer
#   publish-sc\        self-contained single-file build                          - for the offline installer
#   RadioSure-portable.zip   portable version (framework-dependent + portable.flag)
#   installer output via Inno Setup when ISCC is available
param([switch]$SkipInno)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$dist = Join-Path $root 'dist'
$proj = Join-Path $root 'src\RadioClassic\RadioClassic.csproj'

Write-Host '== clean =='
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
New-Item -ItemType Directory $dist | Out-Null

Write-Host '== publish framework-dependent =='
dotnet publish $proj -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -o (Join-Path $dist 'publish-fd') | Select-Object -Last 2

Write-Host '== publish self-contained =='
dotnet publish $proj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
    -o (Join-Path $dist 'publish-sc') | Select-Object -Last 2

# Symbols are not shipped: smaller downloads and no build-path metadata.
Get-ChildItem (Join-Path $dist 'publish-fd') -Filter *.pdb -Recurse | Remove-Item -Force
Get-ChildItem (Join-Path $dist 'publish-sc') -Filter *.pdb -Recurse | Remove-Item -Force

Write-Host '== portable zip =='
$portable = Join-Path $dist 'portable-staging'
Copy-Item (Join-Path $dist 'publish-fd') $portable -Recurse
# Marker file: the app stores settings/database next to the executable when present.
New-Item -ItemType File (Join-Path $portable 'portable.flag') | Out-Null
Compress-Archive -Path (Join-Path $portable '*') -DestinationPath (Join-Path $dist 'RadioSure-portable.zip')
Remove-Item $portable -Recurse -Force

Write-Host '== sizes =='
foreach ($dir in 'publish-fd', 'publish-sc') {
    $size = (Get-ChildItem (Join-Path $dist $dir) -Recurse | Measure-Object Length -Sum).Sum
    Write-Host ("{0}: {1:N1} MB" -f $dir, ($size / 1MB))
}
$zip = (Get-Item (Join-Path $dist 'RadioSure-portable.zip')).Length
Write-Host ("portable zip: {0:N1} MB" -f ($zip / 1MB))

if (-not $SkipInno) {
    $iscc = Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'
    if (-not (Test-Path $iscc)) { $iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source }
    if ($iscc) {
        Write-Host '== inno setup =='
        & $iscc (Join-Path $root 'installer\online.iss') | Select-Object -Last 3
        & $iscc (Join-Path $root 'installer\offline.iss') | Select-Object -Last 3
    } else {
        Write-Host 'Inno Setup not found; skipping installers'
    }
}
Write-Host '== done =='
