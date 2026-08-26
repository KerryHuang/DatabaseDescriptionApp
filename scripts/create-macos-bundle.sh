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

# 程式碼簽署
# 若有 Developer ID 憑證，設定環境變數 CODESIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)"
# 若無，使用 ad-hoc 簽署（使用者需右鍵 > 打開）
CODESIGN_IDENTITY="${CODESIGN_IDENTITY:--}"

echo "=== 程式碼簽署 ==="
if [ "${CODESIGN_IDENTITY}" = "-" ]; then
    echo "使用 ad-hoc 簽署（未設定 CODESIGN_IDENTITY）"
else
    echo "使用憑證簽署: ${CODESIGN_IDENTITY}"
fi

# 先簽署所有 dylib，再簽署主執行檔，最後簽署整個 .app
find "${BUNDLE_DIR}/Contents/MacOS" -name "*.dylib" -exec \
    codesign --force --sign "${CODESIGN_IDENTITY}" --timestamp {} \; 2>/dev/null || true
codesign --force --sign "${CODESIGN_IDENTITY}" --timestamp "${BUNDLE_DIR}/Contents/MacOS/Specurai.Desktop" 2>/dev/null || true
codesign --force --sign "${CODESIGN_IDENTITY}" --timestamp "${BUNDLE_DIR}" 2>/dev/null || true

echo "簽署完成"

# 若有 Developer ID 且設定了 APPLE_ID，進行公證
if [ "${CODESIGN_IDENTITY}" != "-" ] && [ -n "${APPLE_ID:-}" ] && [ -n "${APPLE_TEAM_ID:-}" ]; then
    echo "=== 提交 Apple 公證 ==="
    # 先建立 zip 供公證使用
    NOTARIZE_ZIP=$(mktemp -d)/Specurai.zip
    ditto -c -k --keepParent "${BUNDLE_DIR}" "${NOTARIZE_ZIP}"
    xcrun notarytool submit "${NOTARIZE_ZIP}" \
        --apple-id "${APPLE_ID}" \
        --team-id "${APPLE_TEAM_ID}" \
        --password "${APPLE_APP_PASSWORD}" \
        --wait
    # 裝訂公證票據
    xcrun stapler staple "${BUNDLE_DIR}"
    rm -rf "$(dirname "${NOTARIZE_ZIP}")"
    echo "公證完成"
fi

echo "已建立 ${BUNDLE_DIR}"

# 建立 .dmg（設定 SKIP_DMG=1 可跳過，保留 .app 供本機安裝使用）
if [ "${SKIP_DMG:-0}" = "1" ]; then
    echo "=== 已跳過 .dmg 建立（SKIP_DMG=1）==="
    echo "=== 完成 ==="
    du -sh "${BUNDLE_DIR}"
    exit 0
fi

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
