(function () {
  const root = document.querySelector(".buy-checkout");
  if (!root) return;

  const yearsInput = document.getElementById("license-years");
  const yearsMinus = document.getElementById("years-minus");
  const yearsPlus = document.getElementById("years-plus");
  const btnContinue = document.getElementById("btn-checkout-continue");
  const pricingCard = document.getElementById("pricing");
  const modeButtons = root.querySelectorAll(".pay-mode-btn");

  const payModal = document.getElementById("pay-modal");
  const payModalBackdrop = document.getElementById("pay-modal-backdrop");
  const payModalClose = document.getElementById("pay-modal-close");
  const modalEmail = document.getElementById("modal-email");
  const modalEmailHint = document.getElementById("modal-email-hint");
  const modalYearsDisplay = document.getElementById("modal-years-display");
  const modalTotalLine = document.getElementById("modal-total-line");
  const modalBankDl = document.getElementById("modal-bank-dl");
  const modalWaitHint = document.getElementById("modal-wait-hint");
  const modalQrInner = document.getElementById("pay-modal-qr-inner");
  const modalQrCol = document.getElementById("pay-modal-qr-col");
  const modalIntlActions = document.getElementById("modal-intl-actions");
  const modalBtnPaddle = document.getElementById("modal-btn-paddle");
  const payModalKicker = document.getElementById("pay-modal-kicker");
  const payModalTitle = document.getElementById("pay-modal-title");

  const unitVnd = parseInt(root.dataset.unitPriceVnd || "0", 10) || 0;
  const maxYears = parseInt(root.dataset.maxYears || "5", 10) || 5;
  const priceUsdUnit = parseFloat(root.dataset.priceUsd || "") || 0;
  const cookieName = root.dataset.cookieName || "be_pay_mode";
  const paddleAvailable = root.dataset.paddleAvailable === "1";
  let currentMode = root.dataset.payMode || "vn";
  let modalMode = "vn";
  let qrFetchTimer = null;
  let lastQrKey = "";

  function validEmail(v) {
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test((v || "").trim());
  }

  function getModalEmail() {
    return (modalEmail && modalEmail.value.trim()) || "";
  }

  function getYears() {
    let y = parseInt(yearsInput && yearsInput.value, 10);
    if (!y || y < 1) y = 1;
    if (y > maxYears) y = maxYears;
    return y;
  }

  function setYears(y) {
    y = Math.max(1, Math.min(maxYears, y));
    if (yearsInput) yearsInput.value = String(y);
    syncPricingTotals();
    syncModalSummary();
    scheduleVnQrReload();
  }

  function fmtVnd(n) {
    return String(n).replace(/\B(?=(\d{3})+(?!\d))/g, ".");
  }

  function syncPricingTotals() {
    const y = getYears();
    const totalVnd = unitVnd * y;
    const elVn = document.getElementById("pricing-amount-vn");
    if (elVn && unitVnd > 0) {
      elVn.innerHTML = "<strong>" + fmtVnd(totalVnd) + " VND</strong>";
    }
    const elUsd = document.getElementById("pricing-amount-usd");
    if (elUsd && priceUsdUnit > 0) {
      const totalUsd = (priceUsdUnit * y).toFixed(2).replace(/\.?0+$/, "");
      elUsd.innerHTML = "<strong>$" + totalUsd + " USD</strong>";
    }
    const subIntl = document.getElementById("pricing-sub-intl");
    if (subIntl && unitVnd > 0) {
      subIntl.textContent =
        (y > 1 ? y + " × " : "") + fmtVnd(unitVnd) + " VND / " + (root.dataset.msgPerYear || "year");
    }
  }

  function syncModalSummary() {
    const y = getYears();
    if (modalYearsDisplay) modalYearsDisplay.textContent = String(y);
    if (modalTotalLine && unitVnd > 0) {
      const totalVnd = unitVnd * y;
      if (currentMode === "intl" && priceUsdUnit > 0) {
        const usd = (priceUsdUnit * y).toFixed(2).replace(/\.?0+$/, "");
        modalTotalLine.innerHTML =
          "<strong>$" + usd + " USD</strong> <span class=\"muted\">(" + fmtVnd(totalVnd) + " VND)</span>";
      } else {
        modalTotalLine.innerHTML = "<strong>" + fmtVnd(totalVnd) + " VND</strong>";
      }
    }
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

  if (yearsMinus) yearsMinus.addEventListener("click", () => setYears(getYears() - 1));
  if (yearsPlus) yearsPlus.addEventListener("click", () => setYears(getYears() + 1));
  if (yearsInput) {
    yearsInput.addEventListener("change", () => setYears(getYears()));
    yearsInput.addEventListener("input", () => setYears(getYears()));
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

  function openModal(mode) {
    modalMode = mode;
    if (!payModal) return;
    payModal.classList.remove("is-hidden");
    payModal.classList.toggle("pay-modal--intl", mode === "intl");
    payModal.classList.toggle("pay-modal--vn", mode === "vn");
    document.body.classList.add("pay-modal-open");

    if (payModalKicker) {
      payModalKicker.textContent =
        mode === "intl"
          ? root.dataset.msgModalTitleIntl || "International payment"
          : root.dataset.msgModalTitleVn || "VietQR";
    }
    if (payModalTitle) {
      payModalTitle.textContent = root.dataset.msgModalProduct || "Body Exporter";
    }

    if (modalQrCol) modalQrCol.classList.toggle("is-hidden", mode === "intl");
    if (modalIntlActions) modalIntlActions.classList.toggle("is-hidden", mode !== "intl");
    if (modalBankDl) modalBankDl.classList.add("is-hidden");
    if (modalWaitHint) modalWaitHint.classList.add("is-hidden");

    syncModalSummary();
    showModalEmailHint("");

    if (mode === "vn" && modalQrInner) {
      modalQrInner.innerHTML =
        '<p class="hint muted pay-modal-qr-placeholder">' +
        escapeHtml(root.dataset.msgVnQrHint || "Enter email to show QR.") +
        "</p>";
      loadVnQr();
    }
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
        '</dt><dd><code class="copyable">' +
        escapeHtml(bank.account) +
        "</code></dd>";
    }
    html +=
      "<dt>" +
      escapeHtml(data.labels.amount) +
      "</dt><dd><strong>" +
      escapeHtml(data.amount_fmt) +
      " VND</strong></dd>";
    html +=
      "<dt>" +
      escapeHtml(data.labels.memo) +
      '</dt><dd><code class="copyable">' +
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
        '" alt="VietQR" width="280" height="280" loading="lazy">';
    }
    renderBankDl(data);
    if (modalWaitHint) {
      modalWaitHint.innerHTML = data.wait_hint_html || "";
      modalWaitHint.classList.toggle("is-hidden", !data.wait_hint_html);
    }
  }

  function loadVnQr() {
    if (modalMode !== "vn" || root.dataset.vietqrAvailable !== "1") return;
    const email = getModalEmail();
    const years = getYears();
    if (!validEmail(email)) {
      showModalEmailHint(root.dataset.msgEnterEmail || "Enter a valid email.");
      if (modalBankDl) modalBankDl.classList.add("is-hidden");
      if (modalWaitHint) modalWaitHint.classList.add("is-hidden");
      if (modalQrInner) {
        modalQrInner.innerHTML =
          '<p class="hint muted pay-modal-qr-placeholder">' +
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
        '<p class="hint muted">' + escapeHtml(root.dataset.msgQrLoading || "…") + "</p>";
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
              '<p class="hint flash-err">' + escapeHtml(root.dataset.msgQrFail || "Error") + "</p>";
          }
        }
      })
      .catch(() => {
        lastQrKey = "";
        if (modalQrInner) {
          modalQrInner.innerHTML =
            '<p class="hint flash-err">' + escapeHtml(root.dataset.msgNetFail || "Error") + "</p>";
        }
      });
  }

  function scheduleVnQrReload() {
    if (modalMode !== "vn" || !payModal || payModal.classList.contains("is-hidden")) return;
    clearTimeout(qrFetchTimer);
    lastQrKey = "";
    qrFetchTimer = setTimeout(loadVnQr, 350);
  }

  if (modalEmail) {
    modalEmail.addEventListener("input", scheduleVnQrReload);
    modalEmail.addEventListener("change", scheduleVnQrReload);
    modalEmail.addEventListener("keydown", (e) => {
      if (e.key === "Enter") {
        e.preventDefault();
        if (modalMode === "intl") openIntlPaddle();
        else loadVnQr();
      }
    });
  }

  function openIntlPaddle() {
    const email = getModalEmail();
    if (!validEmail(email)) {
      showModalEmailHint(root.dataset.msgEnterEmail || "Enter a valid email.");
      modalEmail && modalEmail.focus();
      return;
    }
    showModalEmailHint("");
    closeModal();

    if (!paddleAvailable || !window.BodyExporterPaddle) {
      window.location.href =
        "/buy/paddle?email=" + encodeURIComponent(email) + "&years=" + getYears();
      return;
    }
    const cfgEl = document.getElementById("paddle-config");
    if (!cfgEl) {
      window.location.href =
        "/buy/paddle?email=" + encodeURIComponent(email) + "&years=" + getYears();
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
      btnContinue.textContent = root.dataset.msgPaddleLoading || "Loading…";
    }
    window.BodyExporterPaddle.setConfig(cfg);
    window.BodyExporterPaddle.startFromApi("/buy/api/paddle-checkout", {
      email: email,
      years: getYears(),
    })
      .catch(function () {
        alert(root.dataset.msgPaddleFail || "Could not open checkout");
      })
      .finally(function () {
        if (btnContinue) {
          btnContinue.disabled = false;
          btnContinue.textContent = root.dataset.msgBtnPay || "Pay";
        }
      });
  }

  if (modalBtnPaddle) modalBtnPaddle.addEventListener("click", openIntlPaddle);

  function onContinue() {
    if (currentMode === "intl" && paddleAvailable) {
      openModal("intl");
      if (validEmail(getModalEmail())) {
        modalEmail && modalEmail.focus();
      }
      return;
    }
    if (currentMode === "vn" && root.dataset.vietqrAvailable === "1") {
      openModal("vn");
      modalEmail && modalEmail.focus();
      return;
    }
    const cardForm = document.getElementById("buy-form-card");
    if (cardForm) cardForm.submit();
  }

  if (btnContinue) btnContinue.addEventListener("click", onContinue);

  const prefill = (root.dataset.prefillEmail || "").trim();
  if (modalEmail && prefill && validEmail(prefill)) {
    modalEmail.value = prefill;
  }

  syncPricingMode(currentMode);
  syncPricingTotals();
})();
