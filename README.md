<p align="center">
  <img src="assets/chip-source.png" alt="Chip" width="40%" align="middle">
  <img src="assets/capri.png" alt="Capri" width="40%" align="middle">
</p>

# Capri

A small desktop companion for Windows: Capri, from *Cyberpunk: Edgerunners*, lives on your screen. She sits there, stretches, yawns, falls asleep, peeks in from the edge, sits on your windows, gets head pats, plays tricks on your screen, and talks to you through a model that runs on your own computer.

Beware that Capri may try to take over your human CPU. No responsibility is taken for her actions.

Everything runs locally. There is no account, no telemetry, and nothing is sent anywhere except to a model server on your own machine.

<p align="center">
  <img src="docs/sit.png" alt="Capri sitting" height="220">
  <img src="docs/pat.png" alt="Capri getting a head pat" height="220">
</p>
<p align="center">
  <img src="docs/hack-note.png" alt="A hacked note" width="70%">
</p>

## Download

Get `Capri-Setup.exe` from the [Releases](../../releases) page and run it. Windows may show a SmartScreen warning because the file is not code-signed; the source is all here, and you can build it yourself (below).

## What she does

**On your desktop**
- Idle reactions: stretching, looking around, yawning, falling asleep, peeking from the screen edge. Each can be switched off.
- Head pats: click her.
- She sits on the top edge of your windows and follows them when they move. Drop her on one to seat her.
- Steps aside while a full-screen game or video is in front, and comes back after.
- Alarms and a study timer, with a chime (or your own voice clips).
- A tray menu for everything, in a black, crimson and green terminal style.

**Mischief** (off by default; Ctrl+Alt+C stops it at once)
- Screen glitches in six styles: bars, tear, corruption, hex rain, a fake "ENGRAM SYNC" progress box, and a red cursor that chases yours. All of them are painted on a click-through overlay and stay gentle: low contrast, nothing flashes, and reduced motion in Windows turns them off.
- "You've been hacked" notes.

**Talk to her** (needs a local model, see below)
- A chat window in her style (Ctrl+Alt+T), or just talk: click the mic beside her, or press Ctrl+Alt+V, and she answers in a speech bubble without opening a window.
- Speech recognition uses Whisper if you install it, otherwise the recognizer built into Windows.
- She knows what music or video is playing and comments on it.
- **Look**: press Ctrl+Alt+L or ask "what am I looking at?". She takes one screenshot, sends it to your local vision model, and throws it away. It is only taken when you ask, held in memory only, and never written to disk or sent off the machine. You can switch it off in Chat, "Let her look when I ask".

## Hotkeys

| Keys | What it does |
| --- | --- |
| Ctrl+Alt+C | Hop off the cursor, and stop any glitch |
| Ctrl+Alt+T | Open the chat window |
| Ctrl+Alt+V | Talk to her (click again to stop, or press DONE to send early) |
| Ctrl+Alt+L | Look at my screen |

## Setting up her brain

Chat, voice and Look use a model server on your own computer. [Ollama](https://ollama.com) is the easiest.

1. Install Ollama and pull a model. A vision-capable one (for example `qwen3.5:9b`) also lets her look at your screen. A text-only model works for chat.
2. In her tray menu choose Chat, then Use a local model. She finds Ollama or LM Studio on the usual local ports. Only `localhost` and loopback addresses are accepted.
3. Optional: put a `persona.txt` in `%LocalAppData%\Capri` to change how she talks.

### Better ears (optional)

Install [whisper.cpp](https://github.com/ggml-org/whisper.cpp) and put it here:

```
%LocalAppData%\Capri\speech\bin\Release\whisper-cli.exe
%LocalAppData%\Capri\speech\models\ggml-small.en.bin
```

She uses it automatically when it is there (Chat menu shows which ears are active). `base.en` is faster and less accurate.

### Voice lines (optional)

Nothing is bundled. Put `.wav` clips in `%LocalAppData%\Capri\voices` named for an occasion (`pat.wav`, `hello-2.wav`, ...). A `README.txt` is written into that folder with the full list of names.

## Build it yourself

Windows with the .NET Framework 4 (already part of Windows 10 and 11). No other tools or packages.

```
build.bat
```

This builds `build\Capri.exe`, then `build\Capri-Setup.exe`, which contains `Capri.exe`. Quit Capri first if she is running from the build folder.

The art is embedded from `assets\art`. `tools\make-art.ps1` rebuilds those trimmed sprites from full-size frames, which are not kept in this repository (put them in `assets\originals` if you have them).

Her smoke test is built in: `Capri.exe --smoke-test <folder>` runs the checks and saves screenshots to the folder.

## Disclaimer

This is an unofficial fan project, not affiliated with or endorsed by CD PROJEKT RED or Studio Trigger, and it is not for sale. Capri, Chip, and all artwork and sprites used here belong to CD PROJEKT RED and Studio Trigger (*Cyberpunk: Edgerunners*). If you are a rights holder and want something changed or removed, open an issue and it will be dealt with.

## Credits

- Started from [lachydotmcg/capri-desktop](https://github.com/lachydotmcg/capri-desktop), which brought Capri to the desktop first. Inspired by jpartnma_'s TikTok video.
- Code added since then is under the [MIT license](LICENSE); the original code remains under its author's terms.
