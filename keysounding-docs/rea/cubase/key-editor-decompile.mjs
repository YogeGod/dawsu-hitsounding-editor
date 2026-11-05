import { createWriteStream } from "node:fs";
import { Client } from "./client-entry.mjs";

const log = createWriteStream(new URL("./key-editor-decompile.jsonl", import.meta.url));
const timeout = 8 * 60 * 60 * 1000;
const exe = "C:\\Program Files\\Steinberg\\Cubase 15\\Cubase15.exe";
const sites = [
  ["0x14154ff12", "Set Note Pitch"],
  ["0x141550002", "Set Note Velocity"],
  ["0x1415500f2", "Set Off Velocity"],
  ["0x1415502d2", "Set Release Length"],
  ["0x141a57606", "KeyEditorCLSelectView"],
  ["0x141af2d42", "keyeditor-window"],
];
const write = (record) => log.write(JSON.stringify({ at: new Date().toISOString(), ...record }) + "\n");
const payload = (result) => {
  if (result?.structuredContent !== undefined) return result.structuredContent;
  const text = result?.content?.find((item) => item.type === "text")?.text;
  if (typeof text !== "string") return result;
  try { return JSON.parse(text); } catch { return { text }; }
};
const client = new Client();
write({ event: "start" });
try {
  await client.connect();
  write({ event: "opened", result: payload(await client.call("open_binary", { path: exe }, timeout)) });
  for (const [address, label] of sites) {
    const info = payload(await client.call("procedure_info", { procedure: address }, timeout));
    write({ event: "procedure_info", address, label, evidence_id: info?.evidence_id ?? null, result: info });
    const code = payload(await client.call("procedure_pseudo_code", { procedure: address }, timeout));
    write({ event: "pseudo", address, label, evidence_id: code?.evidence_id ?? null, result: code });
  }
  write({ event: "done" });
} catch (error) {
  write({ event: "error", message: error instanceof Error ? error.stack ?? error.message : String(error) });
  process.exitCode = 1;
} finally {
  await client.close().catch(() => {});
  log.end();
}
