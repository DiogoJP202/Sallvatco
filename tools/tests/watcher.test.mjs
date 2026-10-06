import assert from "node:assert/strict";
import { mkdtemp, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { test } from "node:test";
import watcher from "@parcel/watcher";

// Verifica o override usado pelo modo --watch do Tailwind após npm ci --ignore-scripts.
test("watcher nativo continua detectando alteração de arquivo", async () => {
  const root = await mkdtemp(join(tmpdir(), "sallvat-watcher-test-"));
  const file = join(root, "entrada.css");
  let subscription;
  let timeout;
  try {
    const { promise, resolve, reject } = Promise.withResolvers();
    subscription = await watcher.subscribe(root, (error, events) => {
      if (error) reject(error);
      else if (events.some((event) => event.path === file)) resolve(events);
    });
    timeout = setTimeout(() => reject(new Error("Watcher não notificou em 10 segundos")), 10_000);
    await writeFile(file, ".teste { color: red; }\n");
    const events = await promise;
    assert.ok(events.some((event) => event.path === file && ["create", "update"].includes(event.type)));
  } finally {
    clearTimeout(timeout);
    await subscription?.unsubscribe();
    await rm(root, { recursive: true, force: true });
  }
});
