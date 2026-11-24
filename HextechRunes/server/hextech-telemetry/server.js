const http = require("node:http");
const fs = require("node:fs");
const readline = require("node:readline");
const { spawn } = require("node:child_process");
const path = require("node:path");
const crypto = require("node:crypto");
const community = require("./community");
const { sendJson, readBody, writeFileAtomic } = require("./http-util");

const HOST = process.env.HOST || "127.0.0.1";
const PORT = Number.parseInt(process.env.PORT || "3000", 10);
const DATA_DIR = process.env.DATA_DIR || path.join(__dirname, "data");
const PUBLIC_DIR = process.env.PUBLIC_DIR || path.join(__dirname, "public");
const DERIVED_DIR = path.join(DATA_DIR, "derived");
const SUMMARY_FILE = path.join(DERIVED_DIR, "summary.json");
const REBUILD_LOCK_FILE = path.join(DERIVED_DIR, ".summary-rebuild.lock");
const LABELS_FILE = path.join(__dirname, "labels.json");
const LATEST_VERSION_FILE = path.join(PUBLIC_DIR, "latest-version.json");
const RESULTS_FILE = path.join(DATA_DIR, "run_results.jsonl");
// 原始库按大小滚动：写满的段改名进 archive/，后台用 zstd 压成 .jsonl.zst，校验通过后删除明文段。
const ARCHIVE_DIR = path.join(DATA_DIR, "archive");
const ARCHIVE_SEGMENT_PATTERN = /^run_results-\d{8}T\d{6}Z\.jsonl(\.zst)?$/;
const RECENT_RUN_IDS_FILE = path.join(DATA_DIR, "recent-run-ids.json");
const parsedRotateBytes = Number.parseInt(process.env.RESULTS_ROTATE_BYTES || String(2 * 1024 * 1024 * 1024), 10);
const RESULTS_ROTATE_BYTES = Number.isFinite(parsedRotateBytes) ? Math.max(1024, parsedRotateBytes) : 2 * 1024 * 1024 * 1024;
const ZSTD_BIN = process.env.ZSTD_BIN || "zstd";
const ZSTD_LEVEL = /^\d{1,2}$/.test(process.env.ZSTD_LEVEL || "") ? process.env.ZSTD_LEVEL : "19";
const ARCHIVE_RETRY_DELAY_MS = 60 * 60 * 1000;
const MAX_BODY_BYTES = 256 * 1024;
const MIN_RUN_TIME_FOR_DEFAULT_STATS = 60;
const parsedSummaryFlushIntervalMs = Number.parseInt(process.env.SUMMARY_FLUSH_INTERVAL_MS || "300000", 10);
const SUMMARY_FLUSH_INTERVAL_MS = Number.isFinite(parsedSummaryFlushIntervalMs) ? Math.max(1000, parsedSummaryFlushIntervalMs) : 300000;
const parsedRecentRunIdLimit = Number.parseInt(process.env.RECENT_RUN_ID_LIMIT || "250000", 10);
const RECENT_RUN_ID_LIMIT = Number.isFinite(parsedRecentRunIdLimit) ? Math.max(1000, parsedRecentRunIdLimit) : 250000;
const parsedRecentRunIdTailBytes = Number.parseInt(process.env.RECENT_RUN_ID_TAIL_BYTES || String(64 * 1024 * 1024), 10);
const RECENT_RUN_ID_TAIL_BYTES = Number.isFinite(parsedRecentRunIdTailBytes) ? Math.max(1024 * 1024, parsedRecentRunIdTailBytes) : 64 * 1024 * 1024;
const INCREMENTAL_SCHEMA_VERSION = 1;

fs.mkdirSync(DATA_DIR, { recursive: true });
fs.mkdirSync(DERIVED_DIR, { recursive: true });

const LABELS = loadLabels();
const isRebuildProcess = process.argv.includes("--rebuild-derived");
const recentRunIds = isRebuildProcess ? new Map() : loadRecentRunIds();
const summaryCache = new Map();
const derivedState = {
  dirty: false,
  flushing: false,
  timer: null,
  lastBuiltAtMs: 0,
  lastError: null,
  pendingRecords: 0,
  persistedOffset: 0
};
let summaryBundle = null;
let resultsEndOffset = fs.existsSync(RESULTS_FILE) ? fs.statSync(RESULTS_FILE).size : 0;
const archiveState = {
  rotateRetryAtMs: 0,
  compressing: null,
  retryTimer: null,
  lastError: null
};

function loadLabels() {
  try {
    if (fs.existsSync(LABELS_FILE)) {
      return JSON.parse(fs.readFileSync(LABELS_FILE, "utf8"));
    }
  } catch (error) {
    console.warn(`failed to load labels: ${error.message}`);
  }
  return {};
}

function getLabel(category, id) {
  if (!id) {
    return "";
  }
  return LABELS?.[category]?.[id] || id;
}

// labels.json 的 runes / monsterHexes 即官方内容清单（本体 + 拓展包，含已退役条目）。
// 其他模组通过对接接口注册的海克斯也会以模型 ID 上报，它们不在清单里。
function isOfficialContent(category, id) {
  return Boolean(id && LABELS?.[category]?.[id]);
}

function parseOfficialOnly(value) {
  return value === "1" || value === "true";
}

// 0.10.2 起其他模组也能注册敌方海克斯（以展示遗物的模型 ID 上报），所以三张表都按官方清单过滤；
// 旧客户端偶尔把不认识的敌方编号报成数字，这些行同样不在清单里，仅官方时一并隐藏。
const CONTENT_TABLES = [
  ["playerRuneRuns", "runes"],
  ["playerRuneChoices", "runes"],
  ["monsterHexRuns", "monsterHexes"]
];

// 派生快照里的中文名是生成时写入的；展示时按当前 labels 重新标注，新增内容的中文名在服务重启后立即生效。
// officialOnly 时去掉不在官方清单里的我方与敌方海克斯条目（只影响海克斯表，局数与胜率口径不变）。
function prepareSummaryForDisplay(summary, officialOnly) {
  const tables = { ...summary.tables };
  let hiddenUnofficialEntries = 0;
  for (const [tableName, category] of CONTENT_TABLES) {
    const rows = Array.isArray(tables[tableName]) ? tables[tableName] : [];
    const relabeled = rows.map((row) => ({
      ...row,
      name: getLabel(category, row.id),
      official: isOfficialContent(category, row.id)
    }));
    const visible = officialOnly ? relabeled.filter((row) => row.official) : relabeled;
    hiddenUnofficialEntries += relabeled.length - visible.length;
    tables[tableName] = visible;
  }
  return {
    ...summary,
    filters: { ...summary.filters, officialOnly },
    hiddenUnofficialEntries,
    tables
  };
}

function displayLabel(row) {
  if (!row || !row.name || row.name === row.id) {
    return row?.id || "";
  }
  return `${row.name} (${row.id})`;
}

function loadRecentRunIds() {
  const ids = new Map();
  // 滚动后当前段很短，读不到足够的尾部；滚动时落盘的窗口补上旧段末尾的 runId。
  try {
    if (fs.existsSync(RECENT_RUN_IDS_FILE)) {
      for (const runId of JSON.parse(fs.readFileSync(RECENT_RUN_IDS_FILE, "utf8"))) {
        if (typeof runId === "string" && runId.length > 0) {
          rememberRunId(ids, runId);
        }
      }
    }
  } catch (error) {
    console.warn(`failed to load ${RECENT_RUN_IDS_FILE}: ${error.message}`);
  }
  if (!fs.existsSync(RESULTS_FILE)) {
    return ids;
  }

  const fileSize = fs.statSync(RESULTS_FILE).size;
  const startOffset = Math.max(0, fileSize - RECENT_RUN_ID_TAIL_BYTES);
  forEachJsonlLineFromOffset(RESULTS_FILE, startOffset, (line) => {
    try {
      const record = JSON.parse(line);
      const runId = record?.payload?.run?.runId;
      if (typeof runId === "string" && runId.length > 0) {
        rememberRunId(ids, runId);
      }
    } catch {
      // 损坏的历史尾行由增量回放统计；近期去重窗口只收录有效 runId。
    }
  });
  return ids;
}

function rememberRunId(ids, runId) {
  if (ids.has(runId)) {
    ids.delete(runId);
  }
  ids.set(runId, true);
  while (ids.size > RECENT_RUN_ID_LIMIT) {
    ids.delete(ids.keys().next().value);
  }
}

function sendText(res, status, value) {
  res.writeHead(status, {
    "content-type": "text/plain; charset=utf-8",
    "cache-control": "no-store"
  });
  res.end(value);
}

function sendFile(res, filePath, contentType) {
  if (!fs.existsSync(filePath) || fs.statSync(filePath).isDirectory()) {
    return sendText(res, 404, "not found");
  }

  res.writeHead(200, {
    "content-type": contentType,
    "cache-control": "no-store"
  });
  fs.createReadStream(filePath).pipe(res);
}

function sendHtml(res, status, body) {
  res.writeHead(status, {
    "content-type": "text/html; charset=utf-8",
    "cache-control": "no-store",
    "content-length": Buffer.byteLength(body)
  });
  res.end(body);
}

function readLatestVersionInfo() {
  try {
    if (fs.existsSync(LATEST_VERSION_FILE)) {
      return JSON.parse(fs.readFileSync(LATEST_VERSION_FILE, "utf8"));
    }
  } catch (error) {
    console.warn(`failed to load latest version info: ${error.message}`);
  }
  return null;
}

function validatePayload(payload) {
  if (!payload || typeof payload !== "object") {
    return "payload must be an object";
  }
  if (payload.schemaVersion !== 1) {
    return "unsupported schemaVersion";
  }
  if (payload.modId !== "HextechRunes") {
    return "modId must be HextechRunes";
  }
  if (!payload.run || typeof payload.run !== "object") {
    return "run is required";
  }
  if (typeof payload.run.runId !== "string" || payload.run.runId.length < 16) {
    return "run.runId is required";
  }
  if (typeof payload.run.seedHash !== "string" || payload.run.seedHash.length < 16) {
    return "run.seedHash is required";
  }
  if (typeof payload.run.isVictory !== "boolean") {
    return "run.isVictory must be boolean";
  }
  if (!Array.isArray(payload.players) || !Array.isArray(payload.runeChoices) || !Array.isArray(payload.monsterHexes)) {
    return "players, runeChoices, and monsterHexes must be arrays";
  }
  return null;
}

async function handleIngest(req, res) {
  let payload;
  try {
    payload = JSON.parse(await readBody(req, MAX_BODY_BYTES));
  } catch (error) {
    return sendJson(res, error.statusCode || 400, { ok: false, error: error.message || "invalid json" });
  }

  const validationError = validatePayload(payload);
  if (validationError) {
    return sendJson(res, 400, { ok: false, error: validationError });
  }

  const runId = payload.run.runId;
  if (recentRunIds.has(runId)) {
    return sendJson(res, 202, { ok: true, duplicate: true, runId });
  }

  const record = {
    receivedAtUtc: new Date().toISOString(),
    payloadHash: sha256(JSON.stringify(payload)),
    payload
  };
  const serializedRecord = `${JSON.stringify(record)}\n`;
  fs.appendFileSync(RESULTS_FILE, serializedRecord, "utf8");
  resultsEndOffset += Buffer.byteLength(serializedRecord);
  rememberRunId(recentRunIds, runId);
  applyRecordToSummaryBundle(summaryBundle, record);
  markDerivedDirty(resultsEndOffset);
  scheduleSummaryFlush();
  maybeRotateResults();
  return sendJson(res, 202, { ok: true, duplicate: false, runId });
}

function sha256(value) {
  return crypto.createHash("sha256").update(value).digest("hex");
}

// includeUnterminatedTail:全量重建把文件末尾没有换行的最后一行也当成一行处理(完整 JSON 照常计入,
// 截断的计为格式错误),因为重建的检查点设在文件末尾,这一行之后不会再被回放。增量回放不开启:
// 末尾半行可能还在写入中,检查点停在最后一个换行处。
function forEachJsonlLineFromOffset(filePath, requestedOffset, callback, { includeUnterminatedTail = false } = {}) {
  if (!fs.existsSync(filePath)) {
    return { lineCount: 0, endOffset: 0 };
  }

  const fileSize = fs.statSync(filePath).size;
  const startOffset = Math.max(0, Math.min(requestedOffset, fileSize));
  const fd = fs.openSync(filePath, "r");
  const buffer = Buffer.allocUnsafe(1024 * 1024);
  let carry = Buffer.alloc(0);
  let readOffset = startOffset;
  let lineOffset = startOffset;
  let lineCount = 0;
  let skipPartialLine = false;
  try {
    if (startOffset > 0) {
      const previousByte = Buffer.allocUnsafe(1);
      skipPartialLine = fs.readSync(fd, previousByte, 0, 1, startOffset - 1) === 1 && previousByte[0] !== 10;
    }
    while (readOffset < fileSize) {
      const bytesRead = fs.readSync(fd, buffer, 0, Math.min(buffer.length, fileSize - readOffset), readOffset);
      if (bytesRead <= 0) {
        break;
      }
      readOffset += bytesRead;
      const chunk = carry.length > 0
        ? Buffer.concat([carry, buffer.subarray(0, bytesRead)])
        : buffer.subarray(0, bytesRead);
      let lineStart = 0;
      for (let i = 0; i < chunk.length; i++) {
        if (chunk[i] !== 10) {
          continue;
        }
        const nextOffset = lineOffset + i + 1;
        if (skipPartialLine) {
          skipPartialLine = false;
        } else {
          let line = chunk.subarray(lineStart, i);
          if (line.length > 0 && line[line.length - 1] === 13) {
            line = line.subarray(0, line.length - 1);
          }
          if (line.toString("utf8").trim().length > 0) {
            callback(line.toString("utf8"), lineOffset + lineStart, nextOffset);
            lineCount += 1;
          }
        }
        lineStart = i + 1;
      }
      carry = Buffer.from(chunk.subarray(lineStart));
      lineOffset = readOffset - carry.length;
    }

    if (includeUnterminatedTail && carry.length > 0 && !skipPartialLine) {
      let line = carry;
      if (line[line.length - 1] === 13) {
        line = line.subarray(0, line.length - 1);
      }
      if (line.toString("utf8").trim().length > 0) {
        callback(line.toString("utf8"), lineOffset, readOffset);
        lineCount += 1;
      }
      lineOffset = readOffset;
    }
  } finally {
    fs.closeSync(fd);
  }

  return { lineCount, endOffset: lineOffset };
}

// 归档段按文件名（时间戳）排序，最后是当前段。同一段既有明文又有 .zst 时（压缩尚未收尾）读明文。
function listResultSegments() {
  const segments = [];
  if (fs.existsSync(ARCHIVE_DIR)) {
    const names = fs.readdirSync(ARCHIVE_DIR).filter((name) => ARCHIVE_SEGMENT_PATTERN.test(name));
    const byBase = new Map();
    for (const name of names) {
      const base = name.replace(/\.zst$/, "");
      const entry = byBase.get(base) || {};
      if (name.endsWith(".zst")) {
        entry.compressed = path.join(ARCHIVE_DIR, name);
      } else {
        entry.plain = path.join(ARCHIVE_DIR, name);
      }
      byBase.set(base, entry);
    }
    for (const base of [...byBase.keys()].sort()) {
      const entry = byBase.get(base);
      segments.push(entry.plain
        ? { path: entry.plain, compressed: false }
        : { path: entry.compressed, compressed: true });
    }
  }
  segments.push({ path: RESULTS_FILE, compressed: false });
  return segments;
}

function forEachZstdLine(filePath, callback) {
  return new Promise((resolve, reject) => {
    const child = spawn(ZSTD_BIN, ["-dcq", filePath], { stdio: ["ignore", "pipe", "pipe"] });
    let stderr = "";
    let lineNumber = 0;
    let callbackError = null;
    child.stderr.on("data", (chunk) => {
      stderr += chunk;
    });
    child.on("error", reject);
    const lines = readline.createInterface({ input: child.stdout, crlfDelay: Infinity });
    lines.on("line", (line) => {
      lineNumber += 1;
      if (callbackError || line.trim().length === 0) {
        return;
      }
      try {
        callback(line, lineNumber);
      } catch (error) {
        callbackError = error;
        child.kill();
      }
    });
    child.on("close", (code) => {
      if (callbackError) {
        reject(callbackError);
      } else if (code !== 0) {
        reject(new Error(`zstd failed to read ${filePath} (exit ${code}): ${stderr.trim()}`));
      } else {
        resolve();
      }
    });
  });
}

// 两遍扫描用同一份段列表与同一种读取方式，键为 段序号×2^40 + 段内位置（明文段是字节偏移、压缩段是行号），
// 用数字而不是字符串，千万级记录的索引才放得进服务机内存。段在两遍之间被压缩或删除时直接报错。
const SEGMENT_KEY_STRIDE = 2 ** 40;

async function forEachResultsLine(segments, callback) {
  for (let index = 0; index < segments.length; index++) {
    const segment = segments[index];
    if (!fs.existsSync(segment.path)) {
      throw new Error(`results segment disappeared during rebuild: ${segment.path}; stop the service before --rebuild-derived`);
    }
    if (segment.compressed) {
      await forEachZstdLine(segment.path, (line, lineNumber) => callback(line, index * SEGMENT_KEY_STRIDE + lineNumber));
    } else {
      forEachJsonlLineFromOffset(segment.path, 0, (line, offset) => callback(line, index * SEGMENT_KEY_STRIDE + offset), { includeUnterminatedTail: true });
    }
  }
}

async function buildRecordIndex(segments) {
  const latestKeysByRunId = new Map();
  let physicalLines = 0;
  let malformedLines = 0;
  await forEachResultsLine(segments, (line, key) => {
    physicalLines += 1;
    try {
      const record = JSON.parse(line);
      const runId = record?.payload?.run?.runId;
      if (typeof runId === "string" && runId.length > 0) {
        latestKeysByRunId.set(runId, key);
      }
    } catch {
      malformedLines += 1;
    }
  });

  return {
    latestKeys: new Set(latestKeysByRunId.values()),
    physicalLines,
    duplicateLines: Math.max(0, physicalLines - latestKeysByRunId.size - malformedLines),
    malformedLines,
    totalUniqueRuns: latestKeysByRunId.size
  };
}

async function forEachLatestRecord(segments, recordIndex, callback) {
  await forEachResultsLine(segments, (line, key) => {
    if (!recordIndex.latestKeys.has(key)) {
      return;
    }
    try {
      callback(JSON.parse(line));
    } catch {
      // Malformed latest lines are already counted during indexing.
    }
  });
}

function getRun(record) {
  return record?.payload?.run || {};
}

function normalizeAggregatedModVersion(version) {
  // 持久化聚合规则:把四段式热修版本(a.b.c.d，如 0.8.2.1 / 0.5.3.1)始终并入对应的三段式
  // 基础版本(a.b.c，如 0.8.2 / 0.5.3)再做统计，使热修子版本与其基础版本合并展示。
  // 带非数字后缀的版本(如 0.8.1hf1)不匹配此规则，保持原样。
  const match = /^(\d+\.\d+\.\d+)\.\d+$/.exec(version);
  return match ? match[1] : version;
}

function getModVersion(record) {
  const version = record?.payload?.modVersion;
  if (typeof version !== "string" || version.trim().length === 0) {
    return "(unknown)";
  }
  return normalizeAggregatedModVersion(version.trim());
}

function normalizeVersionFilter(value) {
  if (typeof value !== "string") {
    return null;
  }
  const trimmed = value.trim();
  if (!trimmed || trimmed === "all") {
    return null;
  }
  return trimmed;
}

function getRunTime(record) {
  const runTime = Number(getRun(record).runTime);
  return Number.isFinite(runTime) ? runTime : 0;
}

function isDefaultEligible(record) {
  return getRunTime(record) >= MIN_RUN_TIME_FOR_DEFAULT_STATS;
}

function addCounter(map, key, isVictory) {
  if (!key) {
    return;
  }
  if (!map[key]) {
    map[key] = { runs: 0, wins: 0 };
  }
  map[key].runs += 1;
  if (isVictory) {
    map[key].wins += 1;
  }
}

function addMonsterCounter(map, key, isPlayerVictory) {
  if (!key) {
    return;
  }
  if (!map[key]) {
    map[key] = { runs: 0, wins: 0, playerWins: 0, monsterWins: 0 };
  }
  map[key].runs += 1;
  if (isPlayerVictory) {
    map[key].wins += 1;
    map[key].playerWins += 1;
  } else {
    map[key].monsterWins += 1;
  }
}

function addChoiceCounter(map, key, field, isVictory) {
  if (!key) {
    return;
  }
  if (!map[key]) {
    map[key] = { offered: 0, selected: 0, selectedWins: 0 };
  }
  map[key][field] += 1;
  if (field === "selected" && isVictory) {
    map[key].selectedWins += 1;
  }
}

function addSimpleCounter(map, key) {
  if (!key) {
    return;
  }
  map[key] = (map[key] || 0) + 1;
}

function createEmptySummary(versionFilter, recordIndex) {
  return {
    generatedAtUtc: new Date().toISOString(),
    filters: {
      minRunTimeForDefaultStats: MIN_RUN_TIME_FOR_DEFAULT_STATS,
      defaultExcludes: ["short_run"],
      version: versionFilter || "all"
    },
    versionFilter: versionFilter || "all",
    availableVersions: [],
    raw: {
      physicalLines: recordIndex.physicalLines,
      uniqueRuns: 0,
      totalUniqueRuns: recordIndex.totalUniqueRuns,
      duplicateLines: recordIndex.duplicateLines,
      malformedLines: recordIndex.malformedLines
    },
    runCount: 0,
    winCount: 0,
    winRate: 0,
    excludedShortRuns: 0,
    playerRuneRuns: {},
    playerRuneChoices: {},
    monsterHexRuns: {},
    versions: {},
    netModes: {},
    characters: {},
    tables: {
      playerRuneRuns: [],
      playerRuneChoices: [],
      monsterHexRuns: [],
      versions: [],
      netModes: [],
      characters: []
    }
  };
}

function createUnavailableSummary(versionFilter) {
  const summary = createEmptySummary(versionFilter, {
    physicalLines: 0,
    totalUniqueRuns: 0,
    duplicateLines: 0,
    malformedLines: 0
  });
  summary.generatedAtUtc = null;
  summary.pendingRefresh = true;
  return finalizeSummary(summary, []);
}

function addRecordToSummary(summary, record, recordVersion, isEligible) {
  summary.raw.uniqueRuns += 1;
  if (!isEligible) {
    summary.excludedShortRuns += 1;
    return;
  }

  const payload = record.payload || {};
  const run = payload.run || {};
  const isVictory = run.isVictory === true;
  if (isVictory) {
    summary.winCount += 1;
  }
  summary.runCount += 1;
  addSimpleCounter(summary.versions, recordVersion);
  addSimpleCounter(summary.netModes, run.netMode || "(unknown)");

  for (const player of payload.players || []) {
    const character = player.character || "";
    addSimpleCounter(summary.characters, character || "(unknown)");
    for (const rune of Array.isArray(player.hextechRunes) ? player.hextechRunes : []) {
      addCounter(summary.playerRuneRuns, rune, isVictory);
    }
  }

  for (const choice of payload.runeChoices || []) {
    const options = Array.isArray(choice.options) ? choice.options : [];
    const selected = typeof choice.selected === "string" ? choice.selected : "";
    for (const option of options) {
      const isSelected = option === selected;
      addChoiceCounter(summary.playerRuneChoices, option, "offered", isVictory);
      if (isSelected) {
        addChoiceCounter(summary.playerRuneChoices, option, "selected", isVictory);
      }
    }
    if (selected && !options.includes(selected)) {
      addChoiceCounter(summary.playerRuneChoices, selected, "offered", isVictory);
      addChoiceCounter(summary.playerRuneChoices, selected, "selected", isVictory);
    }
  }

  for (const monsterHex of payload.monsterHexes || []) {
    addMonsterCounter(summary.monsterHexRuns, monsterHex.hex, isVictory);
  }
}

function finalizeSummary(summary, availableVersions) {
  summary.winRate = pctNumber(summary.winCount, summary.runCount);
  summary.availableVersions = availableVersions;
  summary.tables.playerRuneRuns = buildRateRows(summary.playerRuneRuns, "runes");
  summary.tables.playerRuneChoices = buildChoiceRows(summary.playerRuneChoices, "runes");
  summary.tables.monsterHexRuns = buildMonsterRows(summary.monsterHexRuns, "monsterHexes");
  summary.tables.versions = buildCountRows(summary.versions);
  summary.tables.netModes = buildCountRows(summary.netModes, "netModes");
  summary.tables.characters = buildCountRows(summary.characters, "characters");
  return summary;
}

async function buildSummaryBundle() {
  const segments = listResultSegments();
  const recordIndex = await buildRecordIndex(segments);
  const allSummary = createEmptySummary(null, recordIndex);
  const byVersion = {};
  const availableVersionCounts = {};

  await forEachLatestRecord(segments, recordIndex, (record) => {
    const recordVersion = getModVersion(record);
    const isEligible = isDefaultEligible(record);
    if (!byVersion[recordVersion]) {
      byVersion[recordVersion] = createEmptySummary(recordVersion, recordIndex);
    }
    if (isEligible) {
      addSimpleCounter(availableVersionCounts, recordVersion);
    }
    addRecordToSummary(allSummary, record, recordVersion, isEligible);
    addRecordToSummary(byVersion[recordVersion], record, recordVersion, isEligible);
  });

  const availableVersions = buildCountRows(availableVersionCounts);
  finalizeSummary(allSummary, availableVersions);
  for (const summary of Object.values(byVersion)) {
    finalizeSummary(summary, availableVersions);
  }

  allSummary.versionSummaries = byVersion;
  return allSummary;
}

function getResultsIdentity() {
  if (!fs.existsSync(RESULTS_FILE)) {
    fs.closeSync(fs.openSync(RESULTS_FILE, "a"));
  }
  const stat = fs.statSync(RESULTS_FILE);
  return {
    device: String(stat.dev),
    inode: String(stat.ino),
    size: stat.size
  };
}

function setIncrementalCheckpoint(summary, identity, offset) {
  summary._incremental = {
    schemaVersion: INCREMENTAL_SCHEMA_VERSION,
    source: {
      device: identity.device,
      inode: identity.inode,
      offset
    }
  };
}

function createFreshSummaryBundle() {
  const summary = createEmptySummary(null, {
    physicalLines: 0,
    totalUniqueRuns: 0,
    duplicateLines: 0,
    malformedLines: 0
  });
  finalizeSummary(summary, []);
  summary.versionSummaries = {};
  return summary;
}

function initializeIncrementalSummary() {
  const identity = getResultsIdentity();
  let summary = readDerivedSummary();
  if (!summary) {
    if (identity.size > 0) {
      throw new Error("run_results.jsonl contains data but no summary checkpoint; run --rebuild-derived or restore summary.json first");
    }
    summary = createFreshSummaryBundle();
    setIncrementalCheckpoint(summary, identity, 0);
    writeFileAtomic(SUMMARY_FILE, `${JSON.stringify(summary, null, 2)}\n`);
    derivedState.persistedOffset = 0;
    return summary;
  }

  const checkpoint = summary._incremental;
  if (!checkpoint && identity.size === 0) {
    // 原始库为空时没有可回放的数据,旧 summary 直接从 offset 0 补检查点。
    setIncrementalCheckpoint(summary, identity, 0);
    writeFileAtomic(SUMMARY_FILE, `${JSON.stringify(summary, null, 2)}\n`);
    return summary;
  }
  if (checkpoint?.schemaVersion !== INCREMENTAL_SCHEMA_VERSION) {
    throw new Error(`summary.json has no supported incremental checkpoint (schema ${checkpoint?.schemaVersion}); stop the service and run node server.js --rebuild-derived`);
  }
  if (checkpoint.source?.device !== identity.device || checkpoint.source?.inode !== identity.inode) {
    // 滚动在改名之后、summary 落盘之前中断：检查点仍指向已改名进 archive/ 的明文段。补完它再从当前段开头接着回放。
    const rotated = findArchivedPlainSegment(checkpoint.source);
    if (!rotated) {
      throw new Error("run_results.jsonl identity differs from summary checkpoint; stop the service and run node server.js --rebuild-derived");
    }
    summary.versionSummaries ||= {};
    replayIncrementalTail(summary, checkpoint.source.offset, rotated.identity, rotated.path);
    setIncrementalCheckpoint(summary, identity, 0);
    writeFileAtomic(SUMMARY_FILE, `${JSON.stringify(summary, null, 2)}\n`);
    derivedState.persistedOffset = 0;
    replayIncrementalTail(summary, 0, identity);
    return summary;
  }
  if (!Number.isSafeInteger(checkpoint.source.offset) || checkpoint.source.offset < 0 || checkpoint.source.offset > identity.size) {
    throw new Error("summary checkpoint offset is outside run_results.jsonl");
  }
  summary.versionSummaries ||= {};
  replayIncrementalTail(summary, checkpoint.source.offset, identity);
  return summary;
}

function findArchivedPlainSegment(source) {
  if (!fs.existsSync(ARCHIVE_DIR)) {
    return null;
  }
  for (const name of fs.readdirSync(ARCHIVE_DIR)) {
    if (!ARCHIVE_SEGMENT_PATTERN.test(name) || name.endsWith(".zst")) {
      continue;
    }
    const filePath = path.join(ARCHIVE_DIR, name);
    const stat = fs.statSync(filePath);
    if (String(stat.dev) === source?.device && String(stat.ino) === source?.inode) {
      return { path: filePath, identity: { device: String(stat.dev), inode: String(stat.ino), size: stat.size } };
    }
  }
  return null;
}

function archiveTimestamp(date = new Date()) {
  return date.toISOString().replace(/[-:]/g, "").replace(/\.\d{3}Z$/, "Z");
}

// 滚动失败不能影响写入：记下错误，一小时后再试；在那之前当前段照常追加。
function maybeRotateResults() {
  if (resultsEndOffset < RESULTS_ROTATE_BYTES || Date.now() < archiveState.rotateRetryAtMs) {
    return;
  }
  try {
    rotateResults();
  } catch (error) {
    archiveState.lastError = `rotate failed: ${error?.message || error}`;
    archiveState.rotateRetryAtMs = Date.now() + ARCHIVE_RETRY_DELAY_MS;
    console.error(archiveState.lastError);
  }
}

// 在一次同步调用里完成改名、建新段与检查点切换，期间不会有上传插进来。
// 先落盘近期 runId 窗口：重启后新段太短，单靠它的尾部拦不住刚滚动前已收过的重传。
function rotateResults() {
  fs.mkdirSync(ARCHIVE_DIR, { recursive: true });
  let segmentPath = path.join(ARCHIVE_DIR, `run_results-${archiveTimestamp()}.jsonl`);
  if (fs.existsSync(segmentPath) || fs.existsSync(`${segmentPath}.zst`)) {
    segmentPath = path.join(ARCHIVE_DIR, `run_results-${archiveTimestamp(new Date(Date.now() + 1000))}.jsonl`);
  }
  writeFileAtomic(RECENT_RUN_IDS_FILE, `${JSON.stringify([...recentRunIds.keys()])}\n`);
  fs.renameSync(RESULTS_FILE, segmentPath);
  fs.closeSync(fs.openSync(RESULTS_FILE, "a", 0o640));
  resultsEndOffset = 0;
  setIncrementalCheckpoint(summaryBundle, getResultsIdentity(), 0);
  derivedState.dirty = true;
  flushSummaryNow();
  console.log(`rotated results into ${segmentPath}`);
  compressPendingArchives();
}

function listPlainArchiveSegments() {
  if (!fs.existsSync(ARCHIVE_DIR)) {
    return [];
  }
  return fs.readdirSync(ARCHIVE_DIR)
    .filter((name) => ARCHIVE_SEGMENT_PATTERN.test(name) && !name.endsWith(".zst"))
    .sort()
    .map((name) => path.join(ARCHIVE_DIR, name));
}

function runArchiveTool(args) {
  return new Promise((resolve, reject) => {
    // nice 让压缩只用空闲 CPU；服务机只有 2 核，不能和请求处理抢。
    const child = spawn("nice", ["-n", "19", ZSTD_BIN, ...args], { stdio: ["ignore", "ignore", "pipe"] });
    let stderr = "";
    child.stderr.on("data", (chunk) => {
      stderr += chunk;
    });
    child.on("error", reject);
    child.on("close", (code) => {
      if (code === 0) {
        resolve();
      } else {
        reject(new Error(`zstd ${args.join(" ")} exited ${code}: ${stderr.trim()}`));
      }
    });
  });
}

// 逐段压缩：先写 .partial，完整性校验通过后改名为 .zst，再删明文。服务重启会打断子进程，下次启动从明文重来。
async function compressPendingArchives() {
  if (archiveState.compressing || isRebuildProcess) {
    return;
  }
  const source = listPlainArchiveSegments()[0];
  if (!source) {
    return;
  }
  const target = `${source}.zst`;
  const partial = `${target}.partial`;
  archiveState.compressing = source;
  try {
    fs.rmSync(partial, { force: true });
    await runArchiveTool(["-q", `-${ZSTD_LEVEL}`, "-T1", "-f", source, "-o", partial]);
    await runArchiveTool(["-tq", partial]);
    fs.renameSync(partial, target);
    fs.rmSync(source);
    archiveState.lastError = null;
    console.log(`compressed ${source}`);
  } catch (error) {
    fs.rmSync(partial, { force: true });
    archiveState.lastError = error?.message || String(error);
    console.error(`failed to compress ${source}: ${archiveState.lastError}`);
    if (!archiveState.retryTimer) {
      archiveState.retryTimer = setTimeout(() => {
        archiveState.retryTimer = null;
        compressPendingArchives();
      }, ARCHIVE_RETRY_DELAY_MS);
      archiveState.retryTimer.unref?.();
    }
    return;
  } finally {
    archiveState.compressing = null;
  }
  compressPendingArchives();
}

function getArchiveStatus() {
  const names = fs.existsSync(ARCHIVE_DIR)
    ? fs.readdirSync(ARCHIVE_DIR).filter((name) => ARCHIVE_SEGMENT_PATTERN.test(name))
    : [];
  return {
    compressedSegments: names.filter((name) => name.endsWith(".zst")).length,
    pendingSegments: names.filter((name) => !name.endsWith(".zst")).length,
    compressing: archiveState.compressing ? path.basename(archiveState.compressing) : null,
    lastError: archiveState.lastError,
    rotateBytes: RESULTS_ROTATE_BYTES
  };
}

function incrementGlobalRawCounters(summary, field) {
  summary.raw[field] = (summary.raw[field] || 0) + 1;
  for (const versionSummary of Object.values(summary.versionSummaries || {})) {
    versionSummary.raw[field] = (versionSummary.raw[field] || 0) + 1;
  }
}

function applyRecordToSummaryBundle(summary, record) {
  const recordVersion = getModVersion(record);
  const isEligible = isDefaultEligible(record);
  incrementGlobalRawCounters(summary, "physicalLines");
  incrementGlobalRawCounters(summary, "totalUniqueRuns");
  addRecordToSummary(summary, record, recordVersion, isEligible);

  if (!summary.versionSummaries[recordVersion]) {
    summary.versionSummaries[recordVersion] = createEmptySummary(recordVersion, {
      physicalLines: summary.raw.physicalLines,
      totalUniqueRuns: summary.raw.totalUniqueRuns,
      duplicateLines: summary.raw.duplicateLines || 0,
      malformedLines: summary.raw.malformedLines || 0
    });
  }
  addRecordToSummary(summary.versionSummaries[recordVersion], record, recordVersion, isEligible);
}

function recordMalformedIncrementalLine(summary) {
  incrementGlobalRawCounters(summary, "physicalLines");
  incrementGlobalRawCounters(summary, "malformedLines");
}

function replayIncrementalTail(summary, startOffset, identity, filePath = RESULTS_FILE) {
  let appliedRecords = 0;
  let malformedLines = 0;
  const replay = forEachJsonlLineFromOffset(filePath, startOffset, (line) => {
    try {
      const record = JSON.parse(line);
      const runId = record?.payload?.run?.runId;
      if (typeof runId !== "string" || runId.length === 0) {
        throw new Error("missing runId");
      }
      applyRecordToSummaryBundle(summary, record);
      rememberRunId(recentRunIds, runId);
      appliedRecords += 1;
    } catch {
      recordMalformedIncrementalLine(summary);
      malformedLines += 1;
    }
  });
  if (replay.endOffset > startOffset) {
    setIncrementalCheckpoint(summary, identity, replay.endOffset);
    finalizeSummaryBundle(summary);
    writeFileAtomic(SUMMARY_FILE, `${JSON.stringify(summary, null, 2)}\n`);
    derivedState.persistedOffset = replay.endOffset;
  }
  if (appliedRecords > 0 || malformedLines > 0) {
    console.log(`replayed ${appliedRecords} records and ${malformedLines} malformed lines from incremental tail`);
  }
}

function finalizeSummaryBundle(summary) {
  const generatedAtUtc = new Date().toISOString();
  const availableVersions = buildCountRows(summary.versions || {});
  summary.generatedAtUtc = generatedAtUtc;
  finalizeSummary(summary, availableVersions);
  for (const versionSummary of Object.values(summary.versionSummaries || {})) {
    versionSummary.generatedAtUtc = generatedAtUtc;
    finalizeSummary(versionSummary, availableVersions);
  }
  return summary;
}

function pctNumber(part, total) {
  return total > 0 ? Number(((part / total) * 100).toFixed(1)) : 0;
}

function buildRateRows(map, labelCategory) {
  return Object.entries(map)
    .map(([id, stat]) => ({
      id,
      name: getLabel(labelCategory, id),
      runs: stat.runs,
      wins: stat.wins,
      winRate: pctNumber(stat.wins, stat.runs)
    }))
    .sort((a, b) => b.runs - a.runs || b.winRate - a.winRate || a.id.localeCompare(b.id));
}

function buildChoiceRows(map, labelCategory) {
  return Object.entries(map)
    .map(([id, stat]) => ({
      id,
      name: getLabel(labelCategory, id),
      offered: stat.offered,
      selected: stat.selected,
      pickRate: pctNumber(stat.selected, stat.offered),
      selectedWins: stat.selectedWins,
      selectedWinRate: pctNumber(stat.selectedWins, stat.selected)
    }))
    .sort((a, b) => b.selected - a.selected || b.pickRate - a.pickRate || b.offered - a.offered || a.id.localeCompare(b.id));
}

function buildMonsterRows(map, labelCategory) {
  return Object.entries(map)
    .map(([id, stat]) => ({
      id,
      name: getLabel(labelCategory, id),
      runs: stat.runs,
      playerWins: stat.playerWins,
      playerWinRate: pctNumber(stat.playerWins, stat.runs),
      monsterWins: stat.monsterWins,
      monsterWinRate: pctNumber(stat.monsterWins, stat.runs)
    }))
    .sort((a, b) => b.runs - a.runs || b.monsterWinRate - a.monsterWinRate || a.id.localeCompare(b.id));
}

function buildCountRows(map, labelCategory = null) {
  return Object.entries(map)
    .map(([id, count]) => ({ id, name: labelCategory ? getLabel(labelCategory, id) : id, count }))
    .sort((a, b) => b.count - a.count || a.id.localeCompare(b.id));
}

function readDerivedSummary() {
  if (!fs.existsSync(SUMMARY_FILE)) {
    return null;
  }
  try {
    return JSON.parse(fs.readFileSync(SUMMARY_FILE, "utf8"));
  } catch {
    return null;
  }
}

function derivedFilesAreCurrent() {
  if (!summaryBundle?._incremental?.source) {
    return false;
  }
  return summaryBundle._incremental.source.offset >= resultsEndOffset && !derivedState.dirty;
}

function markDerivedDirty(offset) {
  const identity = getResultsIdentity();
  setIncrementalCheckpoint(summaryBundle, identity, offset);
  derivedState.dirty = true;
  derivedState.pendingRecords += 1;
}

function scheduleSummaryFlush(delayMs = SUMMARY_FLUSH_INTERVAL_MS) {
  if (derivedState.flushing || derivedState.timer) {
    return;
  }
  const safeDelayMs = Number.isFinite(delayMs) ? Math.max(0, delayMs) : SUMMARY_FLUSH_INTERVAL_MS;
  derivedState.timer = setTimeout(() => {
    derivedState.timer = null;
    if (derivedState.dirty) {
      flushSummaryNow();
    }
  }, safeDelayMs);
  derivedState.timer.unref?.();
}

function flushSummaryNow() {
  if (!summaryBundle || derivedState.flushing || !derivedState.dirty) {
    return;
  }
  if (derivedState.timer) {
    clearTimeout(derivedState.timer);
    derivedState.timer = null;
  }
  derivedState.flushing = true;
  try {
    finalizeSummaryBundle(summaryBundle);
    writeFileAtomic(SUMMARY_FILE, `${JSON.stringify(summaryBundle, null, 2)}\n`);
    derivedState.dirty = false;
    derivedState.pendingRecords = 0;
    derivedState.persistedOffset = summaryBundle._incremental.source.offset;
    derivedState.lastBuiltAtMs = Date.now();
    derivedState.lastError = null;
    summaryCache.clear();
  } catch (error) {
    derivedState.lastError = error?.message || String(error);
    console.error(`failed to flush incremental summary: ${derivedState.lastError}`);
  } finally {
    derivedState.flushing = false;
  }
  if (derivedState.dirty) {
    scheduleSummaryFlush(Math.max(SUMMARY_FLUSH_INTERVAL_MS, 30000));
  }
}

// 只由离线 --rebuild-derived 进程调用，服务进程不会同时持有未落盘的增量状态。
async function rebuildDerivedTablesNow() {
  const summary = await buildSummaryBundle();
  const identity = getResultsIdentity();
  setIncrementalCheckpoint(summary, identity, identity.size);
  writeFileAtomic(SUMMARY_FILE, `${JSON.stringify(summary, null, 2)}\n`);
  return summary;
}

function lockLooksStale(lockPath) {
  try {
    const ageMs = Date.now() - fs.statSync(lockPath).mtimeMs;
    return ageMs > 30 * 60 * 1000;
  } catch {
    return false;
  }
}

async function rebuildDerivedTablesWithLock() {
  fs.mkdirSync(DERIVED_DIR, { recursive: true });
  let fd = null;
  try {
    fd = fs.openSync(REBUILD_LOCK_FILE, "wx");
  } catch (error) {
    if (error?.code !== "EEXIST") {
      throw error;
    }
    if (!lockLooksStale(REBUILD_LOCK_FILE)) {
      // 不能静默返回旧 summary:--rebuild-derived 会以 0 退出,运维看不出重建被跳过。
      throw new Error(`summary rebuild lock is held (${REBUILD_LOCK_FILE}); another rebuild may be running. Remove the lock if it is left over.`);
    }
    fs.rmSync(REBUILD_LOCK_FILE, { force: true });
    fd = fs.openSync(REBUILD_LOCK_FILE, "wx");
  }
  try {
    fs.writeFileSync(fd, `${process.pid}\n${new Date().toISOString()}\n`, "utf8");
    return await rebuildDerivedTablesNow();
  } finally {
    if (fd != null) {
      fs.closeSync(fd);
    }
    fs.rmSync(REBUILD_LOCK_FILE, { force: true });
  }
}

function getSummaryFileMtimeMs() {
  try {
    return fs.statSync(SUMMARY_FILE).mtimeMs;
  } catch {
    return 0;
  }
}

function getDerivedStatus() {
  const summaryMtimeMs = getSummaryFileMtimeMs();
  const resultsMtimeMs = fs.existsSync(RESULTS_FILE) ? fs.statSync(RESULTS_FILE).mtimeMs : 0;
  return {
    summaryExists: summaryMtimeMs > 0,
    summaryCurrent: derivedFilesAreCurrent(),
    summaryGeneratedAtUtc: summaryMtimeMs > 0 ? new Date(summaryMtimeMs).toISOString() : null,
    resultsUpdatedAtUtc: resultsMtimeMs > 0 ? new Date(resultsMtimeMs).toISOString() : null,
    refreshIntervalMs: SUMMARY_FLUSH_INTERVAL_MS,
    scheduled: Boolean(derivedState.timer),
    rebuilding: false,
    flushing: derivedState.flushing,
    pendingRecords: derivedState.pendingRecords,
    recentRunIds: recentRunIds.size,
    checkpointOffset: derivedState.persistedOffset,
    pendingOffset: summaryBundle?._incremental?.source?.offset ?? null,
    resultsSize: resultsEndOffset,
    lastBuiltAtUtc: derivedState.lastBuiltAtMs ? new Date(derivedState.lastBuiltAtMs).toISOString() : null,
    lastError: derivedState.lastError
  };
}

function makeEmptyDerivedVersionSummary(allSummary, versionFilter) {
  const empty = createEmptySummary(versionFilter, {
    physicalLines: allSummary.raw?.physicalLines || 0,
    totalUniqueRuns: allSummary.raw?.totalUniqueRuns || allSummary.raw?.uniqueRuns || 0,
    duplicateLines: allSummary.raw?.duplicateLines || 0,
    malformedLines: allSummary.raw?.malformedLines || 0
  });
  empty.generatedAtUtc = allSummary.generatedAtUtc;
  empty.availableVersions = allSummary.availableVersions || allSummary.tables?.versions || [];
  return finalizeSummary(empty, empty.availableVersions);
}

function getSummaryFromDerived(versionFilter = null) {
  const summary = readDerivedSummary();
  if (!summary) {
    return null;
  }
  const normalizedVersionFilter = normalizeVersionFilter(versionFilter);
  if (!normalizedVersionFilter) {
    const { versionSummaries, _incremental, ...publicSummary } = summary;
    return publicSummary;
  }
  return summary.versionSummaries?.[normalizedVersionFilter] || makeEmptyDerivedVersionSummary(summary, normalizedVersionFilter);
}

function getSummaryForDisplay(versionFilter = null) {
  const normalizedVersionFilter = normalizeVersionFilter(versionFilter);
  const summaryMtimeMs = getSummaryFileMtimeMs();
  const cacheKey = normalizedVersionFilter || "all";
  const cached = summaryCache.get(cacheKey);
  if (cached && cached.summaryMtimeMs === summaryMtimeMs) {
    return cached.summary;
  }

  const derivedSummary = getSummaryFromDerived(normalizedVersionFilter);
  if (derivedSummary) {
    summaryCache.set(cacheKey, { summaryMtimeMs, summary: derivedSummary });
    return derivedSummary;
  }

  const summary = createUnavailableSummary(normalizedVersionFilter);
  summaryCache.set(cacheKey, { summaryMtimeMs: 0, summary });
  return summary;
}

function serveDerived(req, res, pathname) {
  if (path.basename(pathname) !== "summary.json") {
    return sendText(res, 404, "not found");
  }
  if (!fs.existsSync(SUMMARY_FILE)) {
    return sendJson(res, 200, getSummaryForDisplay(null));
  }
  return sendFile(res, SUMMARY_FILE, "application/json; charset=utf-8");
}

function escapeHtml(value) {
  return String(value ?? "").replace(/[&<>"']/g, (ch) => ({
    "&": "&amp;",
    "<": "&lt;",
    ">": "&gt;",
    '"': "&quot;",
    "'": "&#39;"
  })[ch]);
}

function fmtPct(value) {
  return `${Number(value || 0).toFixed(1)}%`;
}

function compareVersionsDesc(a, b) {
  return String(b).localeCompare(String(a), undefined, { numeric: true, sensitivity: "base" });
}

function getVersionIdsForDisplay(summary, latestVersion = null) {
  const versions = new Set();
  for (const row of summary?.availableVersions || []) {
    if (row?.id) {
      versions.add(row.id);
    }
  }
  const normalizedLatestVersion = normalizeVersionFilter(latestVersion);
  if (normalizedLatestVersion) {
    versions.add(normalizedLatestVersion);
  }
  return [...versions].sort(compareVersionsDesc);
}

function getDefaultDisplayVersion() {
  const latestVersion = normalizeVersionFilter(readLatestVersionInfo()?.latestVersion);
  const summary = getSummaryForDisplay(null);
  const availableVersions = new Set((summary.availableVersions || []).map((row) => row.id).filter(Boolean));
  if (latestVersion && availableVersions.has(latestVersion)) {
    return latestVersion;
  }
  const newestAvailableVersion = [...availableVersions].sort(compareVersionsDesc)[0];
  return newestAvailableVersion || latestVersion || null;
}

function buildFilterNote(summary) {
  const versionText = summary.versionFilter === "all" ? "全部版本" : `版本 ${summary.versionFilter}`;
  const contentText = summary.filters.officialOnly
    ? `仅官方海克斯（已隐藏 ${summary.hiddenUnofficialEntries || 0} 个其他模组添加的条目）`
    : "包含其他模组添加的海克斯";
  return `当前统计口径：${versionText}，${contentText}，排除 runTime < ${summary.filters.minRunTimeForDefaultStats} 秒的历史局；0.5.0 起客户端会直接跳过短局上传。`;
}

function renderContentFilterOptions(officialOnly) {
  return [
    `<option value="all"${officialOnly ? "" : " selected"}>全部海克斯</option>`,
    `<option value="official"${officialOnly ? " selected" : ""}>仅官方海克斯</option>`
  ].join("");
}

function renderTable(headers, rows) {
  if (!rows.length) {
    return [
      `<thead><tr>${headers.map((header) => `<th>${escapeHtml(header)}</th>`).join("")}</tr></thead>`,
      `<tbody><tr><td colspan="${headers.length}">暂无可显示数据</td></tr></tbody>`
    ].join("");
  }
  return [
    `<thead><tr>${headers.map((header) => `<th>${escapeHtml(header)}</th>`).join("")}</tr></thead>`,
    `<tbody>${rows.map((row) => `<tr>${row.map((cell) => `<td>${escapeHtml(cell)}</td>`).join("")}</tr>`).join("")}</tbody>`
  ].join("");
}

function renderVersionOptions(summary, selectedVersion, latestVersion) {
  const versionCounts = new Map((summary.availableVersions || []).map((row) => [row.id, row.count]));
  const normalizedSelectedVersion = selectedVersion || "all";
  const options = [
    `<option value="all"${normalizedSelectedVersion === "all" ? " selected" : ""}>全部版本</option>`
  ];
  for (const version of getVersionIdsForDisplay(summary, latestVersion)) {
    const count = versionCounts.get(version);
    const suffix = Number.isFinite(count) ? `（${count}局）` : "（暂无样本）";
    options.push(
      `<option value="${escapeHtml(version)}"${normalizedSelectedVersion === version ? " selected" : ""}>${escapeHtml(`${version}${suffix}`)}</option>`
    );
  }
  return options.join("");
}

function renderIndexHtml(versionFilter = null, officialOnly = false) {
  const summary = prepareSummaryForDisplay(getSummaryForDisplay(versionFilter), officialOnly);
  const latestVersion = normalizeVersionFilter(readLatestVersionInfo()?.latestVersion);
  const indexPath = path.join(PUBLIC_DIR, "index.html");
  let html = fs.readFileSync(indexPath, "utf8");
  const note = buildFilterNote(summary);
  const generatedAtUtc = summary.generatedAtUtc || "";
  const updatedText = generatedAtUtc ? `更新时间：${generatedAtUtc}` : "等待生成统计数据";
  const replacements = [
    [
      /<div class="muted" id="updated"(?: data-generated-at-utc="[^"]*")?>.*?<\/div>/,
      `<div class="muted" id="updated" data-generated-at-utc="${escapeHtml(generatedAtUtc)}">${escapeHtml(updatedText)}</div>`
    ],
    [/<select id="versionFilter">\s*<option value="all">全部版本<\/option>\s*<\/select>/, `<select id="versionFilter">${renderVersionOptions(summary, summary.versionFilter, latestVersion)}</select>`],
    [/<select id="contentFilter">[\s\S]*?<\/select>/, `<select id="contentFilter">${renderContentFilterOptions(officialOnly)}</select>`],
    [/<b id="eligibleRuns">0<\/b>/, `<b id="eligibleRuns">${summary.runCount}</b>`],
    [/<b id="rawRuns">0<\/b>/, `<b id="rawRuns">${summary.raw.uniqueRuns}</b>`],
    [/<b id="shortRuns">0<\/b>/, `<b id="shortRuns">${summary.excludedShortRuns}</b>`],
    [/<b id="wins">0<\/b>/, `<b id="wins">${summary.winCount}</b>`],
    [/<b id="winRate">0%<\/b>/, `<b id="winRate">${fmtPct(summary.winRate)}</b>`],
    [/<div class="muted" id="filterNote"><\/div>/, `<div class="muted" id="filterNote">${escapeHtml(note)}</div>`],
    [/<table id="versions"><\/table>/, `<table id="versions">${renderTable(["版本", "局数"], summary.tables.versions.slice(0, 20).map((row) => [displayLabel(row), row.count]))}</table>`],
    [/<table id="netModes"><\/table>/, `<table id="netModes">${renderTable(["模式", "局数"], summary.tables.netModes.slice(0, 20).map((row) => [displayLabel(row), row.count]))}</table>`],
    [/<table id="characters"><\/table>/, `<table id="characters">${renderTable(["角色", "玩家样本"], summary.tables.characters.slice(0, 20).map((row) => [displayLabel(row), row.count]))}</table>`],
    [/<span class="panel-meta" id="choicesMeta">0 条<\/span>/, `<span class="panel-meta" id="choicesMeta">${summary.tables.playerRuneChoices.length} 条</span>`],
    [/<span class="panel-meta" id="monsterHexesMeta">0 条<\/span>/, `<span class="panel-meta" id="monsterHexesMeta">${summary.tables.monsterHexRuns.length} 条</span>`],
    [/<table id="choices"><\/table>/, `<table id="choices">${renderTable(["海克斯", "出现", "选择", "选择率", "选择后胜率"], summary.tables.playerRuneChoices.map((row) => [displayLabel(row), row.offered, row.selected, fmtPct(row.pickRate), fmtPct(row.selectedWinRate)]))}</table>`],
    [/<table id="monsterHexes"><\/table>/, `<table id="monsterHexes">${renderTable(["敌方海克斯", "出现局数", "敌方胜利", "敌方胜率", "玩家胜率"], summary.tables.monsterHexRuns.map((row) => [displayLabel(row), row.runs, row.monsterWins, fmtPct(row.monsterWinRate), fmtPct(row.playerWinRate)]))}</table>`]
  ];
  for (const [pattern, replacement] of replacements) {
    html = html.replace(pattern, replacement);
  }
  return html;
}

function serveStatic(req, res) {
  const url = new URL(req.url, "http://localhost");
  let pathname = decodeURIComponent(url.pathname);
  if (pathname === "/") {
    pathname = "/index.html";
  }

  if (pathname === "/index.html") {
    const versionFilter = url.searchParams.has("version")
      ? normalizeVersionFilter(url.searchParams.get("version"))
      : getDefaultDisplayVersion();
    return sendHtml(res, 200, renderIndexHtml(versionFilter, parseOfficialOnly(url.searchParams.get("official"))));
  }

  const filePath = path.normalize(path.join(PUBLIC_DIR, pathname));
  const relativePath = path.relative(PUBLIC_DIR, filePath);
  if (relativePath.startsWith("..") || path.isAbsolute(relativePath)) {
    return sendText(res, 403, "forbidden");
  }

  if (!fs.existsSync(filePath) || fs.statSync(filePath).isDirectory()) {
    return sendText(res, 404, "not found");
  }

  const ext = path.extname(filePath);
  const contentType = {
    ".html": "text/html; charset=utf-8",
    ".css": "text/css; charset=utf-8",
    ".js": "application/javascript; charset=utf-8",
    ".json": "application/json; charset=utf-8",
    ".svg": "image/svg+xml"
  }[ext] || "application/octet-stream";

  res.writeHead(200, {
    "content-type": contentType,
    "cache-control": ext === ".html" ? "no-cache" : "public, max-age=3600"
  });
  fs.createReadStream(filePath).pipe(res);
}

const server = http.createServer(async (req, res) => {
  const url = new URL(req.url, "http://localhost");
  if (req.method === "GET" && url.pathname === "/health") {
    return sendJson(res, 200, {
      ok: true,
      service: "hextech-runes-telemetry",
      runs: summaryBundle?.raw?.totalUniqueRuns || 0,
      derived: getDerivedStatus(),
      archive: getArchiveStatus()
    });
  }
  if (req.method === "GET" && url.pathname === "/api/hextech-runes/summary") {
    return sendJson(res, 200, prepareSummaryForDisplay(
      getSummaryForDisplay(url.searchParams.get("version")),
      parseOfficialOnly(url.searchParams.get("official"))));
  }
  if (req.method === "GET" && url.pathname === "/api/hextech-runes/latest-version") {
    const latestVersionInfo = readLatestVersionInfo();
    if (!latestVersionInfo) {
      return sendJson(res, 503, { ok: false, error: "latest version info is unavailable" });
    }
    return sendJson(res, 200, latestVersionInfo);
  }
  if (req.method === "GET" && url.pathname.startsWith("/api/hextech-runes/derived/")) {
    return serveDerived(req, res, url.pathname);
  }
  if (req.method === "POST" && url.pathname === "/api/hextech-runes/run-result") {
    return handleIngest(req, res);
  }
  if (community.handleRequest(req, res, url)) {
    return;
  }
  if (req.method === "GET" || req.method === "HEAD") {
    return serveStatic(req, res);
  }
  return sendText(res, 405, "method not allowed");
});

function shutdown(signal) {
  if (derivedState.dirty) {
    flushSummaryNow();
  }
  server.close(() => process.exit(0));
  setTimeout(() => process.exit(1), 10000).unref();
  console.log(`received ${signal}; telemetry summary flushed`);
}

function startService() {
  summaryBundle = initializeIncrementalSummary();
  resultsEndOffset = fs.statSync(RESULTS_FILE).size;
  derivedState.persistedOffset = summaryBundle._incremental.source.offset;
  community.init({ dataDir: DATA_DIR, publicDir: PUBLIC_DIR });
  maybeRotateResults();
  compressPendingArchives();

  process.once("SIGTERM", () => shutdown("SIGTERM"));
  process.once("SIGINT", () => shutdown("SIGINT"));

  server.listen(PORT, HOST, () => {
    console.log(`hextech-runes-telemetry listening on ${HOST}:${PORT}`);
  });
}

if (isRebuildProcess) {
  rebuildDerivedTablesWithLock().then(
    () => process.exit(0),
    (error) => {
      console.error(error?.stack || error);
      process.exit(1);
    });
} else {
  startService();
}
