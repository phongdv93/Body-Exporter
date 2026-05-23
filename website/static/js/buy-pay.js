(function () {
  const root = document.querySelector(".buy-checkout");
  if (!root) return;

  const btnContinue = document.getElementById("btn-checkout-continue");
  const pricingCard = document.getElementById("pricing");
  const modeButtons = root.querySelectorAll(".pay-mode-btn");

  const payModal = document.getElementById("pay-modal");
  const payModalBackdrop = document.getElementById("pay-modal-backdrop");
  const payModalClose = document.getElementById("pay-modal-close");
  const modalEmail = document.getElementById("modal-email");
  const modalEmailHint = document.getElementById("modal-email-hint");
  const modalYearsInput = document.getElementById("modal-years");
  const modalYearsMinus = document.getElementById("modal-years-minus");
  const modalYearsPlus = document.getElementById("modal-years-plus");
  const modalTotalLine = document.getElementById("modal-total-line");
  const modalBankDl = document.getElementById("modal-bank-dl");
  const modalWaitHint = document.getElementById("modal-wait-hint");
  const modalQrInner = document.getElementById("pay-modal-qr-inner");

  const unitVnd = parseInt(root.dataset.unitPriceVnd || "0", 10) || 0;
  const maxYears = parseInt(root.dataset.maxYears || "5", 10) || 5;
  const cookieName = root.dataset.cookieName || "be_pay_mode";
  const paddleAvailable = root.dataset.paddleAvailable === "1";
  let currentMode = root.dataset.payMode || "vn";
  let qrFetchTimer = null;
  let lastQrKey = "";

  function validEmail(v) {
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test((v || "").trim());
  }

  function getYears() {
    let y = parseInt(modalYearsInput && modalYearsInput.value, 10);
    if (!y || y < 1) y = 1;
    if (y > maxYears) y = maxYears;
    return y;
  }

  function setYears(y) {
    y = Math.max(1, Math.min(maxYears, y));
    if (modalYearsInput) modalYearsInput.value = String(y);
    syncModalSummary();
    scheduleVnQrReload();
  }

  function fmtVnd(n) {
    return String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ".");
  }

  function syncModalSummary() {
    if (!modalTotalLine || unitVnd <= 0) return;
    const y = getYears();
    const total = unitVnd * y;
    const totalLbl = root.dataset.msgTotal || "Total";
    const yearsLbl = root.dataset.msgYearsCount || "years";
    const label = y === 1 ? totalLbl : y + " " + yearsLbl;
    modalTotalLine.innerHTML =
      '<span class="pay-modal-due-label">' +
      escapeHtml(label) +
      '</span> <strong class="pay-modal-due-amt">' +
      fmtVnd(total) +
      " VND</strong>";
  }

  function setCookie(mode) {
    const maxAge = 60 * 60 * 24 * 90;
    const secure = location.protocol === "https:" ? "; Secure" : "";
    document.cookie =
      cookieName + "=" + mode + "; path=/; max-age=" + maxAge + "; SameSite=Lax" + secure;
  }

  function syncPricingMode(mode) {
    if (!pricingCard) return;
    pricingCard.dataset.payMode = mode;
    pricingCard.querySelectorAll(".price-vn").forEach((el) => {
      el.classList.toggle("is-hidden", mode !== "vn");
    });
    pricingCard.querySelectorAll(".price-intl").forEach((el) => {
      el.classList.toggle("is-hidden", mode !== "intl");
    });
  }

  function setPayMode(mode) {
    currentMode = mode;
    root.dataset.payMode = mode;
    modeButtons.forEach((btn) => {
      const on = btn.dataset.payMode === mode;
      btn.classList.toggle("active", on);
      btn.setAttribute("aria-selected", on ? "true" : "false");
    });
    syncPricingMode(mode);
    setCookie(mode);
  }

  modeButtons.forEach((btn) => {
    btn.addEventListener("click", () => setPayMode(btn.dataset.payMode));
  });

  if (modalYearsMinus) modalYearsMinus.addEventListener("click", () => setYears(getYears() - 1));
  if (modalYearsPlus) modalYearsPlus.addEventListener("click", () => setYears(getYears() + 1));
  if (modalYearsInput) {
    modalYearsInput.addEventListener("change", () => setYears(getYears()));
    modalYearsInput.addEventListener("input", () => setYears(getYears()));
  }

  function escapeHtml(s) {
    return String(s)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function showModalEmailHint(msg) {
    if (!modalEmailHint) return;
    if (msg) {
      modalEmailHint.textContent = msg;
      modalEmailHint.classList.remove("is-hidden");
    } else {
      modalEmailHint.textContent = "";
      modalEmailHint.classList.add("is-hidden");
    }
  }

  function openVnModal() {
    if (!payModal) return;
    payModal.classList.remove("is-hidden");
    document.body.classList.add("pay-modal-open");
    syncModalSummary();
    showModalEmailHint("");
    lastQrKey = "";
    if (modalBankDl) modalBankDl.classList.add("is-hidden");
    if (modalWaitHint) modalWaitHint.classList.add("is-hidden");
    if (modalQrInner) {
      modalQrInner.innerHTML =
        '<p class="pay-modal-qr-placeholder">' +
        escapeHtml(root.dataset.msgVnQrHint || "") +
        "</p>";
    }
    modalEmail && modalEmail.focus();
    loadVnQr();
  }

  function closeModal() {
    if (!payModal) return;
    payModal.classList.add("is-hidden");
    document.body.classList.remove("pay-modal-open");
    clearTimeout(qrFetchTimer);
  }

  if (payModalClose) payModalClose.addEventListener("click", closeModal);
  if (payModalBackdrop) payModalBackdrop.addEventListener("click", closeModal);

  function renderBankDl(data) {
    if (!modalBankDl) return;
    const bank = data.bank || {};
    let html = "";
    if (bank.bank) {
      html += "<dt>" + escapeHtml(data.labels.bank) + "</dt><dd>" + escapeHtml(bank.bank) + "</dd>";
    }
    if (bank.account) {
      html +=
        "<dt>" +
        escapeHtml(data.labels.account) +
        '</dt><dd><code>' +
        escapeHtml(bank.account) +
        "</code></dd>";
    }
    html +=
      "<dt>" +
      escapeHtml(data.labels.memo) +
      '</dt><dd><code>' +
      escapeHtml(data.memo) +
      "</code></dd>";
    modalBankDl.innerHTML = html;
    modalBankDl.classList.remove("is-hidden");
  }

  function renderVnQr(data) {
    if (modalQrInner) {
      modalQrInner.innerHTML =
        '<img class="pay-modal-qr-img" src="' +
        escapeHtml(data.qr_url) +
        '" alt="VietQR" loading="lazy">';
    }
    renderBankDl(data);
    if (modalWaitHint) {
      modalWaitHint.innerHTML = data.wait_hint_html || "";
      modalWaitHint.classList.toggle("is-hidden", !data.wait_hint_html);
    }
  }

  function loadVnQr() {
    if (!payModal || payModal.classList.contains("is-hidden")) return;
    if (root.dataset.vietqrAvailable !== "1") return;
    const email = (modalEmail && modalEmail.value.trim()) || "";
    const years = getYears();
    if (!validEmail(email)) {
      showModalEmailHint(root.dataset.msgEnterEmail || "");
      if (modalBankDl) modalBankDl.classList.add("is-hidden");
      if (modalWaitHint) modalWaitHint.classList.add("is-hidden");
      if (modalQrInner) {
        modalQrInner.innerHTML =
          '<p class="pay-modal-qr-placeholder">' +
          escapeHtml(root.dataset.msgVnQrHint || "") +
          "</p>";
      }
      return;
    }
    showModalEmailHint("");
    const key = email + "|" + years;
    if (key === lastQrKey) return;
    lastQrKey = key;

    if (modalQrInner) {
      modalQrInner.innerHTML =
        '<p class="pay-modal-qr-placeholder">' +
        escapeHtml(root.dataset.msgQrLoading || "…") +
        "</p>";
    }
    fetch(
      "/buy/api/vietqr?email=" +
        encodeURIComponent(email) +
        "&years=" +
        encodeURIComponent(String(years))
    )
      .then((r) => r.json())
      .then((data) => {
        if (data.ok) renderVnQr(data);
        else {
          lastQrKey = "";
          if (modalQrInner) {
            modalQrInner.innerHTML =
              '<p class="pay-modal-qr-err">' + escapeHtml(root.dataset.msgQrFail || "") + "</p>";
          }
        }
      })
      .catch(() => {
        lastQrKey = "";
        if (modalQrInner) {
          modalQrInner.innerHTML =
            '<p class="pay-modal-qr-err">' + escapeHtml(root.dataset.msgNetFail || "") + "</p>";
        }
      });
  }

  function scheduleVnQrReload() {
    if (!payModal || payModal.classList.contains("is-hidden")) return;
    clearTimeout(qrFetchTimer);
    lastQrKey = "";
    qrFetchTimer = setTimeout(loadVnQr, 300);
  }

  if (modalEmail) {
    modalEmail.addEventListener("input", scheduleVnQrReload);
    modalEmail.addEventListener("change", scheduleVnQrReload);
  }

  function openIntlPaddle() {
    if (!paddleAvailable || !window.BodyExporterPaddle) {
      window.location.href = "/buy/paddle";
      return;
    }
    const cfgEl = document.getElementById("paddle-config");
    if (!cfgEl) {
      window.location.href = "/buy/paddle";
      return;
    }
    let cfg = {};
    try {
      cfg = JSON.parse(cfgEl.textContent || "{}");
    } catch (e) {
      return;
    }
    if (btnContinue) {
      btnContinue.disabled = true;
      btnContinue.textContent = root.dataset.msgPaddleLoading || "…";
    }
    window.BodyExporterPaddle.setConfig(cfg);
    const qty = 1;
    const p = window.BodyExporterPaddle.openWithItems
      ? window.BodyExporterPaddle.openWithItems({
          priceId: cfg.price_id,
          quantity: qty,
        })
      : Promise.reject(new Error("no_items"));
    p.catch(function () {
      alert(root.dataset.msgPaddleFail || "Could not open checkout");
    }).finally(function () {
      if (btnContinue) {
        btnContinue.disabled = false;
        btnContinue.textContent = root.dataset.msgBtnPay || "Pay";
      }
    });
  }

  function onContinue() {
    if (currentMode === "intl" && paddleAvailable) {
      openIntlPaddle();
      return;
    }
    if (currentMode === "vn" && root.dataset.vietqrAvailable === "1") {
      openVnModal();
      return;
    }
  }

  if (btnContinue) btnContinue.addEventListener("click", onContinue);

  const prefill = (root.dataset.prefillEmail || "").trim();
  if (modalEmail && prefill && validEmail(prefill)) {
    modalEmail.value = prefill;
  }

  syncPricingMode(currentMode);
})();
