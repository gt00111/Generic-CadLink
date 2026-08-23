# GenericCadLink.Macro.dll を macro\ にビルドする
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$lib = Join-Path $root "lib"
$out = Join-Path $root "macro\BendExportMacro.dll"
$src = Join-Path $root "src\GenericCadLink.Macro"

if (-not (Test-Path (Join-Path $lib "SolidWorks.Interop.sldworks.dll"))) {
    & (Join-Path $PSScriptRoot "setup-lib.ps1")
}

$csc = "${env:WINDIR}\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    throw "csc.exe not found"
}

$refs = @(
    "/reference:`"$(Join-Path $lib 'SolidWorks.Interop.sldworks.dll')`""
    "/reference:`"$(Join-Path $lib 'SolidWorks.Interop.swconst.dll')`""
    "/reference:System.dll"
    "/reference:System.Core.dll"
    "/reference:System.Windows.Forms.dll"
)

$files = Get-ChildItem $src -Recurse -Filter *.cs | ForEach-Object { "`"$($_.FullName)`"" }

$args = @(
    "/nologo", "/target:library", "/platform:anycpu",
    "/out:`"$out`"",
    "/langversion:5"
) + $refs + $files

Write-Host "Building $out ..."
& $csc @args
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "OK: $out"
