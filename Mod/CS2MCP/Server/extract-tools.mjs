#!/usr/bin/env node
/**
 * Extracts the 44 cs2_* tools from the upstream MCP server source
 * (Mod/CS2MCP/Server/index.ts) into Mod/Agent/ToolCatalog.json.
 *
 * The catalog is the single source of truth for:
 *  - OpenAI-compatible tool definitions (name, description, JSON schema);
 *  - in-process bridge route + query-parameter mapping;
 *  - response kind (json | png).
 *
 * Run: node Mod/CS2MCP/Server/extract-tools.mjs
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const sourcePath = path.join(here, "index.ts");
const outputPath = path.join(here, "..", "..", "Agent", "ToolCatalog.json");

const src = fs.readFileSync(sourcePath, "utf8");

function isWhitespace(ch) {
  return ch === " " || ch === "\t" || ch === "\n" || ch === "\r";
}

/** Splits a string on top-level commas, respecting strings, parens and braces. */
function splitTopLevel(text) {
  const parts = [];
  let depth = 0;
  let start = 0;
  let quote = null;
  for (let i = 0; i < text.length; i++) {
    const ch = text[i];
    if (quote) {
      if (ch === "\\") i++;
      else if (ch === quote) quote = null;
      continue;
    }
    if (ch === '"' || ch === "'" || ch === "`") quote = ch;
    else if (ch === "(" || ch === "{" || ch === "[") depth++;
    else if (ch === ")" || ch === "}" || ch === "]") depth--;
    else if (ch === "," && depth === 0) {
      parts.push(text.slice(start, i).trim());
      start = i + 1;
    }
  }
  parts.push(text.slice(start).trim());
  return parts.filter((p) => p.length > 0);
}

/** Finds the index of the matching close paren/bracket/brace for open at `openIndex`. */
function findClose(text, openIndex) {
  const open = text[openIndex];
  const close = open === "(" ? ")" : open === "{" ? "}" : "]";
  let depth = 0;
  let quote = null;
  for (let i = openIndex; i < text.length; i++) {
    const ch = text[i];
    if (quote) {
      if (ch === "\\") i++;
      else if (ch === quote) quote = null;
      continue;
    }
    if (ch === '"' || ch === "'" || ch === "`") quote = ch;
    else if (ch === open) depth++;
    else if (ch === close) {
      depth--;
      if (depth === 0) return i;
    }
  }
  return -1;
}

function readStringLiteral(raw) {
  const m = /^"((?:\\.|[^"])*)"$/.exec(raw.trim());
  if (!m) return null;
  return JSON.parse(`"${m[1]}"`);
}

/** Concatenated string expression: `"a" + "b"`. */
function readStringExpression(raw) {
  let rest = raw.trim();
  let result = "";
  while (rest.length > 0) {
    rest = rest.trimStart();
    if (!rest.startsWith('"')) return null;
    let end = 1;
    while (end < rest.length) {
      if (rest[end] === "\\") end += 2;
      else if (rest[end] === '"') break;
      else end++;
    }
    if (end >= rest.length) return null;
    const literal = readStringLiteral(rest.slice(0, end + 1));
    if (literal === null) return null;
    result += literal;
    rest = rest.slice(end + 1).replace(/^\s*\+\s*/, "");
  }
  return result;
}

/** Splits a zod chain like `z.string().optional().describe("...")` into method calls. */
function splitZodChain(expr) {
  let rest = expr.trim();
  if (!rest.startsWith("z")) return [];
  rest = rest.slice(1);
  const calls = [];
  while (rest.length > 0) {
    rest = rest.replace(/^\s*\.?/, "");
    const m = /^([a-zA-Z]+)\(/.exec(rest);
    if (!m) break;
    const openIndex = rest.indexOf("(");
    const closeIndex = findClose(rest, openIndex);
    if (closeIndex < 0) break;
    calls.push({
      method: m[1],
      args: rest.slice(openIndex + 1, closeIndex).trim(),
    });
    rest = rest.slice(closeIndex + 1).trim();
  }
  return calls;
}

function parseZodSchema(expr) {
  const calls = splitZodChain(expr);
  const base = calls.find((c) => c.method === "string" || c.method === "number" || c.method === "boolean" || c.method === "enum");
  if (!base) return null;

  const schema = { type: "string" };
  if (base.method === "number") {
    schema.type = calls.some((c) => c.method === "int") ? "integer" : "number";
    for (const call of calls) {
      if (call.method === "min") schema.minimum = Number(call.args);
      if (call.method === "max") schema.maximum = Number(call.args);
    }
  } else if (base.method === "boolean") {
    schema.type = "boolean";
  } else if (base.method === "enum") {
    schema.type = "string";
    schema.enum = JSON.parse(base.args);
  }

  const describe = calls.find((c) => c.method === "describe");
  if (describe) {
    const description = readStringLiteral(describe.args);
    if (description !== null) schema.description = description;
  }
  return schema;
}

function parseInputSchema(raw) {
  const start = raw.indexOf("{");
  if (start < 0) return null;
  const end = findClose(raw, start);
  if (end < 0) return null;
  const body = raw.slice(start + 1, end);
  const properties = {};
  const required = [];
  for (const entry of splitTopLevel(body)) {
    const colon = entry.indexOf(":");
    if (colon < 0) continue;
    const key = entry.slice(0, colon).trim().replace(/^"|"$/g, "");
    const zodExpr = entry.slice(colon + 1).trim();
    const schema = parseZodSchema(zodExpr);
    if (!schema) continue;
    properties[key] = schema;
    if (!zodExpr.includes(".optional()")) required.push(key);
  }
  return { properties, required };
}

function argFromExpression(expr) {
  const trimmed = expr.trim();
  const stringCall = /^String\(\s*([A-Za-z_][\w]*)\s*\)$/.exec(trimmed);
  if (stringCall) return { kind: "arg", value: stringCall[1] };
  const boolTrue = /^"true"$/.test(trimmed);
  const boolFalse = /^"false"$/.test(trimmed);
  if (boolTrue || boolFalse) return { kind: "literal", value: boolTrue ? "true" : "false" };
  if (/^[A-Za-z_][\w]*$/.test(trimmed)) return { kind: "arg", value: trimmed };
  return null;
}

function parseQueryMapping(block) {
  const query = [];
  const seen = new Set();

  const add = (key, spec) => {
    if (!seen.has(key)) {
      seen.add(key);
      query.push({ key, ...spec });
    }
  };

  // URLSearchParams object shorthand / explicit entries.
  const paramsObject = /new URLSearchParams\(\{([^}]*)\}\)/.exec(block);
  if (paramsObject) {
    for (const entry of splitTopLevel(paramsObject[1])) {
      const colon = entry.indexOf(":");
      let key;
      let expr;
      if (colon < 0) {
        key = entry.trim();
        expr = key;
      } else {
        key = entry.slice(0, colon).trim();
        expr = entry.slice(colon + 1).trim();
      }
      const parsed = argFromExpression(expr);
      if (parsed && parsed.kind === "arg") add(key, { arg: parsed.value });
    }
  }

  // bool true-only pattern: if (force) params.set("force", "true")
  const trueOnlyRe = /if\s*\(\s*([A-Za-z_][\w]*)\s*\)\s*params\.set\(\s*"([^"]+)"\s*,\s*"true"\s*\)/g;
  let trueMatch;
  while ((trueMatch = trueOnlyRe.exec(block)) !== null) {
    const key = trueMatch[2];
    add(key, { arg: trueMatch[1], boolMode: "trueOnly" });
  }

  // params.set("key", expr) calls.
  const setRe = /params\.set\(\s*"([^"]+)"\s*,\s*([^;]+)\s*\)/g;
  let setMatch;
  while ((setMatch = setRe.exec(block)) !== null) {
    const key = setMatch[1];
    const expr = setMatch[2].trim();
    if (query.some((q) => q.key === key)) continue;
    const parsed = argFromExpression(expr);
    if (parsed && parsed.kind === "arg") {
      add(key, { arg: parsed.value });
    } else if (parsed && parsed.kind === "literal") {
      add(key, { literal: parsed.value });
    }
  }

  return query;
}

function parseHandler(block) {
  const fetchRe = /bridge(?:Json|Fetch)\((?:"([^"]*)"|`([^`]*)`)/;
  const fetchMatch = fetchRe.exec(block);
  if (!fetchMatch) return null;
  const template = fetchMatch[1] !== undefined ? fetchMatch[1] : fetchMatch[2];

  const response = /bridgeFetch\(/.test(block) && template.includes("/screenshot") ? "png" : "json";
  const query = parseQueryMapping(block);

  // Template-literal pairs without URLSearchParams, e.g. `/city/taxes/set?area=${area}&rate=${rate}`.
  if (template.includes("${")) {
    const pairRe = /([A-Za-z_][\w]*)=(\$\{[^}]+\})/g;
    let pairMatch;
    while ((pairMatch = pairRe.exec(template)) !== null) {
      const key = pairMatch[1];
      const parsed = argFromExpression(pairMatch[2].slice(2, -1));
      if (parsed && parsed.kind === "arg") {
        if (!query.some((q) => q.key === key)) query.push({ key, arg: parsed.value });
      }
    }
  }

  // Screenshot width default: `const w = width ?? 1280`.
  if (template.includes("/screenshot")) {
    query.splice(0, query.length, { key: "width", arg: "width", default: "1280" });
  }

  // Plain-quoted route (no template): "/ping", "/state", "/city/budget"...
  if (!template.includes("${")) {
    const routeStart = template.indexOf("?");
    const cleanRoute = routeStart < 0 ? template : template.slice(0, routeStart);
    return {
      route: cleanRoute.startsWith("/") ? cleanRoute : `/${cleanRoute}`,
      query,
      response,
    };
  }

  let route = template.split("${")[0];
  const queryStart = route.indexOf("?");
  if (queryStart >= 0) route = route.slice(0, queryStart);
  if (!route.startsWith("/")) route = `/${route}`;
  return { route, query, response };
}

const tools = [];
const registrationRe = /(?:^|\n)((?:server\.)?(?:registerTool|registerJsonTool))\(/g;
let registration;
while ((registration = registrationRe.exec(src)) !== null) {
  const openIndex = src.indexOf("(", registration.index);
  const closeIndex = findClose(src, openIndex);
  if (closeIndex < 0) break;
  const body = src.slice(openIndex + 1, closeIndex);
  const args = splitTopLevel(body);
  const isJsonTool = registration[1] === "registerJsonTool";

  let name;
  let description;
  let schema = { properties: {}, required: [] };
  let handler;

  if (isJsonTool) {
    name = readStringLiteral(args[0]);
    description = readStringExpression(args[2]);
    const route = readStringLiteral(args[3]);
    handler = { route, query: [], response: "json" };
  } else {
    name = readStringLiteral(args[0]);
    const options = args[1];
    const titleMatch = /title:\s*("[^"]*")/.exec(options);
    const title = titleMatch ? readStringLiteral(titleMatch[1]) : name;
    const descMatch = /description:\s*((?:"(?:\\.|[^"])*"\s*\+\s*)*"(?:\\.|[^"])*")/.exec(options);
    description = descMatch ? readStringExpression(descMatch[1]) : title;
    const schemaStart = options.indexOf("inputSchema:");
    if (schemaStart >= 0) {
      const parsedSchema = parseInputSchema(options.slice(schemaStart + "inputSchema:".length));
      if (parsedSchema) schema = parsedSchema;
    }
    handler = parseHandler(args[2]);
  }

  if (!name || !handler) {
    console.error(`skip unparsable registration at ${registration.index}`);
    continue;
  }

  tools.push({
    name,
    description,
    parameters: {
      type: "object",
      properties: schema.properties,
      required: schema.required,
    },
    route: handler.route,
    query: handler.query,
    response: handler.response,
  });
}

const catalog = {
  source: "https://github.com/LancerComet/cities-skylines-2-mcp (Apache-2.0)",
  mcpVersion: "0.8.0",
  tools,
};

fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(outputPath, JSON.stringify(catalog, null, 2) + "\n", "utf8");
console.log(`wrote ${tools.length} tools to ${outputPath}`);
