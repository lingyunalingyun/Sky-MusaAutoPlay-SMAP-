# SkyMusicPlayer build script - jpackage app-image
$ErrorActionPreference = "Continue"
# Avoid PowerShell 5.1 wrapping native exe stderr as terminating error
$PSNativeCommandUseErrorActionPreference = $false
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $ScriptDir

$AppName = "SkyMusicPlayer"
$AppVersion = "1.2"
$MainClass = "org.example.skymusicplayer.Launcher"
$IconPath = "$ScriptDir\icon.ico"
$JdkHome = "C:\Program Files\Java\jdk-26"
$JPackage = "$JdkHome\bin\jpackage.exe"

if (-not (Test-Path $JPackage)) { throw "jpackage not found: $JPackage" }
if (-not (Test-Path $IconPath)) { throw "icon not found: $IconPath" }
$env:JAVA_HOME = $JdkHome

Write-Host "`n=== 1/6 Zip songs to resources/songs.zip ===" -ForegroundColor Cyan
$SongsSrc = "$ScriptDir\songs"
$SongsZip = "$ScriptDir\src\main\resources\org\example\skymusicplayer\songs.zip"
if (-not (Test-Path $SongsSrc)) { throw "songs/ folder missing" }
if (Test-Path $SongsZip) { Remove-Item $SongsZip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($SongsSrc, $SongsZip,
    [System.IO.Compression.CompressionLevel]::Optimal, $false)
$ZipMb = [math]::Round((Get-Item $SongsZip).Length / 1MB, 1)
Write-Host ("songs.zip = " + $ZipMb + " MB")

Write-Host "`n=== 2/6 mvn clean package ===" -ForegroundColor Cyan
& .\mvnw.cmd -q -DskipTests clean package
if ($LASTEXITCODE -ne 0) { throw "mvn package failed" }

Write-Host "`n=== 3/6 Collect dependencies to build/input ===" -ForegroundColor Cyan
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

Write-Host "`n=== 4/6 Clean dist ===" -ForegroundColor Cyan
$DistDir = "$ScriptDir\dist"
if (Test-Path "$DistDir\$AppName") { Remove-Item "$DistDir\$AppName" -Recurse -Force }
New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

Write-Host "`n=== 5/6 jpackage app-image ===" -ForegroundColor Cyan
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
    --description "Sky Automatic Piano Assistant" `
    --java-options "-Xmx512m" `
    --java-options "--enable-native-access=ALL-UNNAMED"
if ($LASTEXITCODE -ne 0) { throw "jpackage failed" }
$ExeSize = [math]::Round((Get-ChildItem "$DistDir\$AppName" -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
Write-Host ("app-image total size: " + $ExeSize + " MB")

Write-Host "`n=== 6/6 Zip app-image folder ===" -ForegroundColor Cyan
$ZipName = "$AppName-v$AppVersion-win64.zip"
$ZipPath = "$DistDir\$ZipName"
if (Test-Path $ZipPath) { Remove-Item $ZipPath -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory("$DistDir\$AppName", $ZipPath,
    [System.IO.Compression.CompressionLevel]::Optimal, $true)
$FinalMb = [math]::Round((Get-Item $ZipPath).Length / 1MB, 1)
Write-Host ("`nDONE: " + $ZipPath + " (" + $FinalMb + " MB)") -ForegroundColor Green
Write-Host ("exe: " + $DistDir + "\" + $AppName + "\" + $AppName + ".exe")
