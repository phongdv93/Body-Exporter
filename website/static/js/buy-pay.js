(function () {
  const root = document.querySelector(".buy-checkout");
  if (!root) return;

  const emailInput = document.getElementById("email");
  const emailHint = document.getElementById("email-hint");
  const step1 = document.getElementById("checkout-step-1");
  const step2 = document.getElementById("checkout-step-2");
  const stepInd1 = document.getElementById("checkout-step-indicator-1");
  const stepInd2 = document.getElementById("checkout-step-indicator-2");
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
  let currentMode = root.dataset.payMode || "vn";
  let checkoutStep = 1;

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
    if (checkoutStep === 2) loadStep2Payment();
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
      return;
    }
    if (emailHint) emailHint.classList.add("is-hidden");
    if (confirmedEmail) confirmedEmail.textContent = email;
    const hidden = document.getElementById("email-hidden-card");
    if (hidden) hidden.value = email;
    showStep(2);
    loadStep2Payment();
  }

  function goToStep1() {
    showStep(1);
    if (emailHint) emailHint.classList.remove("is-hidden");
    emailInput && emailInput.focus();
  }

  if (btnContinue) btnContinue.addEventListener("click", goToStep2);
  if (btnEditEmail) btnEditEmail.addEventListener("click", goToStep1);

  if (emailInput) {
    emailInput.addEventListener("keydown", (e) => {
      if (e.key === "Enter" && checkoutStep === 1) {
        e.preventDefault();
        goToStep2();
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

  function paddleDefaultLinkMessage() {
    return (
      root.dataset.msgPaddleDefaultLink ||
      "Set Default payment link in Paddle Dashboard (Checkout settings)."
    );
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
          eventCallback: function (ev) {
            if (ev && ev.name === "checkout.error") {
              console.error("Paddle checkout.error", ev);
            }
          },
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
      fetch("/buy/api/paddle-checkout", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email: email }),
      })
        .then((r) => r.json().then((data) => ({ status: r.status, data: data })))
        .then(({ status, data }) => {
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
          if (data.use_client_price !== false && cfg.price_id) {
            Paddle.Checkout.open({
              items: [{ priceId: cfg.price_id, quantity: 1 }],
              customer: { email: email },
              customData: { buyer_email: email },
              settings: {
                successUrl: cfg.success_url + "?email=" + encodeURIComponent(email),
              },
            });
            return;
          }
          alert(
            (data.error && String(data.error)) ||
              root.dataset.msgPaddleFail ||
              "Checkout unavailable"
          );
        })
        .catch((err) => {
          console.error(err);
          if (cfg.price_id) {
            try {
              Paddle.Checkout.open({
                items: [{ priceId: cfg.price_id, quantity: 1 }],
                customer: { email: email },
                customData: { buyer_email: email },
              });
            } catch (e2) {
              alert(root.dataset.msgPaddleFail || "Checkout error");
            }
          } else {
            alert(root.dataset.msgPaddleFail || "Checkout error");
          }
        });
    }

    paddleBtn.addEventListener("click", () => {
      if (checkoutStep !== 2) {
        goToStep2();
        return;
      }
      const email = (emailInput && emailInput.value.trim()) || "";
      if (!validEmail(email)) {
        goToStep1();
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
        alert(root.dataset.msgPaddleFail || "Checkout error");
      }
    });
  }

  syncPricingMode(currentMode);
  if (emailInput && validEmail(emailInput.value)) {
    goToStep2();
  } else {
    showStep(1);
  }
})();
