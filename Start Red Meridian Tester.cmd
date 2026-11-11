@echo off
cd /d "%~dp0Source" || exit /b 1
echo Open http://127.0.0.1:5288/ after the tester server starts.
echo Tester mode uses a separate save file.
dotnet run --configuration Release --no-launch-profile -- --urls http://127.0.0.1:5288 --developer true
if errorlevel 1 pause
