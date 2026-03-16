@echo off
REM GridView Build & Installer - Quick Start
REM Einfache BAT-Datei für schnelles Ausführen

cls
echo.
echo ╔════════════════════════════════════════════╗
echo ║  GridView Build ^& Installer Generator     ║
echo ╚════════════════════════════════════════════╝
echo.

REM Prüfe ob PowerShell Skript vorhanden ist
if not exist "%~dp0build-installer.ps1" (
    echo FEHLER: build-installer.ps1 nicht gefunden!
    pause
    exit /b 1
)

REM Führe PowerShell Skript aus
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1"

pause
