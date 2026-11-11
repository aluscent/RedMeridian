@echo off
cd /d "%~dp0Source" || exit /b 1
echo Open http://127.0.0.1:5287/ after the server starts.
echo Save your colony before closing this window.
dotnet run --configuration Release --no-launch-profile -- --urls http://127.0.0.1:5287
if errorlevel 1 pause
