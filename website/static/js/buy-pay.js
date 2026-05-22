(function () {
  const root = document.querySelector(".buy-checkout");
  if (!root) return;

  const emailInput = document.getElementById("email");
  const cookieName = root.dataset.cookieName || "be_pay_mode";
  const panels = {
    vn: document.getElementById("pay-panel-vn"),
    intl: document.getElementById("pay-panel-intl"),
  };
  const modeButtons = root.querySelectorAll(".pay-mode-btn");

  function syncHiddenEmails() {
    const v = (emailInput && emailInput.value) || "";
    ["email-hidden-qr", "email-hidden-card"].forEach((id) => {
      const el = document.getElementById(id);
      if (el) el.value = v;
    });
  }

  function setCookie(mode) {
    const maxAge = 60 * 60 * 24 * 90;
    const secure = location.protocol === "https:" ? "; Secure" : "";
    document.cookie =
      cookieName + "=" + mode + "; path=/; max-age=" + maxAge + "; SameSite=Lax" + secure;
  }

  function setPayMode(mode) {
    if (!panels.vn || !panels.intl) return;
    modeButtons.forEach((btn) => {
      const on = btn.dataset.payMode === mode;
      btn.classList.toggle("active", on);
      btn.setAttribute("aria-selected", on ? "true" : "false");
    });
    panels.vn.classList.toggle("is-hidden", mode !== "vn");
    panels.intl.classList.toggle("is-hidden", mode !== "intl");
    setCookie(mode);
  }

  modeButtons.forEach((btn) => {
    btn.addEventListener("click", () => setPayMode(btn.dataset.payMode));
  });

  if (emailInput) {
    emailInput.addEventListener("input", syncHiddenEmails);
    syncHiddenEmails();
  }

  const qrForm = document.getElementById("buy-form-qr");
  if (qrForm && emailInput) {
    qrForm.addEventListener("submit", () => {
      const hidden = document.getElementById("email-hidden-qr");
      if (hidden) hidden.value = emailInput.value;
    });
  }

  const cardForm = document.getElementById("buy-form-card");
  if (cardForm && emailInput) {
    cardForm.addEventListener("submit", () => {
      const hidden = document.getElementById("email-hidden-card");
      if (hidden) hidden.value = emailInput.value;
    });
  }

  const paddleBtn = document.getElementById("btn-paddle-checkout");
  const cfgEl = document.getElementById("paddle-config");
  if (!paddleBtn || !cfgEl) return;

  let cfg = {};
  try {
    cfg = JSON.parse(cfgEl.textContent || "{}");
  } catch (e) {
    console.error("Invalid paddle-config JSON", e);
    return;
  }

  let paddleReady = false;

  function whenPaddleReady(cb, attempts) {
    if (typeof Paddle !== "undefined") {
      cb();
      return;
    }
    if (attempts <= 0) return;
    setTimeout(() => whenPaddleReady(cb, attempts - 1), 200);
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
      console.error("Paddle.Initialize failed", err);
      return false;
    }
  }

  paddleBtn.disabled = true;
  whenPaddleReady(() => initPaddleOnce(), 40);

  paddleBtn.addEventListener("click", () => {
    const email = (emailInput && emailInput.value.trim()) || "";
    if (!email) {
      emailInput && emailInput.focus();
      return;
    }
    if (!paddleReady && !initPaddleOnce()) {
      alert("Cổng thanh toán đang tải. Vui lòng đợi vài giây rồi thử lại.");
      return;
    }
    try {
      Paddle.Checkout.open({
        items: [{ priceId: cfg.price_id, quantity: 1 }],
        customer: { email: email },
        customData: { buyer_email: email },
        settings: {
          successUrl: cfg.success_url + "?email=" + encodeURIComponent(email),
        },
      });
    } catch (err) {
      console.error(err);
      alert("Không mở được cổng thanh toán. Thử refresh trang hoặc email " + (cfg.support_email || "hotro@bodyexporter.com"));
    }
  });
})();
