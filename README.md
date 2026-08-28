# Youtube Light

Youtube Light é um player portátil para Windows, focado em acessibilidade com leitores de tela, especialmente NVDA.

O aplicativo busca, reproduz e baixa conteúdo do YouTube usando ferramentas portáteis incluídas no pacote de release. A navegação principal foi pensada para teclado e fala: pressione Alt para abrir o menu principal, use setas para navegar e Enter para executar.

## Download

Baixe a versão portátil mais recente pela página de Releases do GitHub.

O pacote oficial de release inclui:

- Youtube Light compilado para Windows.
- Python portátil com as dependências necessárias.
- yt-dlp e youtube-dl.
- FFmpeg e FFplay.
- VLC/libVLC.
- MPV/libmpv.
- Integração com NVDA Controller Client.

## Desenvolvimento

O código principal fica em `includes/YoutubeMusicLightAccessible.cs`.

Scripts auxiliares ficam em `librarys/`.

Para gerar uma versão portátil local:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build_release.ps1 -Version 3.11.0
```

O ZIP final será criado em `release/`, junto com o arquivo `.sha256`.

## Dados do usuário

O programa não grava dados pessoais na pasta portátil quando executado normalmente:

- Configurações e dados do usuário ficam no AppData do Windows.
- Cache, logs e arquivos temporários ficam no LocalAppData.
- Dados antigos são migrados por cópia, sem apagar os originais.

## Créditos

Criado por Diego Vinicius Carmo Grando.
