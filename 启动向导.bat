@echo off
chcp 65001 >nul
cd /d "%~dp0"
set "PS=pwsh"
where pwsh >nul 2>nul || set "PS=powershell"
echo.
echo   Mortal Kombat 1 - Voice Export Wizard
echo   ====================================
%PS% -NoProfile -ExecutionPolicy Bypass -File "%~dp0wizard.ps1" %*
echo.
pause
