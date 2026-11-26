#!/bin/sh
set -eu

CONFIGURATION="${1:-debug}"
APP_NAME="RadioClassic"
EXECUTABLE_NAME="RadioClassicMac"
RESOURCE_BUNDLE="RadioClassicMac_RadioClassicCore.bundle"
BUILD_DIR=".build/${CONFIGURATION}"
APP_DIR=".build/${APP_NAME}.app"

swift build -c "${CONFIGURATION}"

rm -rf "${APP_DIR}"
mkdir -p "${APP_DIR}/Contents/MacOS" "${APP_DIR}/Contents/Resources"
cp "${BUILD_DIR}/${EXECUTABLE_NAME}" "${APP_DIR}/Contents/MacOS/${APP_NAME}"
if [ -d "${BUILD_DIR}/${RESOURCE_BUNDLE}" ]; then
    cp -R "${BUILD_DIR}/${RESOURCE_BUNDLE}" "${APP_DIR}/Contents/Resources/"
fi

ICON_SOURCE="Shared/radiosure-icon.jpg"
if [ -f "${ICON_SOURCE}" ]; then
    ICONSET=".build/RadioClassic.iconset"
    rm -rf "${ICONSET}"
    mkdir -p "${ICONSET}"
    make_icon() {
        sips -s format png -z "$1" "$1" "${ICON_SOURCE}" --out "${ICONSET}/$2" >/dev/null
    }
    make_icon 16 icon_16x16.png
    make_icon 32 icon_16x16@2x.png
    make_icon 32 icon_32x32.png
    make_icon 64 icon_32x32@2x.png
    make_icon 128 icon_128x128.png
    make_icon 256 icon_128x128@2x.png
    make_icon 256 icon_256x256.png
    make_icon 512 icon_256x256@2x.png
    make_icon 512 icon_512x512.png
    make_icon 1024 icon_512x512@2x.png
    iconutil -c icns "${ICONSET}" -o "${APP_DIR}/Contents/Resources/AppIcon.icns"
fi

cat > "${APP_DIR}/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleDevelopmentRegion</key><string>en</string>
    <key>CFBundleExecutable</key><string>RadioClassic</string>
    <key>CFBundleIdentifier</key><string>com.radioclassic.mac</string>
    <key>CFBundleInfoDictionaryVersion</key><string>6.0</string>
    <key>CFBundleName</key><string>RadioClassic</string>
    <key>CFBundleDisplayName</key><string>RadioClassic</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleIconFile</key><string>AppIcon</string>
    <key>CFBundleShortVersionString</key><string>1.0.1</string>
    <key>CFBundleVersion</key><string>2</string>
    <key>LSMinimumSystemVersion</key><string>13.0</string>
    <key>NSPrincipalClass</key><string>NSApplication</string>
    <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

printf '%s\n' "${APP_DIR}"
