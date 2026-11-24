const assert = require("node:assert/strict");
const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { spawn, spawnSync } = require("node:child_process");
const test = require("node:test");

const SERVER_FILE = path.join(__dirname, "..", "server.js");

function makePayload(runId, isVictory = true) {
  return {
    schemaVersion: 1,
    modId: "HextechRunes",
    modVersion: "0.9.1",
    gameVersion: "0.111.0",
    uploadedAtUtc: new Date().toISOString(),
    run: {
      runId,
      seedHash: `seed-${runId}`,
      isVictory,
      runTime: 120,
      netMode: "Singleplayer",
      playerCount: 1
    },
    players: [{ slot: 0, character: "Ironclad", hextechRunes: ["TestRune"] }],
    runeChoices: [{ actIndex: 1, playerSlot: 0, rarity: "Silver", rerollCount: 0, options: ["TestRune"], selected: "TestRune" }],
    monsterHexes: [{ actIndex: 1, rarity: "Silver", hex: "TestHex" }]
  };
}

async function waitForServer(port, child) {
  for (let attempt = 0; attempt < 100; attempt += 1) {
    if (child.exitCode != null) {
      throw new Error(`server exited early with code ${child.exitCode}`);
    }
    try {
      const response = await fetch(`http://127.0.0.1:${port}/health`);
      if (response.ok) {
        return;
      }
    } catch {
      // 服务进程尚未开始监听。
    }
    await new Promise((resolve) => setTimeout(resolve, 25));
  }
  throw new Error("server did not become ready");
}

async function startServer(dataDir, port, extraEnv = {}) {
  const publicDir = path.join(dataDir, "public");
  fs.mkdirSync(publicDir, { recursive: true });
  for (const fileName of ["index.html", "latest-version.json"]) {
    fs.copyFileSync(path.join(__dirname, "..", "public", fileName), path.join(publicDir, fileName));
  }
  const child = spawn(process.execPath, [SERVER_FILE], {
    env: {
      ...process.env,
      HOST: "127.0.0.1",
      PORT: String(port),
      DATA_DIR: dataDir,
      PUBLIC_DIR: publicDir,
      SUMMARY_FLUSH_INTERVAL_MS: "1000",
      RECENT_RUN_ID_LIMIT: "1000",
      RECENT_RUN_ID_TAIL_BYTES: String(1024 * 1024),
      ...extraEnv
    },
    stdio: ["ignore", "pipe", "pipe"]
  });
  let output = "";
  child.stdout.on("data", (chunk) => { output += chunk; });
  child.stderr.on("data", (chunk) => { output += chunk; });
  try {
    await waitForServer(port, child);
  } catch (error) {
    child.kill("SIGKILL");
    throw new Error(`${error.message}\n${output}`);
  }
  return { child, getOutput: () => output };
}

async function stopServer(server) {
  const exited = new Promise((resolve) => server.child.once("exit", resolve));
  server.child.kill("SIGTERM");
  await exited;
}

async function postRun(port, payload) {
  const response = await fetch(`http://127.0.0.1:${port}/api/hextech-runes/run-result`, {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(payload)
  });
  assert.equal(response.status, 202);
  return response.json();
}

test("增量快照写入、尾部回放、近期去重与离线全量重建", async () => {
  const dataDir = fs.mkdtempSync(path.join(os.tmpdir(), "hextech-telemetry-"));
  const port = 32000 + Math.floor(Math.random() * 10000);
  const server = await startServer(dataDir, port);
  try {
    const firstPayload = makePayload("run-0000000000000001");
    assert.deepEqual(await postRun(port, firstPayload), {
      ok: true,
      duplicate: false,
      runId: firstPayload.run.runId
    });
    const duplicate = await postRun(port, firstPayload);
    assert.equal(duplicate.duplicate, true);
    await new Promise((resolve) => setTimeout(resolve, 1200));

    const summary = await fetch(`http://127.0.0.1:${port}/api/hextech-runes/summary`).then((response) => response.json());
    assert.equal(summary.runCount, 1);
    assert.equal(summary.raw.totalUniqueRuns, 1);
    assert.equal(summary.versionSummaries, undefined);
  } finally {
    await stopServer(server);
  }

  // 模拟崩溃：记录已追加到 run_results.jsonl，但 summary 检查点还停在旧位置。
  const tailReceivedAtUtc = new Date(Date.now() + 1000).toISOString();
  const tailRecords = [];
  let lastPayload = null;
  for (let index = 1; index <= 10000; index += 1) {
    lastPayload = makePayload(`run-tail-${String(index).padStart(16, "0")}`, index % 2 === 0);
    tailRecords.push(JSON.stringify({
      receivedAtUtc: tailReceivedAtUtc,
      payloadHash: `simulated-crash-tail-${index}`,
      payload: lastPayload
    }));
  }
  fs.appendFileSync(path.join(dataDir, "run_results.jsonl"), `${tailRecords.join("\n")}\n`, "utf8");

  async function assertTailCounted() {
    const tailServer = await startServer(dataDir, port);
    try {
      const summary = await fetch(`http://127.0.0.1:${port}/api/hextech-runes/summary`).then((response) => response.json());
      assert.equal(summary.runCount, 10001);
      assert.equal(summary.winCount, 5001);
      assert.equal(summary.raw.totalUniqueRuns, 10001);

      const duplicate = await postRun(port, lastPayload);
      assert.equal(duplicate.duplicate, true);
      const health = await fetch(`http://127.0.0.1:${port}/health`).then((response) => response.json());
      assert.equal(health.runs, 10001);
      assert.equal(health.derived.rebuilding, false);
      assert.equal(health.derived.checkpointOffset, health.derived.resultsSize);
    } finally {
      await stopServer(tailServer);
    }
  }

  // 服务启动时从检查点回放尾部。
  await assertTailCounted();

  // 检查点丢失时服务拒绝启动，离线全量重建后得到同样的结果。
  const summaryPath = path.join(dataDir, "derived", "summary.json");
  const uncheckpointedSummary = JSON.parse(fs.readFileSync(summaryPath, "utf8"));
  delete uncheckpointedSummary._incremental;
  fs.writeFileSync(summaryPath, `${JSON.stringify(uncheckpointedSummary, null, 2)}\n`, "utf8");

  const refused = spawnSync(process.execPath, [SERVER_FILE], {
    env: { ...process.env, HOST: "127.0.0.1", PORT: String(port), DATA_DIR: dataDir },
    encoding: "utf8",
    timeout: 10000
  });
  assert.notEqual(refused.status, 0, refused.stderr || refused.stdout);
  assert.match(refused.stderr, /--rebuild-derived/);

  const rebuild = spawnSync(process.execPath, [SERVER_FILE, "--rebuild-derived"], {
    env: { ...process.env, DATA_DIR: dataDir },
    encoding: "utf8"
  });
  assert.equal(rebuild.status, 0, rebuild.stderr || rebuild.stdout);

  await assertTailCounted();
});

test("重建锁被占用时 --rebuild-derived 以非零退出", () => {
  const dataDir = fs.mkdtempSync(path.join(os.tmpdir(), "hextech-telemetry-lock-"));
  try {
    fs.mkdirSync(path.join(dataDir, "derived"), { recursive: true });
    fs.writeFileSync(path.join(dataDir, "run_results.jsonl"), "", "utf8");
    fs.writeFileSync(path.join(dataDir, "derived", ".summary-rebuild.lock"), "12345\n", "utf8");

    const rebuild = spawnSync(process.execPath, [SERVER_FILE, "--rebuild-derived"], {
      env: { ...process.env, DATA_DIR: dataDir },
      encoding: "utf8"
    });
    assert.equal(rebuild.status, 1, rebuild.stderr || rebuild.stdout);
    assert.match(rebuild.stderr, /rebuild lock is held/);
  } finally {
    fs.rmSync(dataDir, { recursive: true, force: true });
  }
});

test("--rebuild-derived 计入文件末尾没有换行的完整记录，截断的末行计为格式错误", () => {
  const dataDir = fs.mkdtempSync(path.join(os.tmpdir(), "hextech-telemetry-tail-"));
  try {
    const resultsPath = path.join(dataDir, "run_results.jsonl");
    const summaryPath = path.join(dataDir, "derived", "summary.json");
    const record = (runId) => JSON.stringify({ receivedAtUtc: new Date().toISOString(), payloadHash: `hash-${runId}`, payload: makePayload(runId) });
    const rebuild = () => spawnSync(process.execPath, [SERVER_FILE, "--rebuild-derived"], {
      env: { ...process.env, DATA_DIR: dataDir },
      encoding: "utf8"
    });

    fs.writeFileSync(resultsPath, `${record("run-tail-a")}\n${record("run-tail-b")}`, "utf8");
    let result = rebuild();
    assert.equal(result.status, 0, result.stderr || result.stdout);
    let summary = JSON.parse(fs.readFileSync(summaryPath, "utf8"));
    assert.equal(summary.runCount, 2, "complete last record without a trailing newline is counted");
    assert.equal(summary.raw.malformedLines, 0);
    assert.equal(summary._incremental.source.offset, fs.statSync(resultsPath).size);

    fs.writeFileSync(resultsPath, `${record("run-tail-a")}\n${record("run-tail-b").slice(0, 40)}`, "utf8");
    result = rebuild();
    assert.equal(result.status, 0, result.stderr || result.stdout);
    summary = JSON.parse(fs.readFileSync(summaryPath, "utf8"));
    assert.equal(summary.runCount, 1);
    assert.equal(summary.raw.malformedLines, 1, "truncated last line is reported as malformed, not silently skipped");
  } finally {
    fs.rmSync(dataDir, { recursive: true, force: true });
  }
});

const hasZstd = spawnSync("zstd", ["--version"]).status === 0;

async function waitFor(predicate, message) {
  for (let attempt = 0; attempt < 200; attempt += 1) {
    if (await predicate()) {
      return;
    }
    await new Promise((resolve) => setTimeout(resolve, 50));
  }
  throw new Error(message);
}

test("原始库按大小滚动进 archive/ 并压缩，去重与全量重建跨段生效", { skip: !hasZstd && "zstd not installed" }, async () => {
  const dataDir = fs.mkdtempSync(path.join(os.tmpdir(), "hextech-telemetry-rotate-"));
  const port = 39000 + Math.floor(Math.random() * 1000);
  const env = { RESULTS_ROTATE_BYTES: "4096", ZSTD_LEVEL: "3" };
  const archiveDir = path.join(dataDir, "archive");
  const health = () => fetch(`http://127.0.0.1:${port}/health`).then((response) => response.json());
  let server = await startServer(dataDir, port, env);
  try {
    for (let index = 0; index < 8; index += 1) {
      await postRun(port, makePayload(`run-rotate-${String(index).padStart(16, "0")}`, index % 2 === 0));
    }
    await waitFor(async () => {
      const status = (await health()).archive;
      return status.compressedSegments >= 1 && status.pendingSegments === 0 && !status.compressing;
    }, "archive segment was not compressed");
    assert.ok(fs.readdirSync(archiveDir).every((name) => name.endsWith(".jsonl.zst")), "plain segments are removed after compression");
    assert.ok(fs.statSync(path.join(dataDir, "run_results.jsonl")).size < 4096, "current segment restarted after rotation");

    // 第一条已滚进归档段，内存里的近期窗口仍能拦住重传。
    assert.equal((await postRun(port, makePayload(`run-rotate-${"0".padStart(16, "0")}`))).duplicate, true);
    assert.equal((await health()).runs, 8);
  } finally {
    await stopServer(server);
  }

  // 重启后靠 recent-run-ids.json 继续拦住归档段里的重传。
  server = await startServer(dataDir, port, env);
  try {
    assert.equal((await postRun(port, makePayload(`run-rotate-${"0".padStart(16, "0")}`))).duplicate, true);
    assert.equal((await health()).runs, 8);
  } finally {
    await stopServer(server);
  }

  const rebuild = spawnSync(process.execPath, [SERVER_FILE, "--rebuild-derived"], {
    env: { ...process.env, DATA_DIR: dataDir, ...env },
    encoding: "utf8"
  });
  assert.equal(rebuild.status, 0, rebuild.stderr || rebuild.stdout);
  const rebuilt = JSON.parse(fs.readFileSync(path.join(dataDir, "derived", "summary.json"), "utf8"));
  assert.equal(rebuilt.runCount, 8, "full rebuild reads compressed archive segments and the current segment");
  assert.equal(rebuilt.raw.physicalLines, 8);
  fs.rmSync(dataDir, { recursive: true, force: true });
});

test("滚动在改名后、summary 落盘前中断时，启动先补完旧段再接着回放当前段", async () => {
  const dataDir = fs.mkdtempSync(path.join(os.tmpdir(), "hextech-telemetry-rotate-crash-"));
  const port = 40000 + Math.floor(Math.random() * 1000);
  const resultsPath = path.join(dataDir, "run_results.jsonl");
  const record = (runId) => `${JSON.stringify({ receivedAtUtc: new Date().toISOString(), payloadHash: `hash-${runId}`, payload: makePayload(runId) })}\n`;
  try {
    fs.writeFileSync(resultsPath, record("run-crash-a-0000000000000000") + record("run-crash-b-0000000000000000"), "utf8");
    const rebuild = spawnSync(process.execPath, [SERVER_FILE, "--rebuild-derived"], {
      env: { ...process.env, DATA_DIR: dataDir },
      encoding: "utf8"
    });
    assert.equal(rebuild.status, 0, rebuild.stderr || rebuild.stdout);

    // 检查点之后旧段又写了一条，然后改名进 archive/、开了新段并写入一条，summary 没来得及落盘。
    fs.appendFileSync(resultsPath, record("run-crash-c-0000000000000000"), "utf8");
    fs.mkdirSync(path.join(dataDir, "archive"));
    fs.renameSync(resultsPath, path.join(dataDir, "archive", "run_results-20260101T000000Z.jsonl"));
    fs.writeFileSync(resultsPath, record("run-crash-d-0000000000000000"), "utf8");

    const server = await startServer(dataDir, port, { ZSTD_BIN: "/nonexistent/zstd" });
    try {
      const status = await fetch(`http://127.0.0.1:${port}/health`).then((response) => response.json());
      assert.equal(status.runs, 4);
      assert.equal((await postRun(port, makePayload("run-crash-c-0000000000000000"))).duplicate, true);
    } finally {
      await stopServer(server);
    }
  } finally {
    fs.rmSync(dataDir, { recursive: true, force: true });
  }
});

test("仅官方海克斯过滤：隐藏不在 labels.json 里的条目，并按当前 labels 标注中文名", async () => {
  const dataDir = fs.mkdtempSync(path.join(os.tmpdir(), "hextech-telemetry-official-"));
  const port = 32000 + Math.floor(Math.random() * 10000);
  const server = await startServer(dataDir, port);
  try {
    const payload = makePayload("run-official-filter-0001");
    payload.players[0].hextechRunes = ["BLOODTHIRST_RUNE", "OTHER_MOD_RUNE"];
    payload.runeChoices[0].options = ["BLOODTHIRST_RUNE", "OTHER_MOD_RUNE"];
    payload.runeChoices[0].selected = "OTHER_MOD_RUNE";
    payload.monsterHexes[0].hex = "SpinToWin";
    payload.monsterHexes.push({ ...payload.monsterHexes[0], hex: "OTHER_MOD_ENEMY_HEX" });
    await postRun(port, payload);
    await new Promise((resolve) => setTimeout(resolve, 1200));

    const base = `http://127.0.0.1:${port}/api/hextech-runes/summary`;
    const all = await fetch(base).then((response) => response.json());
    assert.equal(all.filters.officialOnly, false);
    assert.deepEqual(all.tables.playerRuneChoices.map((row) => row.id).sort(), ["BLOODTHIRST_RUNE", "OTHER_MOD_RUNE"]);
    assert.equal(all.tables.playerRuneChoices.find((row) => row.id === "BLOODTHIRST_RUNE").name, "渴血");
    assert.equal(all.tables.playerRuneChoices.find((row) => row.id === "OTHER_MOD_RUNE").official, false);
    assert.deepEqual(all.tables.monsterHexRuns.map((row) => row.id).sort(), ["OTHER_MOD_ENEMY_HEX", "SpinToWin"]);
    assert.equal(all.tables.monsterHexRuns.find((row) => row.id === "SpinToWin").name, "转出胜利");
    assert.equal(all.tables.monsterHexRuns.find((row) => row.id === "OTHER_MOD_ENEMY_HEX").official, false);

    const official = await fetch(`${base}?official=1`).then((response) => response.json());
    assert.equal(official.filters.officialOnly, true);
    assert.deepEqual(official.tables.playerRuneChoices.map((row) => row.id), ["BLOODTHIRST_RUNE"]);
    assert.deepEqual(official.tables.playerRuneRuns.map((row) => row.id), ["BLOODTHIRST_RUNE"]);
    assert.deepEqual(official.tables.monsterHexRuns.map((row) => row.id), ["SpinToWin"]);
    assert.equal(official.hiddenUnofficialEntries, 3);
    assert.equal(official.runCount, all.runCount, "run totals are unaffected by the entry filter");

    const html = await fetch(`http://127.0.0.1:${port}/?version=all&official=1`).then((response) => response.text());
    assert.match(html, /<option value="official" selected>仅官方海克斯<\/option>/);
    assert.match(html, /已隐藏 3 个其他模组添加的条目/);
    assert.doesNotMatch(html, /OTHER_MOD_RUNE/);
    assert.doesNotMatch(html, /OTHER_MOD_ENEMY_HEX/);
  } finally {
    await stopServer(server);
    fs.rmSync(dataDir, { recursive: true, force: true });
  }
});
