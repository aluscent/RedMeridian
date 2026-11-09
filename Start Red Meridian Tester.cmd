@echo off
cd /d "%~dp0Play" || exit /b 1
echo Open http://127.0.0.1:5288/ after the tester server starts.
echo Tester mode uses a separate save file.
RedMeridian.exe --urls http://127.0.0.1:5288 --developer true
if errorlevel 1 pause
