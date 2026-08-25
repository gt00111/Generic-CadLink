# Build the SolidWorks 2022 schema-v0.3 macro DLL into macro\.
$ErrorActionPreference = "Stop"

$projectRoot = Split-Path $PSScriptRoot -Parent
$libDir = Join-Path $projectRoot "lib"
$outputPath = Join-Path $projectRoot "macro\BendExportMacro.dll"
$sourceDir = Join-Path $projectRoot "src\GenericCadLink.Macro"

if (-not (Test-Path -LiteralPath (Join-Path $libDir "SolidWorks.Interop.sldworks.dll"))) {
    & (Join-Path $PSScriptRoot "setup-lib.ps1")
}

$sdkRoot = Join-Path $env:ProgramFiles "dotnet\sdk"
$compiler = Get-ChildItem -LiteralPath $sdkRoot -Directory |
    Sort-Object { [version]($_.Name.Split('-')[0]) } -Descending |
    ForEach-Object { Join-Path $_.FullName "Roslyn\bincore\csc.dll" } |
    Where-Object { Test-Path -LiteralPath $_ } |
    Select-Object -First 1
if (-not $compiler) { throw ".NET SDK Roslyn compiler was not found." }

$framework = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$gac = Join-Path $env:WINDIR "Microsoft.Net\assembly\GAC_MSIL"
$references = @(
    (Join-Path $framework "mscorlib.dll"),
    (Join-Path $gac "System\v4.0_4.0.0.0__b77a5c561934e089\System.dll"),
    (Join-Path $gac "System.Core\v4.0_4.0.0.0__b77a5c561934e089\System.Core.dll"),
    (Join-Path $gac "System.Windows.Forms\v4.0_4.0.0.0__b77a5c561934e089\System.Windows.Forms.dll"),
    (Join-Path $libDir "SolidWorks.Interop.sldworks.dll"),
    (Join-Path $libDir "SolidWorks.Interop.swconst.dll")
)
foreach ($reference in $references) {
    if (-not (Test-Path -LiteralPath $reference)) { throw "Reference not found: $reference" }
}

$arguments = @(
    $compiler, "/nologo", "/target:library", "/platform:x64", "/langversion:latest", "/nostdlib+",
    "/out:$outputPath"
)
$arguments += $references | ForEach-Object { "/reference:$_" }
$arguments += Get-ChildItem -LiteralPath $sourceDir -Recurse -Filter "*.cs" | Select-Object -ExpandProperty FullName

Write-Host "Building $outputPath ..."
& dotnet @arguments
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "OK: $outputPath"
