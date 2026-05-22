(function () {
  const cfgEl = document.getElementById("paddle-config");
  const txnEl = document.getElementById("paddle-txn");
  const statusEl = document.getElementById("paddle-status");
  const helpEl = document.getElementById("paddle-network-help");
  const fallbackEl = document.getElementById("paddle-fallback-actions");
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
      Paddle.Initialize({
        token: cfg.client_token,
        checkout: {
          settings: {
            displayMode: "overlay",
            theme: "dark",
            locale: document.documentElement.lang === "vi" ? "vi" : "en",
            successUrl: successUrl,
          },
        },
        eventCallback: function (ev) {
          if (!ev) return;
          if (ev.name === "checkout.error") {
            console.error("Paddle checkout.error", ev);
            showFallback();
          }
          if (ev.name === "checkout.completed") {
            window.location.href = successUrl;
          }
          if (ev.name === "checkout.closed") {
            showFallback();
          }
        },
      });
      if (statusEl) statusEl.classList.add("is-hidden");
      /* URL có ?_ptxn= — Paddle tự mở checkout sau Initialize (không gọi Checkout.open thêm) */
    } catch (err) {
      console.error(err);
      showFallback();
    }
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initPaddle);
  } else {
    initPaddle();
  }
})();
