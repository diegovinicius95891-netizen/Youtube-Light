import json
import sys
import time

try:
    import vlc
except Exception as exc:
    print(json.dumps({"ok": False, "error": "python-vlc nao esta instalado: " + str(exc)}), flush=True)
    sys.exit(1)


def out(data):
    print(json.dumps(data, ensure_ascii=False), flush=True)


def as_int(value, default=0):
    try:
        return int(float(value))
    except Exception:
        return default


def player_state_name(player):
    try:
        return str(player.get_state())
    except Exception:
        return ""


def is_ended(player):
    state = player_state_name(player)
    return "Ended" in state or "Stopped" in state or "Error" in state


def wait_until_ready(player, timeout=10):
    deadline = time.time() + timeout
    last_state = ""
    while time.time() < deadline:
        state = player_state_name(player)
        last_state = state or last_state
        if "Error" in state:
            return False, state
        if "Playing" in state or "Buffering" in state:
            return True, state
        time.sleep(0.15)
    return False, last_state or "sem estado do VLC"


def list_audio_devices(player):
    devices = []
    try:
        head = player.audio_output_device_enum()
        item = head
        while item:
            contents = item.contents
            device_id = contents.device.decode("utf-8", "replace") if contents.device else ""
            description = contents.description.decode("utf-8", "replace") if contents.description else device_id
            devices.append({"id": device_id, "name": description})
            item = contents.next
        try:
            vlc.libvlc_audio_output_device_list_release(head)
        except Exception:
            pass
    except Exception:
        pass
    return devices


def main():
    if len(sys.argv) < 2:
        out({"ok": False, "error": "arquivo de audio nao informado"})
        return 1

    media_path = sys.argv[1]
    volume = max(0, min(200, as_int(sys.argv[2], 50) if len(sys.argv) > 2 else 50))

    try:
        instance = vlc.Instance("--no-video", "--quiet")
        if instance is None:
            out({"ok": False, "error": "nao consegui iniciar o libVLC"})
            return 1
        player = instance.media_player_new()
        media = instance.media_new(media_path)
        media.add_option(":network-caching=700")
        media.add_option(":http-user-agent=Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36")
        player.set_media(media)
        player.audio_set_volume(volume)
        result = player.play()
        if result == -1:
            out({"ok": False, "error": "VLC recusou a reproducao"})
            return 1
        ready, state = wait_until_ready(player, 10)
        if not ready:
            out({"ok": False, "error": "VLC nao iniciou a reproducao. Estado: " + state})
            try:
                player.stop()
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
                player.pause()
            elif name == "seek":
                delta = float(command.get("delta", 0))
                current = max(0, player.get_time())
                player.set_time(max(0, current + int(delta * 1000)))
            elif name == "seek-to":
                seconds = float(command.get("seconds", 0))
                player.set_time(max(0, int(seconds * 1000)))
            elif name == "set-volume":
                volume = max(0, min(200, as_int(command.get("volume", volume), volume)))
                player.audio_set_volume(volume)
            elif name == "get-volume":
                out({"ok": True, "volume": max(0, player.audio_get_volume())})
            elif name == "get-time":
                position = max(0, player.get_time()) / 1000.0
                duration = max(0, player.get_length()) / 1000.0
                out({"ok": True, "position": position, "duration": duration})
            elif name == "list-devices":
                out({"ok": True, "devices": list_audio_devices(player)})
            elif name == "set-device":
                device_id = command.get("id", "")
                player.audio_output_device_set(None, device_id)
                out({"ok": True})
            elif name == "status":
                out({"ok": True, "state": player_state_name(player), "ended": is_ended(player)})
            elif name == "stop":
                player.stop()
                break
        except Exception as exc:
            out({"ok": False, "error": str(exc)})

    return 0


if __name__ == "__main__":
    sys.exit(main())
