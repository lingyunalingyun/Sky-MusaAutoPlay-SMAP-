# Build SMAP installer: publish SMAP -> zip into Payload\app.zip -> publish installer (embeds payload) as single-file exe.
$ErrorActionPreference = "Stop"
$inst       = $PSScriptRoot
$smapCsproj = Join-Path (Split-Path $inst) "SMAP-WPF.csproj"   # Installer/ 在 SMAP-WPF 仓库内, 父目录即项目根
$pubDir     = Join-Path $inst "_smap_publish"
$payload    = Join-Path $inst "Payload\app.zip"
$outDir     = Join-Path $inst "_setup_out"
$dotnet     = "C:\Program Files\dotnet\dotnet.exe"

Write-Host "[1/3] Publishing SMAP (self-contained win-x64)..." -ForegroundColor Cyan
if (Test-Path $pubDir) { Remove-Item $pubDir -Recurse -Force }
& $dotnet publish $smapCsproj -c Release -r win-x64 --self-contained true -o $pubDir /p:DebugType=none /p:DebugSymbols=false --nologo
if ($LASTEXITCODE -ne 0) { throw "SMAP publish failed" }

# 示例曲谱: 放进发布产物的 songs\ , 装机后本地曲库自带这几首
$sampleDir = Join-Path $inst "sample-songs"
if (Test-Path $sampleDir) {
    $songsOut = Join-Path $pubDir "songs"
    New-Item -ItemType Directory -Force $songsOut | Out-Null
    Copy-Item (Join-Path $sampleDir "*") $songsOut -Force
    "  {0} sample songs bundled" -f (Get-ChildItem $songsOut).Count | Write-Host
}

Write-Host "[2/3] Zipping into Payload\app.zip..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force (Split-Path $payload) | Out-Null
if (Test-Path $payload) { Remove-Item $payload -Force }
Compress-Archive -Path (Join-Path $pubDir "*") -DestinationPath $payload -CompressionLevel Optimal
"{0:N1} MB payload" -f ((Get-Item $payload).Length / 1MB) | Write-Host

Write-Host "[3/3] Publishing installer (embed payload, single-file)..." -ForegroundColor Cyan
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
& $dotnet publish (Join-Path $inst "SMAP-Installer.csproj") -c Release -r win-x64 --self-contained true -o $outDir /p:PublishSingleFile=true /p:DebugType=none /p:DebugSymbols=false --nologo
if ($LASTEXITCODE -ne 0) { throw "Installer publish failed" }

$setup = Join-Path $outDir "SMAP-Setup.exe"
"{0:N1} MB installer" -f ((Get-Item $setup).Length / 1MB) | Write-Host
Write-Host "DONE: $setup" -ForegroundColor Green
