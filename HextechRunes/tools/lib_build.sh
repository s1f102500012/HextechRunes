# shellcheck shell=bash
# 海克斯本体与拓展包构建脚本共用的定义与步骤(tools/build_and_deploy.sh、
# HextechRunesSponsorPack/tools/build_and_deploy.sh、tools/run_tests.sh)。bash 与 zsh 都可以 source。
#
# source 前调用方需设置 HEXTECH_TOOLS_DIR(本文件所在目录)。构建步骤函数另外读取调用方设置的:
#   ROOT FILE_STEM VARIANT_MANIFEST_NAME REFS_ROOT BUILD_ROOT DIST MOD_DIR
# 游戏安装位置可用环境变量 STS2_GAME_APP 覆盖(与 tools/mplab/run_mplab.sh 一致)。

# 发布目标的唯一清单;变体版本符号见仓库根 Directory.Build.targets。
HEXTECH_TARGETS=(0.107.1 0.111.0 0.113.0)
# 加载器固定对最低目标编译(加载器自身只用各版本共有的 API)。
HEXTECH_LOADER_REFS_TARGET=0.107.1

# 测试工程同时直接引用本体、并通过拓展包间接引用本体。默认并行 MSBuild
# 在这张菱形工程图上偶尔会卡在子节点 IPC 重连；固定单节点即可稳定构建。
HEXTECH_BUILD_STABILITY_ARGS=(
	-m:1
	-nodeReuse:false
	-p:UseSharedCompilation=false
	-p:NuGetAudit=false
	-p:RestoreIgnoreFailedSources=true
)

GAME_APP="${STS2_GAME_APP:-/Users/iniad/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app}"
GAME_BIN="$GAME_APP/Contents/MacOS/Slay the Spire 2"
GAME_RELEASE_INFO="$GAME_APP/Contents/Resources/release_info.json"

hextech_fail() {
	echo "$*" >&2
	exit 1
}

hextech_resolve_tools() {
	local default_godot="$HEXTECH_TOOLS_DIR/../../.tools/godot-4.5.1/Godot_mono.app/Contents/MacOS/Godot"
	if [[ -z "${GODOT_EDITOR:-}" && -x "$default_godot" ]]; then
		GODOT_EDITOR="$default_godot"
	else
		GODOT_EDITOR="${GODOT_EDITOR:-/opt/homebrew/bin/godot}"
	fi

	if command -v dotnet >/dev/null 2>&1; then
		DOTNET_BIN="$(command -v dotnet)"
	elif [[ -x "/opt/homebrew/bin/dotnet" ]]; then
		DOTNET_BIN="/opt/homebrew/bin/dotnet"
	else
		hextech_fail "Could not find a usable .NET 9 SDK."
	fi
}

hextech_major_minor_version() {
	sed -E 's/^([0-9]+[.][0-9]+).*/\1/' <<< "$1"
}

hextech_clean_directory() {
	local directory="$1"
	mkdir -p "$directory"
	find "$directory" -mindepth 1 -depth -delete
}

hextech_clean_macos_metadata() {
	local target="$1"
	[[ -d "$target" ]] || return 0

	find "$target" -name "__MACOSX" -type d -prune -exec rm -rf {} +
	find "$target" -name ".DS_Store" -type f -delete
	find "$target" -name "._*" -type f -delete
}

# 构建前检查:全部目标的游戏引用、游戏可执行文件与 Godot 编辑器;导入用 Godot 与游戏运行时主次版本不一致时告警。
hextech_check_prerequisites() {
	local target reference refs
	for target in "${HEXTECH_TARGETS[@]}"; do
		refs="$REFS_ROOT/$target/game-refs"
		for reference in sts2.dll GodotSharp.dll 0Harmony.dll Steamworks.NET.dll; do
			[[ -f "$refs/$reference" ]] || hextech_fail "Missing reference for STS2 $target: $refs/$reference"
		done
	done

	[[ -x "$GAME_BIN" ]] || hextech_fail "Missing Slay the Spire 2 executable: $GAME_BIN (set STS2_GAME_APP to override the install location)"
	[[ -x "$GODOT_EDITOR" ]] || hextech_fail "Missing Godot editor: $GODOT_EDITOR"

	local game_version import_version
	game_version="$("$GAME_BIN" --version 2>/dev/null | head -n 1)"
	import_version="$("$GODOT_EDITOR" --version 2>/dev/null | head -n 1)"
	if [[ -n "$game_version" && -n "$import_version" \
		&& "$(hextech_major_minor_version "$game_version")" != "$(hextech_major_minor_version "$import_version")" ]]; then
		echo "Warning: asset import Godot version ($import_version) differs from game runtime ($game_version)." >&2
		echo "Set GODOT_EDITOR to a matching 4.5.x editor if mobile/runtime texture compatibility regresses." >&2
	fi
}

hextech_prepare_output() {
	hextech_clean_directory "$BUILD_ROOT"
	hextech_clean_directory "$DIST"
	rm -rf "$ROOT/src/bin" "$ROOT/src/obj" "$ROOT/loader/bin" "$ROOT/loader/obj"
}

# 用法: hextech_build_variants <工程>
# 每个目标以 HextechSts2Target=<目标> 构建(全局属性,拓展包引用的本体工程同样按该目标编译),
# 产物复制到 dist/lib/<目标>/。各目标共用 obj 而宏定义不同,所以每个目标构建前都要 clean。
hextech_build_variants() {
	local project="$1"
	local target refs output
	for target in "${HEXTECH_TARGETS[@]}"; do
		refs="$REFS_ROOT/$target/game-refs"
		output="$BUILD_ROOT/variants/$target"
		mkdir -p "$output"

		echo "Building $FILE_STEM implementation for STS2 $target using $refs"
		"$DOTNET_BIN" clean "$project" -c Release --disable-build-servers \
			-p:HextechSts2Target="$target" \
			-p:GameDataDir="$refs" >/dev/null
		"$DOTNET_BIN" build "$project" -c Release --disable-build-servers \
			-p:HextechSts2Target="$target" \
			-p:GameDataDir="$refs" \
			-o "$output"

		mkdir -p "$DIST/lib/$target"
		cp "$output/$FILE_STEM.dll" "$DIST/lib/$target/$FILE_STEM.dll"
	done
}

hextech_build_loader() {
	local project="$1"
	local refs="$REFS_ROOT/$HEXTECH_LOADER_REFS_TARGET/game-refs"
	local output="$BUILD_ROOT/loader"
	mkdir -p "$output"
	# loader 的 bin/obj 已在 hextech_prepare_output 里删掉,这里不必再 clean。
	echo "Building stable $FILE_STEM loader against STS2 $HEXTECH_LOADER_REFS_TARGET references"
	"$DOTNET_BIN" build "$project" -c Release --disable-build-servers \
		-p:GameDataDir="$refs" \
		-o "$output"
	cp "$output/$FILE_STEM.Loader.dll" "$DIST/$FILE_STEM.dll"
}

hextech_generate_variant_manifest() {
	local target_args=()
	local target
	for target in "${HEXTECH_TARGETS[@]}"; do
		target_args+=(--target "$target")
	done

	python3 "$HEXTECH_TOOLS_DIR/multi_version/generate_variant_manifest.py" \
		--dist "$DIST" \
		--mod-id "$FILE_STEM" \
		--manifest-name "$VARIANT_MANIFEST_NAME" \
		"${target_args[@]}"
}

# 用游戏自带版本匹配的 Godot 导入资源并打 PCK(manifest json 不进 PCK,单独复制)。
hextech_pack_assets() {
	local import_project="$BUILD_ROOT/import_project"
	local manifest_src="$ROOT/assets/$FILE_STEM.json"
	mkdir -p "$import_project/$FILE_STEM"
	cp "$HEXTECH_TOOLS_DIR/project.godot" "$import_project/project.godot"
	rsync -a --exclude "$FILE_STEM.json" "$ROOT/assets/" "$import_project/$FILE_STEM/"
	hextech_clean_macos_metadata "$import_project"

	"$GODOT_EDITOR" --headless \
		--path "$import_project" \
		--import

	cp "$manifest_src" "$DIST/$FILE_STEM.json"
	"$GAME_BIN" --headless \
		--path "$HEXTECH_TOOLS_DIR" \
		-s res://pack_mod.gd -- \
		"$manifest_src" \
		"$DIST/$FILE_STEM.pck" \
		"$import_project"
}

hextech_validate_bundle() {
	hextech_clean_macos_metadata "$DIST"
	python3 "$HEXTECH_TOOLS_DIR/multi_version/validate_variant_bundle.py" \
		--dist "$DIST" \
		--mod-id "$FILE_STEM" \
		--manifest-name "$VARIANT_MANIFEST_NAME"
}

# 用法: hextech_deploy_or_skip <部署开关值>;开关为 0 时只保留 dist。
hextech_deploy_or_skip() {
	if [[ "$1" == "0" ]]; then
		echo "Built multi-version package in $DIST without deploying."
		return 0
	fi

	local stage="$MOD_DIR.tmp.$$"
	local previous="$MOD_DIR.previous.$$"
	rm -rf "$stage" "$previous"
	mkdir -p "$stage"
	rsync -a --delete "$DIST/" "$stage/"
	if [[ -d "$MOD_DIR" ]]; then
		mv "$MOD_DIR" "$previous"
	fi
	mv "$stage" "$MOD_DIR"
	rm -rf "$previous"
	echo "Deployed multi-version package to $MOD_DIR"
}

hextech_report_installed_version() {
	[[ -f "$GAME_RELEASE_INFO" ]] || return 0
	local version
	version="$(sed -nE 's/.*"version"[[:space:]]*:[[:space:]]*"v([^"]+)".*/\1/p' "$GAME_RELEASE_INFO" | head -n 1)"
	echo "Installed STS2 version: ${version:-unknown}; loader will choose the greatest bundled target not newer than the host."
}
