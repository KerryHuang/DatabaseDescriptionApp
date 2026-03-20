#!/bin/bash
# 建立 macOS .app bundle 和 .dmg
# 用法: ./scripts/create-macos-bundle.sh <版本號> <runtime> <publish目錄>
# 範例: ./scripts/create-macos-bundle.sh 1.0.0 osx-arm64 publish

set -euo pipefail

VERSION="${1:?請提供版本號，例如 1.0.0}"
RUNTIME="${2:?請提供 runtime，例如 osx-arm64}"
PUBLISH_DIR="${3:?請提供 publish 目錄路徑}"
OUTPUT_DIR="${4:-Releases}"

APP_NAME="Specurai"
BUNDLE_DIR="${OUTPUT_DIR}/${APP_NAME}.app"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"
PLIST_TEMPLATE="${PROJECT_ROOT}/src/Specurai.Desktop/Assets/macOS/Info.plist"

echo "=== 建立 macOS .app bundle ==="
echo "版本: ${VERSION}"
echo "平台: ${RUNTIME}"
echo "來源: ${PUBLISH_DIR}"

# 清理舊的 bundle
rm -rf "${BUNDLE_DIR}"

# 建立 .app 目錄結構
mkdir -p "${BUNDLE_DIR}/Contents/MacOS"
mkdir -p "${BUNDLE_DIR}/Contents/Resources"

# 複製執行檔和相依函式庫
cp -R "${PUBLISH_DIR}/"* "${BUNDLE_DIR}/Contents/MacOS/"

# 移除不需要的 pdb 檔案
find "${BUNDLE_DIR}/Contents/MacOS" -name "*.pdb" -delete

# 建立 Info.plist（替換版本號）
sed "s/VERSION_PLACEHOLDER/${VERSION}/g" "${PLIST_TEMPLATE}" > "${BUNDLE_DIR}/Contents/Info.plist"

# 產生 .icns 圖示
ICON_PNG="${PROJECT_ROOT}/src/Specurai.Desktop/Assets/Specurai.png"
if [ -f "${ICON_PNG}" ]; then
    ICONSET_DIR=$(mktemp -d)/Specurai.iconset
    mkdir -p "${ICONSET_DIR}"

    # 產生各尺寸圖示
    for SIZE in 16 32 64 128 256 512; do
        sips -z ${SIZE} ${SIZE} "${ICON_PNG}" --out "${ICONSET_DIR}/icon_${SIZE}x${SIZE}.png" > /dev/null 2>&1
    done
    for SIZE in 32 64 128 256 512 1024; do
        HALF=$((SIZE / 2))
        sips -z ${SIZE} ${SIZE} "${ICON_PNG}" --out "${ICONSET_DIR}/icon_${HALF}x${HALF}@2x.png" > /dev/null 2>&1
    done

    iconutil -c icns "${ICONSET_DIR}" -o "${BUNDLE_DIR}/Contents/Resources/Specurai.icns"
    rm -rf "$(dirname "${ICONSET_DIR}")"
    echo "已產生 .icns 圖示"
else
    echo "警告: 找不到 ${ICON_PNG}，跳過圖示產生"
fi

# 設定執行權限
chmod +x "${BUNDLE_DIR}/Contents/MacOS/Specurai.Desktop"

echo "已建立 ${BUNDLE_DIR}"

# 建立 .dmg
DMG_NAME="${APP_NAME}-${VERSION}-${RUNTIME}.dmg"
DMG_PATH="${OUTPUT_DIR}/${DMG_NAME}"

echo "=== 建立 .dmg 安裝映像檔 ==="

# 建立暫存目錄作為 DMG 內容
DMG_STAGING=$(mktemp -d)
cp -R "${BUNDLE_DIR}" "${DMG_STAGING}/"
ln -s /Applications "${DMG_STAGING}/Applications"

# 計算所需大小（來源大小 + 20% 緩衝）
BUNDLE_SIZE_KB=$(du -sk "${DMG_STAGING}" | cut -f1)
DMG_SIZE_MB=$(( (BUNDLE_SIZE_KB / 1024) * 120 / 100 + 10 ))

# 建立 DMG
hdiutil create -volname "${APP_NAME}" \
    -srcfolder "${DMG_STAGING}" \
    -ov -format UDZO \
    -size "${DMG_SIZE_MB}m" \
    "${DMG_PATH}" > /dev/null 2>&1

rm -rf "${DMG_STAGING}"

echo "已建立 ${DMG_PATH}"

# 清理 .app（DMG 已包含）
rm -rf "${BUNDLE_DIR}"

echo "=== 完成 ==="
ls -lh "${DMG_PATH}"
