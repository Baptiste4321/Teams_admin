#!/bin/bash
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

echo "=== 1. Génération de l'icône maître 1024x1024 ==="
swift scripts/generate_icon.swift "$REPO_ROOT/apple/AppIcon_1024.png"
mkdir -p "$REPO_ROOT/src/MeetingCapture.UI/Resources"
cp "$REPO_ROOT/apple/AppIcon_1024.png" "$REPO_ROOT/src/MeetingCapture.UI/Resources/AppIcon_1024.png"

echo "=== 2. Génération de l'iconset macOS ==="
ICONSET_DIR="/tmp/AppIcon.iconset"
rm -rf "$ICONSET_DIR"
mkdir -p "$ICONSET_DIR"

MASTER="$REPO_ROOT/apple/AppIcon_1024.png"
sips -z 16 16     "$MASTER" --out "$ICONSET_DIR/icon_16x16.png" > /dev/null
sips -z 32 32     "$MASTER" --out "$ICONSET_DIR/icon_16x16@2x.png" > /dev/null
sips -z 32 32     "$MASTER" --out "$ICONSET_DIR/icon_32x32.png" > /dev/null
sips -z 64 64     "$MASTER" --out "$ICONSET_DIR/icon_32x32@2x.png" > /dev/null
sips -z 128 128   "$MASTER" --out "$ICONSET_DIR/icon_128x128.png" > /dev/null
sips -z 256 256   "$MASTER" --out "$ICONSET_DIR/icon_128x128@2x.png" > /dev/null
sips -z 256 256   "$MASTER" --out "$ICONSET_DIR/icon_256x256.png" > /dev/null
sips -z 512 512   "$MASTER" --out "$ICONSET_DIR/icon_256x256@2x.png" > /dev/null
sips -z 512 512   "$MASTER" --out "$ICONSET_DIR/icon_512x512.png" > /dev/null
sips -z 1024 1024 "$MASTER" --out "$ICONSET_DIR/icon_512x512@2x.png" > /dev/null

echo "=== 3. Compilation du fichier AppIcon.icns ==="
iconutil -c icns "$ICONSET_DIR" -o "$REPO_ROOT/apple/MeetingCapture/App/AppIcon.icns"

echo "=== 4. Génération du fichier app.ico (Windows multi-résolution) ==="
python3 - << 'PYEOF'
import struct
import os

sizes = [16, 32, 48, 64, 128, 256]
repo_root = os.environ.get("REPO_ROOT", ".")
master_path = f"{repo_root}/apple/AppIcon_1024.png"

# Prépare les PNG redimensionnés
png_data_list = []
for s in sizes:
    tmp_path = f"/tmp/icon_{s}.png"
    os.system(f"sips -z {s} {s} '{master_path}' --out '{tmp_path}' > /dev/null 2>&1")
    with open(tmp_path, "rb") as f:
        png_data_list.append((s, f.read()))

# Écriture du format ICO
num_images = len(png_data_list)
header = struct.pack("<HHH", 0, 1, num_images)

offset = 6 + 16 * num_images
entries = bytearray()
image_data = bytearray()

for s, data in png_data_list:
    w = 0 if s == 256 else s
    h = 0 if s == 256 else s
    entry = struct.pack("<BBBBHHII", w, h, 0, 0, 1, 32, len(data), offset)
    entries.extend(entry)
    image_data.extend(data)
    offset += len(data)

ico_bytes = header + entries + image_data
out_apple_ico = f"{repo_root}/apple/MeetingCapture/App/app.ico"
out_win_ico = f"{repo_root}/src/MeetingCapture.UI/Resources/app.ico"

with open(out_apple_ico, "wb") as f:
    f.write(ico_bytes)
with open(out_win_ico, "wb") as f:
    f.write(ico_bytes)
print(f"✅ Fichiers ICO générés : {out_apple_ico} et {out_win_ico}")
PYEOF

echo "=== 5. Compilation de l'exécutable MeetingCapture (Release) ==="
cd "$REPO_ROOT/apple"
swift build -c release
cd "$REPO_ROOT"

echo "=== 6. Packaging du Bundle macOS MeetingCapture.app ==="
APP_BUNDLE="$REPO_ROOT/publish/MeetingCapture-mac-arm64/MeetingCapture.app"
rm -rf "$APP_BUNDLE"
mkdir -p "$APP_BUNDLE/Contents/MacOS"
mkdir -p "$APP_BUNDLE/Contents/Resources/scripts"

# Copie de l'exécutable
cp "$REPO_ROOT/apple/.build/release/MeetingCapture" "$APP_BUNDLE/Contents/MacOS/MeetingCapture"
chmod +x "$APP_BUNDLE/Contents/MacOS/MeetingCapture"

# Copie de l'icône
cp "$REPO_ROOT/apple/MeetingCapture/App/AppIcon.icns" "$APP_BUNDLE/Contents/Resources/AppIcon.icns"

# Copie du script Whisper
cp "$REPO_ROOT/scripts/transcribe_wav.py" "$APP_BUNDLE/Contents/Resources/scripts/"

# Info.plist
cp "$REPO_ROOT/apple/MeetingCapture/App/Info.plist" "$APP_BUNDLE/Contents/Info.plist"

# Signature ad-hoc macOS
codesign --force --deep --sign - "$APP_BUNDLE" || true

echo "=== 7. Création de l'archive ZIP distributable ==="
cd "$REPO_ROOT/publish/MeetingCapture-mac-arm64"
zip -r -q "$REPO_ROOT/publish/MeetingCapture-mac-arm64.zip" "MeetingCapture.app"
cd "$REPO_ROOT"

echo "🎉 BUILD & PACKAGING TERMINÉS AVEC SUCCÈS !"
