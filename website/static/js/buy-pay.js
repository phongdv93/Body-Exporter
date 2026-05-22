(function () {
  const root = document.querySelector(".buy-checkout");
  if (!root) return;

  const emailInput = document.getElementById("email");
  const emailHint = document.getElementById("email-hint");
  const yearsInput = document.getElementById("license-years");
  const yearsMinus = document.getElementById("years-minus");
  const yearsPlus = document.getElementById("years-plus");
  const btnContinue = document.getElementById("btn-checkout-continue");
  const pricingCard = document.getElementById("pricing");
  const modeButtons = root.querySelectorAll(".pay-mode-btn");
  const payModal = document.getElementById("pay-modal");
  const payModalBody = document.getElementById("pay-modal-body");
  const payModalClose = document.getElementById("pay-modal-close");
  const payModalBackdrop = document.getElementById("pay-modal-backdrop");
  const payModalTitle = document.getElementById("pay-modal-title");

  const unitVnd = parseInt(root.dataset.unitPriceVnd || "0", 10) || 0;
  const maxYears = parseInt(root.dataset.maxYears || "5", 10) || 5;
  const usdRate = parseFloat(root.dataset.usdRate || "25000") || 25000;
  const priceUsdUnit = parseFloat(root.dataset.priceUsd || "") || 0;
  const cookieName = root.dataset.cookieName || "be_pay_mode";
  const paddleAvailable = root.dataset.paddleAvailable === "1";
  let currentMode = root.dataset.payMode || "vn";

  function validEmail(v) {
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test((v || "").trim());
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

  function openModal() {
    if (!payModal) return;
    payModal.classList.remove("is-hidden");
    document.body.classList.add("pay-modal-open");
  }

  function closeModal() {
    if (!payModal) return;
    payModal.classList.add("is-hidden");
    document.body.classList.remove("pay-modal-open");
  }

  if (payModalClose) payModalClose.addEventListener("click", closeModal);
  if (payModalBackdrop) payModalBackdrop.addEventListener("click", closeModal);

  function renderVnModal(data) {
    if (!payModalBody) return;
    const bank = data.bank || {};
    let html = '<div class="pay-modal-grid">';
    html += '<div class="pay-modal-summary">';
    html += "<h3>" + escapeHtml(data.labels.product || "Body Exporter") + "</h3>";
    html +=
      "<p class=\"pay-modal-years\"><span>" +
      escapeHtml(data.labels.years || "Years") +
      "</span> <strong>" +
      escapeHtml(String(data.years)) +
      "</strong></p>";
    if (data.summary_html) {
      html += "<p class=\"pay-modal-total\">" + data.summary_html + "</p>";
    }
    html += "<p class=\"hint muted pay-modal-email\">" + (data.wait_hint_html || "") + "</p>";
    html += "</div>";
    html += '<div class="pay-modal-pay">';
    html += '<div class="qr-wrap"><img src="' + escapeHtml(data.qr_url) + '" alt="VietQR" width="220" height="220" loading="lazy"></div>';
    html += '<dl class="bank-dl">';
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
    html += "</dl></div></div>";
    payModalBody.innerHTML = html;
    if (payModalTitle) {
      payModalTitle.textContent = root.dataset.msgModalTitleVn || "VietQR payment";
    }
    openModal();
  }

  function openVnModal(email) {
    const years = getYears();
    if (!payModalBody) return;
    payModalBody.innerHTML =
      '<p class="hint muted">' + escapeHtml(root.dataset.msgQrLoading || "…") + "</p>";
    openModal();
    fetch(
      "/buy/api/vietqr?email=" +
        encodeURIComponent(email) +
        "&years=" +
        encodeURIComponent(String(years))
    )
      .then((r) => r.json())
      .then((data) => {
        if (data.ok) renderVnModal(data);
        else {
          payModalBody.innerHTML =
            '<p class="hint flash-err">' + escapeHtml(root.dataset.msgQrFail || "Error") + "</p>";
        }
      })
      .catch(() => {
        payModalBody.innerHTML =
          '<p class="hint flash-err">' + escapeHtml(root.dataset.msgNetFail || "Error") + "</p>";
      });
  }

  function openIntlPaddle(email) {
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

  function onContinue() {
    const email = (emailInput && emailInput.value.trim()) || "";
    if (!validEmail(email)) {
      if (emailHint) {
        emailHint.classList.remove("is-hidden");
        emailHint.textContent = root.dataset.msgEnterEmail || emailHint.textContent;
      }
      emailInput && emailInput.focus();
      return;
    }
    if (emailHint) emailHint.classList.add("is-hidden");

    if (currentMode === "intl" && paddleAvailable) {
      openIntlPaddle(email);
      return;
    }
    if (currentMode === "vn" && root.dataset.vietqrAvailable === "1") {
      openVnModal(email);
      return;
    }
    const cardForm = document.getElementById("buy-form-card");
    if (cardForm) cardForm.submit();
  }

  if (btnContinue) btnContinue.addEventListener("click", onContinue);

  if (emailInput) {
    emailInput.addEventListener("keydown", (e) => {
      if (e.key === "Enter") {
        e.preventDefault();
        onContinue();
      }
    });
  }

  syncPricingMode(currentMode);
  syncPricingTotals();
})();
