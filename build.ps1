$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$framework = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$csc = Join-Path $framework "csc.exe"

if (-not (Test-Path $csc)) {
    $framework = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319"
    $csc = Join-Path $framework "csc.exe"
}

if (-not (Test-Path $csc)) {
    throw "Cannot find .NET Framework 4.x csc.exe. Windows 10/11 usually includes .NET Framework 4.8."
}

$references = @(
    (Join-Path $framework "System.dll"),
    (Join-Path $framework "System.Core.dll"),
    (Join-Path $framework "System.Windows.Forms.dll"),
    (Join-Path $framework "System.Drawing.dll"),
    (Join-Path $framework "System.Web.Extensions.dll"),
    (Join-Path $framework "System.Security.dll"),
    (Join-Path $framework "System.Net.Http.dll"),
    (Join-Path $framework "System.Xml.dll"),
    (Join-Path $framework "WPF\UIAutomationClient.dll"),
    (Join-Path $framework "WPF\UIAutomationTypes.dll"),
    (Join-Path $framework "WPF\WindowsBase.dll")
)

foreach ($reference in $references) {
    if (-not (Test-Path $reference)) {
        throw "Missing reference assembly: $reference"
    }
}

$sources = Get-ChildItem -Path $root -Filter "*.cs" |
    Sort-Object Name |
    ForEach-Object { $_.FullName }

if ($sources.Count -eq 0) {
    throw "No C# source files found."
}

$outputDirectory = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$output = Join-Path $outputDirectory "SelectionTranslator.exe"
$manifest = Join-Path $root "app.manifest"

$arguments = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/langversion:5",
    "/codepage:65001",
    "/optimize+",
    "/out:$output",
    "/win32manifest:$manifest"
)

foreach ($reference in $references) {
    $arguments += "/reference:$reference"
}

$arguments += $sources

Write-Host "Building..."
& $csc $arguments

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

Write-Host ""
Write-Host "Output: $output"
Write-Host "Run: $output"
Write-Host "Self-test: $output --selftest"
Write-Host "Self-test result: $env:APPDATA\SelectionTranslator\selftest.txt"
