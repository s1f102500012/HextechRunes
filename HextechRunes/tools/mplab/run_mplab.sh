#!/bin/zsh
# 本机双客户端联机确定性实验:
#   主机进程 --fastmp=host_standard 起 ENet 主机(127.0.0.1:33771),客户端进程 --fastmp=join --clientId=1001 自动连入。
#   两个进程都装了 HextechMpLab 驱动(按 HEXTECH_MPLAB_ROLE 自动选角色/就绪/投票/结束回合),
#   客户端用独立 HOME 隔离存档。结束后比对两边日志里游戏自己的校验和分叉报错。
set -euo pipefail

ROOT="${0:A:h:h:h}"                       # HextechRunes 仓库根
LAB_DIR="$ROOT/tools/mplab"
GAME_APP="${STS2_GAME_APP:-/Users/iniad/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app}"
GAME_BIN="$GAME_APP/Contents/MacOS/Slay the Spire 2"
GAME_DIR="${GAME_BIN:h}"
MODS_DIR="$GAME_DIR/mods"
LAB_MOD_DIR="$MODS_DIR/HextechMpLab"
STEAM_APPID_FILE="$GAME_DIR/steam_appid.txt"
STEAM_APP_ID="${STS2_STEAM_APP_ID:-2868840}"
WORKSHOP_DIR="${GAME_APP:h:h:h}/workshop/content/$STEAM_APP_ID"
OUT="${HEXTECH_MPLAB_OUT:-/tmp/hextech-mplab-$(date +%Y%m%d-%H%M%S)}"
SEED="${HEXTECH_MPLAB_SEED:-HEXTECHLAB}"
TIMEOUT_SEC="${HEXTECH_MPLAB_TIMEOUT_SEC:-420}"
CLIENT_DELAY_SEC="${HEXTECH_MPLAB_CLIENT_DELAY_SEC:-15}"
QUIT_AFTER="${HEXTECH_MPLAB_QUIT_AFTER:-60000}"
CLIENT_HOME="$OUT/client-home"
# HEXTECH_MPLAB_ONLY_MODS="HextechRunes,..."：主机也用独立 HOME，两端设置里只启用这些模组（不动本机真实设置）。
ONLY_MODS="${HEXTECH_MPLAB_ONLY_MODS:-}"
HOST_HOME="$HOME"
[[ -n "$ONLY_MODS" ]] && HOST_HOME="$OUT/host-home"

mkdir -p "$OUT" "$CLIENT_HOME"
print "输出目录: $OUT"

# 客户端用独立 HOME 隔离存档,但设置文件(含"已看过模组警告"与模组启用表)必须和主机一致,
# 否则全新档会跳过加载所有模组,主机直接判 ModMismatch。只复制 settings/profile,不带任何 run 存档。
REAL_SAVE_ROOT="$HOME/Library/Application Support/SlayTheSpire2"
CLIENT_SAVE_ROOT="$CLIENT_HOME/Library/Application Support/SlayTheSpire2"
copy_settings() {
  local target_root="$1"
  for dir in "$REAL_SAVE_ROOT"/steam/*/ "$REAL_SAVE_ROOT"/default/*/; do
    [[ -f "$dir/settings.save" ]] || continue
    rel="${dir#$REAL_SAVE_ROOT/}"
    mkdir -p "$target_root/$rel"
    cp "$dir/settings.save" "$target_root/$rel/settings.save"
    [[ -f "$dir/profile.save" ]] && cp "$dir/profile.save" "$target_root/$rel/profile.save"
    if [[ -n "$ONLY_MODS" ]]; then
      python3 - "$target_root/$rel/settings.save" "$ONLY_MODS" "$MODS_DIR" "$WORKSHOP_DIR" <<'PY'
import json, pathlib, sys
path, mods, mods_dir, workshop_dir = sys.argv[1], [m for m in sys.argv[2].split(",") if m], sys.argv[3], sys.argv[4]
# 没列在设置里的模组默认启用：把本机找得到的其他模组在两种来源下都显式禁用。
ids = set()
for root in (mods_dir, workshop_dir):
    for manifest in pathlib.Path(root).glob("*/*.json") if pathlib.Path(root).is_dir() else []:
        try:
            mod_id = json.load(open(manifest, encoding="utf-8")).get("id")
        except Exception:
            continue
        if isinstance(mod_id, str):
            ids.add(mod_id)
entries = [{"id": m, "is_enabled": True, "source": "mods_directory"} for m in mods]
for mod_id in sorted(ids - set(mods)):
    for source in ("mods_directory", "steam_workshop"):
        entries.append({"id": mod_id, "is_enabled": False, "source": source})
for mod_id in mods:
    entries.append({"id": mod_id, "is_enabled": False, "source": "steam_workshop"})
data = json.load(open(path))
data["mod_settings"] = {"mod_list": entries, "mods_enabled": True}
json.dump(data, open(path, "w"))
PY
    fi
  done
}
copy_settings "$CLIENT_SAVE_ROOT"
[[ -n "$ONLY_MODS" ]] && copy_settings "$HOST_HOME/Library/Application Support/SlayTheSpire2"

print "== 构建驱动"
# 先落日志再判 dotnet 自身的退出码:管道接 grep 会吞掉构建失败。
BUILD_LOG="$OUT/mplab-build.log"
if ! dotnet build "$LAB_DIR/HextechMpLab.csproj" -c Release -nologo -v q -p:NuGetAudit=false > "$BUILD_LOG" 2>&1; then
  grep -E "error|Error\(s\)" "$BUILD_LOG" >&2 || true
  print -u2 "驱动构建失败,完整日志: $BUILD_LOG"
  exit 1
fi
grep -E "error|Error\(s\)" "$BUILD_LOG" || true
LAB_DLL="$LAB_DIR/bin/Release/net9.0/HextechMpLab.dll"
[[ -f "$LAB_DLL" ]] || { print -u2 "驱动 DLL 未生成: $LAB_DLL"; exit 1; }

print "== 部署驱动模组(实验结束自动移除)"
mkdir -p "$LAB_MOD_DIR"
cp "$LAB_DLL" "$LAB_MOD_DIR/HextechMpLab.dll"
cp "$LAB_DIR/HextechMpLab.json" "$LAB_MOD_DIR/HextechMpLab.json"

created_appid=0
if [[ ! -f "$STEAM_APPID_FILE" ]]; then
  printf '%s\n' "$STEAM_APP_ID" > "$STEAM_APPID_FILE"; created_appid=1
fi

host_pid=""; client_pid=""
cleanup() {
  for pid in "$host_pid" "$client_pid"; do
    [[ -n "$pid" ]] && kill "$pid" 2>/dev/null || true
  done
  sleep 2
  for pid in "$host_pid" "$client_pid"; do
    [[ -n "$pid" ]] && kill -9 "$pid" 2>/dev/null || true
  done
  rm -rf "$LAB_MOD_DIR"
  [[ "$created_appid" == 1 ]] && rm -f "$STEAM_APPID_FILE"
  print "已移除驱动模组: $LAB_MOD_DIR"
}
trap cleanup EXIT

print "== 启动主机"
(
  cd "$GAME_DIR"
  HOME="$HOST_HOME" HEXTECH_MPLAB_ROLE=host HEXTECH_MPLAB_PLAYERS=2 HEXTECH_MPLAB_SEED="$SEED" HEXTECH_MPLAB_MAX_SEC="$TIMEOUT_SEC" \
    exec "$GAME_BIN" --headless --fastmp=host_standard --quit-after "$QUIT_AFTER" --log-file "$OUT/host.log" > "$OUT/host.stdout" 2>&1
) &
host_pid=$!
sleep "$CLIENT_DELAY_SEC"

print "== 启动客户端(独立 HOME)"
(
  cd "$GAME_DIR"
  HOME="$CLIENT_HOME" HEXTECH_MPLAB_ROLE=client HEXTECH_MPLAB_PLAYERS=2 HEXTECH_MPLAB_MAX_SEC="$TIMEOUT_SEC" \
    exec "$GAME_BIN" --headless --fastmp=join --clientId=1001 --quit-after "$QUIT_AFTER" --log-file "$OUT/client.log" > "$OUT/client.stdout" 2>&1
) &
client_pid=$!

elapsed=0
while (( elapsed < TIMEOUT_SEC )); do
  if ! kill -0 "$host_pid" 2>/dev/null && ! kill -0 "$client_pid" 2>/dev/null; then
    break
  fi
  sleep 5; elapsed=$((elapsed + 5))
done
print "== 运行结束(耗时 ${elapsed}s)"

for side in host client; do
  f="$OUT/$side.log"
  print -- "--- $side: MpLab 阶段"
  grep -a "\[MpLab\]" "$f" 2>/dev/null | sed 's/^.*\[MpLab\]/  [MpLab]/' | tail -25 || true
  print -- "--- $side: 形态批处理 / 分叉"
  grep -a "FormBatch\|State divergence\|divergence\|Desync\|checksum" "$f" 2>/dev/null | grep -v "Generating checksum" | cut -c1-200 | tail -12 || true
done
print "日志: $OUT/host.log  $OUT/client.log"
