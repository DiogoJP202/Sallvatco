import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import vm from "node:vm";
import test from "node:test";

const script = readFileSync(new URL("../../src/Sallvat.Web/wwwroot/js/checkout-validation.js", import.meta.url), "utf8");

function fixture() {
  const handlers = {};
  const attributes = {};
  const classes = new Set(["field-validation-valid"]);
  const message = {
    dataset: { valmsgFor: "Form.Phone" }, textContent: "", setAttribute() {},
    classList: { toggle(name, enabled) { if (enabled) classes.add(name); else classes.delete(name); } },
  };
  const field = {
    id: "Form_Phone", name: "Form.Phone", value: "", required: true, maxLength: 32,
    dataset: { valRequired: "Informe um telefone com DDD." },
    title: "Confira o telefone.",
    validity: { typeMismatch: false, patternMismatch: false },
    setCustomValidity(value) { this.error = value; },
    setAttribute(name, value) { attributes[name] = value; },
    getAttribute(name) { return attributes[name]; },
    addEventListener(name, action) { handlers[name] = action; },
    focus() { this.focused = true; },
    reportValidity() { this.reported = true; },
  };
  const formHandlers = {};
  const form = {
    querySelectorAll(selector) { return selector === "[data-valmsg-for]" ? [message] : [field]; },
    addEventListener(name, action) { formHandlers[name] = action; },
  };
  vm.runInNewContext(script, { document: { querySelector: () => form } });
  return { field, message, handlers, attributes, formHandlers, classes };
}

test("required and whitespace fields show associated accessible feedback", () => {
  const { field, message, handlers, attributes, classes } = fixture();
  field.value = "   ";
  handlers.blur();
  assert.equal(message.textContent, "Informe um telefone com DDD.");
  assert.equal(attributes["aria-invalid"], "true");
  assert.equal(attributes["aria-describedby"], message.id);
  assert.equal(classes.has("field-validation-valid"), false);
  assert.equal(classes.has("field-validation-error"), true);
});

test("malformed values show feedback and corrected or saved-address values clear it", () => {
  const { field, message, handlers, attributes } = fixture();
  field.value = "abc";
  field.validity.patternMismatch = true;
  handlers.invalid();
  assert.equal(message.textContent, "Confira o telefone.");
  field.value = "(11) 99999-8888";
  field.validity.patternMismatch = false;
  handlers.input();
  assert.equal(message.textContent, "");
  assert.equal(field.error, "");
  assert.equal(attributes["aria-invalid"], "false");
});

test("submit rejects whitespace and focuses first invalid field", () => {
  const { field, formHandlers } = fixture();
  let prevented = false;
  field.value = " ";
  formHandlers.submit({ preventDefault() { prevented = true; } });
  assert.equal(prevented, true);
  assert.equal(field.focused, true);
  assert.equal(field.reported, true);
});

test("email and maximum length have readable messages", () => {
  const { field, message, handlers } = fixture();
  field.value = "bad-email";
  field.dataset.valEmail = "Informe um e-mail válido.";
  field.validity.typeMismatch = true;
  handlers.blur();
  assert.equal(message.textContent, "Informe um e-mail válido.");
  field.validity.typeMismatch = false;
  field.value = "a".repeat(33);
  handlers.blur();
  assert.equal(message.textContent, "Use no máximo 32 caracteres.");
});

test("pages without checkout are unaffected", () => {
  assert.doesNotThrow(() => vm.runInNewContext(script, { document: { querySelector: () => null } }));
});
