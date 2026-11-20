param([string]$Runtime = 'win-x64')
$ErrorActionPreference = 'Stop'
$projectRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $projectRoot 'src\KeyTrail.Desktop\KeyTrail.Desktop.csproj'
$output = Join-Path $projectRoot 'dist\KeyTrail'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet publish $project -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:UseSharedCompilation=false -o $output --nologo
if ($LASTEXITCODE -ne 0) { throw '发布失败，请检查上面的输出。' }
Write-Output "已发布：$output\KeyTrail.exe"
