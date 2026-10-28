<p align="center">
  <img src="assets/chip-source.png" alt="Chip" width="40%" align="middle">
  <img src="assets/capri.png" alt="Capri" width="40%" align="middle">
</p>

# Capri Desktop for Linux

An unofficial Linux desktop companion featuring Capri from *Cyberpunk: Edgerunners*. The app is a native GTK 4 application with an animated character, direct AI chat, and a transparent character-first interface.

The Linux companion is maintained by Zetsukae. Capri can move around your desktop, react to clicks, and chat using a local model or an OpenAI-compatible cloud API.

<p align="center">
  <img src="docs/sit.png" alt="Capri sitting" height="220">
  <img src="docs/pat.png" alt="Capri getting a head pat" height="220">
</p>

## Download and launch

Download the `Capri-Desktop-Linux-<architecture>.AppImage` for your system from the [Releases](../../releases) page. The project supports `x86_64` and `aarch64` AppImages.

Make the file executable if needed, then launch it:

```sh
chmod +x Capri-Desktop-Linux-*.AppImage
./Capri-Desktop-Linux-*.AppImage
```

The AppImage does not require FUSE 2 (`libfuse.so.2`), but it does use the system's Python 3, PyGObject, and GTK 4 installation. Install those runtime dependencies if Capri reports that GTK is missing:

```sh
# Fedora
sudo dnf install python3 python3-gobject gtk4

# Debian or Ubuntu
sudo apt install python3 python3-gi gir1.2-gtk-4.0
```

## Features

- Animated idle poses and optional idle movement.
- Left-click Capri to give her a pat, drag her to move the window, and right-click for settings.
- Transparent character-first window by default, with controls for background transparency and compositor-supported blur.
- Adjustable character size and speech-bubble display time.
- Direct chat from the main window. Replies appear in an animated, typewriter-style speech bubble.
- Conversation context is kept in memory while the app is running.
- Automatically checks local Ollama and LM Studio endpoints and can detect available models.
- Optional chat with an OpenAI-compatible cloud API.

Desktop placement and background effects depend on the desktop environment and compositor. In particular, blur is not available on every system. Settings are saved in `~/.config/capri/settings.json` (or `$XDG_CONFIG_HOME/capri/settings.json`).

## Set up AI chat

### Local model

1. Install and start [Ollama](https://ollama.com) or LM Studio, then download a chat model in that application.
2. Launch Capri and right-click her to open settings. Capri checks common local endpoints on startup; choose **Detect local models** to scan again.
3. Select or enter the model name and save the settings.

Local model connections are restricted to this machine's loopback addresses.

### Cloud API (optional)

In Capri's right-click settings, enable the OpenAI-compatible cloud API option, enter the provider's API URL, model name, and API key, then choose **Save**. Providers may bill API usage separately; a subscription to a chat service is not necessarily an API key. When cloud mode is enabled, chat messages are sent to the configured provider.

## Build from source

Install Python 3 and GTK 4 with the Python bindings:

```sh
# Fedora
sudo dnf install python3 python3-gobject gtk4

# Debian or Ubuntu
sudo apt install python3 python3-gi gir1.2-gtk-4.0
```

Run the app from the repository directory:

```sh
./linux/run.sh
```

### Build an AppImage

Install `mksquashfs` and `appimagetool` for your architecture, then run:

```sh
./linux/build-appimage.sh
```

The output is `build/Capri-Desktop-Linux-<architecture>.AppImage`. You can pass a different output path as the first argument, or select an `appimagetool` executable with `APPIMAGETOOL=/path/to/appimagetool`. The generated AppImage includes Capri and her artwork; Python 3, PyGObject, and GTK 4 are still required at runtime.

## Disclaimer

This is an unofficial fan project, not affiliated with or endorsed by CD PROJEKT RED or Studio Trigger, and it is not for sale. Capri, Chip, and all artwork and sprites used here belong to CD PROJEKT RED and Studio Trigger (*Cyberpunk: Edgerunners*). If you are a rights holder and want something changed or removed, open an issue and it will be dealt with.

## Credits

- Started from [lachydotmcg/capri-desktop](https://github.com/lachydotmcg/capri-desktop), which brought Capri to the desktop first. Inspired by jpartnma_'s TikTok video. Based on lachydotmcg's 0.2.0 release.
- Code added since then is under the [MIT license](LICENSE); the original code remains under its author's terms.
