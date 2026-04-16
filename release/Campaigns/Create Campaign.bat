@echo off
echo ========================================
echo   Custom Campaign Creator
echo ========================================
echo.
py "%~dp0create_campaign.py"
if errorlevel 1 (
    echo.
    echo Python not found. Install Python from python.org or Microsoft Store.
    pause
)
pause
