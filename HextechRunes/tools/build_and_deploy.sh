#!/bin/zsh
set -euo pipefail

# 构建 HextechRunes 全部变体(目标清单见 tools/lib_build.sh 的 HEXTECH_TARGETS) + 加载器 + PCK,默认部署到游戏 mods 目录。
#   HEXTECH_DEPLOY=0         只构建,不部署。
#   HEXTECH_UPDATE_LATEST=1  发布时显式开启:用本次 dist 改写 server/hextech-telemetry/public/latest-version.json。
#   STS2_GAME_APP=<.app>     覆盖游戏安装位置。
# 共用步骤见 tools/lib_build.sh。

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
HEXTECH_TOOLS_DIR="$SCRIPT_DIR"
source "$HEXTECH_TOOLS_DIR/lib_build.sh"

FILE_STEM="HextechRunes"
VARIANT_MANIFEST_NAME="hextech-runes-variants.manifest"
REFS_ROOT="$ROOT/versioned-dll-backups"
BUILD_ROOT="$ROOT/.build"
DIST="$ROOT/dist"
MOD_DIR="$GAME_APP/Contents/MacOS/mods/$FILE_STEM"
HEXTECH_DEPLOY="${HEXTECH_DEPLOY:-1}"

hextech_resolve_tools
hextech_check_prerequisites
hextech_prepare_output

python3 "$ROOT/tools/validate_hextech_content.py"

hextech_build_variants "$ROOT/src/$FILE_STEM.csproj"
hextech_build_loader "$ROOT/loader/$FILE_STEM.Loader.csproj"
hextech_generate_variant_manifest
hextech_pack_assets
hextech_validate_bundle

# 更新检查读取 latestVersion。这是已跟踪的发布文件,本地构建默认不改写。
if [[ "${HEXTECH_UPDATE_LATEST:-0}" != "0" ]]; then
	python3 "$ROOT/tools/update_latest_version.py" \
		--latest-json "$ROOT/server/hextech-telemetry/public/latest-version.json" \
		--dist "$DIST" \
		--mod-id "$FILE_STEM" \
		--server-name "海克斯大乱斗"
else
	echo "Skipped latest-version.json update (set HEXTECH_UPDATE_LATEST=1 when publishing)."
fi

hextech_deploy_or_skip "$HEXTECH_DEPLOY"
hextech_report_installed_version
