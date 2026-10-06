import assert from "node:assert/strict";
import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import { lintDocumentation, run } from "../lint-markdown.mjs";

async function fixture(t) {
  const root = await mkdtemp(join(tmpdir(), "sallvat-lint-test-"));
  t.after(() => rm(root, { recursive: true, force: true }));
  await mkdir(join(root, "docs"));
  await writeFile(join(root, "README.md"), "# Projeto\n\nApresentação.\n");
  return root;
}

test("inclui README e todos os Markdown diretos de docs em ordem estável", async (t) => {
  const root = await fixture(t);
  await writeFile(join(root, "docs", "Z.md"), "# Z\n");
  await writeFile(join(root, "docs", "A.md"), "# A\n");
  await writeFile(join(root, "docs", "dados.json"), "{}");
  await mkdir(join(root, "docs", "nested.md"));
  await writeFile(join(root, "docs", "nested.md", "ignorado.md"), "inválido");
  const result = await lintDocumentation(root);
  assert.deepEqual(result.files, ["README.md", "docs/A.md", "docs/Z.md"]);
  assert.equal(result.errors, 0);
});

test("violação em documento faz a execução falhar e identifica regra/arquivo", async (t) => {
  const root = await fixture(t);
  await writeFile(join(root, "docs", "invalido.md"), "# Título\nTexto sem espaço.\n");
  const messages = [];
  const status = await run(root, { log: (x) => messages.push(x), error: (x) => messages.push(x) });
  assert.equal(status, 1);
  assert.match(messages.join("\n"), /invalido\.md/);
  assert.match(messages.join("\n"), /MD022/);
});

test("preserva as exceções explícitas inline já usadas pela documentação", async (t) => {
  const root = await fixture(t);
  await writeFile(join(root, "README.md"),
    `<!-- markdownlint-disable MD013 -->\n\n# Projeto\n\n${"palavra ".repeat(30).trim()}\n`);
  assert.equal((await lintDocumentation(root)).errors, 0);
});

test("Markdown válido retorna status zero", async (t) => {
  const root = await fixture(t);
  assert.equal(await run(root, { log() {}, error() { assert.fail("Diagnóstico inesperado"); } }), 0);
});

test("arquivo obrigatório ausente falha em vez de produzir sucesso vazio", async (t) => {
  const root = await fixture(t);
  await rm(join(root, "README.md"));
  await assert.rejects(() => lintDocumentation(root), /ENOENT/);
});

test("diretório docs ausente falha em vez de ignorar documentação", async (t) => {
  const root = await fixture(t);
  await rm(join(root, "docs"), { recursive: true });
  await assert.rejects(() => lintDocumentation(root), /ENOENT/);
});

test("parser mantém suporte a fórmulas, tabelas e blocos Mermaid", async (t) => {
  const root = await fixture(t);
  await writeFile(join(root, "docs", "recursos.md"),
    "# Recursos\n\nFórmula: $x^2$.\n\n$$\nx^2 + y^2 = z^2\n$$\n\n" +
    "| Campo | Valor |\n| ----- | ----- |\n| A     | B     |\n\n" +
    "```mermaid\nflowchart LR\n  A --> B\n```\n");
  assert.equal((await lintDocumentation(root)).errors, 0);
});
