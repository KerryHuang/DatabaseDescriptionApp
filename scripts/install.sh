#!/usr/bin/env bash
set -euo pipefail

repo="KerryHuang/DatabaseDescriptionApp"
version="${VERSION:-}"
with_mcp=0
[ "${1:-}" = "--with-mcp" ] && with_mcp=1

bindir="$HOME/.local/bin"
sharedir="$HOME/.local/share/specurai"

os="$(uname -s)"; arch="$(uname -m)"
case "$os-$arch" in
  Darwin-arm64)  rid="osx-arm64" ;;
  Darwin-x86_64) rid="osx-x64" ;;
  Linux-x86_64)  rid="linux-x64" ;;
  *) echo "不支援的平台：$os-$arch" >&2; exit 1 ;;
esac

tag="$version"
if [ -z "$tag" ]; then
  tag="$(curl -fsSL "https://api.github.com/repos/$repo/releases/latest" \
        | grep '"tag_name"' | head -1 | sed -E 's/.*"tag_name": *"([^"]+)".*/\1/')"
fi
case "$tag" in v*) : ;; *) tag="v$tag" ;; esac

install_asset() {
  asset="$1"; exe="$2"; final="$3"; sub="$4"
  url="https://github.com/$repo/releases/download/$tag/$asset"
  dest="$sharedir/$sub"
  tmp="$(mktemp -d)"
  echo "下載 $asset ($tag)..."
  curl -fsSL "$url" -o "$tmp/$asset"
  rm -rf "$dest"; mkdir -p "$dest"
  tar xzf "$tmp/$asset" -C "$dest"
  rm -rf "$tmp"
  chmod +x "$dest/$exe"
  xattr -dr com.apple.quarantine "$dest/$exe" 2>/dev/null || true
  mkdir -p "$bindir"
  ln -sf "$dest/$exe" "$bindir/$final"
  echo "已安裝 $final 至 $bindir（→ $dest/$exe）"
}

install_asset "Specurai.Cli-$rid.tar.gz" "Specurai.Cli" "specurai" "cli"
[ "$with_mcp" = 1 ] && install_asset "Specurai.McpServer-$rid.tar.gz" "Specurai.McpServer" "specurai-mcp" "mcp"

case ":$PATH:" in
  *":$bindir:"*) : ;;
  *) echo ""; echo "提醒：請把 $bindir 加入 PATH（在 ~/.zshrc 或 ~/.bashrc 加一行）：";
     echo '  export PATH="$HOME/.local/bin:$PATH"' ;;
esac
echo ""
echo "完成！執行： specurai --help"
