@echo off
cd /d "%~dp0Play" || exit /b 1
echo Open http://127.0.0.1:5287/?v=map-5 after the server starts.
echo Save your colony before closing this window.
RedMeridian.exe --urls http://127.0.0.1:5287
if errorlevel 1 pause
