(() => {
  "use strict";

  const menuButton = document.querySelector("[data-menu-button]");
  const mobileMenu = document.querySelector("[data-mobile-menu]");

  const closeMenu = () => {
    if (!menuButton || !mobileMenu) return;
    menuButton.setAttribute("aria-expanded", "false");
    mobileMenu.hidden = true;
    mobileMenu.classList.add("hidden");
  };

  if (menuButton && mobileMenu) {
    menuButton.addEventListener("click", () => {
      const willOpen = menuButton.getAttribute("aria-expanded") !== "true";
      menuButton.setAttribute("aria-expanded", String(willOpen));
      mobileMenu.hidden = !willOpen;
      mobileMenu.classList.toggle("hidden", !willOpen);
      if (willOpen) mobileMenu.querySelector("a")?.focus();
    });

    mobileMenu.addEventListener("click", (event) => {
      if (event.target.closest("a")) closeMenu();
    });

    document.addEventListener("click", (event) => {
      if (!mobileMenu.contains(event.target) && !menuButton.contains(event.target)) {
        closeMenu();
      }
    });

    document.addEventListener("keydown", (event) => {
      if (event.key === "Escape" && menuButton.getAttribute("aria-expanded") === "true") {
        closeMenu();
        menuButton.focus();
      }
    });
  }

  const galleryMain = document.querySelector("[data-gallery-main]");
  const galleryButtons = [...document.querySelectorAll("[data-gallery-thumbnail]")];
  if (galleryMain && galleryButtons.length > 0) {
    galleryButtons.forEach((button) => {
      button.addEventListener("click", () => {
        galleryButtons.forEach((item) => item.setAttribute("aria-pressed", "false"));
        button.setAttribute("aria-pressed", "true");
        galleryMain.src = button.dataset.largeUrl;
        galleryMain.srcset = `${button.dataset.thumbnailUrl} 480w, ${button.dataset.largeUrl} 1600w`;
        galleryMain.alt = button.dataset.alt;
      });
    });
  }

  const variantOptions = [...document.querySelectorAll("[data-variant-option]")];
  const selectedVolume = document.querySelector("[data-selected-volume]");
  const selectedPrice = document.querySelector("[data-selected-price]");
  const selectedAvailability = document.querySelector("[data-selected-availability]");
  const purchaseButton = document.querySelector("[data-demo-purchase]");

  const selectVariant = (option, updateAddress) => {
    const available = Number.parseInt(option.dataset.available ?? "0", 10);
    variantOptions.forEach((item) => item.removeAttribute("aria-current"));
    option.setAttribute("aria-current", "true");
    if (selectedVolume) selectedVolume.textContent = `${option.dataset.volume} ml`;
    if (selectedPrice) selectedPrice.textContent = option.dataset.price;
    if (selectedAvailability) {
      selectedAvailability.textContent = available > 0
        ? `${available} ${available === 1 ? "unidade disponível" : "unidades disponíveis"}. Valores e estoque ilustrativos.`
        : "Esta variante está temporariamente esgotada.";
    }
    if (purchaseButton) {
      purchaseButton.disabled = available === 0;
      purchaseButton.textContent = available > 0
        ? "Adicionar à sacola · demonstração"
        : "Variante esgotada";
    }
    if (updateAddress) history.replaceState({}, "", option.href);
  };

  variantOptions.forEach((option) => {
    option.addEventListener("click", (event) => {
      event.preventDefault();
      selectVariant(option, true);
    });
  });

  const purchaseDialog = document.querySelector("[data-purchase-dialog]");
  const dialogClose = document.querySelector("[data-dialog-close]");
  if (purchaseButton && purchaseDialog) {
    purchaseButton.addEventListener("click", () => {
      if (!purchaseButton.disabled) purchaseDialog.showModal();
    });
    dialogClose?.addEventListener("click", () => purchaseDialog.close());
    purchaseDialog.addEventListener("click", (event) => {
      if (event.target === purchaseDialog) purchaseDialog.close();
    });
  }

  const catalog = document.querySelector("[data-catalog]");
  if (catalog && document.body.dataset.showcase === "true") {
    const filters = [...catalog.querySelectorAll("[data-catalog-filter]")];
    const cards = [...catalog.querySelectorAll("[data-product-card]")];
    const count = document.querySelector("[data-catalog-count]");
    const empty = catalog.querySelector("[data-catalog-empty]");

    const applyFilter = (family, updateAddress) => {
      const normalizedFamily = family?.trim() ?? "";
      let visible = 0;
      cards.forEach((card) => {
        const show = normalizedFamily === "" || card.dataset.family === normalizedFamily;
        card.hidden = !show;
        if (show) visible += 1;
      });
      filters.forEach((filter) => {
        if (filter.dataset.family === normalizedFamily) filter.setAttribute("aria-current", "page");
        else filter.removeAttribute("aria-current");
      });
      if (count) count.textContent = `${visible} ${visible === 1 ? "fragrância apresentada" : "fragrâncias apresentadas"}`;
      if (empty) empty.classList.toggle("hidden", visible !== 0);
      if (updateAddress) {
        const url = new URL(window.location.href);
        if (normalizedFamily) url.searchParams.set("familia", normalizedFamily);
        else url.searchParams.delete("familia");
        history.replaceState({}, "", url);
      }
    };

    filters.forEach((filter) => {
      filter.addEventListener("click", (event) => {
        event.preventDefault();
        applyFilter(filter.dataset.family, true);
      });
    });

    const requestedFamily = new URL(window.location.href).searchParams.get("familia") ?? "";
    const initialFamily = filters.some((filter) => filter.dataset.family === requestedFamily)
      ? requestedFamily
      : "";
    applyFilter(initialFamily, false);
    window.addEventListener("popstate", () => {
      applyFilter(new URL(window.location.href).searchParams.get("familia") ?? "", false);
    });
  }
})();
