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
  const paddleOpeningHint = document.getElementById("paddle-opening-hint");
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
    if (paddleOpeningHint) paddleOpeningHint.classList.add("is-hidden");
    emailInput && emailInput.focus();
  }

  function setContinueLoading(on) {
    if (!btnContinue) return;
    btnContinue.disabled = on;
    btnContinue.setAttribute("aria-busy", on ? "true" : "false");
    if (paddleOpeningHint) paddleOpeningHint.classList.toggle("is-hidden", !on);
  }

  function paddleDefaultLinkMessage() {
    return (
      root.dataset.msgPaddleDefaultLink ||
      "Set Default payment link in Paddle Dashboard (Checkout settings)."
    );
  }

  /* Paddle — open overlay on Continue (intl), no extra Pay button */
  const cfgEl = document.getElementById("paddle-config");
  let paddleCfg = {};
  let paddleReady = false;

  if (cfgEl) {
    try {
      paddleCfg = JSON.parse(cfgEl.textContent || "{}");
    } catch (e) {
      console.error(e);
    }
  }

  function waitForGlobalPaddle(maxMs) {
    const limit = maxMs || 15000;
    const start = Date.now();
    return new Promise((resolve, reject) => {
      (function tick() {
        if (typeof Paddle !== "undefined") {
          resolve();
          return;
        }
        if (Date.now() - start >= limit) {
          reject(new Error("paddle_script"));
          return;
        }
        setTimeout(tick, 100);
      })();
    });
  }

  function loadPaddleScript() {
    return new Promise((resolve, reject) => {
      if (typeof Paddle !== "undefined") {
        resolve();
        return;
      }
      const src = "https://cdn.paddle.com/paddle/v2/paddle.js";
      const existing = document.querySelector('script[src*="paddle.com/paddle"]');
      if (existing) {
        if (typeof Paddle !== "undefined") {
          resolve();
          return;
        }
        existing.addEventListener("load", () => waitForGlobalPaddle().then(resolve, reject));
        existing.addEventListener("error", () => reject(new Error("paddle_script")));
        waitForGlobalPaddle().then(resolve, reject);
        return;
      }
      const s = document.createElement("script");
      s.src = src;
      s.async = false;
      s.onload = () => waitForGlobalPaddle().then(resolve, reject);
      s.onerror = () => reject(new Error("paddle_script"));
      document.head.appendChild(s);
    });
  }

  function initPaddleOnce() {
    if (paddleReady) return true;
    if (typeof Paddle === "undefined" || !paddleCfg.client_token) return false;
    try {
      if (paddleCfg.environment === "sandbox" && Paddle.Environment && Paddle.Environment.set) {
        Paddle.Environment.set("sandbox");
      }
      Paddle.Initialize({
        token: paddleCfg.client_token,
        checkout: { settings: { displayMode: "overlay" } },
        eventCallback: function (ev) {
          if (ev && ev.name === "checkout.error") {
            console.error("Paddle checkout.error", ev);
            setContinueLoading(false);
          }
          if (ev && (ev.name === "checkout.closed" || ev.name === "checkout.completed")) {
            setContinueLoading(false);
          }
        },
      });
      paddleReady = true;
      return true;
    } catch (err) {
      console.error("Paddle.Initialize failed", err);
      return false;
    }
  }

  let paddleInitPromise = null;

  function ensurePaddleReady() {
    if (!paddleCfg.client_token) {
      return Promise.reject(new Error("paddle_config"));
    }
    if (paddleReady) {
      return Promise.resolve();
    }
    if (paddleInitPromise) return paddleInitPromise;
    paddleInitPromise = loadPaddleScript()
      .then(() => {
        if (!initPaddleOnce()) {
          throw new Error("paddle_init");
        }
      })
      .catch((err) => {
        paddleInitPromise = null;
        throw err;
      });
    return paddleInitPromise;
  }

  if (paddleAvailable && paddleCfg.client_token) {
    ensurePaddleReady().catch(() => {});
  }

  function openPaddleCheckout(email) {
    return fetch("/buy/api/paddle-checkout", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({ email: email }),
    })
      .then((r) => r.json().then((data) => ({ data: data })))
      .then(({ data }) => {
        if (data.error_code === "transaction_default_checkout_url_not_set") {
          alert(paddleDefaultLinkMessage());
          return;
        }
        if (data.ok && data.transaction_id) {
          Paddle.Checkout.open({ transactionId: data.transaction_id });
          return;
        }
        if (data.checkout_url) {
          window.location.href = data.checkout_url;
          return;
        }
        if (data.use_client_price !== false && paddleCfg.price_id) {
          Paddle.Checkout.open({
            items: [{ priceId: paddleCfg.price_id, quantity: 1 }],
            customer: { email: email },
            customData: { buyer_email: email },
            settings: {
              successUrl:
                paddleCfg.success_url + "?email=" + encodeURIComponent(email),
            },
          });
          return;
        }
        alert(
          (data.error && String(data.error)) ||
            root.dataset.msgPaddleFail ||
            "Checkout unavailable"
        );
      });
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
    if (emailHint) emailHint.classList.add("is-hidden");
    setContinueLoading(true);
    ensurePaddleReady()
      .then(() => openPaddleCheckout(email))
      .catch((err) => {
        console.error(err);
        const code = err && err.message;
        if (code === "paddle_config") {
          alert(root.dataset.msgPaddleFail || "Paddle not configured");
        } else if (code === "paddle_script") {
          alert(
            "Không tải được Paddle.js. Tắt adblock hoặc thử trình duyệt khác."
          );
        } else if (code === "paddle_init") {
          alert(root.dataset.msgPaddleFail || "Paddle init failed");
        } else {
          alert(root.dataset.msgPaddleLoading || "Loading…");
        }
      })
      .finally(() => {
        /* overlay open keeps loading off until checkout.closed via eventCallback */
        setTimeout(() => setContinueLoading(false), 800);
      });
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

  if (emailInput && validEmail(emailInput.value)) {
    if (isIntlPaddle()) {
      showStep(1);
    } else {
      goToStep2();
    }
  } else {
    showStep(1);
  }
})();
