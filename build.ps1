$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$artifactRoot = Join-Path $projectRoot 'publish'
$portableDir = Join-Path $artifactRoot 'portable'
$installerDir = $artifactRoot

if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $portableDir, $installerDir | Out-Null

dotnet publish (Join-Path $projectRoot 'MusicConverter.csproj') `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $portableDir

$toolsDir = Join-Path $portableDir 'tools'
New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
Copy-Item -LiteralPath (Join-Path $projectRoot 'tools\qmdec.exe') -Destination $toolsDir -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'tools\ffmpeg.exe') -Destination $toolsDir -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'Packaging\runtime-README.txt') -Destination $toolsDir -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'ThirdPartyNotices.txt') -Destination $portableDir -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'LICENSE') -Destination $portableDir -Force
Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $portableDir -Force

$licenseDir = Join-Path $portableDir 'licenses'
New-Item -ItemType Directory -Force -Path $licenseDir | Out-Null
Copy-Item -Path (Join-Path $projectRoot 'licenses\*') -Destination $licenseDir -Force

$isccCandidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
    (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
)
$iscc = $isccCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iscc) {
    throw 'Inno Setup 6 ISCC.exe was not found.'
}

& $iscc (Join-Path $projectRoot 'Packaging\MusicConverter.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed with exit code $LASTEXITCODE" }

Write-Host "Portable app: $portableDir" -ForegroundColor Green
Write-Host "Installer: $installerDir" -ForegroundColor Green
