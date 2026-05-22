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
  if (paddleBtn && cfgEl) {
    let cfg = {};
    try {
      cfg = JSON.parse(cfgEl.textContent || "{}");
    } catch (e) {
      return;
    }

    function initPaddle() {
      if (typeof Paddle === "undefined" || !cfg.client_token) return false;
      if (cfg.environment === "sandbox" && Paddle.Environment && Paddle.Environment.set) {
        Paddle.Environment.set("sandbox");
      }
      Paddle.Initialize({
        token: cfg.client_token,
        checkout: { settings: { displayMode: "overlay" } },
      });
      return true;
    }

    if (document.readyState === "loading") {
      document.addEventListener("DOMContentLoaded", initPaddle);
    } else {
      initPaddle();
    }

    paddleBtn.addEventListener("click", () => {
      const email = (emailInput && emailInput.value.trim()) || "";
      if (!email) {
        emailInput && emailInput.focus();
        return;
      }
      if (typeof Paddle === "undefined") {
        alert("Payment is loading. Please try again in a moment.");
        return;
      }
      if (!initPaddle()) {
        alert("Payment is not ready. Please refresh the page.");
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
        alert("Could not open checkout. Contact support.");
      }
    });
  }
})();
