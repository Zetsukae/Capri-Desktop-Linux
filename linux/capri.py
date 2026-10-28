#!/usr/bin/env python3
"""GTK 4 Linux companion shell for Capri."""

import json
import ipaddress
import math
import os
import random
import threading
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

import gi

gi.require_version("Gdk", "4.0")
gi.require_version("Gtk", "4.0")
from gi.repository import Gdk, Gio, GLib, Gtk, Pango


ROOT = Path(__file__).resolve().parent.parent
CONFIG_DIR = Path(os.environ.get("XDG_CONFIG_HOME", Path.home() / ".config")) / "capri"
SETTINGS_FILE = CONFIG_DIR / "settings.json"
DEFAULT_PERSONA = (
    "You are Capri, a cheerful, mischievous desktop companion. "
    "Reply in one or two short, plain sentences."
)
DEFAULT_SETTINGS = {
    "size": 280,
    "motion": True,
    "message_timeout": 5,
    "background_blur": False,
    "background_transparency": 100,
    "llm_mode": "local",
    "llm_url": "http://localhost:11434",
    "llm_model": "llama3.1",
    "llm_backend": "ollama",
    "api_key": "",
    "persona": DEFAULT_PERSONA,
}
IDLE_POSES = ("capri.png", "art/arms.png", "art/lean.png", "art/sit.png")
MAX_HISTORY_MESSAGES = 20
CAPRI_CSS = """
window#capri {
  background-color: transparent;
  background-image: none;
  box-shadow: none;
}
window#capri.background {
  background-color: transparent;
  background-image: none;
  box-shadow: none;
}
window#capri > box,
window#capri .capri-stage {
  background-color: transparent;
  background-image: none;
}
window.capri-chat {
  background-color: #07080d;
  color: #d6e0ec;
}
.capri-panel {
  background-color: #0d0f16;
  color: #d6e0ec;
}
.capri-heading {
  color: #ff1446;
  font-weight: bold;
  letter-spacing: 0.08em;
}
.capri-subtle {
  color: #606a7a;
}
.capri-transcript {
  background-color: #0d0f16;
  color: #d6e0ec;
  font-family: monospace;
  padding: 10px;
}
.capri-entry {
  background-color: #0d0f16;
  color: #00ff6e;
  border: 1px solid #28303e;
  padding: 10px;
}
button.capri-send {
  background-color: rgba(116, 128, 150, 0.18);
  background-image: none;
  color: #e7edf7;
  border: 1px solid rgba(160, 176, 206, 0.6);
  border-radius: 6px;
  box-shadow: none;
  font-weight: 600;
  padding: 5px 12px;
}
button.capri-send:hover {
  background-color: rgba(148, 169, 201, 0.24);
  background-image: none;
  border-color: rgba(190, 206, 233, 0.9);
  color: #ffffff;
}
button.capri-send:active {
  background-color: rgba(106, 122, 146, 0.32);
  border-color: rgba(145, 166, 204, 0.9);
}
.capri-bubble {
  background-color: rgba(7, 8, 13, 0.94);
  color: #00ff6e;
  border: 1px solid #00ff6e;
  border-radius: 6px;
  padding: 5px 7px;
  margin: 4px;
}
.capri-speaking-face {
  border: 1px solid #00ff6e;
  margin: 4px;
}
"""


def install_theme():
    display = Gdk.Display.get_default()
    if display is None:
        return
    provider = Gtk.CssProvider()
    provider.load_from_data(CAPRI_CSS.encode("utf-8"))
    Gtk.StyleContext.add_provider_for_display(
        display, provider, Gtk.STYLE_PROVIDER_PRIORITY_USER
    )


def load_settings(path=SETTINGS_FILE):
    settings = DEFAULT_SETTINGS.copy()
    try:
        saved = json.loads(Path(path).read_text(encoding="utf-8"))
        if isinstance(saved, dict):
            size = saved.get("size", settings["size"])
            if isinstance(size, int) and not isinstance(size, bool):
                settings["size"] = max(160, min(420, size))
            if isinstance(saved.get("motion"), bool):
                settings["motion"] = saved["motion"]
            message_timeout = saved.get("message_timeout")
            if isinstance(message_timeout, int) and not isinstance(message_timeout, bool):
                settings["message_timeout"] = max(1, min(30, message_timeout))
            if isinstance(saved.get("background_blur"), bool):
                settings["background_blur"] = saved["background_blur"]
            transparency = saved.get("background_transparency")
            if isinstance(transparency, int) and not isinstance(transparency, bool):
                settings["background_transparency"] = max(0, min(100, transparency))
            settings["llm_mode"] = "cloud" if saved.get("llm_mode") == "cloud" else "local"
            llm_url = saved.get("llm_url")
            if isinstance(llm_url, str) and llm_url.strip():
                settings["llm_url"] = llm_url.strip()
            llm_model = saved.get("llm_model")
            if isinstance(llm_model, str) and llm_model.strip():
                settings["llm_model"] = llm_model.strip()
            backend = saved.get("llm_backend")
            if backend in ("ollama", "openai"):
                settings["llm_backend"] = backend
            api_key = saved.get("api_key")
            if isinstance(api_key, str):
                settings["api_key"] = api_key
            persona = saved.get("persona")
            if isinstance(persona, str) and persona.strip():
                settings["persona"] = persona.strip()
    except FileNotFoundError:
        pass
    except (OSError, json.JSONDecodeError) as error:
        print(f"Could not read Capri settings at {path}: {error}", file=os.sys.stderr)
    return settings


def _is_loopback(url):
    try:
        parsed = urllib.parse.urlparse(url)
        if parsed.scheme not in ("http", "https"):
            return False
        host = parsed.hostname or ""
        if not host:
            return False
        if host.lower() == "localhost":
            return True
        try:
            return ipaddress.ip_address(host).is_loopback
        except ValueError:
            return False
    except ValueError:
        return False


def _read_json(url, timeout=3):
    request = urllib.request.Request(url, headers={"Accept": "application/json"})
    with urllib.request.urlopen(request, timeout=timeout) as response:
        return json.loads(response.read().decode("utf-8"))


def _discover_endpoint_models(base_url):
    if not _is_loopback(base_url):
        return None
    base_url = base_url.rstrip("/")
    try:
        result = _read_json(base_url + "/api/tags")
        models = [item.get("name") for item in result.get("models", []) if item.get("name")]
        if models:
            return {"url": base_url, "backend": "ollama", "models": models}
    except (OSError, ValueError, urllib.error.URLError):
        pass
    try:
        result = _read_json(base_url + "/v1/models")
        models = [item.get("id") for item in result.get("data", []) if item.get("id")]
        if models:
            return {"url": base_url, "backend": "openai", "models": models}
    except (OSError, ValueError, urllib.error.URLError):
        pass
    return None


def discover_local_models(configured_url):
    candidates = []
    for url in (configured_url, "http://localhost:11434", "http://localhost:1234"):
        if url and _is_loopback(url) and url.rstrip("/") not in candidates:
            candidates.append(url.rstrip("/"))
    for url in candidates:
        result = _discover_endpoint_models(url)
        if result:
            return result
    return None


class CapriWindow(Gtk.ApplicationWindow):
    def __init__(self, application):
        super().__init__(application=application, title="Capri")
        self.set_name("capri")
        self.add_css_class("capri-companion")
        self.settings = load_settings()
        self.pose_index = 0
        self.pose_timeout = None
        self.blink_timeout = None
        self.chat_window = None
        self.discovering_models = False
        self.status_timeout = None
        self.drag_moved = False
        self.chat_busy = False
        self.conversation = []
        self.talk_timeout = None
        self.type_timeout = None
        self.speech_finish_timeout = None
        self.speaking_text = ""
        self.speech_started = 0.0
        self.talk_duration = 0.0
        self.speech_duration = 0.0

        self.set_default_size(320, 400)
        self.set_resizable(False)
        self.set_decorated(False)
        self.background_provider = Gtk.CssProvider()
        Gtk.StyleContext.add_provider_for_display(
            Gdk.Display.get_default(),
            self.background_provider,
            Gtk.STYLE_PROVIDER_PRIORITY_USER,
        )

        content = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=12)
        content.add_css_class("capri-companion-content")
        self.set_child(content)

        self.stage = Gtk.Overlay()
        self.stage.add_css_class("capri-stage")
        self.stage.set_hexpand(True)
        self.stage.set_vexpand(True)
        content.append(self.stage)

        self.picture = Gtk.Picture()
        self.picture.set_can_shrink(True)
        self.picture.set_content_fit(Gtk.ContentFit.CONTAIN)
        self.picture.set_size_request(320, 340)
        self.picture.set_hexpand(True)
        self.picture.set_vexpand(True)
        self.picture.set_halign(Gtk.Align.CENTER)
        self.picture.set_valign(Gtk.Align.CENTER)
        self.stage.set_child(self.picture)

        self.speech_bubble = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=6)
        self.speech_bubble.add_css_class("capri-bubble")
        self.speech_bubble.set_halign(Gtk.Align.CENTER)
        self.speech_bubble.set_valign(Gtk.Align.END)
        self.speech_bubble.set_visible(False)
        self.speech_bubble.set_can_target(False)
        self.stage.add_overlay(self.speech_bubble)

        self.talk_face = Gtk.Picture()
        self.talk_face.add_css_class("capri-speaking-face")
        self.talk_face.set_content_fit(Gtk.ContentFit.COVER)
        self.talk_face.set_size_request(104, 58)
        self.talk_face.set_visible(False)
        self.speech_bubble.append(self.talk_face)

        self.status = Gtk.Label()
        self.status.set_wrap(True)
        self.status.set_max_width_chars(32)
        self.speech_bubble.append(self.status)

        chat_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        chat_row.set_margin_start(10)
        chat_row.set_margin_end(10)
        chat_row.set_margin_bottom(10)
        self.inline_entry = Gtk.Entry()
        self.inline_entry.add_css_class("capri-entry")
        self.inline_entry.set_placeholder_text("Talk to Capri…")
        self.inline_entry.set_hexpand(True)
        self.inline_entry.connect(
            "activate", lambda entry: self._send_chat(entry.get_text(), entry)
        )
        chat_row.append(self.inline_entry)

        self.inline_send = Gtk.Button(label="SEND")
        self.inline_send.add_css_class("capri-send")
        self.inline_send.connect(
            "clicked",
            lambda _button: self._send_chat(
                self.inline_entry.get_text(), self.inline_entry
            ),
        )
        chat_row.append(self.inline_send)
        content.append(chat_row)

        self.inline_status = Gtk.Label(label="Type here to chat · Right-click for settings")
        self.inline_status.add_css_class("capri-subtle")
        self.inline_status.set_ellipsize(Pango.EllipsizeMode.END)
        self.inline_status.set_margin_bottom(6)
        content.append(self.inline_status)

        self.settings_popover = Gtk.Popover()
        self.settings_popover.set_child(self._build_settings())
        self.settings_popover.set_parent(self.picture)
        self.picture.set_tooltip_text("Left-click to pat Capri · Right-click for settings · Drag to move")

        click = Gtk.GestureClick()
        click.connect("released", self._pat)
        self.picture.add_controller(click)

        context_click = Gtk.GestureClick()
        context_click.set_button(Gdk.BUTTON_SECONDARY)
        context_click.connect("released", self._show_context_menu)
        self.picture.add_controller(context_click)

        drag = Gtk.GestureDrag()
        drag.connect("drag-begin", self._begin_window_move)
        drag.connect("drag-update", self._update_window_move)
        self.picture.add_controller(drag)

        self._apply_size(self.settings["size"])
        self._apply_background_effect()
        self._set_pose(IDLE_POSES[0])
        self._sync_motion()
        if self.settings["llm_mode"] == "local":
            self._show_status("Checking for local models…", 30)
            self._detect_models(auto=True)

        if self.settings["motion"]:
            self.pose_timeout = GLib.timeout_add_seconds(8, self._next_pose)
            self.blink_timeout = GLib.timeout_add(70, self._breathe)

        self.connect("close-request", self._on_close)

    def _build_settings(self):
        panel = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
        panel.set_margin_top(14)
        panel.set_margin_bottom(14)
        panel.set_margin_start(14)
        panel.set_margin_end(14)
        panel.set_size_request(320, -1)

        heading = Gtk.Label(label="CAPRI // SYSTEM", xalign=0)
        heading.add_css_class("capri-heading")
        panel.append(heading)

        panel.append(Gtk.Label(label="Character size", xalign=0))
        size = Gtk.Scale.new_with_range(Gtk.Orientation.HORIZONTAL, 160, 420, 10)
        size.set_digits(0)
        size.set_value(self.settings["size"])
        size.set_draw_value(True)
        size.connect("value-changed", self._size_changed)
        panel.append(size)

        motion = Gtk.CheckButton(label="Idle movement")
        motion.set_active(self.settings["motion"])
        motion.connect("toggled", self._motion_changed)
        panel.append(motion)

        self.background_blur = Gtk.CheckButton(
            label="Enable background blur (if supported)"
        )
        self.background_blur.set_active(self.settings["background_blur"])
        self.background_blur.connect("toggled", self._background_blur_changed)
        panel.append(self.background_blur)

        panel.append(Gtk.Label(label="Background transparency (%)", xalign=0))
        self.background_transparency = Gtk.Scale.new_with_range(
            Gtk.Orientation.HORIZONTAL, 0, 100, 5
        )
        self.background_transparency.set_digits(0)
        self.background_transparency.set_value(
            self.settings["background_transparency"]
        )
        self.background_transparency.set_draw_value(True)
        self.background_transparency.set_sensitive(
            self.settings["background_blur"]
        )
        self.background_transparency.connect(
            "value-changed", self._background_transparency_changed
        )
        panel.append(self.background_transparency)

        blur_note = Gtk.Label(
            label="Blur is controlled by the compositor. Transparency creates the translucent area it may blur; 100% is clear.",
            wrap=True,
            xalign=0,
        )
        blur_note.add_css_class("capri-subtle")
        panel.append(blur_note)

        panel.append(Gtk.Label(label="Message bubble timeout (seconds)", xalign=0))
        message_timeout = Gtk.Scale.new_with_range(
            Gtk.Orientation.HORIZONTAL, 1, 30, 1
        )
        message_timeout.set_digits(0)
        message_timeout.set_value(self.settings["message_timeout"])
        message_timeout.set_draw_value(True)
        message_timeout.connect("value-changed", self._message_timeout_changed)
        panel.append(message_timeout)

        self.ai_mode = Gtk.CheckButton(label="Use OpenAI-compatible cloud API instead of local model")
        self.ai_mode.set_active(self.settings["llm_mode"] == "cloud")
        self.ai_mode.connect("toggled", self._ai_mode_changed)
        panel.append(self.ai_mode)

        panel.append(Gtk.Label(label="Model URL", xalign=0))
        self.ai_url = Gtk.Entry()
        self.ai_url.set_text(self.settings["llm_url"])
        self.ai_url.set_placeholder_text("http://localhost:11434 or https://api.provider/v1")
        panel.append(self.ai_url)

        panel.append(Gtk.Label(label="Model name", xalign=0))
        self.ai_model = Gtk.Entry()
        self.ai_model.set_text(self.settings["llm_model"])
        self.ai_model.set_placeholder_text("llama3.1 or gpt-4o-mini")
        panel.append(self.ai_model)

        detect_button = Gtk.Button(label="Detect local models")
        detect_button.connect("clicked", self._detect_models_clicked)
        panel.append(detect_button)

        panel.append(Gtk.Label(label="API key (optional)", xalign=0))
        self.ai_key = Gtk.Entry()
        self.ai_key.set_text(self.settings["api_key"])
        self.ai_key.set_placeholder_text("Optional for local models; required for some cloud providers")
        self.ai_key.set_visibility(False)
        panel.append(self.ai_key)

        action_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        save_button = Gtk.Button(label="Save")
        save_button.connect("clicked", self._save_ai_settings)
        action_row.append(save_button)
        panel.append(action_row)

        note = Gtk.Label(
            label="Left-click to pat · Right-click for this menu · Drag Capri to move her. Wayland controls desktop positioning and overlays.",
            wrap=True,
            xalign=0,
        )
        note.add_css_class("capri-subtle")
        panel.append(note)
        return panel

    def _show_context_menu(self, _gesture, _presses, x, y):
        bounds = Gdk.Rectangle()
        bounds.x = int(x)
        bounds.y = int(y)
        bounds.width = 1
        bounds.height = 1
        self.settings_popover.set_pointing_to(bounds)
        self.settings_popover.popup()

    def _begin_window_move(self, gesture, x, y):
        self.drag_moved = False
        event = gesture.get_last_event()
        surface = self.get_surface()
        if event is None or not isinstance(surface, Gdk.Toplevel):
            return
        device = event.get_device()
        if device is not None:
            surface.begin_move(device, Gdk.BUTTON_PRIMARY, x, y, event.get_time())

    def _update_window_move(self, _gesture, offset_x, offset_y):
        if abs(offset_x) > 4 or abs(offset_y) > 4:
            self.drag_moved = True

    def _show_status(self, text, duration=5):
        self._stop_speech()
        if self.status_timeout is not None:
            GLib.source_remove(self.status_timeout)
            self.status_timeout = None
        self.status.set_text(text)
        self.talk_face.set_visible(False)
        self.speech_bubble.set_visible(True)
        self.status_timeout = GLib.timeout_add_seconds(duration, self._hide_status)

    def _hide_status(self):
        self.status_timeout = None
        self.speech_bubble.set_visible(False)
        return GLib.SOURCE_REMOVE

    def _start_talking(self, text):
        self._stop_speech()
        if self.talk_timeout is not None:
            GLib.source_remove(self.talk_timeout)
        if self.status_timeout is not None:
            GLib.source_remove(self.status_timeout)
            self.status_timeout = None
        self.speaking_text = text
        self.speech_started = GLib.get_monotonic_time() / 1_000_000
        self.speech_duration = len(text) / 32.0
        self.talk_duration = max(0.5 + len(text) * 0.045, self.speech_duration)
        self.status.set_text("")
        self.picture.set_visible(False)
        self.speech_bubble.set_visible(True)
        self.talk_face.set_visible(True)
        self._animate_talking()
        self._type_speech()
        if self.talk_duration > 0:
            self.talk_timeout = GLib.timeout_add(110, self._animate_talking)
        if self.speech_duration > 0:
            self.type_timeout = GLib.timeout_add(30, self._type_speech)

    def _animate_talking(self):
        age = GLib.get_monotonic_time() / 1_000_000 - self.speech_started
        if age >= self.talk_duration:
            self.talk_timeout = None
            self._set_talk_face("talk-closed.png")
            return GLib.SOURCE_REMOVE
        is_open = int(age / 0.11) * 7 % 5 < 3
        frame = "talk-open.png" if is_open else "talk-closed.png"
        self._set_talk_face(frame)
        return GLib.SOURCE_CONTINUE

    def _type_speech(self):
        elapsed = GLib.get_monotonic_time() / 1_000_000 - self.speech_started
        count = min(len(self.speaking_text), int(elapsed * 32))
        if count < len(self.speaking_text):
            cursor = "▌" if int(elapsed * 8) % 2 == 0 else ""
            self.status.set_text(self.speaking_text[:count] + cursor)
            return GLib.SOURCE_CONTINUE

        self.status.set_text(self.speaking_text)
        self.type_timeout = None
        remaining = max(self.talk_duration - elapsed, 0)
        self.speech_finish_timeout = GLib.timeout_add(
            max(1, int(remaining * 1000))
            + self.settings["message_timeout"] * 1000,
            self._finish_speech,
        )
        return GLib.SOURCE_REMOVE

    def _stop_speech(self):
        for attribute in ("talk_timeout", "type_timeout", "speech_finish_timeout"):
            source_id = getattr(self, attribute, None)
            if source_id is not None:
                GLib.source_remove(source_id)
                setattr(self, attribute, None)
        self.speaking_text = ""
        if hasattr(self, "picture"):
            self.picture.set_visible(True)
        if hasattr(self, "talk_face"):
            self.talk_face.set_visible(False)

    def _finish_speech(self):
        self.speech_finish_timeout = None
        self.speech_bubble.set_visible(False)
        self.talk_face.set_visible(False)
        self.picture.set_visible(True)
        return GLib.SOURCE_REMOVE

    def _set_talk_face(self, filename):
        image_path = ROOT / "assets" / "art" / filename
        try:
            self.talk_face.set_paintable(Gdk.Texture.new_from_filename(str(image_path)))
        except GLib.Error as error:
            print(f"Could not load Capri talk frame {image_path}: {error}", file=os.sys.stderr)
            self.talk_face.set_visible(False)

    def _save_settings(self):
        try:
            CONFIG_DIR.mkdir(parents=True, exist_ok=True)
            temporary = SETTINGS_FILE.with_suffix(".tmp")
            temporary.write_text(
                json.dumps(self.settings, indent=2) + "\n", encoding="utf-8"
            )
            temporary.replace(SETTINGS_FILE)
        except OSError as error:
            print(f"Could not save Capri settings at {SETTINGS_FILE}: {error}", file=os.sys.stderr)
            self._show_status("Could not save settings; check the terminal for details.")

    def _save_ai_settings(self, _button=None):
        raw_url = (self.ai_url.get_text() or "").strip()
        if not raw_url:
            raw_url = "http://localhost:11434" if self.ai_mode.get_active() is False else "https://api.openai.com/v1"
        if self.ai_mode.get_active():
            if not raw_url.startswith("http://") and not raw_url.startswith("https://"):
                self._show_status("Cloud model URLs must start with http:// or https://")
                return
        else:
            if not _is_loopback(raw_url):
                self._show_status("Local model URLs must point to this machine only (localhost or loopback).")
                return
        self.settings["llm_mode"] = "cloud" if self.ai_mode.get_active() else "local"
        self.settings["llm_url"] = raw_url
        self.settings["llm_model"] = (self.ai_model.get_text() or "").strip() or self.settings["llm_model"]
        self.settings["api_key"] = self.ai_key.get_text()
        self._save_settings()
        self._show_status("AI settings saved. Open the menu and choose Chat.")

    def _ai_mode_changed(self, control):
        self.settings["llm_mode"] = "cloud" if control.get_active() else "local"
        self._save_settings()
        if not control.get_active():
            self._detect_models(auto=True)

    def _detect_models_clicked(self, _button):
        if self.ai_mode.get_active():
            self._show_status("Switch to local mode to discover Ollama or LM Studio models.")
            return
        self._detect_models(auto=False)

    def _detect_models(self, auto):
        if self.discovering_models:
            return
        configured_url = self.ai_url.get_text().strip() or self.settings["llm_url"]
        if not _is_loopback(configured_url):
            if not auto:
                self._show_status("Local model URLs must point to localhost or a loopback address.")
            return

        self.discovering_models = True
        if not auto:
            self._show_status("Looking for local models…", 30)

        def worker():
            result = discover_local_models(configured_url)
            GLib.idle_add(self._models_discovered, result, auto)

        threading.Thread(target=worker, daemon=True).start()

    def _models_discovered(self, result, auto):
        self.discovering_models = False
        if result is None:
            self._show_status(
                "No local models found. Start Ollama or LM Studio, then try Detect local models."
            )
            return GLib.SOURCE_REMOVE

        self.settings["llm_url"] = result["url"]
        self.settings["llm_backend"] = result["backend"]
        self.ai_url.set_text(result["url"])
        current_model = self.ai_model.get_text().strip()
        model = current_model if current_model in result["models"] else result["models"][0]
        self.ai_model.set_text(model)
        self.settings["llm_model"] = model
        self._save_settings()
        self._show_status(
            f"Found {len(result['models'])} model(s): {', '.join(result['models'])}. "
            f"Selected {model}."
        )
        return GLib.SOURCE_REMOVE

    def _size_changed(self, scale):
        self.settings["size"] = int(scale.get_value())
        self._apply_size(self.settings["size"])
        self._save_settings()

    def _message_timeout_changed(self, scale):
        self.settings["message_timeout"] = int(scale.get_value())
        self._save_settings()

    def _background_blur_changed(self, control):
        self.settings["background_blur"] = control.get_active()
        self.background_transparency.set_sensitive(control.get_active())
        self._apply_background_effect()
        self._save_settings()

    def _background_transparency_changed(self, scale):
        self.settings["background_transparency"] = int(scale.get_value())
        self._apply_background_effect()
        self._save_settings()

    def _apply_background_effect(self):
        if self.settings["background_blur"]:
            self.add_css_class("capri-blur-enabled")
            opacity = 1 - self.settings["background_transparency"] / 100
            css = (
                "window#capri.background { "
                f"background-color: rgba(7, 8, 13, {opacity:.2f}); "
                "background-image: none; box-shadow: none; }"
            )
        else:
            self.remove_css_class("capri-blur-enabled")
            css = (
                "window#capri.background { "
                "background-color: transparent; "
                "background-image: none; box-shadow: none; }"
            )
        self.background_provider.load_from_data(css.encode("utf-8"))

    def _apply_size(self, size):
        companion_width = size + 32
        bubble_content_width = max(96, companion_width - 30)
        face_width = min(104, max(48, int(bubble_content_width * 0.36)))
        text_width = max(48, bubble_content_width - face_width - 6)
        self.talk_face.set_size_request(face_width, int(face_width * 58 / 104))
        self.status.set_size_request(text_width, -1)
        self.status.set_max_width_chars(max(8, text_width // 8))
        self.picture.set_size_request(companion_width, size + 72)
        self.set_default_size(companion_width, size + 132)

    def _motion_changed(self, control):
        self.settings["motion"] = control.get_active()
        self._sync_motion()
        self._save_settings()

    def _sync_motion(self):
        if self.settings["motion"]:
            if self.pose_timeout is None:
                self.pose_timeout = GLib.timeout_add_seconds(8, self._next_pose)
            if self.blink_timeout is None:
                self.blink_timeout = GLib.timeout_add(70, self._breathe)
        else:
            if self.pose_timeout is not None:
                GLib.source_remove(self.pose_timeout)
                self.pose_timeout = None
            if self.blink_timeout is not None:
                GLib.source_remove(self.blink_timeout)
                self.blink_timeout = None
            self.picture.set_opacity(1)
            self.pose_index = 0
            self._set_pose(IDLE_POSES[0])

    def _set_pose(self, relative_path):
        image_path = ROOT / "assets" / relative_path
        try:
            self.picture.set_paintable(Gdk.Texture.new_from_filename(str(image_path)))
        except GLib.Error as error:
            print(f"Could not load Capri artwork {image_path}: {error}", file=os.sys.stderr)
            self._show_status(f"Could not load artwork: {image_path.name}")

    def _next_pose(self):
        self.pose_index = (self.pose_index + 1) % len(IDLE_POSES)
        if self.talk_timeout is None:
            self._set_pose(IDLE_POSES[self.pose_index])
        return GLib.SOURCE_CONTINUE

    def _breathe(self):
        self.picture.set_opacity(0.94 + 0.06 * (0.5 + 0.5 * math.sin(GLib.get_monotonic_time() / 1_000_000)))
        return GLib.SOURCE_CONTINUE

    def _pat(self, _gesture, _presses, _x, _y):
        if self.drag_moved:
            self.drag_moved = False
            return
        self._set_pose("art/happy.png")
        self._show_status(random.choice(("Heh. Again.", "That tickles.", "Okay, okay.")), 2)
        GLib.timeout_add(1300, self._finish_pat)

    def _finish_pat(self):
        if self.talk_timeout is None:
            if self.settings["motion"]:
                self._set_pose(IDLE_POSES[self.pose_index])
            else:
                self._set_pose(IDLE_POSES[0])
            self._hide_status()
        return GLib.SOURCE_REMOVE

    def _request_model(self, payload, url, headers=None):
        final_headers = {"Content-Type": "application/json"}
        if headers:
            final_headers.update(headers)
        request = urllib.request.Request(url, data=json.dumps(payload).encode("utf-8"), headers=final_headers, method="POST")
        with urllib.request.urlopen(request, timeout=90) as response:
            response_text = response.read().decode("utf-8")
            return json.loads(response_text)

    def _ask_model(self, prompt, history):
        base_url = self.settings["llm_url"].rstrip("/")
        model = (self.settings["llm_model"] or "").strip() or "llama3.1"
        persona = (self.settings.get("persona") or DEFAULT_PERSONA).strip() or DEFAULT_PERSONA
        messages = [
            {"role": "system", "content": persona},
            *history,
            {"role": "user", "content": prompt},
        ]

        if self.settings["llm_mode"] == "cloud":
            endpoint = base_url.rstrip("/") + "/chat/completions"
            headers = {}
            if self.settings.get("api_key"):
                headers["Authorization"] = f"Bearer {self.settings['api_key']}"
            data = {"model": model, "messages": messages, "stream": False, "max_tokens": 220}
            try:
                result = self._request_model(data, endpoint, headers)
                choices = result.get("choices") or []
                if not choices:
                    return "No response came back from the model server."
                message = choices[0].get("message") or {}
                content = message.get("content") or ""
                if isinstance(content, list):
                    parts = []
                    for item in content:
                        if isinstance(item, dict):
                            text = item.get("text") or ""
                            if text:
                                parts.append(text)
                    content = "".join(parts)
                return str(content).strip() or "The model answered without text."
            except (OSError, ValueError, urllib.error.URLError, KeyError, TypeError) as error:
                return f"AI chat failed: {error}"

        try:
            if self.settings.get("llm_backend") == "openai":
                endpoint = base_url + "/v1/chat/completions"
                data = {"model": model, "messages": messages, "stream": False, "max_tokens": 220}
                result = self._request_model(data, endpoint)
                choices = result.get("choices") or []
                content = (choices[0].get("message") or {}).get("content") if choices else None
                if isinstance(content, list):
                    content = "".join(
                        item.get("text", "") for item in content if isinstance(item, dict)
                    )
            else:
                endpoint = base_url + "/api/chat"
                data = {"model": model, "messages": messages, "stream": False}
                result = self._request_model(data, endpoint)
                content = result.get("message", {}).get("content")
            if not content:
                return "The local model answered without usable content."
            return str(content).strip()
        except (OSError, ValueError, urllib.error.URLError, KeyError, TypeError) as error:
            return f"Local model failed: {error}"

    def _send_chat(self, prompt, source_entry=None):
        if not prompt or not prompt.strip():
            return
        if self.chat_busy:
            return
        if source_entry is None and self.chat_window is not None:
            source_entry = self.chat_window["entry"]
        transcript = self.chat_window["buffer"] if self.chat_window is not None else None
        user_prompt = prompt.strip()
        if transcript is not None:
            end = transcript.get_end_iter()
            transcript.insert(end, f"You: {user_prompt}\n", -1)
            self.chat_window["status"].set_text("Thinking...")
        if source_entry is not None:
            source_entry.set_text("")
        self.chat_busy = True
        self.inline_entry.set_sensitive(False)
        self.inline_send.set_sensitive(False)
        self.inline_status.set_text("Capri is thinking…")
        history = list(self.conversation)

        def worker():
            reply = self._ask_model(user_prompt, history)
            GLib.idle_add(self._render_chat_reply, user_prompt, reply, transcript)

        threading.Thread(target=worker, daemon=True).start()

    def _render_chat_reply(self, prompt, reply, transcript):
        self.chat_busy = False
        self.inline_entry.set_sensitive(True)
        self.inline_send.set_sensitive(True)
        self.inline_entry.grab_focus()
        self.inline_status.set_text("Type here to chat · Right-click for settings")
        if not reply.startswith(("AI chat failed:", "Local model failed:")) and reply not in (
            "No response came back from the model server.",
            "The local model answered without usable content.",
            "The model answered without text.",
        ):
            self.conversation.extend(
                ({"role": "user", "content": prompt}, {"role": "assistant", "content": reply})
            )
            self.conversation = self.conversation[-MAX_HISTORY_MESSAGES:]
        self._start_talking(reply)
        if transcript is not None:
            end = transcript.get_end_iter()
            transcript.insert(end, f"Capri: {reply}\n\n", -1)
        if self.chat_window is not None:
            self.chat_window["status"].set_text("Ready")
        return False

    def _open_chat_window(self, _button=None):
        if self.chat_window is not None:
            self.chat_window["window"].present()
            return

        window = Gtk.ApplicationWindow(
            application=self.get_application(), title="Capri Chat"
        )
        window.add_css_class("capri-chat")
        window.set_default_size(500, 360)
        window.set_transient_for(self)

        box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=10)
        box.add_css_class("capri-panel")
        box.set_margin_top(12)
        box.set_margin_bottom(12)
        box.set_margin_start(12)
        box.set_margin_end(12)
        window.set_child(box)

        text_view = Gtk.TextView()
        text_view.set_editable(False)
        text_view.set_wrap_mode(Gtk.WrapMode.WORD_CHAR)
        text_view.add_css_class("capri-transcript")
        buffer = text_view.get_buffer()
        buffer.set_text("Capri: Hello. Tell me what is on your mind.\n\n")
        for line in self.conversation:
            speaker = "You" if line["role"] == "user" else "Capri"
            end = buffer.get_end_iter()
            buffer.insert(end, f"{speaker}: {line['content']}\n", -1)
            if line["role"] == "assistant":
                end = buffer.get_end_iter()
                buffer.insert(end, "\n", -1)
        scroller = Gtk.ScrolledWindow()
        scroller.set_child(text_view)
        box.append(scroller)

        status = Gtk.Label(label="Ready")
        status.add_css_class("dim-label")
        box.append(status)

        entry_row = Gtk.Box(orientation=Gtk.Orientation.HORIZONTAL, spacing=8)
        entry = Gtk.Entry()
        entry.set_editable(True)
        entry.set_can_focus(True)
        entry.add_css_class("capri-entry")
        entry.set_placeholder_text("Ask Capri something…")
        entry.connect("activate", lambda _widget: self._send_chat(entry.get_text(), entry))
        entry_row.append(entry)

        send_button = Gtk.Button(label="Send")
        send_button.add_css_class("capri-send")
        send_button.connect("clicked", lambda _button: self._send_chat(entry.get_text(), entry))
        entry_row.append(send_button)
        box.append(entry_row)

        self.chat_window = {"window": window, "buffer": buffer, "entry": entry, "status": status}
        window.connect("close-request", self._close_chat_window)
        window.present()
        GLib.idle_add(self._focus_chat_entry, entry)

    def _focus_chat_entry(self, entry):
        entry.grab_focus()
        return GLib.SOURCE_REMOVE

    def _close_chat_window(self, window):
        self.chat_window = None
        window.destroy()
        return False

    def _on_close(self, _window):
        self._save_settings()
        return False


class CapriApplication(Gtk.Application):
    def __init__(self):
        super().__init__(application_id="io.github.zetsukae.CapriDesktopLinux",
                         flags=Gio.ApplicationFlags.FLAGS_NONE)
        install_theme()
        self.connect("activate", self._activate)

    def _activate(self, application):
        window = application.get_active_window()
        if window is None:
            window = CapriWindow(application)
        window.present()


def main():
    return CapriApplication().run(None)


if __name__ == "__main__":
    raise SystemExit(main())
