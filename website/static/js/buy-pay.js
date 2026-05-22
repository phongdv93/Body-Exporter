(function () {
  const root = document.querySelector(".buy-checkout");
  if (!root) return;

  const emailInput = document.getElementById("email");
  const emailHint = document.getElementById("email-hint");
  const step1 = document.getElementById("checkout-step-1");
  const step2 = document.getElementById("checkout-step-2");
  const stepInd1 = document.getElementById("checkout-step-indicator-1");
  const stepInd2 = document.getElementById("checkout-step-indicator-2");
  const stepsNav = document.querySelector(".checkout-steps");
  const btnContinue = document.getElementById("btn-checkout-continue");
  const btnEditEmail = document.getElementById("btn-edit-email");
  const confirmedEmail = document.getElementById("confirmed-email");
  const pricingCard = document.getElementById("pricing");
  const vnMount = document.getElementById("vn-qr-mount");
  const panels = {
    vn: document.getElementById("pay-panel-vn"),
    intl: document.getElementById("pay-panel-intl"),
  };
  const modeButtons = root.querySelectorAll(".pay-mode-btn");
  const cookieName = root.dataset.cookieName || "be_pay_mode";
  const paddleAvailable = root.dataset.paddleAvailable === "1";
  let currentMode = root.dataset.payMode || "vn";
  let checkoutStep = 1;

  function validEmail(v) {
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test((v || "").trim());
  }

  function isIntlPaddle() {
    return currentMode === "intl" && paddleAvailable;
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

  function syncStepsNav() {
    if (!stepsNav) return;
    stepsNav.classList.toggle("checkout-steps--intl-paddle", isIntlPaddle());
  }

  function setPayMode(mode) {
    currentMode = mode;
    root.dataset.payMode = mode;
    modeButtons.forEach((btn) => {
      const on = btn.dataset.payMode === mode;
      btn.classList.toggle("active", on);
      btn.setAttribute("aria-selected", on ? "true" : "false");
    });
    if (panels.vn) panels.vn.classList.toggle("is-hidden", mode !== "vn");
    if (panels.intl) panels.intl.classList.toggle("is-hidden", mode !== "intl");
    syncPricingMode(mode);
    syncStepsNav();
    setCookie(mode);
    if (checkoutStep === 2 && !isIntlPaddle()) loadStep2Payment();
  }

  modeButtons.forEach((btn) => {
    btn.addEventListener("click", () => setPayMode(btn.dataset.payMode));
  });

  function escapeHtml(s) {
    return String(s)
      .replace(/&/g, "&amp;")
      .replace(/</g, "&lt;")
      .replace(/>/g, "&gt;")
      .replace(/"/g, "&quot;");
  }

  function showStep(n) {
    checkoutStep = n;
    if (step1) step1.classList.toggle("is-hidden", n !== 1);
    if (step2) step2.classList.toggle("is-hidden", n !== 2);
    if (stepInd1) stepInd1.classList.toggle("is-active", n === 1);
    if (stepInd2) stepInd2.classList.toggle("is-active", n === 2);
    if (stepInd1) stepInd1.classList.toggle("is-done", n === 2);
  }

  function renderVietQR(data) {
    if (!vnMount) return;
    const bank = data.bank || {};
    let html = '<div class="pay-grid"><div class="qr-wrap">';
    html +=
      '<img src="' +
      escapeHtml(data.qr_url) +
      '" alt="VietQR" width="240" height="240" loading="lazy"></div><dl class="bank-dl">';
    if (bank.bank) {
      html +=
        "<dt>" +
        escapeHtml(data.labels.bank) +
        "</dt><dd>" +
        escapeHtml(bank.bank) +
        "</dd>";
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
      escapeHtml(data.labels.amount) +
      "</dt><dd><strong>" +
      escapeHtml(data.amount_fmt) +
      " VND</strong></dd>";
    html +=
      "<dt>" +
      escapeHtml(data.labels.memo) +
      '</dt><dd><code>' +
      escapeHtml(data.memo) +
      "</code></dd></dl></div>";
    html +=
      '<p class="hint buy-email-hint">' + (data.wait_hint_html || "") + "</p>";
    vnMount.innerHTML = html;
  }

  function loadVietQR(email) {
    if (!vnMount || root.dataset.vietqrAvailable !== "1") return;
    vnMount.innerHTML =
      '<p class="hint muted">' + escapeHtml(root.dataset.msgQrLoading || "…") + "</p>";
    fetch("/buy/api/vietqr?email=" + encodeURIComponent(email))
      .then((r) => r.json())
      .then((data) => {
        if (data.ok) renderVietQR(data);
        else
          vnMount.innerHTML =
            '<p class="hint flash-err">' +
            escapeHtml(root.dataset.msgQrFail || "Error") +
            "</p>";
      })
      .catch(() => {
        vnMount.innerHTML =
          '<p class="hint flash-err">' +
          escapeHtml(root.dataset.msgNetFail || "Error") +
          "</p>";
      });
  }

  function loadStep2Payment() {
    const email = (emailInput && emailInput.value.trim()) || "";
    if (!validEmail(email)) return;
    if (currentMode === "vn") loadVietQR(email);
    else if (vnMount) vnMount.innerHTML = "";
  }

  function goToStep2() {
    const email = (emailInput && emailInput.value.trim()) || "";
    if (!validEmail(email)) {
      if (emailHint) {
        emailHint.classList.remove("is-hidden");
        emailHint.textContent = root.dataset.msgEnterEmail || emailHint.textContent;
      }
      emailInput && emailInput.focus();
      return false;
    }
    if (emailHint) emailHint.classList.add("is-hidden");
    if (confirmedEmail) confirmedEmail.textContent = email;
    const hidden = document.getElementById("email-hidden-card");
    if (hidden) hidden.value = email;
    showStep(2);
    loadStep2Payment();
    return true;
  }

  function goToStep1() {
    showStep(1);
    if (emailHint) emailHint.classList.remove("is-hidden");
    emailInput && emailInput.focus();
  }

  function startIntlPaddleCheckout() {
    const email = (emailInput && emailInput.value.trim()) || "";
    if (!validEmail(email)) {
      if (emailHint) {
        emailHint.classList.remove("is-hidden");
        emailHint.textContent = root.dataset.msgEnterEmail || emailHint.textContent;
      }
      emailInput && emailInput.focus();
      return;
    }
    window.location.href = "/buy/paddle?email=" + encodeURIComponent(email);
  }

  function onContinue() {
    if (isIntlPaddle()) {
      startIntlPaddleCheckout();
      return;
    }
    goToStep2();
  }

  if (btnContinue) btnContinue.addEventListener("click", onContinue);
  if (btnEditEmail) btnEditEmail.addEventListener("click", goToStep1);

  if (emailInput) {
    emailInput.addEventListener("keydown", (e) => {
      if (e.key === "Enter" && checkoutStep === 1) {
        e.preventDefault();
        onContinue();
      }
    });
  }

  const cardForm = document.getElementById("buy-form-card");
  if (cardForm && emailInput) {
    cardForm.addEventListener("submit", () => {
      const hidden = document.getElementById("email-hidden-card");
      if (hidden) hidden.value = emailInput.value;
    });
  }

  syncPricingMode(currentMode);
  syncStepsNav();

  if (emailInput && validEmail(emailInput.value) && !isIntlPaddle()) {
    goToStep2();
  } else {
    showStep(1);
  }
})();
