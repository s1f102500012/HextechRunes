#!/bin/zsh
set -euo pipefail

# 构建 HextechRunesSponsorPack 两个变体(0.107.1 / 0.111.0) + 加载器 + PCK,默认部署到游戏 mods 目录。
#   HEXTECH_SPONSOR_DEPLOY=0          只构建,不部署。
#   STS2_GAME_APP=<.app>              覆盖游戏安装位置。
# 加载器与本体共用源码(见 loader/HextechRunesSponsorPack.Loader.csproj),构建步骤与多版本清单工具
# 直接使用本体的 tools/lib_build.sh 与 tools/multi_version/。

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
HEXTECH_TOOLS_DIR="$(cd "$ROOT/../HextechRunes/tools" && pwd)"
source "$HEXTECH_TOOLS_DIR/lib_build.sh"

FILE_STEM="HextechRunesSponsorPack"
VARIANT_MANIFEST_NAME="hextech-runes-sponsor-pack-variants.manifest"
REFS_ROOT="$ROOT/../HextechRunes/versioned-dll-backups"
BUILD_ROOT="$ROOT/.build"
DIST="$ROOT/dist"
MOD_DIR="$GAME_APP/Contents/MacOS/mods/$FILE_STEM"
HEXTECH_SPONSOR_DEPLOY="${HEXTECH_SPONSOR_DEPLOY:-1}"

hextech_resolve_tools
hextech_check_prerequisites
hextech_prepare_output

hextech_build_variants "$ROOT/src/$FILE_STEM.csproj"
hextech_build_loader "$ROOT/loader/$FILE_STEM.Loader.csproj"
hextech_generate_variant_manifest
hextech_pack_assets
hextech_validate_bundle
hextech_deploy_or_skip "$HEXTECH_SPONSOR_DEPLOY"
hextech_report_installed_version
