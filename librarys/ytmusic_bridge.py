import json
import os
import sys
import time
import urllib.request
from pathlib import Path

BASE = Path(__file__).resolve().parent
CONFIG = Path(os.environ.get("YOUTUBE_LIGHT_CONFIG_DIR", BASE.parent / "config")).resolve()
CONFIG.mkdir(parents=True, exist_ok=True)
OAUTH_FILE = CONFIG / "oauth.json"
BROWSER_FILE = CONFIG / "browser.json"
COOKIES_FILE = CONFIG / "cookies.txt"
CLIENT_FILE = CONFIG / "ytmusic_client.json"


def out(data):
    print(json.dumps(data, ensure_ascii=True))


def fail(message):
    out({"ok": False, "error": str(message)})
    raise SystemExit(1)


def load_client():
    if not CLIENT_FILE.exists():
        return "", ""
    data = json.loads(CLIENT_FILE.read_text(encoding="utf-8"))
    return data.get("client_id", ""), data.get("client_secret", "")


def save_client(client_id, client_secret):
    CLIENT_FILE.write_text(
        json.dumps({"client_id": client_id, "client_secret": client_secret}, indent=2),
        encoding="utf-8",
    )


def ytmusic(auth_required=False):
    from ytmusicapi import OAuthCredentials, YTMusic

    client_id, client_secret = load_client()
    if BROWSER_FILE.exists():
        ensure_browser_authorization()
        return YTMusic(str(BROWSER_FILE))
    if OAUTH_FILE.exists() and client_id and client_secret:
        return YTMusic(
            str(OAUTH_FILE),
            oauth_credentials=OAuthCredentials(client_id=client_id, client_secret=client_secret),
        )
    if auth_required:
        fail("Conta nao logada. Use o botao Login primeiro.")
    return YTMusic()


def artists_text(item):
    artists = item.get("artists") or []
    names = [a.get("name", "") for a in artists if isinstance(a, dict)]
    return ", ".join([n for n in names if n])


def duration_text(item):
    return item.get("duration") or item.get("duration_seconds") or ""


def video_url(video_id):
    if not video_id:
        return ""
    return "https://music.youtube.com/watch?v=" + video_id


def normalize_track(item):
    video_id = item.get("videoId") or item.get("video_id") or ""
    title = item.get("title") or item.get("name") or "Sem titulo"
    channel = artists_text(item) or item.get("artist") or item.get("author") or item.get("channel") or item.get("views") or ""
    return {
        "kind": "track",
        "title": title,
        "channel": channel,
        "duration": str(duration_text(item) or ""),
        "url": video_url(video_id),
        "videoId": video_id,
        "browseId": item.get("browseId") or "",
        "playlistId": item.get("playlistId") or "",
        "likeStatus": item.get("likeStatus") or item.get("feedbackTokens", {}).get("likeStatus", ""),
    }


def normalize_playlist(item):
    channel = item.get("author") or item.get("channel") or ""
    if isinstance(channel, list):
        channel = ", ".join([a.get("name", "") for a in channel if isinstance(a, dict)])
    return {
        "kind": "playlist",
        "title": item.get("title") or "Playlist sem titulo",
        "channel": channel,
        "duration": str(item.get("count") or item.get("itemCount") or ""),
        "url": "",
        "videoId": "",
        "browseId": item.get("browseId") or "",
        "playlistId": item.get("playlistId") or item.get("browseId") or "",
    }


def normalize_subscription(item):
    title = item.get("artist") or item.get("title") or item.get("name") or "Canal sem titulo"
    browse_id = item.get("browseId") or item.get("channelId") or ""
    url = ""
    if browse_id:
        url = "https://www.youtube.com/channel/" + browse_id
    return {
        "kind": "channel",
        "title": title,
        "channel": "Canal inscrito",
        "duration": "",
        "url": url,
        "videoId": "",
        "browseId": browse_id,
        "playlistId": "",
    }


def normalize_any(item):
    if not isinstance(item, dict):
        return None
    if item.get("videoId") or item.get("video_id"):
        return normalize_track(item)
    if item.get("playlistId") or item.get("browseId"):
        return normalize_playlist(item)
    return None


def flatten_sections(sections, wanted_title=None):
    items = []
    for section in sections or []:
        title = (section.get("title") or "").lower() if isinstance(section, dict) else ""
        if wanted_title and wanted_title.lower() not in title:
            continue
        for raw in section.get("contents", []):
            item = normalize_any(raw)
            if item:
                items.append(item)
    return items


def command_login(args):
    if len(args) < 2:
        fail("Informe Client ID e Client Secret.")
    client_id, client_secret = args[0], args[1]
    save_client(client_id, client_secret)
    from ytmusicapi import setup_oauth

    setup_oauth(client_id, client_secret, filepath=str(OAUTH_FILE), open_browser=True)
    if OAUTH_FILE.exists():
        out({"ok": True, "message": "Login salvo em oauth.json."})
    else:
        fail("Login nao concluido. O arquivo oauth.json nao foi criado.")


def command_browser_login(args):
    headers_raw = "\n".join(args).strip()
    if not headers_raw:
        fail("Cole os headers copiados do navegador.")
    from ytmusicapi import setup

    setup(filepath=str(BROWSER_FILE), headers_raw=headers_raw)
    ensure_browser_authorization()
    out({"ok": True, "message": "Login do navegador salvo em browser.json."})


def ensure_browser_authorization():
    if not BROWSER_FILE.exists():
        return
    try:
        from ytmusicapi.helpers import get_authorization, sapisid_from_cookie

        data = json.loads(BROWSER_FILE.read_text(encoding="utf-8"))
        cookie = data.get("cookie", "")
        origin = data.get("origin", "https://music.youtube.com")
        if cookie:
            sapisid = sapisid_from_cookie(cookie)
            data["authorization"] = get_authorization(sapisid + " " + origin)
            BROWSER_FILE.write_text(json.dumps(data, ensure_ascii=True, indent=4, sort_keys=True), encoding="utf-8")
    except Exception:
        return


def get_browser_cookie_jar(browser_name):
    try:
        import browser_cookie3
    except ImportError:
        subprocess_run = __import__("subprocess").run
        subprocess_run([sys.executable, "-m", "pip", "install", "-U", "browser-cookie3"], check=True)
        import browser_cookie3

    browser_name = (browser_name or "auto").lower()
    loaders = []
    if browser_name in ("auto", "edge"):
        loaders.append(("edge", browser_cookie3.edge))
    if browser_name in ("auto", "chrome"):
        loaders.append(("chrome", browser_cookie3.chrome))

    last_error = None
    for name, loader in loaders:
        try:
            jar = loader(domain_name=".youtube.com")
            cookies = list(jar)
            if cookies:
                return name, cookies
        except Exception as exc:
            last_error = exc
    if last_error:
        raise last_error
    raise RuntimeError("Nao encontrei cookies do YouTube Music no Edge ou Chrome. Abra music.youtube.com, faca login e tente de novo.")


def command_auto_browser_login(args):
    try:
        browser_name = args[0] if args else "auto"
        name, cookies = get_browser_cookie_jar(browser_name)
        cookie_header = "; ".join([c.name + "=" + c.value for c in cookies if "youtube.com" in c.domain])
        if not cookie_header:
            fail("Nao encontrei cookies validos do YouTube.")

        headers_raw = "\n".join(
            [
                "cookie: " + cookie_header,
                "x-goog-authuser: 0",
                "user-agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Safari/537.36",
                "accept: */*",
                "origin: https://music.youtube.com",
                "referer: https://music.youtube.com/",
            ]
        )
        from ytmusicapi import setup

        setup(filepath=str(BROWSER_FILE), headers_raw=headers_raw)
        ensure_browser_authorization()
        out({"ok": True, "message": "Login automatico salvo usando cookies do " + name + "."})
    except Exception as exc:
        fail(
            "Nao consegui ler os cookies do navegador. O Edge ou Chrome pode estar protegendo os cookies da conta. "
            "Use a opcao de janela separada do Login automatico. Detalhe: " + str(exc)
        )


def ws_recv(ws):
    msg = ws.recv()
    return json.loads(msg)


def read_json_url(url, timeout=2):
    with urllib.request.urlopen(url, timeout=timeout) as response:
        return json.loads(response.read().decode("utf-8"))


def cdp_call(ws, method, params=None, call_id=1):
    payload = {"id": call_id, "method": method}
    if params is not None:
        payload["params"] = params
    ws.send(json.dumps(payload))
    while True:
        response = ws_recv(ws)
        if response.get("id") == call_id:
            if "error" in response:
                raise RuntimeError(response["error"])
            return response


def command_cdp_login(args):
    port = int(args[0]) if args else 9222
    try:
        import websocket
    except ImportError:
        subprocess_run = __import__("subprocess").run
        subprocess_run([sys.executable, "-m", "pip", "install", "-U", "websocket-client"], check=True)
        import websocket

    version_url = f"http://127.0.0.1:{port}/json/version"
    tabs_url = f"http://127.0.0.1:{port}/json"
    last_error = None
    info = None
    for _ in range(30):
        try:
            info = read_json_url(version_url)
            break
        except Exception as exc:
            last_error = exc
            time.sleep(1)
    if not info:
        fail("Nao consegui conversar com o navegador de login. Detalhe: " + str(last_error))

    ws_url = ""
    for _ in range(60):
        try:
            tabs = read_json_url(tabs_url)
            for tab in tabs:
                url = tab.get("url", "")
                if "youtube.com" in url and tab.get("webSocketDebuggerUrl"):
                    ws_url = tab.get("webSocketDebuggerUrl")
                    break
            if ws_url:
                break
        except Exception as exc:
            last_error = exc
        time.sleep(1)

    if not ws_url:
        ws_url = info.get("webSocketDebuggerUrl")
    if not ws_url:
        fail("Nao encontrei a aba do YouTube Music. Deixe a janela do login aberta em music.youtube.com e tente de novo.")

    try:
        ws = websocket.create_connection(ws_url, timeout=10, origin=f"http://127.0.0.1:{port}")
        try:
            try:
                cdp_call(ws, "Network.enable", call_id=1)
            except Exception:
                pass
            response = cdp_call(
                ws,
                "Network.getCookies",
                {
                    "urls": [
                        "https://music.youtube.com/",
                        "https://www.youtube.com/",
                        "https://youtube.com/",
                        "https://accounts.google.com/",
                    ]
                },
                call_id=2,
            )
            cookies = ((response.get("result") or {}).get("cookies")) or []
            if not cookies:
                response = cdp_call(ws, "Network.getAllCookies", call_id=3)
                cookies = ((response.get("result") or {}).get("cookies")) or []
            if not cookies:
                response = cdp_call(ws, "Storage.getCookies", call_id=4)
                cookies = ((response.get("result") or {}).get("cookies")) or []
        finally:
            ws.close()
    except Exception as exc:
        fail("O Edge recusou a conexao de login. Feche a janela de login, abra o app atualizado e tente de novo. Detalhe: " + str(exc))

    youtube = [c for c in cookies if "youtube.com" in c.get("domain", "")]
    if not youtube:
        domains = sorted(set([c.get("domain", "") for c in cookies if c.get("domain")]))
        detail = ", ".join(domains[:8]) if domains else "nenhum cookie retornado"
        fail("Nao encontrei cookies do YouTube Music. Confirme que voce entrou na conta na janela de login do app e que ela esta aberta em music.youtube.com. Cookies vistos: " + detail)

    cookie_header = "; ".join([c.get("name", "") + "=" + c.get("value", "") for c in youtube])
    headers_raw = "\n".join(
        [
            "cookie: " + cookie_header,
            "x-goog-authuser: 0",
            "user-agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120 Safari/537.36",
            "accept: */*",
            "origin: https://music.youtube.com",
            "referer: https://music.youtube.com/",
        ]
    )
    write_netscape_cookies(youtube)
    try:
        from ytmusicapi import setup

        setup(filepath=str(BROWSER_FILE), headers_raw=headers_raw)
        ensure_browser_authorization()
    except Exception:
        pass
    out({"ok": True, "message": "Login do YouTube Music salvo a partir da janela do navegador do app."})


def write_netscape_cookies(cookies):
    lines = ["# Netscape HTTP Cookie File"]
    for c in cookies:
        domain = c.get("domain", "")
        include_subdomains = "TRUE" if domain.startswith(".") else "FALSE"
        path = c.get("path", "/")
        secure = "TRUE" if c.get("secure") else "FALSE"
        expires = str(int(c.get("expires") or 0))
        name = c.get("name", "")
        value = c.get("value", "")
        lines.append("\t".join([domain, include_subdomains, path, secure, expires, name, value]))
    COOKIES_FILE.write_text("\n".join(lines) + "\n", encoding="utf-8")


def command_account():
    yt = ytmusic(auth_required=True)
    out({"ok": True, "account": yt.get_account_info()})


def command_search(args):
    query = " ".join(args).strip()
    if not query:
        fail("Digite uma busca.")
    yt = ytmusic(False)
    items = yt.search(query, filter="songs", limit=20)
    out({"ok": True, "items": [normalize_track(i) for i in items]})


def command_search_filter(args):
    if len(args) < 2:
        fail("Informe texto e filtro da busca.")
    query = args[0].strip()
    filter_name = args[1].strip()
    if filter_name not in ("songs", "videos", "albums", "playlists", "artists"):
        filter_name = "songs"
    yt = ytmusic(False)
    items = yt.search(query, filter=filter_name, limit=25)
    out({"ok": True, "items": [normalize_any(i) for i in items]})


def command_library_songs():
    yt = ytmusic(auth_required=True)
    out({"ok": True, "items": [normalize_track(i) for i in yt.get_library_songs(limit=100)]})


def command_liked():
    yt = ytmusic(auth_required=True)
    data = yt.get_liked_songs(limit=100)
    tracks = data.get("tracks", data if isinstance(data, list) else [])
    out({"ok": True, "items": [normalize_track(i) for i in tracks]})


def command_history():
    yt = ytmusic(auth_required=True)
    out({"ok": True, "items": [normalize_track(i) for i in yt.get_history()]})


def command_playlists():
    yt = ytmusic(auth_required=True)
    out({"ok": True, "items": [normalize_playlist(i) for i in yt.get_library_playlists(limit=100)]})


def command_subscriptions():
    yt = ytmusic(auth_required=True)
    out({"ok": True, "items": [normalize_subscription(i) for i in yt.get_library_subscriptions(limit=100)]})


def command_playlist(args):
    if not args:
        fail("Playlist nao informada.")
    yt = ytmusic(auth_required=True)
    data = yt.get_playlist(args[0], limit=100)
    tracks = data.get("tracks", [])
    out({"ok": True, "items": [normalize_track(i) for i in tracks]})


def command_charts():
    yt = ytmusic(False)
    data = yt.get_charts(country="BR")
    songs = data.get("songs", {}).get("items", [])
    videos = data.get("videos", [])
    if songs:
        items = [normalize_track(i) for i in songs]
    else:
        items = [normalize_playlist(i) for i in videos]
    out({"ok": True, "items": items})


def command_home():
    yt = ytmusic(auth_required=True)
    out({"ok": True, "items": flatten_sections(yt.get_home(limit=5))})


def command_listen_again():
    yt = ytmusic(auth_required=True)
    out({"ok": True, "items": flatten_sections(yt.get_home(limit=5), "listen again")})


def command_explore():
    yt = ytmusic(False)
    data = yt.get_explore()
    items = []
    for key in ("new_releases", "charts", "moods_and_genres"):
        value = data.get(key)
        if isinstance(value, list):
            for raw in value:
                item = normalize_any(raw)
                if item:
                    items.append(item)
        elif isinstance(value, dict):
            for raw in value.get("items", []):
                item = normalize_any(raw)
                if item:
                    items.append(item)
    if not items:
        items = [normalize_playlist(i) for i in (yt.get_charts(country="BR").get("videos", []))]
    out({"ok": True, "items": items})


def command_watch(args):
    if not args:
        fail("Video nao informado.")
    yt = ytmusic(False)
    data = yt.get_watch_playlist(videoId=args[0], limit=25, radio=True)
    tracks = data.get("tracks", [])
    out({"ok": True, "items": [normalize_track(i) for i in tracks]})


def command_lyrics(args):
    if not args:
        fail("Video nao informado.")
    yt = ytmusic(False)
    watch = yt.get_watch_playlist(videoId=args[0], limit=1)
    browse_id = watch.get("lyrics")
    if not browse_id:
        song = yt.get_song(args[0])
        browse_id = (((song.get("videoDetails") or {}).get("lyrics")) or {}).get("browseId")
    if not browse_id:
        out({"ok": True, "lyrics": "Letra nao disponivel."})
        return
    lyrics = yt.get_lyrics(browse_id)
    if lyrics is None:
        text = "Letra nao disponivel."
    elif isinstance(lyrics, dict):
        text = lyrics.get("lyrics") or "Letra nao disponivel."
    else:
        text = getattr(lyrics, "lyrics", None) or "Letra nao disponivel."
    out({"ok": True, "lyrics": text})


def command_rate(args):
    if len(args) < 2:
        fail("Informe videoId e avaliacao.")
    yt = ytmusic(auth_required=True)
    rating = args[1].upper()
    if rating not in ("LIKE", "DISLIKE", "INDIFFERENT"):
        fail("Avaliacao invalida. Use LIKE, DISLIKE ou INDIFFERENT.")
    try:
        result = yt.rate_song(args[0], rating)
    except Exception as exc:
        if "401" not in str(exc):
            raise
        ensure_browser_authorization()
        yt = ytmusic(auth_required=True)
        try:
            result = yt.rate_song(args[0], rating)
        except Exception as retry_exc:
            if "401" in str(retry_exc):
                fail("Nao autorizado pelo YouTube Music. Faca login de novo em Mais opcoes, Logar com Google.")
            raise
    out({"ok": True, "result": result})


def command_add_to_playlist(args):
    if len(args) < 2:
        fail("Informe playlistId e videoId.")
    yt = ytmusic(auth_required=True)
    result = yt.add_playlist_items(args[0], videoIds=[args[1]], duplicates=False)
    out({"ok": True, "result": result})


def main():
    if len(sys.argv) < 2:
        fail("Comando nao informado.")
    command = sys.argv[1]
    args = sys.argv[2:]
    commands = {
        "login": command_login,
        "browser_login": command_browser_login,
        "auto_browser_login": command_auto_browser_login,
        "cdp_login": command_cdp_login,
        "account": lambda _args: command_account(),
        "search": command_search,
        "search_filter": command_search_filter,
        "library_songs": lambda _args: command_library_songs(),
        "liked": lambda _args: command_liked(),
        "history": lambda _args: command_history(),
        "playlists": lambda _args: command_playlists(),
        "subscriptions": lambda _args: command_subscriptions(),
        "playlist": command_playlist,
        "charts": lambda _args: command_charts(),
        "home": lambda _args: command_home(),
        "listen_again": lambda _args: command_listen_again(),
        "explore": lambda _args: command_explore(),
        "watch": command_watch,
        "lyrics": command_lyrics,
        "rate": command_rate,
        "add_to_playlist": command_add_to_playlist,
    }
    if command not in commands:
        fail("Comando desconhecido: " + command)
    commands[command](args)


if __name__ == "__main__":
    try:
        main()
    except SystemExit:
        raise
    except Exception as exc:
        fail(exc)
