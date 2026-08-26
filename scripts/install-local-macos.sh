#!/usr/bin/env bash
# 從目前原始碼建置並安裝 Specurai 到本機（macOS 專用）
#
# 用法: ./scripts/install-local-macos.sh [選項] [版本號]
#
# 選項:
#   --app        只安裝桌面應用程式（/Applications/Specurai.app）
#   --mcp        只安裝 MCP Server（~/Tools/SpecuraiMcp）
#   --cli        只安裝 CLI（~/.local/bin/specurai）
#   -h, --help   顯示此說明
#
# 未指定元件時三者全部安裝。選項可組合，例如 --app --cli。
#
# 範例:
#   ./scripts/install-local-macos.sh              # 全部安裝，版本號取自最新 git tag
#   ./scripts/install-local-macos.sh --mcp        # 只更新 MCP Server
#   ./scripts/install-local-macos.sh --app 1.24.0 # 只更新桌面 App，指定版本號
#
# 說明：全部產生 self-contained 版本（內含 .NET runtime），安裝後與原始碼脫鉤。
#       三者共用 ~/Library/Application Support/Specurai/connections.json，重裝不影響連線設定。

set -euo pipefail

APP_NAME="Specurai"
TARGET_APP="/Applications/${APP_NAME}.app"
MCP_DIR="${HOME}/Tools/SpecuraiMcp"
MCP_EXE="${MCP_DIR}/Specurai.McpServer"
CLI_DIR="${HOME}/.local/share/specurai/cli"
CLI_BIN_DIR="${HOME}/.local/bin"
CLI_LINK="${CLI_BIN_DIR}/specurai"

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
PROJECT_ROOT="$(cd "${SCRIPT_DIR}/.." && pwd)"

usage() {
    sed -n '2,20p' "$0" | sed 's/^# \{0,1\}//'
}

# --- 解析參數 ---
DO_APP=0
DO_MCP=0
DO_CLI=0
# 先保存環境變數，下面的 VERSION 會被參數解析覆寫
VERSION_ENV="${VERSION:-}"
VERSION=""

while [ $# -gt 0 ]; do
    case "$1" in
        --app) DO_APP=1 ;;
        --mcp) DO_MCP=1 ;;
        --cli) DO_CLI=1 ;;
        -h|--help) usage; exit 0 ;;
        -*) echo "未知選項：$1" >&2; echo ""; usage >&2; exit 1 ;;
        *) VERSION="$1" ;;
    esac
    shift
done

# 未指定任何元件時，三者全部安裝
if [ $((DO_APP + DO_MCP + DO_CLI)) -eq 0 ]; then
    DO_APP=1
    DO_MCP=1
    DO_CLI=1
fi

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
VERSION="${VERSION:-${VERSION_ENV}}"
if [ -z "${VERSION}" ]; then
    VERSION="$(git -C "${PROJECT_ROOT}" describe --tags --abbrev=0 2>/dev/null || true)"
    VERSION="${VERSION#v}"
fi
if [ -z "${VERSION}" ]; then
    VERSION="1.0.0"
    echo "找不到 git tag，版本號使用預設值 ${VERSION}"
fi

COMPONENTS=""
if [ "${DO_APP}" = 1 ]; then COMPONENTS="${COMPONENTS} 桌面App"; fi
if [ "${DO_MCP}" = 1 ]; then COMPONENTS="${COMPONENTS} MCP"; fi
if [ "${DO_CLI}" = 1 ]; then COMPONENTS="${COMPONENTS} CLI"; fi

echo "=== 本機安裝 ${APP_NAME} ==="
echo "版本: ${VERSION}"
echo "平台: ${RUNTIME}"
echo "元件:${COMPONENTS}"
echo ""

# --- 全程使用暫存目錄，安裝完成後清除，不在專案留下產物 ---
WORK_DIR="$(mktemp -d)"
cleanup() { rm -rf "${WORK_DIR}"; }
trap cleanup EXIT

# 以 rename 原子置換單一執行檔。
# 直接覆蓋執行中的執行檔會失敗（ETXTBSY），rename 則讓執行中的程序續用舊 inode。
replace_executable() {
    src="$1"
    dest="$2"
    mkdir -p "$(dirname "${dest}")"
    chmod +x "${src}"
    mv -f "${src}" "${dest}.new"
    mv -f "${dest}.new" "${dest}"
    # ad-hoc 建置未經 Apple 公證，清除隔離屬性避免 Gatekeeper 攔截
    xattr -dr com.apple.quarantine "${dest}" 2>/dev/null || true
}

# --- 桌面應用程式 ---
install_app() {
    echo "=== [1/3] 桌面應用程式 ==="
    dotnet publish "${PROJECT_ROOT}/src/Specurai.Desktop" \
        -c Release \
        -r "${RUNTIME}" \
        --self-contained \
        -p:Version="${VERSION}" \
        -o "${WORK_DIR}/app-publish"

    # SKIP_DMG 讓 bundle 腳本保留 .app、不建 dmg
    SKIP_DMG=1 "${SCRIPT_DIR}/create-macos-bundle.sh" \
        "${VERSION}" "${RUNTIME}" "${WORK_DIR}/app-publish" "${WORK_DIR}/app-bundle"

    new_app="${WORK_DIR}/app-bundle/${APP_NAME}.app"
    if [ ! -d "${new_app}" ]; then
        echo "打包失敗：找不到 ${new_app}" >&2
        exit 1
    fi

    # 關閉執行中的舊版本（先請求正常結束，逾時再強制終止）
    if pgrep -f "${TARGET_APP}/Contents/MacOS/" > /dev/null 2>&1; then
        echo "關閉執行中的 ${APP_NAME}..."
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

    # 打包成功後才置換舊版本（避免失敗殘留破損狀態）
    rm -rf "${TARGET_APP}"
    cp -R "${new_app}" "${TARGET_APP}"
    xattr -dr com.apple.quarantine "${TARGET_APP}" 2>/dev/null || true
    echo "已安裝 ${TARGET_APP}（$(du -sh "${TARGET_APP}" | cut -f1)）"
    echo ""
}

# --- MCP Server ---
install_mcp() {
    echo "=== [2/3] MCP Server ==="
    dotnet publish "${PROJECT_ROOT}/src/Specurai.McpServer" \
        -c Release \
        -r "${RUNTIME}" \
        --self-contained \
        -p:PublishSingleFile=true \
        -p:Version="${VERSION}" \
        -o "${WORK_DIR}/mcp-publish"

    replace_executable "${WORK_DIR}/mcp-publish/Specurai.McpServer" "${MCP_EXE}"
    echo "已安裝 ${MCP_EXE}（$(du -h "${MCP_EXE}" | cut -f1)）"

    # 若尚未註冊到 Claude Code，提示註冊指令
    if command -v claude > /dev/null 2>&1; then
        if ! claude mcp list 2>/dev/null | grep -q "^specurai:"; then
            echo "提醒：尚未註冊到 Claude Code，請執行："
            echo "  claude mcp add specurai -s user -- ${MCP_EXE}"
        fi
    fi
    echo ""
}

# --- CLI ---
install_cli() {
    echo "=== [3/3] CLI ==="
    dotnet publish "${PROJECT_ROOT}/src/Specurai.Cli" \
        -c Release \
        -r "${RUNTIME}" \
        --self-contained \
        -p:PublishSingleFile=true \
        -p:Version="${VERSION}" \
        -o "${WORK_DIR}/cli-publish"

    replace_executable "${WORK_DIR}/cli-publish/Specurai.Cli" "${CLI_DIR}/Specurai.Cli"
    mkdir -p "${CLI_BIN_DIR}"
    ln -sf "${CLI_DIR}/Specurai.Cli" "${CLI_LINK}"
    echo "已安裝 ${CLI_LINK} → ${CLI_DIR}/Specurai.Cli（$(du -h "${CLI_DIR}/Specurai.Cli" | cut -f1)）"

    case ":${PATH}:" in
        *":${CLI_BIN_DIR}:"*) : ;;
        *)
            echo "提醒：${CLI_BIN_DIR} 不在 PATH，請在 ~/.zshrc 加一行："
            echo '  export PATH="$HOME/.local/bin:$PATH"'
            ;;
    esac
    echo ""
}

if [ "${DO_APP}" = 1 ]; then install_app; fi
if [ "${DO_MCP}" = 1 ]; then install_mcp; fi
if [ "${DO_CLI}" = 1 ]; then install_cli; fi

echo "=== 完成 ==="
if [ "${DO_APP}" = 1 ]; then echo "桌面App : 透過 Spotlight、Launchpad 或 open -a ${APP_NAME} 開啟"; fi
if [ "${DO_MCP}" = 1 ]; then echo "MCP     : 重開 Claude Code 後生效"; fi
if [ "${DO_CLI}" = 1 ]; then echo "CLI     : 執行 specurai --help"; fi
exit 0
