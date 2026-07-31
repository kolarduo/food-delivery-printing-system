$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$packageVersion = '1.0.4078.44'
$vendorDir = Join-Path $projectDir 'vendor\webview2'
$packageFile = Join-Path $projectDir "vendor\Microsoft.Web.WebView2.$packageVersion.nupkg"
$outputDir = Join-Path $projectDir 'dist-lite\food-delivery-printing-system-0.2.0'

if (-not (Test-Path (Join-Path $vendorDir 'lib\net462\Microsoft.Web.WebView2.Core.dll'))) {
    New-Item -ItemType Directory -Force -Path (Split-Path $packageFile) | Out-Null
    Invoke-WebRequest -Uri "https://www.nuget.org/api/v2/package/Microsoft.Web.WebView2/$packageVersion" -OutFile $packageFile
    New-Item -ItemType Directory -Force -Path $vendorDir | Out-Null
    tar -xf $packageFile -C $vendorDir
}

New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $outputDir 'ui') | Out-Null

$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$webViewLib = Join-Path $vendorDir 'lib\net462'

& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
    /out:"$(Join-Path $outputDir 'FoodDeliveryPrintingSystem-0.2.0.exe')" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll `
    /reference:"$(Join-Path $webViewLib 'Microsoft.Web.WebView2.Core.dll')" `
    /reference:"$(Join-Path $webViewLib 'Microsoft.Web.WebView2.WinForms.dll')" `
    (Join-Path $projectDir 'native\App.cs')

if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed.' }

Copy-Item (Join-Path $webViewLib 'Microsoft.Web.WebView2.Core.dll') $outputDir -Force
Copy-Item (Join-Path $webViewLib 'Microsoft.Web.WebView2.WinForms.dll') $outputDir -Force
Copy-Item (Join-Path $vendorDir 'runtimes\win-x64\native\WebView2Loader.dll') $outputDir -Force
Copy-Item (Join-Path $projectDir 'src\renderer\*') (Join-Path $outputDir 'ui') -Recurse -Force

$files = Get-ChildItem $outputDir -Recurse -File
$size = ($files | Measure-Object Length -Sum).Sum
Write-Host "Built: $outputDir"
Write-Host ("Total size: {0:N2} MB" -f ($size / 1MB))
