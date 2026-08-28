import json
import os
import sys
import threading
import time

try:
    import soundcard as sc
except Exception as exc:
    print(json.dumps({"ok": False, "error": "soundcard nao esta instalado: " + str(exc)}), flush=True)
    sys.exit(1)


def out(data):
    print(json.dumps(data, ensure_ascii=False), flush=True)


def log_error(message):
    # Mantem o diagnostico fora do stdout, que e usado pelo protocolo do app.
    try:
        text = time.strftime("%Y-%m-%d %H:%M:%S ") + message + "\n"
        paths = [os.path.join(os.environ.get("YOUTUBE_LIGHT_CONFIG_DIR", "."), "microfone.log"),
                 os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "microfone.log")]
        for path in dict.fromkeys(paths):
            try:
                os.makedirs(os.path.dirname(path), exist_ok=True)
                with open(path, "a", encoding="utf-8") as handle:
                    handle.write(text)
            except Exception:
                pass
    except Exception:
        pass


def number(value, default=70):
    try:
        return int(float(value))
    except Exception:
        return default


class Router:
    def __init__(self, microphone, output, volume, muted):
        self.microphone = microphone
        self.output = output
        self.volume = max(0, min(200, volume))
        self.muted = muted
        self.stop_event = threading.Event()
        self.restart_event = threading.Event()
        self.lock = threading.Lock()
        self.ready_sent = False
        self.error_sent = False
        self.thread = threading.Thread(target=self.loop, daemon=True)

    def find_mic(self):
        devices = sc.all_microphones(include_loopback=True)
        for device in devices:
            if device.name.lower() == self.microphone.lower() or self.microphone.lower() in device.name.lower():
                return device
        raise RuntimeError("microfone nao encontrado: " + self.microphone)

    def find_output(self):
        if not self.output:
            return sc.default_speaker()
        for device in sc.all_speakers():
            if device.id == self.output or device.name == self.output or self.output.lower() in device.name.lower():
                return device
        raise RuntimeError("saida nao encontrada: " + self.output)

    def loop(self):
        while not self.stop_event.is_set():
            try:
                microphone = self.find_mic()
                speaker = self.find_output()
                mic_channels = max(1, int(getattr(microphone, "channels", 1)))
                speaker_channels = max(1, int(getattr(speaker, "channels", 2)))
                with microphone.recorder(samplerate=48000, channels=mic_channels, blocksize=1024) as recorder:
                    with speaker.player(samplerate=48000, channels=speaker_channels, blocksize=1024) as player:
                        if not self.ready_sent:
                            out({"ok": True, "event": "ready", "microphone": microphone.name, "output": speaker.name})
                            self.ready_sent = True
                        self.error_sent = False
                        while not self.stop_event.is_set() and not self.restart_event.is_set():
                            data = recorder.record(numframes=1024).copy()
                            if getattr(data, "size", 0) == 0:
                                continue
                            with self.lock:
                                muted = self.muted
                                volume = self.volume
                            if muted:
                                data[:] = 0
                            elif volume != 100:
                                data = data * (volume / 100.0)
                            data = data.clip(-1.0, 1.0)
                            player.play(data)
            except Exception as exc:
                # Reabre a captura em caso de falha transitória sem encher o
                # canal de comandos com mensagens que o aplicativo não pediu.
                if not self.error_sent:
                    out({"ok": False, "error": str(exc)})
                    self.error_sent = True
                log_error("Falha na captura/reproducao: " + repr(exc))
                self.stop_event.wait(0.5)
            self.restart_event.clear()

    def start(self):
        self.thread.start()

    def set_output(self, output):
        with self.lock:
            self.output = output
        self.restart_event.set()

    def stop(self):
        self.stop_event.set()
        self.restart_event.set()
        self.thread.join(timeout=2)


def main():
    if len(sys.argv) >= 2 and sys.argv[1] in ("--list-devices", "--list-inputs"):
        try:
            if sys.argv[1] == "--list-inputs":
                devices = [{"id": d.id, "name": d.name} for d in sc.all_microphones(include_loopback=False)]
            else:
                devices = [{"id": d.id, "name": d.name} for d in sc.all_speakers()]
            out({"ok": True, "devices": devices})
            return 0
        except Exception as exc:
            out({"ok": False, "error": str(exc)})
            return 1
    if len(sys.argv) < 2:
        out({"ok": False, "error": "microfone nao informado"})
        return 1
    router = Router(sys.argv[1], sys.argv[2] if len(sys.argv) > 2 else "", number(sys.argv[3]) if len(sys.argv) > 3 else 70, len(sys.argv) > 4 and sys.argv[4].lower() == "true")
    router.start()
    for line in sys.stdin:
        if not line.strip():
            continue
        try:
            command = json.loads(line)
            name = command.get("command", "")
            if name == "set-device":
                router.set_output(str(command.get("id", "")))
                out({"ok": True})
            elif name == "set-volume":
                with router.lock:
                    router.volume = max(0, min(200, number(command.get("volume", router.volume))))
                out({"ok": True})
            elif name == "set-muted":
                with router.lock:
                    router.muted = bool(command.get("muted", router.muted))
                out({"ok": True})
            elif name == "stop":
                router.stop()
                break
        except Exception as exc:
            out({"ok": False, "error": str(exc)})
    return 0


if __name__ == "__main__":
    sys.exit(main())
