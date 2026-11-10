# 遥测服务部署与数据布局

- app：`/opt/hextech-runes-telemetry/app`，部署 `server.js`、`community.js`、`http-util.js`、`labels.json`、`public/`，以及可选的 `sensitive-words.txt`（社区配置的敏感词表，缺失时不做敏感词过滤）。改完先 `node --check server.js` 再重启 `hextech-runes-telemetry`。
- data：`/opt/hextech-runes-telemetry/data`，属主 `hextelemetry`。服务器需要装 `zstd`。

## 原始库

- `run_results.jsonl` 是当前写入段，每局一行，按 runId 去重、后写覆盖。
- 当前段超过 `RESULTS_ROTATE_BYTES`（默认 2 GiB）时，服务在线把它改名为 `archive/run_results-<UTC时间>.jsonl`，开新段并把 summary 检查点切到新段开头，同时把近期 runId 窗口写进 `recent-run-ids.json`，重启后仍能拦住滚动前已收过的重传。
- 改名后的明文段由后台 `nice -n 19 zstd -19 -T1` 压成 `.jsonl.zst`（约 1/9），校验通过才删除明文；服务重启会打断压缩，下次启动从明文重来。进度看 `/health` 的 `archive` 字段。
- 抽数时按时间顺序读全部段：

  ```bash
  cd /opt/hextech-runes-telemetry/data
  { zstdcat archive/*.jsonl.zst; cat archive/*.jsonl run_results.jsonl 2>/dev/null; } | LC_ALL=C grep -F '"modVersion":"0.9.7"'
  ```

  未压完的段是明文 `.jsonl`，上面的命令两种都读。需要严格按时间顺序时，按文件名排序逐段读。

## 全量重建

停服后执行，重建会依次读所有归档段和当前段，检查点设在当前段末尾：

```bash
systemctl stop hextech-runes-telemetry
runuser -u hextelemetry -- env DATA_DIR=/opt/hextech-runes-telemetry/data node /opt/hextech-runes-telemetry/app/server.js --rebuild-derived
systemctl start hextech-runes-telemetry
```

重建锁被占用时命令以非零退出；确认没有其他重建在跑后删掉 `derived/.summary-rebuild.lock` 再执行。
