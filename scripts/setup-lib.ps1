# SolidWorks 2022 Interop DLL を lib\ にコピーする
param(
    [string]$SolidWorksRoot = "C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS"
)

$ErrorActionPreference = "Stop"
$lib = Join-Path $PSScriptRoot "..\lib"
$redist = Join-Path $SolidWorksRoot "api\redist"

if (-not (Test-Path $redist)) {
    throw "SolidWorks API not found: $redist (use -SolidWorksRoot to override)"
}

New-Item -ItemType Directory -Force -Path $lib | Out-Null

Copy-Item (Join-Path $redist "SolidWorks.Interop.sldworks.dll") $lib -Force
Copy-Item (Join-Path $redist "SolidWorks.Interop.swconst.dll") $lib -Force

Write-Host "Copied Interop DLLs to $lib"
