import { createWriteStream } from "node:fs";
import { Client } from "./client-entry.mjs";

const logPath = new URL("./key-editor-probe.jsonl", import.meta.url);
const log = createWriteStream(logPath);
const timeout = 8 * 60 * 60 * 1000;
const exe = "C:\\Program Files\\Steinberg\\Cubase 15\\Cubase15.exe";
const patterns = [
  "KeyEditor",
  "Set Note Pitch",
  "Set Note Velocity",
  "Set Off Velocity",
  "Set Release Length",
];

function write(record) {
  log.write(`${JSON.stringify({ at: new Date().toISOString(), ...record })}\n`);
}

function payload(result) {
  if (result?.structuredContent !== undefined) return result.structuredContent;
  const text = result?.content?.find((item) => item.type === "text")?.text;
  if (typeof text !== "string") return result;
  try {
    return JSON.parse(text);
  } catch {
    return { text };
  }
}

const client = new Client();
const call = (name, args) => client.call(name, args, timeout);

write({ event: "start" });
try {
  await client.connect();
  write({ event: "connected" });
  const opened = payload(await call("open_binary", { path: exe }));
  write({ event: "opened", result: opened });

  const strings = [];
  for (const pattern of patterns) {
    const result = payload(await call("search_strings", {
      pattern,
      mode: "literal",
      case_sensitive: false,
      detail: "complete",
    }));
    const hits = Array.isArray(result?.normalized_result) ? result.normalized_result : result;
    write({
      event: "strings",
      pattern,
      evidence_id: result?.evidence_id ?? null,
      hits,
    });
    const list = Array.isArray(hits) ? hits : [];
    for (const hit of list) strings.push({ pattern, ...hit });
  }

  const seen = new Set();
  const functions = [];
  for (const hit of strings) {
    if (!hit.address || seen.has(hit.address)) continue;
    seen.add(hit.address);
    const result = payload(await call("xrefs", { address: hit.address }));
    write({
      event: "xrefs",
      pattern: hit.pattern,
      address: hit.address,
      value: hit.value ?? null,
      evidence_id: result?.evidence_id ?? null,
      result,
    });
    const refs = result?.normalized_result ?? result?.references ?? result;
    const items = Array.isArray(refs) ? refs : [];
    for (const item of items.slice(0, 8)) {
      const from = item.from ?? item.address ?? item.source ?? item.function_address;
      if (typeof from === "string") functions.push({ from, pattern: hit.pattern, string: hit.value });
    }
  }

  const chosen = [];
  for (const item of functions) {
    if (chosen.some((entry) => entry.from === item.from)) continue;
    chosen.push(item);
    if (chosen.length === 6) break;
  }
  for (const item of chosen) {
    const info = payload(await call("procedure_info", { procedure: item.from }));
    write({ event: "procedure_info", ...item, evidence_id: info?.evidence_id ?? null, result: info });
    const code = payload(await call("procedure_pseudo_code", { procedure: item.from }));
    write({ event: "pseudo", ...item, evidence_id: code?.evidence_id ?? null, result: code });
  }
  write({ event: "done", functions: chosen });
} catch (error) {
  write({ event: "error", message: error instanceof Error ? error.stack ?? error.message : String(error) });
  process.exitCode = 1;
} finally {
  await client.close().catch((error) => write({
    event: "close_error",
    message: error instanceof Error ? error.message : String(error),
  }));
  log.end();
}
