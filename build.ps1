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
# 允许通过环境变量临时指定输出名，便于在程序运行时先编译到旁路口径做验证。
$exeName = "SelectionTranslator.exe"
if ($env:ST_OUTPUT_NAME) {
    $exeName = $env:ST_OUTPUT_NAME
}
$output = Join-Path $outputDirectory $exeName
$manifest = Join-Path $root "app.manifest"
$icon = Join-Path $root "app.ico"

# app.ico 缺失时用 tools\MakeIcon.cs 现场生成（不依赖任何第三方库）。
if (-not (Test-Path $icon)) {
    $generatorSource = Join-Path $root "tools\MakeIcon.cs"
    if (-not (Test-Path $generatorSource)) {
        throw "Missing app.ico and tools\MakeIcon.cs; cannot build with an icon."
    }
    Write-Host "Generating app.ico..."
    $generatorExe = Join-Path $outputDirectory "MakeIcon.exe"
    & $csc "/nologo" "/target:exe" "/out:$generatorExe" `
        "/reference:$(Join-Path $framework 'System.dll')" `
        "/reference:$(Join-Path $framework 'System.Drawing.dll')" `
        $generatorSource
    if ($LASTEXITCODE -ne 0) {
        throw "Icon generator build failed with exit code $LASTEXITCODE"
    }
    & $generatorExe $icon
    if ($LASTEXITCODE -ne 0) {
        throw "Icon generation failed with exit code $LASTEXITCODE"
    }
    Remove-Item $generatorExe -Force
}

if (-not (Test-Path $icon)) {
    throw "Missing icon file: $icon"
}

$arguments = @(
    "/nologo",
    "/target:winexe",
    "/platform:anycpu",
    "/langversion:5",
    "/codepage:65001",
    "/optimize+",
    "/out:$output",
    "/win32manifest:$manifest",
    "/win32icon:$icon",
    "/resource:$icon,SelectionTranslator.app.ico"
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
