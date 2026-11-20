param([string]$CompilerPath = '', [string]$ReportDirectory = 'artifacts/distribution')
$ErrorActionPreference = 'Stop'
$taskRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$taskProject = Join-Path $taskRoot 'src\KeyTrail.Desktop\KeyTrail.Desktop.csproj'
$taskVersion = ([xml](Get-Content -LiteralPath $taskProject -Raw)).Project.PropertyGroup.Version
if ($taskVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'A three-part release version is required.' }
$taskReport = [IO.Path]::GetFullPath((Join-Path $taskRoot $ReportDirectory))
if (-not $taskReport.StartsWith($taskRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Build files must remain inside the project.' }
if (-not $CompilerPath) { $CompilerPath = Join-Path $taskRoot 'artifacts\installer-tools\InnoSetup7\ISCC.exe' }
if (-not (Test-Path -LiteralPath $CompilerPath)) { throw 'Inno Setup 7 ISCC.exe is required; supply -CompilerPath.' }
$taskCompilerVersion = ((& $CompilerPath --version) | Out-String).Trim()
if ([version]$taskCompilerVersion -lt [version]'7.0') { throw 'Inno Setup 7 or later is required.' }

# Each build gets a new payload directory; only its explicit file list is shipped.
$taskBuild = Join-Path $taskReport ('build-' + [Guid]::NewGuid().ToString('N'))
$taskPublish = Join-Path $taskBuild 'publish'
$taskPayload = Join-Path $taskBuild 'payload'
$taskLicenses = Join-Path $taskPayload '第三方许可'
$taskRelease = Join-Path $taskRoot ('release\' + $taskVersion)
New-Item -ItemType Directory -Force -Path $taskPublish, $taskLicenses, $taskRelease | Out-Null
$env:DOTNET_CLI_HOME = Join-Path $taskRoot 'artifacts\dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:MSBuildEnableWorkloadResolver = 'false'
& dotnet restore $taskProject -r win-x64 --nologo
if ($LASTEXITCODE -ne 0) { throw 'Release dependency restore failed.' }
# Force a clean compilation: incremental publish can otherwise retain embedded
# developer symbols from an earlier local build with the same configuration.
& dotnet build $taskProject -c Release -r win-x64 --self-contained true --no-restore -t:Rebuild -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -p:UseSharedCompilation=false -m:1 --nologo
if ($LASTEXITCODE -ne 0) { throw 'Release compilation failed.' }
& dotnet publish $taskProject -c Release -r win-x64 --self-contained true --no-restore --no-build -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -p:DebugSymbols=false -p:UseSharedCompilation=false -m:1 -o $taskPublish --nologo
if ($LASTEXITCODE -ne 0) { throw 'Application publish failed.' }
Copy-Item -LiteralPath (Join-Path $taskPublish 'KeyTrail.exe') -Destination (Join-Path $taskPayload '键盘统计器.exe')
$taskReadme = [IO.File]::ReadAllText((Join-Path $taskRoot 'installer\使用说明.txt')).Replace('{{VERSION}}', $taskVersion)
[IO.File]::WriteAllText((Join-Path $taskPayload '使用说明.txt'), $taskReadme, [Text.UTF8Encoding]::new($true))

# Resolve notices from the exact runtime packs used by the SDK.
$taskAssets = Get-Content -LiteralPath (Join-Path $taskRoot 'src\KeyTrail.Desktop\obj\project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$taskDownloads = @($taskAssets.project.frameworks.Values | ForEach-Object { $_.downloadDependencies })
foreach ($taskPack in @('Microsoft.NETCore.App.Runtime.win-x64', 'Microsoft.WindowsDesktop.App.Runtime.win-x64')) {
    $taskDependency = @($taskDownloads | Where-Object { $_.name -eq $taskPack })[0]
    if (-not $taskDependency) { throw ('Runtime pack version not found: ' + $taskPack) }
    $taskRuntimeVersion = $taskDependency.version.Trim('[', ']', ' ').Split(',')[0].Trim()
    $taskPackDirectory = $null
    foreach ($taskPackageFolder in $taskAssets.packageFolders.Keys) {
        $taskCandidate = Join-Path $taskPackageFolder ($taskPack.ToLowerInvariant() + '\' + $taskRuntimeVersion)
        if (Test-Path -LiteralPath $taskCandidate) { $taskPackDirectory = $taskCandidate; break }
    }
    if (-not $taskPackDirectory) { throw ('Runtime pack not found: ' + $taskPack) }
    if ($taskPack -eq 'Microsoft.NETCore.App.Runtime.win-x64') {
        Copy-Item -LiteralPath (Join-Path $taskPackDirectory 'LICENSE.TXT') -Destination (Join-Path $taskLicenses 'dotnet-LICENSE.txt')
        Copy-Item -LiteralPath (Join-Path $taskPackDirectory 'THIRD-PARTY-NOTICES.TXT') -Destination (Join-Path $taskLicenses 'dotnet-THIRD-PARTY-NOTICES.txt')
    } else {
        Copy-Item -LiteralPath (Join-Path $taskPackDirectory 'LICENSE') -Destination (Join-Path $taskLicenses 'windowsdesktop-LICENSE.txt')
    }
}
$taskExecutable = Join-Path $taskPayload '键盘统计器.exe'
$taskFileInfo = [Diagnostics.FileVersionInfo]::GetVersionInfo($taskExecutable)
if ($taskFileInfo.ProductVersion -ne $taskVersion -or $taskFileInfo.ProductName -ne '键盘统计器') { throw 'Payload identity does not match this release.' }
$taskManifest = @(Get-ChildItem -LiteralPath $taskPayload -File -Recurse | ForEach-Object {
    [pscustomobject]@{Path=[IO.Path]::GetRelativePath($taskPayload, $_.FullName);Bytes=$_.Length;Sha256=(Get-FileHash -LiteralPath $_.FullName).Hash}
})
if ($taskManifest.Count -ne 5) { throw 'Unexpected files in the clean payload.' }
$taskManifest | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskReport 'payload-manifest.json') -Encoding UTF8

& $CompilerPath '--quiet-progress' ('--define=PayloadDir=' + $taskPayload) ('--define=ReleaseDir=' + $taskRelease) ('--define=AppVersion=' + $taskVersion) (Join-Path $taskRoot 'installer\KeyboardStats.iss')
if ($LASTEXITCODE -ne 0) { throw 'Inno Setup compilation failed.' }
$taskInstaller = Join-Path $taskRelease ('键盘统计器-' + $taskVersion + '-Windows-x64-安装包.exe')
$taskInstallerHash = (Get-FileHash -LiteralPath $taskInstaller).Hash
$taskSharing = Join-Path $taskBuild ('键盘统计器-' + $taskVersion)
New-Item -ItemType Directory -Path $taskSharing | Out-Null
Copy-Item -LiteralPath $taskInstaller -Destination $taskSharing
Copy-Item -LiteralPath (Join-Path $taskPayload '使用说明.txt') -Destination $taskSharing
$taskHashText = $taskInstallerHash + '  ' + [IO.Path]::GetFileName($taskInstaller) + "`r`n"
[IO.File]::WriteAllText((Join-Path $taskSharing 'SHA256.txt'), $taskHashText, [Text.UTF8Encoding]::new($true))
$taskZip = Join-Path $taskRelease ('键盘统计器-' + $taskVersion + '-Windows-x64-分享包.zip')
Compress-Archive -LiteralPath $taskSharing -DestinationPath $taskZip -CompressionLevel NoCompression -Force
Copy-Item -LiteralPath (Join-Path $taskSharing 'SHA256.txt'), (Join-Path $taskSharing '使用说明.txt') -Destination $taskRelease -Force
$taskResult = [pscustomobject]@{
    Version=$taskVersion; CompilerVersion=$taskCompilerVersion; Installer=$taskInstaller
    InstallerBytes=(Get-Item -LiteralPath $taskInstaller).Length; InstallerSha256=$taskInstallerHash
    ShareZip=$taskZip; Payload=$taskPayload; PayloadFiles=$taskManifest.Count
    PayloadExecutableSha256=(Get-FileHash -LiteralPath $taskExecutable).Hash
    Signed=(Get-AuthenticodeSignature -LiteralPath $taskInstaller).Status -eq 'Valid'
}
$taskResult | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskReport 'build.json') -Encoding UTF8
$taskResult | ConvertTo-Json
