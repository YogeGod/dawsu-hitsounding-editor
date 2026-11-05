import { readFileSync } from "node:fs";

const lines = readFileSync("E:/Claude/REA/cubase/key-editor-decompile.jsonl", "utf8")
  .trim()
  .split(/\n/)
  .map((line) => JSON.parse(line));

for (const line of lines) {
  if (line.event !== "procedure_info" && line.event !== "pseudo") continue;
  const result = line.result?.normalized_result ?? line.result;
  const keys = result && typeof result === "object" ? Object.keys(result) : [];
  console.log("---", line.event, line.label, line.evidence_id);
  console.log("keys", keys.join(","));
  const text = JSON.stringify(result);
  console.log(text.slice(0, 700).replaceAll("\n", " "));
  console.log("");
}
