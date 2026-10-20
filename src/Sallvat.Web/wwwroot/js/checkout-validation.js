(() => {
  "use strict";
  const form = document.querySelector("[data-checkout-form]");
  if (!form) return;

  const messages = new Map(
    [...form.querySelectorAll("[data-valmsg-for]")]
      .map((message) => [message.dataset.valmsgFor, message]),
  );

  const validate = (field) => {
    field.setCustomValidity("");
    let error = "";
    if (field.required && !field.value.trim()) {
      error = field.dataset.valRequired || "Preencha este campo.";
    } else if (field.validity.typeMismatch) {
      error = field.dataset.valEmail || "Informe um valor válido.";
    } else if (field.validity.patternMismatch) {
      error = field.title || "Confira o formato deste campo.";
    } else if (field.maxLength > 0 && field.value.length > field.maxLength) {
      error = `Use no máximo ${field.maxLength} caracteres.`;
    }
    field.setCustomValidity(error);
    field.setAttribute("aria-invalid", String(Boolean(error)));
    const message = messages.get(field.name);
    if (message) {
      message.textContent = error;
      message.classList.toggle("field-validation-error", Boolean(error));
      message.classList.toggle("field-validation-valid", !error);
    }
    return !error;
  };

  form.querySelectorAll("input:not([type=hidden]), select").forEach((field) => {
    const message = messages.get(field.name);
    if (message) {
      message.id ||= `${field.id}-error`;
      const describedBy = new Set((field.getAttribute("aria-describedby") || "").split(" ").filter(Boolean));
      describedBy.add(message.id);
      field.setAttribute("aria-describedby", [...describedBy].join(" "));
      message.setAttribute("aria-live", "polite");
      if (message.textContent.trim()) field.setAttribute("aria-invalid", "true");
    }
    field.addEventListener("blur", () => validate(field));
    field.addEventListener("input", () => validate(field));
    field.addEventListener("change", () => validate(field));
    field.addEventListener("invalid", () => validate(field));
  });

  // Keep HTML constraints active without JavaScript; server validation is authoritative.
  form.addEventListener("submit", (event) => {
    const fields = [...form.querySelectorAll("input:not([type=hidden]), select")];
    const firstInvalid = fields.filter((field) => !validate(field))[0];
    if (firstInvalid) {
      event.preventDefault();
      firstInvalid.focus();
      firstInvalid.reportValidity();
    }
  });
})();
