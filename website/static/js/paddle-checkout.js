(function () {
  const cfgEl = document.getElementById("paddle-config");
  const txnEl = document.getElementById("paddle-txn");
  const frame = document.getElementById("paddle-checkout-frame");
  const statusEl = document.getElementById("paddle-status");
  const helpEl = document.getElementById("paddle-network-help");
  const fallbackEl = document.getElementById("paddle-fallback-actions");
  const braveHint = document.getElementById("paddle-brave-hint");
  if (!cfgEl || !txnEl) return;

  let cfg = {};
  let txnId = "";
  try {
    cfg = JSON.parse(cfgEl.textContent || "{}");
    txnId = JSON.parse(txnEl.textContent || '""');
  } catch (e) {
    console.error(e);
    return;
  }
  if (!cfg.client_token || !txnId) return;

  const params = new URLSearchParams(window.location.search);
  const email = (params.get("email") || "").trim();
  const successUrl =
    cfg.success_url + (email ? "?email=" + encodeURIComponent(email) : "");

  function showFallback() {
    if (helpEl) helpEl.classList.remove("is-hidden");
    if (fallbackEl) fallbackEl.classList.remove("is-hidden");
    if (statusEl) statusEl.classList.add("is-hidden");
    if (frame) frame.classList.add("is-hidden");
  }

  function showBraveHint() {
    if (braveHint) braveHint.classList.remove("is-hidden");
  }

  function detectBrave() {
    if (navigator.brave && typeof navigator.brave.isBrave === "function") {
      return navigator.brave.isBrave();
    }
    return Promise.resolve(false);
  }

  function initPaddle() {
    if (typeof Paddle === "undefined") {
      if (statusEl) statusEl.textContent = "Paddle.js not loaded";
      showFallback();
      return;
    }
    try {
      if (cfg.environment === "sandbox" && Paddle.Environment && Paddle.Environment.set) {
        Paddle.Environment.set("sandbox");
      }
      const settings = {
        displayMode: "inline",
        frameTarget: "paddle-checkout-frame",
        frameInitialHeight: "720",
        frameStyle:
          "width: 100%; min-width: 312px; min-height: 720px; background: transparent; border: none;",
        theme: "dark",
        locale: document.documentElement.lang === "vi" ? "vi" : "en",
        successUrl: successUrl,
      };
      Paddle.Initialize({
        token: cfg.client_token,
        checkout: { settings: settings },
        eventCallback: function (ev) {
          if (!ev) return;
          if (ev.name === "checkout.error") {
            console.error("Paddle checkout.error", ev);
            showFallback();
            showBraveHint();
          }
          if (ev.name === "checkout.completed") {
            window.location.href = successUrl;
          }
        },
      });
      if (statusEl) statusEl.classList.add("is-hidden");
      /* ?_ptxn= on URL — Paddle opens inline checkout after Initialize */
    } catch (err) {
      console.error(err);
      showFallback();
    }
  }

  detectBrave().then(function (isBrave) {
    if (isBrave) showBraveHint();
    if (document.readyState === "loading") {
      document.addEventListener("DOMContentLoaded", initPaddle);
    } else {
      initPaddle();
    }
  });
})();
