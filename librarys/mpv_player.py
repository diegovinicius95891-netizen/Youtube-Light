import json
import os
import sys
import time
from pathlib import Path


def out(data):
    print(json.dumps(data, ensure_ascii=False), flush=True)


def as_int(value, default=0):
    try:
        return int(float(value))
    except Exception:
        return default


def bootstrap_mpv():
    library_dir = Path(os.environ.get("YOUTUBE_LIGHT_LIBRARY_DIR", "") or Path(__file__).resolve().parent)
    candidates = [
        library_dir / "py" / "MPV",
        Path(os.environ.get("MPV_HOME", "")),
        Path(os.environ.get("MPV_DLL_DIR", "")),
    ]
    for candidate in candidates:
        try:
            if not str(candidate):
                continue
            if any((candidate / name).is_file() for name in ("libmpv-2.dll", "mpv-2.dll", "mpv-1.dll")):
                os.environ["PATH"] = str(candidate) + os.pathsep + os.environ.get("PATH", "")
                add_dll_directory = getattr(os, "add_dll_directory", None)
                if add_dll_directory is not None:
                    add_dll_directory(str(candidate))
                return str(candidate)
        except Exception:
            pass
    return ""


def wait_until_ready(player, timeout=10):
    deadline = time.time() + timeout
    last_error = ""
    while time.time() < deadline:
        try:
            if bool(getattr(player, "core_idle", False)):
                time.sleep(0.12)
                continue
            if getattr(player, "path", None):
                return True, "tocando"
        except Exception as exc:
            last_error = str(exc)
        time.sleep(0.12)
    return False, last_error or "MPV nao confirmou a reproducao"


def get_number(player, property_name, default=0.0):
    try:
        value = getattr(player, property_name.replace("-", "_"))
        if value is None:
            return default
        return float(value)
    except Exception:
        return default


def list_audio_devices(player):
    devices = []
    try:
        raw = player.audio_device_list or []
        for item in raw:
            if not isinstance(item, dict):
                continue
            device_id = str(item.get("name") or "")
            description = str(item.get("description") or device_id)
            if device_id or description:
                devices.append({"id": device_id, "name": description})
    except Exception:
        pass
    return devices


def main():
    if len(sys.argv) < 2:
        out({"ok": False, "error": "arquivo de audio nao informado"})
        return 1

    runtime = bootstrap_mpv()
    if not runtime:
        out({"ok": False, "error": "runtime do MPV nao encontrado"})
        return 1

    try:
        import mpv
    except Exception as exc:
        out({"ok": False, "error": "python-mpv nao esta instalado: " + str(exc)})
        return 1

    media_path = sys.argv[1]
    if media_path.startswith("@"):
        try:
            media_path = Path(media_path[1:]).read_text(encoding="utf-8").strip()
        except Exception as exc:
            out({"ok": False, "error": "nao consegui ler a origem do audio: " + str(exc)})
            return 1
    volume = max(0, min(200, as_int(sys.argv[2], 50) if len(sys.argv) > 2 else 50))
    last_error = ""

    try:
        player = mpv.MPV(
            video=False,
            ytdl=False,
            input_default_bindings=False,
            input_vo_keyboard=False,
            osc=False,
            keep_open="yes",
            audio_fallback_to_null="yes",
            network_timeout=60,
            stream_lavf_o="reconnect=1,reconnect_streamed=1,reconnect_delay_max=5",
            loglevel="warn",
        )

        @player.event_callback("end-file")
        def on_end_file(event):
            nonlocal last_error
            try:
                reason = getattr(getattr(event, "data", None), "reason", None)
                if reason is not None and "ERROR" in str(reason).upper():
                    last_error = str(reason)
            except Exception:
                pass

        player.volume = volume
        player.play(media_path)
        ready, state = wait_until_ready(player, 10)
        if not ready:
            out({"ok": False, "error": state})
            try:
                player.terminate()
            except Exception:
                pass
            return 1
        out({"ok": True, "event": "ready", "state": state})
    except Exception as exc:
        out({"ok": False, "error": str(exc)})
        return 1

    for line in sys.stdin:
        line = line.strip()
        if not line:
            continue
        try:
            command = json.loads(line)
            name = command.get("command", "")
            if name == "pause-toggle":
                player.pause = not bool(player.pause)
            elif name == "seek":
                player.seek(float(command.get("delta", 0)), reference="relative")
            elif name == "seek-to":
                player.seek(float(command.get("seconds", 0)), reference="absolute")
            elif name == "set-volume":
                player.volume = max(0, min(200, as_int(command.get("volume", volume), volume)))
            elif name == "get-volume":
                out({"ok": True, "volume": max(0, get_number(player, "volume", volume))})
            elif name == "get-time":
                out({
                    "ok": True,
                    "position": max(0, get_number(player, "time-pos", 0)),
                    "duration": max(0, get_number(player, "duration", 0)),
                })
            elif name == "list-devices":
                out({"ok": True, "devices": list_audio_devices(player)})
            elif name == "set-device":
                player.audio_device = str(command.get("id", "") or "auto")
            elif name == "status":
                eof = False
                try:
                    eof = bool(player.eof_reached)
                except Exception:
                    pass
                out({"ok": True, "state": "error" if last_error else "playing", "ended": eof, "error": last_error})
            elif name == "stop":
                player.stop()
                break
        except Exception as exc:
            out({"ok": False, "error": str(exc)})

    try:
        player.terminate()
    except Exception:
        pass
    return 0


if __name__ == "__main__":
    sys.exit(main())
