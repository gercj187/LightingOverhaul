param (
    [switch]$NoArchive,
    [string]$OutputDirectory = $PSScriptRoot
)

$ErrorActionPreference = "Stop"
Set-Location -Path $PSScriptRoot

Write-Host ""
Write-Host ""
Write-Host ""
Write-Host "=== Packe die Mod ===" -ForegroundColor Cyan
Write-Host ""

# Mod-Dateien prüfen
$FilesToInclude = @("info.json", "build/*", "LICENSE")
if (!(Test-Path "info.json")) { Write-Error "❌ info.json fehlt!"; exit 1 }
if (!(Test-Path "build")) { Write-Error "❌ build/-Ordner fehlt!"; exit 1 }

# Mod-Infos
$modInfo = Get-Content -Raw -Path "info.json" | ConvertFrom-Json
$modId = $modInfo.Id
$modVersion = $modInfo.Version

Write-Host "Mod-ID: $modId"
Write-Host "Version: $modVersion"

# Zielverzeichnisse
$DistDir = Join-Path $OutputDirectory "dist"
if (!(Test-Path $DistDir)) {
    New-Item -Path $DistDir -ItemType Directory -Force | Out-Null
}

$ZipWorkDir = if ($NoArchive) { $OutputDirectory } else { Join-Path $DistDir "tmp" }
$ZipOutDir = Join-Path $ZipWorkDir $modId

# Arbeitsordner vorbereiten
if (Test-Path $ZipOutDir) {
    Remove-Item $ZipOutDir -Recurse -Force
}
New-Item -ItemType Directory -Path $ZipOutDir -Force | Out-Null

# Dateien kopieren
foreach ($item in $FilesToInclude) {
    Write-Host "Kopiere: $item"
    Copy-Item -Path $item -Destination $ZipOutDir -Recurse -Force
}
Write-Host "Alle Dateien erfolgreich gepackt."

# Archiv erstellen
if (-not $NoArchive) {
    $fileName = "${modId}_v${modVersion}.zip"
    $zipPath = Join-Path $DistDir $fileName

    # Backup bei existierender ZIP
    if (Test-Path $zipPath) {
        $backupDir = Join-Path $OutputDirectory "backup"
        if (!(Test-Path $backupDir)) {
            New-Item -Path $backupDir -ItemType Directory -Force | Out-Null
        }

        $timestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
        $backupName = "${modId}_v${modVersion}_$timestamp.zip"
        $backupPath = Join-Path $backupDir $backupName

        Move-Item $zipPath $backupPath -Force
        Write-Host "Vorherige Version gesichert: ...\backup\$backupName"
    }

    Write-Host "Erzeuge neue Version:"
    Compress-Archive -Path (Join-Path $ZipOutDir "*") -DestinationPath $zipPath -CompressionLevel Fastest
    Write-Host "ZIP erfolgreich erstellt: ...\dist\$fileName"
}
else {
    Write-Host "Archivieren übersprungen (NoArchive aktiviert)."
}

# FETTES, GRÜNES "FERTIG!"
Write-Host ""
Write-Host "=== FERTIG! ===" -ForegroundColor Green
Write-Host ""
Write-Host ""
Write-Host ""
