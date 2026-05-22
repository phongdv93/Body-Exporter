(function () {
  const root = document.getElementById("paddle-root");
  const cfgEl = document.getElementById("paddle-config");
  const txnEl = document.getElementById("paddle-txn");
  const frame = document.getElementById("paddle-checkout-frame");
  const statusEl = document.getElementById("paddle-status");
  const helpEl = document.getElementById("paddle-network-help");
  const detailEl = document.getElementById("paddle-error-detail");
  const fallbackEl = document.getElementById("paddle-fallback-actions");
  const braveHint = document.getElementById("paddle-brave-hint");
  const btnRetry = document.getElementById("paddle-btn-retry");
  const btnOpenOverlay = document.getElementById("paddle-btn-open-overlay");
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

  const params = new URLSearchParams(window.location.search);
  const email = (params.get("email") || cfg.customer_email || "").trim();
  if (!txnId) {
    txnId = (params.get("txn") || params.get("_ptxn") || "").trim();
  }
  if (!cfg.client_token || !txnId) return;

  const displayMode =
    params.get("display") === "inline" || cfg.display_mode === "inline"
      ? "inline"
      : "overlay";
  const successUrl =
    cfg.success_url + (email ? "?email=" + encodeURIComponent(email) : "");

  let checkoutLoaded = false;
  let paddleInitialized = false;
  let failTimer = null;

  function msg(key, fallback) {
    if (root && root.dataset[key]) return root.dataset[key];
    return fallback;
  }

  function formatPaddleError(ev) {
    if (!ev || !ev.data) return "";
    const d = ev.data;
    if (typeof d === "string") return d;
    if (d.detail) return String(d.detail);
    if (d.message) return String(d.message);
    if (d.error && d.error.detail) return String(d.error.detail);
    if (Array.isArray(d.errors) && d.errors.length) {
      const first = d.errors[0];
      return (first && (first.details || first.detail || first.code)) || "";
    }
    try {
      return JSON.stringify(d).slice(0, 240);
    } catch (e) {
      return "";
    }
  }

  function showErrorDetail(text) {
    if (!detailEl || !text) return;
    detailEl.textContent = text;
    detailEl.classList.remove("is-hidden");
  }

  function showFallback(errText) {
    if (helpEl) helpEl.classList.remove("is-hidden");
    if (fallbackEl) fallbackEl.classList.remove("is-hidden");
    if (statusEl) statusEl.classList.add("is-hidden");
    if (frame) frame.classList.add("is-hidden");
    if (errText) showErrorDetail(errText);
    clearTimeout(failTimer);
  }

  function showBrowserHint() {
    if (braveHint) braveHint.classList.remove("is-hidden");
  }

  function markLoaded() {
    checkoutLoaded = true;
    clearTimeout(failTimer);
    if (statusEl) statusEl.classList.add("is-hidden");
    if (braveHint) braveHint.classList.add("is-hidden");
    if (helpEl) helpEl.classList.add("is-hidden");
    if (detailEl) detailEl.classList.add("is-hidden");
    if (fallbackEl) fallbackEl.classList.add("is-hidden");
  }

  function buildSettings() {
    const settings = {
      displayMode: displayMode,
      theme: "dark",
      locale: document.documentElement.lang === "vi" ? "vi" : "en",
      successUrl: successUrl,
    };
    if (displayMode === "inline") {
      settings.frameTarget = "paddle-checkout-frame";
      settings.frameInitialHeight = "720";
      settings.frameStyle =
        "width: 100%; min-width: 312px; min-height: 720px; background: transparent; border: none;";
    }
    return settings;
  }

  function initPaddleOnce() {
    if (paddleInitialized) return true;
    if (typeof Paddle === "undefined") {
      if (statusEl) statusEl.textContent = msg("msgJsMissing", "Paddle.js not loaded");
      showFallback();
      return false;
    }
    try {
      if (cfg.environment === "sandbox" && Paddle.Environment && Paddle.Environment.set) {
        Paddle.Environment.set("sandbox");
      }
      const initCfg = {
        token: cfg.client_token,
        checkout: { settings: buildSettings() },
        eventCallback: function (ev) {
          if (!ev) return;
          if (ev.name === "checkout.loaded") {
            markLoaded();
          }
          if (ev.name === "checkout.error") {
            console.error("Paddle checkout.error", ev);
            const detail = formatPaddleError(ev);
            showFallback(detail);
            showBrowserHint();
          }
          if (ev.name === "checkout.warning") {
            console.warn("Paddle checkout.warning", ev);
          }
          if (ev.name === "checkout.completed") {
            window.location.href = successUrl;
          }
        },
      };
      if (cfg.customer_id) {
        initCfg.pwCustomer = { id: cfg.customer_id };
      }
      Paddle.Initialize(initCfg);
      paddleInitialized = true;
      return true;
    } catch (err) {
      console.error(err);
      showFallback(String(err));
      return false;
    }
  }

  function checkoutCustomer() {
    if (cfg.customer_id) {
      return { id: cfg.customer_id };
    }
    if (email) {
      return { email: email };
    }
    return undefined;
  }

  function openCheckoutManual() {
    if (!initPaddleOnce()) return;
    const payload = { transactionId: txnId };
    const customer = checkoutCustomer();
    if (customer) payload.customer = customer;
    try {
      Paddle.Checkout.open(payload);
    } catch (err) {
      console.error(err);
      showFallback(String(err));
      showBrowserHint();
    }
  }

  function scheduleFailTimer() {
    clearTimeout(failTimer);
    failTimer = setTimeout(function () {
      if (!checkoutLoaded) {
        showFallback(msg("msgTimeout", ""));
        showBrowserHint();
      }
    }, 15000);
  }

  function startCheckout() {
    if (!initPaddleOnce()) return;

    if (displayMode === "overlay" && statusEl) {
      statusEl.textContent = msg(
        "msgOverlay",
        "A secure payment window should open. Allow pop-ups if blocked."
      );
      statusEl.classList.remove("is-hidden");
      if (frame) frame.classList.add("is-hidden");
    }

    openCheckoutManual();
    scheduleFailTimer();
  }

  if (btnRetry) {
    btnRetry.addEventListener("click", function () {
      checkoutLoaded = false;
      if (detailEl) detailEl.classList.add("is-hidden");
      if (helpEl) helpEl.classList.add("is-hidden");
      if (fallbackEl) fallbackEl.classList.add("is-hidden");
      if (statusEl) {
        statusEl.classList.remove("is-hidden");
        statusEl.textContent = msg("msgOpening", "Opening Paddle checkout…");
      }
      startCheckout();
    });
  }

  if (btnOpenOverlay) {
    btnOpenOverlay.addEventListener("click", function () {
      checkoutLoaded = false;
      if (detailEl) detailEl.classList.add("is-hidden");
      if (helpEl) helpEl.classList.add("is-hidden");
      if (fallbackEl) fallbackEl.classList.add("is-hidden");
      openCheckoutManual();
      scheduleFailTimer();
    });
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", startCheckout);
  } else {
    startCheckout();
  }
})();
