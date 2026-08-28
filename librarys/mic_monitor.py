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


def main():
    if len(sys.argv) < 2:
        out({"ok": False, "error": "microfone nao informado"})
        return 1

    microphone = sys.argv[1]
    output_device = sys.argv[2] if len(sys.argv) > 2 else ""
    volume = max(0, min(200, as_int(sys.argv[3], 70) if len(sys.argv) > 3 else 70))
    muted = len(sys.argv) > 4 and sys.argv[4].lower() == "true"

    try:
        instance = vlc.Instance("--aout=directsound", "--quiet")
        if instance is None:
            out({"ok": False, "error": "nao consegui iniciar o libVLC"})
            return 1

        player = instance.media_player_new()
        media = instance.media_new("dshow://")
        media.add_option(":dshow-vdev=none")
        media.add_option(":dshow-adev=" + microphone)
        media.add_option(":live-caching=80")
        media.add_option(":dshow-caching=80")
        if output_device:
            try:
                media.add_option(":directx-audio-device=" + output_device)
            except Exception:
                pass
        player.set_media(media)
        player.audio_set_volume(0 if muted else volume)
        result = player.play()
        if result == -1:
            out({"ok": False, "error": "VLC recusou o monitoramento do microfone"})
            return 1
        if output_device:
            try:
                player.audio_output_device_set(None, output_device)
            except Exception:
                pass
        time.sleep(0.35)
        out({"ok": True, "event": "ready"})
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
            if name == "set-volume":
                volume = max(0, min(200, as_int(command.get("volume", volume), volume)))
                player.audio_set_volume(0 if muted else volume)
                out({"ok": True})
            elif name == "set-muted":
                muted = bool(command.get("muted", muted))
                player.audio_set_volume(0 if muted else volume)
                out({"ok": True})
            elif name == "set-device":
                output_device = command.get("id", "")
                player.stop()
                try:
                    media.add_option(":directx-audio-device=" + output_device)
                except Exception:
                    pass
                player.set_media(media)
                player.audio_set_volume(0 if muted else volume)
                player.play()
                time.sleep(0.2)
                player.audio_output_device_set(None, output_device)
                out({"ok": True})
            elif name == "stop":
                player.stop()
                break
        except Exception as exc:
            out({"ok": False, "error": str(exc)})

    return 0


if __name__ == "__main__":
    sys.exit(main())
