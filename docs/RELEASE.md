# Release do Youtube Light

## Visão geral

O Youtube Light é um aplicativo portátil Windows em C# WinForms, compilado com .NET Framework e auxiliado por scripts Python em `librarys`.

O pacote público não contém código-fonte, dados pessoais, caches ou arquivos de desenvolvimento. O usuário final deve receber apenas o executável, `librarys`, documentação e licenças.

## Alterar versão

Atualize as constantes em `includes/YoutubeMusicLightAccessible.cs`:

- `AppVersion`
- `AppUpdatedAt`

Use tags no formato:

```text
v3.11.0
```

## Build local

Execute no PowerShell:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\build_release.ps1 -Version 3.11.0
```

O resultado fica em:

```text
release\Youtube-Light-Portable-3.11.0.zip
release\Youtube-Light-Portable-3.11.0.zip.sha256
```

## O que o script faz

1. Compila `YoutubeMusicLightAccessible.exe`.
2. Prepara dependências portáteis em `librarys\py`.
3. Instala pacotes Python necessários no Python portátil.
4. Valida `yt-dlp`, `youtube-dl`, FFmpeg, ffplay, VLC e MPV quando presentes.
5. Monta uma pasta limpa de release.
6. Remove caches e arquivos proibidos.
7. Cria ZIP portátil.
8. Gera SHA-256.
9. Testa o ZIP extraído em pasta isolada.

## Publicar no GitHub Releases

1. Faça commit do código.
2. Crie uma tag:

```powershell
git tag v3.11.0
git push origin v3.11.0
```

3. O GitHub Actions gera a Release e anexa:

```text
Youtube-Light-Portable-3.11.0.zip
Youtube-Light-Portable-3.11.0.zip.sha256
```

## Atualizador

O aplicativo consulta a API de Releases do GitHub no máximo uma vez por dia automaticamente. A opção manual em Ajuda ou no menu do aplicativo consulta imediatamente.

O atualizador:

1. encontra o asset `Youtube-Light-Portable-X.Y.Z.zip`;
2. encontra o asset `.sha256`;
3. baixa os dois;
4. calcula SHA-256 local;
5. só continua se o hash bater;
6. cria backup dos binários distribuíveis;
7. fecha o aplicativo;
8. substitui arquivos;
9. grava a versão em `%APPDATA%\YoutubeLight\versao_local.dat`;
10. reabre o aplicativo.

## Testar atualização localmente

Para testar antes de publicar:

1. gere uma versão menor e uma maior;
2. publique a maior como Release de teste no GitHub;
3. abra a menor;
4. use `Ajuda > Verificar atualização`;
5. confirme se o diálogo mostra changelog;
6. confirme se o hash é validado;
7. confirme se o aplicativo fecha, atualiza e reabre na versão nova.

## Dados do usuário

Dados persistentes ficam em:

```text
%APPDATA%\YoutubeLight
```

Cache, logs e backups de atualização ficam em:

```text
%LOCALAPPDATA%\YoutubeLight
```

Na primeira execução, dados antigos de `config` ao lado do executável são copiados para AppData sem apagar o original.

## Limitações

Atualize as constantes `GitHubOwner` e `GitHubRepo` no código caso o repositório oficial tenha outro nome.
