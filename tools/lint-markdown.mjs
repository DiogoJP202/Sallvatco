import { readdir } from "node:fs/promises";
import { dirname, join, relative, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { lint } from "markdownlint/promise";

// Mesmo escopo anterior: README.md e docs/*.md, sem globbing de terceiros.
export async function lintDocumentation(root) {
  const entries = await readdir(join(root, "docs"), { withFileTypes: true });
  const files = ["README.md", ...entries
    .filter((entry) => entry.isFile() && entry.name.endsWith(".md"))
    .map((entry) => `docs/${entry.name}`)
    .sort()];
  const results = await lint({ files: files.map((file) => join(root, file)) });
  const errors = Object.values(results).reduce((total, items) => total + items.length, 0);
  const diagnostics = Object.entries(results).flatMap(([file, items]) => items.map((item) =>
    `${relative(root, file)}:${item.lineNumber} ${item.ruleNames[0]} ${item.ruleDescription}` +
    (item.errorDetail ? ` (${item.errorDetail})` : ""))).join("\n");
  return { files, errors, diagnostics };
}

export async function run(root, output = console) {
  const result = await lintDocumentation(root);
  if (result.diagnostics) output.error(result.diagnostics);
  output.log(`Markdown: ${result.files.length} arquivos, ${result.errors} erros.`);
  return result.errors ? 1 : 0;
}

if (import.meta.main) {
  const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
  try {
    process.exitCode = await run(root);
  } catch (error) {
    console.error(`Falha ao validar Markdown: ${error.message}`);
    process.exitCode = 1;
  }
}
