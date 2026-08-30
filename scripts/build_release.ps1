param(
    [string]$Version = "3.13.6"
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

$Root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$ReleaseDir = Join-Path $Root "release"
$StageRoot = Join-Path $ReleaseDir "stage"
$PackageDir = Join-Path $StageRoot "Youtube Light"
$ZipPath = Join-Path $ReleaseDir ("Youtube-Light-Portable-{0}.zip" -f $Version)
$ShaPath = "$ZipPath.sha256"
$CacheDir = Join-Path $env:USERPROFILE "Downloads\YoutubeLightDependencyCache"
$RuntimeDir = Join-Path $Root "librarys\py"
$PythonDir = Join-Path $RuntimeDir "Python"
$PythonExe = Join-Path $PythonDir "python.exe"

function New-Directory($Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        New-Item -ItemType Directory -Path $Path | Out-Null
    }
}

function Download-File($Url, $Target) {
    New-Directory ([System.IO.Path]::GetDirectoryName($Target))
    if ((Test-Path -LiteralPath $Target) -and ((Get-Item -LiteralPath $Target).Length -gt 1024)) {
        return
    }
    Write-Host "Baixando $Url"
    $client = New-Object System.Net.WebClient
    $client.Headers.Add("User-Agent", "Youtube-Light-build/$Version")
    $client.DownloadFile($Url, $Target)
}

function Invoke-Checked($File, $Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Comando falhou: $File $($Arguments -join ' ')"
    }
}

function Expand-ZipRuntime($Zip, $Destination, $Marker) {
    $Temp = Join-Path $env:TEMP ("youtube-light-expand-" + [guid]::NewGuid().ToString("N"))
    New-Directory $Temp
    if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Recurse -Force }
    Expand-Archive -LiteralPath $Zip -DestinationPath $Temp -Force
    $markerFile = Get-ChildItem -LiteralPath $Temp -Recurse -File -Filter $Marker | Select-Object -First 1
    if (-not $markerFile) { throw "$Marker não encontrado em $Zip" }
    Copy-Item -LiteralPath $markerFile.Directory.FullName -Destination $Destination -Recurse
    Remove-Item -LiteralPath $Temp -Recurse -Force
}

function Get-GitHubAsset($Owner, $Repo, $Pattern) {
    $api = "https://api.github.com/repos/$Owner/$Repo/releases/latest"
    $release = Invoke-RestMethod -Uri $api -Headers @{ "User-Agent" = "Youtube-Light-build/$Version"; "Accept" = "application/vnd.github+json" }
    $asset = $release.assets | Where-Object { $_.name -match $Pattern } | Select-Object -First 1
    if (-not $asset) { throw "Asset não encontrado em ${Owner}/${Repo}: $Pattern" }
    return $asset
}

function Prepare-Python {
    New-Directory $RuntimeDir
    if (-not (Test-Path -LiteralPath $PythonExe)) {
        $zip = Join-Path $CacheDir "python-3.12.10-embed-amd64.zip"
        Download-File "https://www.python.org/ftp/python/3.12.10/python-3.12.10-embed-amd64.zip" $zip
        if (Test-Path -LiteralPath $PythonDir) { Remove-Item -LiteralPath $PythonDir -Recurse -Force }
        New-Directory $PythonDir
        Expand-Archive -LiteralPath $zip -DestinationPath $PythonDir -Force
    }
    if (-not (Test-Path -LiteralPath $PythonExe)) { throw "Python portátil não foi extraído corretamente." }
    $pth = Get-ChildItem -LiteralPath $PythonDir -Filter "python*._pth" | Select-Object -First 1
    if ($pth) {
        $lines = Get-Content -LiteralPath $pth.FullName
        $lines = $lines | ForEach-Object { if ($_.Trim().TrimStart("#").Trim() -eq "import site") { "import site" } else { $_ } }
        if (-not ($lines | Where-Object { $_.Trim() -eq "import site" })) { $lines += "import site" }
        [System.IO.File]::WriteAllLines($pth.FullName, [string[]]$lines, (New-Object System.Text.UTF8Encoding($false)))
    }
    $getPip = Join-Path $CacheDir "get-pip.py"
    Download-File "https://bootstrap.pypa.io/get-pip.py" $getPip
    & $PythonExe -m pip --version | Out-Null
    if ($LASTEXITCODE -ne 0) { Invoke-Checked $PythonExe @($getPip) }
    Invoke-Checked $PythonExe @("-m", "pip", "install", "--upgrade", "pip", "setuptools", "ytmusicapi", "browser-cookie3", "websocket-client", "python-vlc", "python-mpv", "pycryptodomex", "soundcard")
}

function Prepare-Tools {
    New-Directory $RuntimeDir
    Download-File "https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp.exe" (Join-Path $RuntimeDir "yt-dlp.exe")
    Download-File "https://github.com/ytdl-org/youtube-dl/releases/latest/download/youtube-dl.exe" (Join-Path $RuntimeDir "youtube-dl.exe")

    $vlcZip = Join-Path $CacheDir "vlc-3.0.23-win64.zip"
    Download-File "https://get.videolan.org/vlc/3.0.23/win64/vlc-3.0.23-win64.zip" $vlcZip
    Expand-ZipRuntime $vlcZip (Join-Path $RuntimeDir "VLC") "libvlc.dll"

    $ffZip = Join-Path $CacheDir "ffmpeg-master-latest-win64-gpl-shared.zip"
    if (-not (Test-Path -LiteralPath $ffZip)) {
        $ffAsset = Get-GitHubAsset "BtbN" "FFmpeg-Builds" "^ffmpeg-master-latest-win64-gpl-shared\.zip$"
        Download-File $ffAsset.browser_download_url $ffZip
    }
    $ffDest = Join-Path $RuntimeDir "FFmpeg\bin"
    Expand-ZipRuntime $ffZip $ffDest "ffmpeg.exe"

    $mpvArchive = Get-ChildItem -LiteralPath $CacheDir -File -Filter "mpv-v*-x86_64-pc-windows-msvc.zip" | Select-Object -First 1 | Select-Object -ExpandProperty FullName
    if (-not $mpvArchive) {
        $mpvAsset = Get-GitHubAsset "mpv-player" "mpv" "^mpv-v[0-9.]+-x86_64-pc-windows-msvc\.zip$"
        $mpvArchive = Join-Path $CacheDir $mpvAsset.name
        Download-File $mpvAsset.browser_download_url $mpvArchive
    }
    $mpvTemp = Join-Path $env:TEMP ("youtube-light-mpv-" + [guid]::NewGuid().ToString("N"))
    New-Directory $mpvTemp
    Expand-Archive -LiteralPath $mpvArchive -DestinationPath $mpvTemp -Force
    $mpvExe = Get-ChildItem -LiteralPath $mpvTemp -Recurse -File -Filter "mpv.exe" | Select-Object -First 1
    if (-not $mpvExe) { throw "mpv.exe não encontrado" }
    $mpvDest = Join-Path $RuntimeDir "MPV"
    if (Test-Path -LiteralPath $mpvDest) { Remove-Item -LiteralPath $mpvDest -Recurse -Force }
    Copy-Item -LiteralPath $mpvExe.Directory.FullName -Destination $mpvDest -Recurse
    Remove-Item -LiteralPath $mpvTemp -Recurse -Force

    $libMpvArchive = Get-ChildItem -LiteralPath $CacheDir -File -Filter "mpv-dev-x86_64-*.7z" | Select-Object -First 1 | Select-Object -ExpandProperty FullName
    if (-not $libMpvArchive) {
        $libMpvAsset = Get-GitHubAsset "zhongfly" "mpv-winbuild" "^mpv-dev-x86_64-\d{8}-git-[0-9a-f]+\.7z$"
        $libMpvArchive = Join-Path $CacheDir $libMpvAsset.name
        Download-File $libMpvAsset.browser_download_url $libMpvArchive
    }
    $seven = Join-Path $CacheDir "7zr.exe"
    Download-File "https://www.7-zip.org/a/7zr.exe" $seven
    $libMpvTemp = Join-Path $env:TEMP ("youtube-light-libmpv-" + [guid]::NewGuid().ToString("N"))
    New-Directory $libMpvTemp
    & $seven x $libMpvArchive "-o$libMpvTemp" -y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Falha ao extrair libmpv." }
    $libmpv = Get-ChildItem -LiteralPath $libMpvTemp -Recurse -File -Filter "libmpv-2.dll" | Select-Object -First 1
    if (-not $libmpv) { throw "libmpv-2.dll não encontrado" }
    Copy-Item -Path (Join-Path $libmpv.Directory.FullName "*") -Destination $mpvDest -Recurse -Force
    Remove-Item -LiteralPath $libMpvTemp -Recurse -Force
}

function Compile-App {
    $nvda = Join-Path $Root "librarys\nvdaControllerClient64.dll"
    $legacyNvda = Join-Path $Root "Library\nvdaControllerClient64.dll"
    if ((-not (Test-Path -LiteralPath $nvda)) -and (Test-Path -LiteralPath $legacyNvda)) {
        Copy-Item -LiteralPath $legacyNvda -Destination $nvda -Force
    }
    & (Join-Path $Root "includes\build_accessible.bat")
    if (-not (Test-Path -LiteralPath (Join-Path $Root "YoutubeMusicLightAccessible.exe"))) {
        throw "Executável principal não foi gerado."
    }
}

function Clean-Runtime {
    Get-ChildItem -LiteralPath (Join-Path $Root "librarys") -Recurse -Directory -Filter "__pycache__" -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
    Get-ChildItem -LiteralPath (Join-Path $Root "librarys") -Recurse -Include "*.pyc","*.pyo" -File -ErrorAction SilentlyContinue | Remove-Item -Force
}

function Smoke-Test($Base) {
    $py = Join-Path $Base "librarys\py\Python\python.exe"
    $rt = Join-Path $Base "librarys\py"
    $env:YOUTUBE_LIGHT_LIBRARY_DIR = Join-Path $Base "librarys"
    $env:PATH = (Join-Path $rt "MPV") + ";" + (Join-Path $rt "VLC") + ";" + (Join-Path $rt "FFmpeg\bin") + ";" + (Join-Path $rt "Python") + ";" + $env:PATH
    if (-not (Test-Path -LiteralPath (Join-Path $Base "YoutubeMusicLightAccessible.exe"))) { throw "exe ausente" }
    if (-not (Test-Path -LiteralPath (Join-Path $Base "librarys\nvdaControllerClient64.dll"))) { throw "NVDA Controller ausente" }
    Invoke-Checked $py @("-c", "import pip, ytmusicapi, browser_cookie3, websocket, vlc, mpv; print('python ok')")
    Invoke-Checked (Join-Path $rt "yt-dlp.exe") @("--version")
    Invoke-Checked (Join-Path $rt "youtube-dl.exe") @("--version")
    Invoke-Checked (Join-Path $rt "FFmpeg\bin\ffmpeg.exe") @("-version")
    Invoke-Checked (Join-Path $rt "FFmpeg\bin\ffplay.exe") @("-version")
    Invoke-Checked $py @("-c", "import os; os.add_dll_directory(r'$rt\MPV'); import mpv; p=mpv.MPV(video=False,ytdl=False,input_default_bindings=False,input_vo_keyboard=False,osc=False); print('mpv ok')")
    Invoke-Checked $py @("-c", "import vlc; i=vlc.Instance('--no-video','--quiet'); print('vlc ok' if i else 'vlc fail')")
    $audioEngine = Join-Path $Base "librarys\audio\Placasom.exe"
    if (-not (Test-Path -LiteralPath $audioEngine)) { throw "Motor de roteamento de áudio ausente" }
    Invoke-Checked $audioEngine @("--list")
}

function Build-Package {
    if (Test-Path -LiteralPath $StageRoot) { Remove-Item -LiteralPath $StageRoot -Recurse -Force }
    New-Directory $PackageDir
    Copy-Item -LiteralPath (Join-Path $Root "YoutubeMusicLightAccessible.exe") -Destination $PackageDir
    Copy-Item -LiteralPath (Join-Path $Root "Tutorial Youtube-Music-Light.txt") -Destination (Join-Path $PackageDir "Tutorial Youtube-Music-Light.txt")
    Copy-Item -LiteralPath (Join-Path $Root "CREDITOS.txt") -Destination (Join-Path $PackageDir "CREDITOS.txt")
    Copy-Item -LiteralPath (Join-Path $Root "THIRD_PARTY_LICENSES.txt") -Destination $PackageDir
    $librarySource = Join-Path $Root "librarys"
    $libraryTarget = Join-Path $PackageDir "librarys"
    New-Directory $libraryTarget
    & robocopy $librarySource $libraryTarget /E /R:2 /W:1 /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -gt 7) { throw "Falha ao copiar librarys com robocopy. Código: $LASTEXITCODE" }
    $stageNvda = Join-Path $PackageDir "librarys\nvdaControllerClient64.dll"
    $legacyNvda = Join-Path $Root "Library\nvdaControllerClient64.dll"
    if ((-not (Test-Path -LiteralPath $stageNvda)) -and (Test-Path -LiteralPath $legacyNvda)) {
        Copy-Item -LiteralPath $legacyNvda -Destination $stageNvda -Force
    }
    Get-ChildItem -LiteralPath $PackageDir -Recurse -Directory -Filter "__pycache__" -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
    Get-ChildItem -LiteralPath $PackageDir -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -eq ".pyc" -or $_.Extension -eq ".pyo" -or $_.Extension -eq ".bat" } |
        Remove-Item -Force
    New-Directory (Join-Path $PackageDir "licenses")
    Copy-Item -LiteralPath (Join-Path $Root "THIRD_PARTY_LICENSES.txt") -Destination (Join-Path $PackageDir "licenses\THIRD_PARTY_LICENSES.txt")

    $packageRoot = (Resolve-Path -LiteralPath $PackageDir).Path.TrimEnd("\")
    $forbidden = Get-ChildItem -LiteralPath $PackageDir -Recurse -Force | Where-Object {
        $relative = $_.FullName.Substring($packageRoot.Length).TrimStart("\")
        $parts = $relative -split "\\"
        ($parts.Length -gt 0 -and ($parts[0] -eq "config" -or $parts[0] -eq "includes")) -or
        ($parts -contains "keytune_ref") -or
        $_.Name -match "^(cookies\.txt|oauth\.json|token\.json|browser\.json|ytmusic_client\.json|headers_auth\.(json|dat)|historico_local\.dat|favoritos_locais\.dat|fila_reproducao\.dat)$|\.(pyc|pyo|bat|log)$"
    }
    if ($forbidden) { throw "Arquivo proibido no pacote: $($forbidden[0].FullName)" }

    Smoke-Test $PackageDir
    Get-ChildItem -LiteralPath $PackageDir -Recurse -Directory -Filter "__pycache__" -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
    Get-ChildItem -LiteralPath $PackageDir -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -eq ".pyc" -or $_.Extension -eq ".pyo" -or $_.Extension -eq ".bat" } |
        Remove-Item -Force
    $forbiddenAfterSmoke = Get-ChildItem -LiteralPath $PackageDir -Recurse -Force | Where-Object {
        $relative = $_.FullName.Substring($packageRoot.Length).TrimStart("\")
        $parts = $relative -split "\\"
        ($parts.Length -gt 0 -and ($parts[0] -eq "config" -or $parts[0] -eq "includes")) -or
        ($parts -contains "keytune_ref") -or
        $_.Name -match "^(cookies\.txt|oauth\.json|token\.json|browser\.json|ytmusic_client\.json|headers_auth\.(json|dat)|historico_local\.dat|favoritos_locais\.dat|fila_reproducao\.dat)$|\.(pyc|pyo|bat|log)$"
    }
    if ($forbiddenAfterSmoke) { throw "Arquivo proibido no pacote depois dos testes: $($forbiddenAfterSmoke[0].FullName)" }
    if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath -Force }
    Compress-Archive -LiteralPath $PackageDir -DestinationPath $ZipPath -CompressionLevel Optimal
    $hash = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $(Split-Path -Leaf $ZipPath)" | Set-Content -LiteralPath $ShaPath -Encoding ASCII

    $iso = Join-Path $env:TEMP ("YoutubeLightIsolated-" + [guid]::NewGuid().ToString("N"))
    New-Directory $iso
    Expand-Archive -LiteralPath $ZipPath -DestinationPath $iso -Force
    Smoke-Test (Join-Path $iso "Youtube Light")
    Remove-Item -LiteralPath $iso -Recurse -Force
}

Prepare-Python
Prepare-Tools
Compile-App
Build-Package

Write-Host "Release criada:"
Write-Host $ZipPath
Write-Host $ShaPath
