[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = Join-Path $repoRoot 'artifacts'
$buildRoot = Join-Path $artifactRoot ('build-' + [Guid]::NewGuid().ToString('N'))
$publishRoot = Join-Path $buildRoot 'publish'
$packageRoot = Join-Path $buildRoot 'TaskbarGroups'
$project = Join-Path $repoRoot 'src/TaskbarGroups/TaskbarGroups.csproj'

New-Item -ItemType Directory -Path $packageRoot -Force | Out-Null
& dotnet publish $project -c Release -r win-x64 --self-contained false -p:UseSharedCompilation=false -o $publishRoot
if ($LASTEXITCODE -ne 0) { throw 'The release build failed.' }

& dotnet (Join-Path $publishRoot 'TaskbarGroups.dll') --config-test
if ($LASTEXITCODE -ne 0) { throw 'Configuration verification failed.' }

# Copy only redistributable application files. Never copy an installation folder.
$applicationFiles = @(
    'TaskbarGroups.exe',
    'TaskbarGroups.dll',
    'TaskbarGroups.deps.json',
    'TaskbarGroups.runtimeconfig.json'
)
foreach ($name in $applicationFiles) {
    Copy-Item -LiteralPath (Join-Path $publishRoot $name) -Destination $packageRoot
}
foreach ($name in @('README.md', 'LEEME.txt')) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $name) -Destination $packageRoot
}

$zipPath = Join-Path $artifactRoot 'TaskbarGroups-win-x64.zip'
Compress-Archive -LiteralPath $packageRoot -DestinationPath $zipPath -Force
Write-Output "Clean package: $zipPath"
