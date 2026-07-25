# SMAP (Sky-MusaAutoPlay) build script - jpackage installer + portable zip
$ErrorActionPreference = "Continue"
$PSNativeCommandUseErrorActionPreference = $false
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

$AppName = "SMAP"
$AppVersion = "1.3"
$MainClass = "org.example.smap.Launcher"
$IconPath = "$ScriptDir\icon.ico"
$JdkHome = "C:\Users\lingy\jdk21\jdk-21.0.11+10"
$JPackage = "$JdkHome\bin\jpackage.exe"
$WixDir = "C:\Program Files (x86)\WiX Toolset v3.14\bin"

if (-not (Test-Path $JPackage)) { throw "jpackage not found: $JPackage" }
if (-not (Test-Path $IconPath)) { throw "icon not found: $IconPath" }
if (-not (Test-Path $WixDir)) { throw "WiX Toolset not found: $WixDir" }
$env:JAVA_HOME = $JdkHome
$env:PATH = "$WixDir;$env:PATH"

Write-Host "`n=== 1/7 Zip songs to resources/songs.zip ===" -ForegroundColor Cyan
$SongsSrc = "$ScriptDir\songs"
$SongsZip = "$ScriptDir\src\main\resources\org\example\smap\songs.zip"
if (-not (Test-Path $SongsSrc)) { throw "songs/ folder missing" }
if (Test-Path $SongsZip) { Remove-Item $SongsZip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($SongsSrc, $SongsZip,
    [System.IO.Compression.CompressionLevel]::Optimal, $false)
$ZipMb = [math]::Round((Get-Item $SongsZip).Length / 1MB, 1)
Write-Host ("songs.zip = " + $ZipMb + " MB")

Write-Host "`n=== 2/7 mvn clean package ===" -ForegroundColor Cyan
& .\mvnw.cmd -q -DskipTests clean package
if ($LASTEXITCODE -ne 0) { throw "mvn package failed" }

Write-Host "`n=== 3/7 Collect dependencies to build/input ===" -ForegroundColor Cyan
$InputDir = "$ScriptDir\build\input"
if (Test-Path "$ScriptDir\build") { Remove-Item "$ScriptDir\build" -Recurse -Force }
New-Item -ItemType Directory -Path $InputDir -Force | Out-Null
Copy-Item "$ScriptDir\target\$AppName-1.0-SNAPSHOT.jar" $InputDir
& .\mvnw.cmd -q dependency:copy-dependencies "-DoutputDirectory=$InputDir" -DincludeScope=runtime
if ($LASTEXITCODE -ne 0) { throw "copy-dependencies failed" }
$M2Openjfx = "$env:USERPROFILE\.m2\repository\org\openjfx"
Get-ChildItem -Path $M2Openjfx -Recurse -Filter "*-21.0.6-win.jar" | ForEach-Object {
    Copy-Item $_.FullName $InputDir
}
$JarCount = (Get-ChildItem $InputDir -Filter "*.jar").Count
Write-Host ("jar count: " + $JarCount)

Write-Host "`n=== 4/7 Clean dist ===" -ForegroundColor Cyan
$DistDir = "$ScriptDir\dist"
if (Test-Path "$DistDir\$AppName") { Remove-Item "$DistDir\$AppName" -Recurse -Force }
New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

Write-Host "`n=== 5/7 jpackage app-image ===" -ForegroundColor Cyan
& $JPackage `
    --type app-image `
    --input $InputDir `
    --dest $DistDir `
    --name $AppName `
    --main-jar "$AppName-1.0-SNAPSHOT.jar" `
    --main-class $MainClass `
    --icon $IconPath `
    --app-version $AppVersion `
    --vendor "lingyunalingyun" `
    --description "Sky-MusaAutoPlay" `
    --java-options "-Xmx512m" `
    --java-options "--enable-native-access=ALL-UNNAMED"
if ($LASTEXITCODE -ne 0) { throw "jpackage app-image failed" }
$ExeSize = [math]::Round((Get-ChildItem "$DistDir\$AppName" -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
Write-Host ("app-image total size: " + $ExeSize + " MB")

Write-Host "`n=== 6/7 jpackage EXE installer ===" -ForegroundColor Cyan
& $JPackage `
    --type exe `
    --app-image "$DistDir\$AppName" `
    --dest $DistDir `
    --name $AppName `
    --icon $IconPath `
    --app-version $AppVersion `
    --vendor "lingyunalingyun" `
    --description "Sky-MusaAutoPlay" `
    --win-dir-chooser `
    --win-shortcut `
    --win-shortcut-prompt `
    --win-menu `
    --win-menu-group "SMAP"
if ($LASTEXITCODE -ne 0) { throw "jpackage installer failed" }
$InstallerExe = Get-ChildItem "$DistDir\$AppName-*.exe" | Select-Object -First 1
Write-Host ("Installer: " + $InstallerExe.FullName + " (" + [math]::Round($InstallerExe.Length / 1MB, 1) + " MB)") -ForegroundColor Green

Write-Host "`n=== 7/7 Zip portable version ===" -ForegroundColor Cyan
$ZipName = "$AppName-v$AppVersion-win64-portable.zip"
$ZipPath = "$DistDir\$ZipName"
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory("$DistDir\$AppName", $ZipPath,
    [System.IO.Compression.CompressionLevel]::Optimal, $true)
$FinalMb = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
Write-Host ("`nDONE!") -ForegroundColor Green
Write-Host ("Installer: " + $InstallerExe.FullName)
Write-Host ("Portable:  " + $ZipPath + " (" + $FinalMb + " MB)")
