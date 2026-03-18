# GridView Build & Installer Script
# Buildmodus kann angepasst werden

param(
    [string]$Configuration = "Release",
    [switch]$SkipBuild,
    [switch]$SkipPublish,
    [switch]$SkipInno
)

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path

Write-Host "════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "  GridView Build & Installer Generator" -ForegroundColor Cyan
Write-Host "════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# Cleanup alte Builds
Write-Host "[1/4] Cleanup alte Build-Artefakte..." -ForegroundColor Yellow
if (Test-Path "$scriptDir\GridView\bin\$Configuration") {
    Remove-Item "$scriptDir\GridView\bin\$Configuration" -Recurse -Force -ErrorAction SilentlyContinue
}
if (Test-Path "$scriptDir\publish") {
    Remove-Item "$scriptDir\publish" -Recurse -Force -ErrorAction SilentlyContinue
}
Write-Host "[OK] Cleanup fertig`n" -ForegroundColor Green

# 1. PROJEKT BAUEN
if (-not $SkipBuild) {
    Write-Host "[2/4] Projekt bauen..." -ForegroundColor Cyan
    try {
        Set-Location "$scriptDir\GridView"
        & dotnet build "DeskManager.csproj" -c $Configuration
        if ($LASTEXITCODE -ne 0) {
            throw "Build fehlgeschlagen"
        }
        Write-Host "[OK] Build erfolgreich`n" -ForegroundColor Green
    }
    catch {
        Write-Host "[ERROR] BUILD FEHLER: $_" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "[SKIP] Build uebersprungen`n" -ForegroundColor DarkGray
}

# 2. RELEASE PUBLIZIEREN
if (-not $SkipPublish) {
    Write-Host "[3/4] Release publizieren..." -ForegroundColor Cyan
    try {
        Set-Location "$scriptDir\GridView"
        & dotnet publish "DeskManager.csproj" -c $Configuration -o "$scriptDir\publish" --no-build
        if ($LASTEXITCODE -ne 0) {
            throw "Publish fehlgeschlagen"
        }
        Write-Host "[OK] Publish erfolgreich`n" -ForegroundColor Green
    }
    catch {
        Write-Host "[ERROR] PUBLISH FEHLER: $_" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "[SKIP] Publish uebersprungen`n" -ForegroundColor DarkGray
}

# 3. INNO SETUP INSTALLER ERSTELLEN
if (-not $SkipInno) {
    Write-Host "[4/4] Inno Setup Installer erstellen..." -ForegroundColor Cyan
    
    # Pruefe ob Inno Setup installiert ist
    $innoPath = Get-Command "iscc.exe" -ErrorAction SilentlyContinue
    if (-not $innoPath) {
        # Versuche Standard-Installation zu finden
        $innoPath = "C:\Program Files (x86)\Inno Setup 6\iscc.exe"
        if (-not (Test-Path $innoPath)) {
            Write-Host "[ERROR] Inno Setup nicht installiert!" -ForegroundColor Red
            Write-Host "        Download: https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
            Write-Host "        Nach Installation: Bitte Powershell neu starten!" -ForegroundColor Yellow
            exit 1
        }
    }

    try {
        Set-Location $scriptDir
        
        Write-Host "      Kompiliere Installer-Script..." -ForegroundColor DarkGray
        & $innoPath /O"$scriptDir" "$scriptDir\DeskManager-Installer.iss"
        
        if ($LASTEXITCODE -ne 0) {
            throw "Inno Setup Kompilierung fehlgeschlagen (Exit Code: $LASTEXITCODE)"
        }
        Write-Host "[OK] Installer erfolgreich erstellt`n" -ForegroundColor Green
    }
    catch {
        Write-Host "[ERROR] INNO SETUP FEHLER: $_" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "[SKIP] Inno Setup uebersprungen`n" -ForegroundColor DarkGray
}

Write-Host "════════════════════════════════════════════" -ForegroundColor Green
Write-Host "  [SUCCESS] Build erfolgreich abgeschlossen!" -ForegroundColor Green
Write-Host "════════════════════════════════════════════" -ForegroundColor Green
Write-Host ""
Write-Host "Dateien:" -ForegroundColor Cyan
Write-Host "  - Installer: $scriptDir\DeskManager-Setup.exe" -ForegroundColor White
Write-Host "  - Portable: $scriptDir\publish\DeskManager.exe" -ForegroundColor White
Write-Host ""
Write-Host "Naechste Schritte:" -ForegroundColor Yellow
Write-Host "  1. Teste DeskManager-Setup.exe lokal" -ForegroundColor White
Write-Host "  2. Erstelle GitHub Release: https://github.com/SimpliAj/DeskManager/releases" -ForegroundColor White
Write-Host "  3. Lade Installer hoch (DeskManager-Setup.exe)" -ForegroundColor White
Write-Host "  4. App findet neue Version automatisch ueber GitHub-API!" -ForegroundColor White
Write-Host ""
Write-Host "Update-System:" -ForegroundColor Cyan
Write-Host "  - UpdateService prueft GitHub-API beim App-Start" -ForegroundColor White
Write-Host "  - Zeigt Dialog wenn neue Version verfuegbar" -ForegroundColor White
Write-Host "  - Eroeffnet Release-Seite zum Download" -ForegroundColor White
Write-Host ""
