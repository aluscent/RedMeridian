@echo off
cd /d "%~dp0Map Edition\Play"
echo Open http://127.0.0.1:5287/?v=map-4 after the server starts.
echo Save your colony before closing this window.
RedMeridian.exe --urls http://127.0.0.1:5287
if errorlevel 1 pause
