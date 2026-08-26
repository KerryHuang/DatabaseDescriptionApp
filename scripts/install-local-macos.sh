#!/usr/bin/env bash
# 從目前原始碼建置並安裝 Specurai.app 到本機 /Applications（macOS 專用）
# 用法: ./scripts/install-local-macos.sh [版本號]
# 範例: ./scripts/install-local-macos.sh          # 版本號取自最新 git tag
#       ./scripts/install-local-macos.sh 1.24.0   # 手動指定版本號
#
# 說明：產生 self-contained 版本（內含 .NET runtime），安裝後與原始碼脫鉤。
#       使用者資料位於 ~/Library/Application Support/Specurai，重裝不受影響。

set -euo pipefail

APP_NAME="Specurai"
INSTALL_DIR="/Applications"
TARGET_APP="${INSTALL_DIR}/${APP_NAME}.app"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

# --- 平台檢查 ---
if [ "$(uname -s)" != "Darwin" ]; then
    echo "此腳本僅支援 macOS（目前為 $(uname -s)）" >&2
    exit 1
fi

case "$(uname -m)" in
    arm64)  RUNTIME="osx-arm64" ;;
    x86_64) RUNTIME="osx-x64" ;;
    *) echo "不支援的架構：$(uname -m)" >&2; exit 1 ;;
esac

# --- 版本號解析：參數 > VERSION 環境變數 > 最新 git tag > 預設值 ---
VERSION="${1:-${VERSION:-}}"
if [ -z "${VERSION}" ]; then
    VERSION="$(git -C "${PROJECT_ROOT}" describe --tags --abbrev=0 2>/dev/null || true)"
    VERSION="${VERSION#v}"
fi
if [ -z "${VERSION}" ]; then
    VERSION="1.0.0"
    echo "找不到 git tag，版本號使用預設值 ${VERSION}"
fi

echo "=== 本機安裝 ${APP_NAME} ==="
echo "版本: ${VERSION}"
echo "平台: ${RUNTIME}"
echo "目標: ${TARGET_APP}"
echo ""

# --- 全程使用暫存目錄，安裝完成後清除，不在專案留下產物 ---
WORK_DIR="$(mktemp -d)"
cleanup() { rm -rf "${WORK_DIR}"; }
trap cleanup EXIT

# --- 發布 self-contained 版本 ---
echo "=== 發布應用程式（需數分鐘）==="
dotnet publish "${PROJECT_ROOT}/src/Specurai.Desktop" \
    -c Release \
    -r "${RUNTIME}" \
    --self-contained \
    -p:Version="${VERSION}" \
    -o "${WORK_DIR}/publish"

# --- 打包 .app（SKIP_DMG 讓 bundle 腳本保留 .app、不建 dmg）---
echo ""
SKIP_DMG=1 "${SCRIPT_DIR}/create-macos-bundle.sh" \
    "${VERSION}" "${RUNTIME}" "${WORK_DIR}/publish" "${WORK_DIR}/bundle"

NEW_APP="${WORK_DIR}/bundle/${APP_NAME}.app"
if [ ! -d "${NEW_APP}" ]; then
    echo "打包失敗：找不到 ${NEW_APP}" >&2
    exit 1
fi

# --- 關閉執行中的舊版本（先請求正常結束，逾時再強制終止）---
if pgrep -f "${TARGET_APP}/Contents/MacOS/" > /dev/null 2>&1; then
    echo ""
    echo "=== 關閉執行中的 ${APP_NAME} ==="
    osascript -e "quit app \"${APP_NAME}\"" > /dev/null 2>&1 || true
    for _ in $(seq 1 10); do
        pgrep -f "${TARGET_APP}/Contents/MacOS/" > /dev/null 2>&1 || break
        sleep 1
    done
    if pgrep -f "${TARGET_APP}/Contents/MacOS/" > /dev/null 2>&1; then
        echo "正常結束逾時，強制終止"
        pkill -f "${TARGET_APP}/Contents/MacOS/" 2>/dev/null || true
        sleep 1
    fi
fi

# --- 置換安裝（打包成功後才動舊版本，避免失敗殘留破損狀態）---
echo ""
echo "=== 安裝至 ${INSTALL_DIR} ==="
rm -rf "${TARGET_APP}"
cp -R "${NEW_APP}" "${TARGET_APP}"

# ad-hoc 簽署未經 Apple 公證，清除隔離屬性避免 Gatekeeper 攔截
xattr -dr com.apple.quarantine "${TARGET_APP}" 2>/dev/null || true

echo ""
echo "=== 完成 ==="
du -sh "${TARGET_APP}"
echo "可透過 Spotlight、Launchpad 或 open -a ${APP_NAME} 開啟"
