@echo off
setlocal
echo Instalando ou atualizando dependencias...
python -m pip install -U pip setuptools yt-dlp ytmusicapi browser-cookie3 websocket-client python-vlc
where vlc.exe >nul 2>nul
if errorlevel 1 winget install --id VideoLAN.VLC -e --source winget --silent --accept-package-agreements --accept-source-agreements
where mpv.exe >nul 2>nul
if errorlevel 1 winget install --id shinchiro.mpv -e --source winget --silent --accept-package-agreements --accept-source-agreements
where node.exe >nul 2>nul
if errorlevel 1 winget install --id OpenJS.NodeJS -e --source winget --silent --accept-package-agreements --accept-source-agreements
echo.
echo Dependencias verificadas.
pause
