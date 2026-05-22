(function () {
  const root = document.querySelector(".buy-checkout");
  if (!root) return;

  const emailInput = document.getElementById("email");
  const emailHint = document.getElementById("email-hint");
  const activeBox = document.getElementById("pay-active-box");
  const pricingCard = document.getElementById("pricing");
  const vnMount = document.getElementById("vn-qr-mount");
  const panels = {
    vn: document.getElementById("pay-panel-vn"),
    intl: document.getElementById("pay-panel-intl"),
  };
  const modeButtons = root.querySelectorAll(".pay-mode-btn");
  const cookieName = root.dataset.cookieName || "be_pay_mode";
  let currentMode = root.dataset.payMode || "vn";
  let qrTimer = null;

  function validEmail(v) {
    return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test((v || "").trim());
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
    if (panels.vn) panels.vn.classList.toggle("is-hidden", mode !== "vn");
    if (panels.intl) panels.intl.classList.toggle("is-hidden", mode !== "intl");
    syncPricingMode(mode);
    setCookie(mode);
    refreshPaymentPanel();
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

  function renderVietQR(data, email) {
    if (!vnMount) return;
    const bank = data.bank || {};
    let html = '<h3 class="pay-panel-title">' + escapeHtml(data.labels.title) + "</h3>";
    html += '<div class="pay-grid"><div class="qr-wrap">';
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
      '<p class="hint buy-email-hint">' +
      (data.wait_hint_html || "") +
      "</p>";
    vnMount.innerHTML = html;
  }

  function loadVietQR(email) {
    if (!vnMount || root.dataset.vietqrAvailable !== "1") return;
    vnMount.innerHTML =
      '<p class="hint muted">' + escapeHtml(root.dataset.msgQrLoading || "…") + "</p>";
    fetch("/buy/api/vietqr?email=" + encodeURIComponent(email))
      .then((r) => r.json())
      .then((data) => {
        if (data.ok) renderVietQR(data, email);
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

  function refreshPaymentPanel() {
    const email = (emailInput && emailInput.value.trim()) || "";
    if (!validEmail(email)) {
      if (activeBox) activeBox.classList.add("is-hidden");
      if (emailHint) emailHint.classList.remove("is-hidden");
      return;
    }
    if (activeBox) activeBox.classList.remove("is-hidden");
    if (emailHint) emailHint.classList.add("is-hidden");
    if (currentMode === "vn") loadVietQR(email);
  }

  function onEmailInput() {
    clearTimeout(qrTimer);
    qrTimer = setTimeout(refreshPaymentPanel, 350);
    const hidden = document.getElementById("email-hidden-card");
    if (hidden) hidden.value = emailInput.value;
  }

  if (emailInput) {
    emailInput.addEventListener("input", onEmailInput);
    emailInput.addEventListener("blur", refreshPaymentPanel);
  }

  const cardForm = document.getElementById("buy-form-card");
  if (cardForm && emailInput) {
    cardForm.addEventListener("submit", () => {
      const hidden = document.getElementById("email-hidden-card");
      if (hidden) hidden.value = emailInput.value;
    });
  }

  /* Paddle */
  const paddleBtn = document.getElementById("btn-paddle-checkout");
  const cfgEl = document.getElementById("paddle-config");
  if (paddleBtn && cfgEl) {
    let cfg = {};
    try {
      cfg = JSON.parse(cfgEl.textContent || "{}");
    } catch (e) {
      console.error(e);
    }
    let paddleReady = false;

    function whenPaddleReady(cb, n) {
      if (typeof Paddle !== "undefined") return cb();
      if (n <= 0) return;
      setTimeout(() => whenPaddleReady(cb, n - 1), 200);
    }

    function initPaddleOnce() {
      if (paddleReady || typeof Paddle === "undefined" || !cfg.client_token) return false;
      try {
        if (cfg.environment === "sandbox" && Paddle.Environment && Paddle.Environment.set) {
          Paddle.Environment.set("sandbox");
        }
        Paddle.Initialize({
          token: cfg.client_token,
          checkout: { settings: { displayMode: "overlay" } },
        });
        paddleReady = true;
        paddleBtn.disabled = false;
        return true;
      } catch (err) {
        console.error(err);
        return false;
      }
    }

    paddleBtn.disabled = true;
    whenPaddleReady(() => initPaddleOnce(), 50);

    function openPaddleCheckout(email) {
      const successUrl = cfg.success_url + "?email=" + encodeURIComponent(email);
      const openOpts = (extra) => {
        Paddle.Checkout.open(
          Object.assign(
            {
              customer: { email: email },
              customData: { buyer_email: email },
              settings: { successUrl: successUrl },
            },
            extra
          )
        );
      };
      fetch("/buy/api/paddle-checkout", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email: email }),
      })
        .then((r) => r.json())
        .then((data) => {
          if (data.ok && data.transaction_id) {
            openOpts({ transactionId: data.transaction_id });
          } else {
            openOpts({ items: [{ priceId: cfg.price_id, quantity: 1 }] });
          }
        })
        .catch(() => {
          openOpts({ items: [{ priceId: cfg.price_id, quantity: 1 }] });
        });
    }

    paddleBtn.addEventListener("click", () => {
      const email = (emailInput && emailInput.value.trim()) || "";
      if (!validEmail(email)) {
        emailInput && emailInput.focus();
        return;
      }
      if (!paddleReady && !initPaddleOnce()) {
        alert(root.dataset.msgPaddleLoading || "Loading…");
        return;
      }
      try {
        openPaddleCheckout(email);
      } catch (err) {
        console.error(err);
        alert(
          root.dataset.msgPaddleFail ||
            "Checkout error. " + (cfg.support_email || "hotro@bodyexporter.com")
        );
      }
    });
  }

  syncPricingMode(currentMode);
  if (emailInput && validEmail(emailInput.value)) refreshPaymentPanel();
})();
