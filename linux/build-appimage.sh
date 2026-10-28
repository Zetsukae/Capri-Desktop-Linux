#!/bin/sh
set -eu

ROOT=$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)
BUILD_DIR="$ROOT/build"
ARCH=$(uname -m)
TOOL=${APPIMAGETOOL:-appimagetool}

case "$ARCH" in
    x86_64|aarch64) ;;
    *)
        printf 'Unsupported AppImage architecture: %s\n' "$ARCH" >&2
        exit 1
        ;;
esac

if ! command -v "$TOOL" >/dev/null 2>&1 && [ ! -x "$TOOL" ]; then
    printf 'appimagetool was not found. Install it or set APPIMAGETOOL to its path.\n' >&2
    exit 1
fi

OUTPUT=${1:-"$BUILD_DIR/Capri-Desktop-Linux-$ARCH.AppImage"}
OUTPUT_DIR=$(CDPATH= cd -- "$(dirname -- "$OUTPUT")" 2>/dev/null && pwd) || {
    mkdir -p "$(dirname -- "$OUTPUT")"
    OUTPUT_DIR=$(CDPATH= cd -- "$(dirname -- "$OUTPUT")" && pwd)
}
OUTPUT="$OUTPUT_DIR/$(basename -- "$OUTPUT")"
EMBEDDED_APPIMAGE="$BUILD_DIR/Capri-Desktop-Linux-$ARCH.payload.elf"
mkdir -p "$BUILD_DIR"
APPDIR=$(mktemp -d "$BUILD_DIR/Capri-AppDir.XXXXXX")
trap 'rm -rf "$APPDIR"; rm -f "$EMBEDDED_APPIMAGE"' EXIT HUP INT TERM

mkdir -p \
    "$APPDIR/usr/bin" \
    "$APPDIR/usr/share/applications" \
    "$APPDIR/usr/share/icons/hicolor/512x512/apps" \
    "$APPDIR/usr/share/capri/linux"

cp "$ROOT/linux/capri.py" "$APPDIR/usr/share/capri/linux/capri.py"
cp -R "$ROOT/assets" "$APPDIR/usr/share/capri/assets"
cp "$ROOT/linux/capri.desktop" "$APPDIR/usr/share/applications/capri.desktop"
cp "$ROOT/assets/capri.png" \
    "$APPDIR/usr/share/icons/hicolor/512x512/apps/io.github.zetsukae.CapriDesktopLinux.png"
cp "$ROOT/linux/AppRun" "$APPDIR/AppRun"
chmod 755 "$APPDIR/AppRun"
ln -s usr/share/applications/capri.desktop "$APPDIR/capri.desktop"
ln -s usr/share/icons/hicolor/512x512/apps/io.github.zetsukae.CapriDesktopLinux.png \
    "$APPDIR/io.github.zetsukae.CapriDesktopLinux.png"

ARCH="$ARCH" APPIMAGE_EXTRACT_AND_RUN=1 "$TOOL" "$APPDIR" "$EMBEDDED_APPIMAGE"

cat > "$OUTPUT" <<'EOF'
#!/bin/sh
set -eu
CAPRI_TMPDIR=$(mktemp -d "${TMPDIR:-/tmp}/capri-appimage.XXXXXX")
trap 'rm -rf "$CAPRI_TMPDIR"' EXIT HUP INT TERM
base64 -d > "$CAPRI_TMPDIR/capri.AppImage" <<'CAPRI_APPIMAGE_PAYLOAD'
EOF
base64 "$EMBEDDED_APPIMAGE" >> "$OUTPUT"
cat >> "$OUTPUT" <<'EOF'
CAPRI_APPIMAGE_PAYLOAD
chmod 755 "$CAPRI_TMPDIR/capri.AppImage"
APPIMAGE_EXTRACT_AND_RUN=1 "$CAPRI_TMPDIR/capri.AppImage" "$@"
EOF
chmod 755 "$OUTPUT"
printf 'Created %s\n' "$OUTPUT"
printf 'The AppImage is self-contained and launches without FUSE 2.\n'
