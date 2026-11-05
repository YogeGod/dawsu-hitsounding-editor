import { Client as McpClient } from "file:///C:/Users/yogem/AppData/Roaming/npm/node_modules/rea-agents/node_modules/@modelcontextprotocol/client/dist/index.mjs";
import { StdioClientTransport } from "file:///C:/Users/yogem/AppData/Roaming/npm/node_modules/rea-agents/node_modules/@modelcontextprotocol/client/dist/stdio.mjs";

const javaBin = "E:\\Codex-tools\\jdk-21\\bin";
const env = {
  ...process.env,
  GHIDRA_INSTALL_DIR: "E:\\Codex-tools\\ghidra_12.1.4_PUBLIC",
  JAVA_HOME: "E:\\Codex-tools\\jdk-21",
  REA_GHIDRA_STARTUP_TIMEOUT_MS: "21600000",
  GHIDRA_HEADLESS_MAXMEM: "16G",
  Path: `${javaBin};${process.env.Path ?? ""}`,
};

export class Client {
  #mcp = new McpClient({ name: "cubase-key-editor-probe", version: "1.0.0" });
  #transport = new StdioClientTransport({
    command: "C:\\Program Files\\nodejs\\node.exe",
    args: ["C:\\Users\\yogem\\AppData\\Roaming\\npm\\node_modules\\rea-agents\\scripts\\rea.mjs", "mcp"],
    env,
    cwd: "E:\\Claude\\REA\\cubase",
    stderr: "inherit",
    maxBufferSize: 64 * 1024 * 1024,
  });

  connect() {
    return this.#mcp.connect(this.#transport, { timeout: 120000 });
  }

  call(name, args, timeout) {
    return this.#mcp.callTool({ name, arguments: args }, { timeout });
  }

  close() {
    return this.#mcp.close();
  }
}
