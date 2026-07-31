$ErrorActionPreference = 'Stop'

$projectDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$packageVersion = '1.0.4078.44'
$cacheRoot = Join-Path $env:TEMP 'FoodDeliveryPrintingSystem-build-cache'
$vendorDir = Join-Path $cacheRoot 'webview2'
$packageFile = Join-Path $cacheRoot "Microsoft.Web.WebView2.$packageVersion.nupkg"
$stageDir = Join-Path $env:TEMP 'FoodDeliveryPrintingSystem-stage-0.2.1'
$payloadFile = Join-Path $env:TEMP 'FoodDeliveryPrintingSystem-payload-0.2.1.zip'
$outputDir = Join-Path $projectDir 'dist'
$outputExe = Join-Path $outputDir 'FoodDeliveryPrintingSystem-0.2.1.exe'

if (-not (Test-Path (Join-Path $vendorDir 'lib\net462\Microsoft.Web.WebView2.Core.dll')) -or
    -not (Test-Path (Join-Path $vendorDir 'runtimes\win-x64\native\WebView2Loader.dll'))) {
    New-Item -ItemType Directory -Force -Path $cacheRoot | Out-Null
    Invoke-WebRequest -Uri "https://www.nuget.org/api/v2/package/Microsoft.Web.WebView2/$packageVersion" -OutFile $packageFile
    if (Test-Path -LiteralPath $vendorDir) { Remove-Item -LiteralPath $vendorDir -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $vendorDir | Out-Null
    tar -xf $packageFile -C $vendorDir
}

if (Test-Path -LiteralPath $stageDir) { Remove-Item -LiteralPath $stageDir -Recurse -Force }
if (Test-Path -LiteralPath $payloadFile) { Remove-Item -LiteralPath $payloadFile -Force }
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $stageDir 'ui') | Out-Null
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$webViewLib = Join-Path $vendorDir 'lib\net462'
$csc = Join-Path $framework 'csc.exe'

& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
    /out:"$(Join-Path $stageDir 'FoodDeliveryPrintingSystem.Core.exe')" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll `
    /reference:"$(Join-Path $webViewLib 'Microsoft.Web.WebView2.Core.dll')" `
    /reference:"$(Join-Path $webViewLib 'Microsoft.Web.WebView2.WinForms.dll')" `
    (Join-Path $projectDir 'native\App.cs')
if ($LASTEXITCODE -ne 0) { throw 'Core compilation failed.' }

Copy-Item (Join-Path $webViewLib 'Microsoft.Web.WebView2.Core.dll') $stageDir -Force
Copy-Item (Join-Path $webViewLib 'Microsoft.Web.WebView2.WinForms.dll') $stageDir -Force
Copy-Item (Join-Path $vendorDir 'runtimes\win-x64\native\WebView2Loader.dll') $stageDir -Force
Copy-Item (Join-Path $projectDir 'src\renderer\*') (Join-Path $stageDir 'ui') -Recurse -Force
Compress-Archive -Path (Join-Path $stageDir '*') -DestinationPath $payloadFile -Force

& $csc /nologo /target:winexe /platform:x64 /optimize+ /codepage:65001 `
    /out:"$outputExe" /resource:"$payloadFile,Payload.zip" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Windows.Forms.dll `
    /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll `
    (Join-Path $projectDir 'native\Launcher.cs')
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed.' }

Get-ChildItem -LiteralPath $outputDir -Force | Where-Object { $_.FullName -ne $outputExe } |
    Remove-Item -Recurse -Force
Remove-Item -LiteralPath $stageDir -Recurse -Force
Remove-Item -LiteralPath $payloadFile -Force

$result = Get-Item -LiteralPath $outputExe
Write-Host "Built: $($result.FullName)"
Write-Host ("Size: {0:N2} MB" -f ($result.Length / 1MB))
