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
    <key>CFBundleShortVersionString</key><string>1.0.0</string>
    <key>CFBundleVersion</key><string>1</string>
    <key>LSMinimumSystemVersion</key><string>13.0</string>
    <key>NSPrincipalClass</key><string>NSApplication</string>
    <key>NSHighResolutionCapable</key><true/>
</dict>
</plist>
PLIST

printf '%s\n' "${APP_DIR}"
