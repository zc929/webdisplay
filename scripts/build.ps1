param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$dotnetExe = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetExe)) { $dotnetExe = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget-packages'
$projectFile = Join-Path $projectRoot 'src\WebDisplay\WebDisplay.csproj'
$outputDir = Join-Path $projectRoot 'dist\WebDisplay-win-x64'
& $dotnetExe publish $projectFile -c Release -r win-x64 --self-contained true -o $outputDir -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $outputDir -Force
foreach ($referenceDoc in Get-ChildItem -LiteralPath $outputDir -Filter 'Microsoft.Web.WebView2.*.xml' -File) {
    Remove-Item -LiteralPath $referenceDoc.FullName
}
foreach ($documentName in @('VALIDATION.md','THIRD-PARTY-NOTICES.md')) {
    $documentPath = Join-Path $projectRoot $documentName
    if (Test-Path -LiteralPath $documentPath) { Copy-Item -LiteralPath $documentPath -Destination $outputDir -Force }
}
$licensesDir = Join-Path $outputDir 'licenses'
New-Item -ItemType Directory -Force -Path $licensesDir | Out-Null
foreach ($packageName in @('microsoft.netcore.app.runtime.win-x64','microsoft.windowsdesktop.app.runtime.win-x64','microsoft.web.webview2')) {
    $packageRoot = Join-Path $env:NUGET_PACKAGES $packageName
    $packageVersion = Get-ChildItem -LiteralPath $packageRoot -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    foreach ($notice in Get-ChildItem -LiteralPath $packageVersion.FullName -File | Where-Object { $_.Name -match '^(LICENSE|THIRD-PARTY-NOTICES)(\.TXT)?$' }) {
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $licensesDir ($packageName + '-' + $notice.Name + '.txt')) -Force
    }
}
if (-not $SkipTests) {
    $testDir = Join-Path $projectRoot 'artifacts\self-test'
    $testProcess = Start-Process -FilePath (Join-Path $outputDir 'WebDisplay.exe') -ArgumentList @('--self-test','--data-dir',('"' + $testDir + '"')) -WindowStyle Hidden -PassThru -Wait
    if ($testProcess.ExitCode -ne 0) { throw "Self-test failed; inspect $testDir" }
    Get-Content -LiteralPath (Join-Path $testDir 'self-test-result.json') -Raw
}
Compress-Archive -Path $outputDir -DestinationPath (Join-Path $projectRoot 'dist\WebDisplay-win-x64.zip') -Force
$sourceDir = Join-Path $projectRoot 'artifacts\source-package\WebDisplay-source'
New-Item -ItemType Directory -Force -Path $sourceDir | Out-Null
foreach ($sourceFile in Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Recurse -File | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }) {
    $relativePath = [IO.Path]::GetRelativePath($projectRoot, $sourceFile.FullName)
    $destination = Join-Path $sourceDir $relativePath
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
    Copy-Item -LiteralPath $sourceFile.FullName -Destination $destination -Force
}
New-Item -ItemType Directory -Force -Path (Join-Path $sourceDir 'scripts') | Out-Null
Copy-Item -LiteralPath $PSCommandPath -Destination (Join-Path $sourceDir 'scripts\build.ps1') -Force
foreach ($sourceDocument in @('README.md','VALIDATION.md','THIRD-PARTY-NOTICES.md','.gitignore')) {
    $documentPath = Join-Path $projectRoot $sourceDocument
    if (Test-Path -LiteralPath $documentPath) { Copy-Item -LiteralPath $documentPath -Destination $sourceDir -Force }
}
Compress-Archive -Path $sourceDir -DestinationPath (Join-Path $projectRoot 'dist\WebDisplay-source.zip') -Force
Get-FileHash -LiteralPath (Join-Path $outputDir 'WebDisplay.exe') -Algorithm SHA256 | Format-List
Write-Output "Published: $outputDir"
