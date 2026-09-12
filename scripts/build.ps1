param([switch]$SkipTests)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot)).TrimEnd('\', '/')

function Reset-WorkspaceDirectory([string]$Path) {
    $absolutePath = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $rootPrefix = $projectRoot + [IO.Path]::DirectorySeparatorChar
    if (-not $absolutePath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clean a directory outside the workspace: $absolutePath"
    }
    # A lexical prefix alone is insufficient if an ancestor is a junction or
    # symlink. Refuse reparse points along the path before any recursive delete.
    $ancestorPath = $absolutePath
    while ($ancestorPath -and -not $ancestorPath.Equals($projectRoot, [StringComparison]::OrdinalIgnoreCase)) {
        if (Test-Path -LiteralPath $ancestorPath) {
            $ancestor = Get-Item -LiteralPath $ancestorPath -Force
            if (($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing to clean through a junction or symbolic link: $ancestorPath"
            }
        }
        $ancestorPath = Split-Path -Parent $ancestorPath
    }
    if (Test-Path -LiteralPath $absolutePath) {
        Remove-Item -LiteralPath $absolutePath -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $absolutePath | Out-Null
}

function Get-ExactDownloadVersion([string]$Range) {
    if ($Range -match '^\[([^,\[\]]+),\s*([^,\[\]]+)\]$') {
        $lower = $Matches[1].Trim()
        $upper = $Matches[2].Trim()
        if ($lower -eq $upper) { return $lower }
    }
    elseif ($Range -match '^\[([^,\[\]]+)\]$') { return $Matches[1].Trim() }
    elseif ($Range -match '^\d+(?:\.\d+){1,3}(?:[-+][0-9A-Za-z.-]+)?$') { return $Range }
    throw "A downloaded package has no exact resolved version in project.assets.json: $Range"
}

function Copy-ResolvedPackageNotices([string]$AssetsPath, [string]$Destination, [string]$RuntimeId) {
    $assets = Get-Content -LiteralPath $AssetsPath -Raw | ConvertFrom-Json
    $packageFolders = @($assets.packageFolders.PSObject.Properties | ForEach-Object { $_.Name })
    if ($packageFolders.Count -eq 0) { throw "No NuGet package folders found in $AssetsPath" }
    $resolvedPackages = @{}
    foreach ($library in $assets.libraries.PSObject.Properties) {
        if ($library.Value.type -ne 'package') { continue }
        $identity = $library.Name -split '/', 2
        $packagePath = [string]$library.Value.path
        if (-not $packagePath) { $packagePath = $library.Name.ToLowerInvariant() }
        $resolvedPackages[$library.Name.ToLowerInvariant()] = [pscustomobject]@{
            Name = $identity[0]; Version = $identity[1]; Path = $packagePath
        }
    }
    # Self-contained .NET runtime packs are often downloadDependencies rather
    # than libraries. Use their exact locked versions, never the newest cache
    # directory. Ignore framework runtime packs that this project does not use.
    foreach ($framework in $assets.project.frameworks.PSObject.Properties) {
        $frameworkReferences = @($framework.Value.frameworkReferences.PSObject.Properties | ForEach-Object { $_.Name })
        foreach ($download in $framework.Value.downloadDependencies) {
            $packageName = [string]$download.name
            if ($packageName -match '^(Microsoft\.(?:NETCore|WindowsDesktop|AspNetCore)\.App)\.Runtime\.(.+)$') {
                if ($frameworkReferences -notcontains $Matches[1] -or $Matches[2] -ne $RuntimeId) { continue }
            }
            $version = Get-ExactDownloadVersion ([string]$download.version)
            $identity = ($packageName + '/' + $version).ToLowerInvariant()
            if (-not $resolvedPackages.ContainsKey($identity)) {
                $resolvedPackages[$identity] = [pscustomobject]@{ Name = $packageName; Version = $version; Path = $identity }
            }
        }
    }
    Reset-WorkspaceDirectory $Destination
    $inventory = @()
    foreach ($package in $resolvedPackages.Values | Sort-Object Name, Version) {
        $resolvedDirectory = $null
        foreach ($folder in $packageFolders) {
            $folderRoot = [IO.Path]::GetFullPath($folder).TrimEnd('\', '/')
            $candidate = [IO.Path]::GetFullPath((Join-Path $folderRoot ($package.Path.Replace('/', [IO.Path]::DirectorySeparatorChar))))
            if (-not $candidate.StartsWith(($folderRoot + [IO.Path]::DirectorySeparatorChar), [StringComparison]::OrdinalIgnoreCase)) {
                throw "Invalid resolved package path: $($package.Name) $($package.Version)"
            }
            if (Test-Path -LiteralPath $candidate -PathType Container) { $resolvedDirectory = $candidate; break }
        }
        if (-not $resolvedDirectory) {
            throw "Resolved package directory is missing: $($package.Name) $($package.Version). Restore before packaging."
        }
        $copiedNotices = @()
        foreach ($notice in Get-ChildItem -LiteralPath $resolvedDirectory -File | Where-Object {
            $_.Name -match '^(LICENSE|LICENCE|COPYING|COPYRIGHT|NOTICE|NOTICES|THIRD[-_ ]?PARTY(?:[-_ ]?NOTICES?)?)(?:[._ -].*)?$'
        }) {
            $fileName = ($package.Name + '-' + $package.Version + '-' + $notice.Name) -replace '[<>:"/\\|?*]', '_'
            Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $Destination $fileName) -Force
            $copiedNotices += $fileName
        }
        $inventory += [pscustomobject]@{ Package = $package.Name; Version = $package.Version; NoticeFiles = @($copiedNotices) }
    }
    ConvertTo-Json -InputObject @($inventory) -Depth 4 | Set-Content -LiteralPath (Join-Path $Destination 'resolved-packages.json') -Encoding UTF8
}

$dotnetExe = Join-Path $projectRoot '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetExe)) { $dotnetExe = (Get-Command dotnet -ErrorAction Stop).Source }
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.tools\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.tools\nuget-packages'
$projectFile = Join-Path $projectRoot 'src\WebDisplay\WebDisplay.csproj'
$outputDir = Join-Path $projectRoot 'dist\WebDisplay-WinUI3-win-x64'
Reset-WorkspaceDirectory $outputDir
& $dotnetExe publish $projectFile -c Release -r win-x64 --self-contained true -o $outputDir -p:Platform=x64 -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false
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
Copy-ResolvedPackageNotices (Join-Path $projectRoot 'src\WebDisplay\obj\project.assets.json') $licensesDir 'win-x64'
if (-not $SkipTests) {
    $testDir = Join-Path $projectRoot 'artifacts\winui-self-test'
    $testProcess = Start-Process -FilePath (Join-Path $outputDir 'WebDisplay.exe') -ArgumentList @('--self-test','--data-dir',('"' + $testDir + '"')) -WindowStyle Hidden -PassThru -Wait
    if ($testProcess.ExitCode -ne 0) { throw "Self-test failed; inspect $testDir" }
    Get-Content -LiteralPath (Join-Path $testDir 'self-test-result.json') -Raw
}
Compress-Archive -Path $outputDir -DestinationPath (Join-Path $projectRoot 'dist\WebDisplay-WinUI3-win-x64.zip') -Force
$sourceDir = Join-Path $projectRoot 'artifacts\winui-source-package\WebDisplay-source'
Reset-WorkspaceDirectory $sourceDir
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
Compress-Archive -Path $sourceDir -DestinationPath (Join-Path $projectRoot 'dist\WebDisplay-WinUI3-source.zip') -Force
Get-FileHash -LiteralPath (Join-Path $outputDir 'WebDisplay.exe') -Algorithm SHA256 | Format-List
Write-Output "Published: $outputDir"
