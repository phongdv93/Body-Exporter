(function () {
  const cfgEl = document.getElementById("paddle-config");
  const txnEl = document.getElementById("paddle-txn");
  const frame = document.getElementById("paddle-checkout-frame");
  const statusEl = document.getElementById("paddle-status");
  const helpEl = document.getElementById("paddle-network-help");
  if (!cfgEl || !txnEl || !frame) return;

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

  function showHelp() {
    if (helpEl) helpEl.classList.remove("is-hidden");
    if (statusEl) statusEl.textContent = "";
  }

  function initPaddle() {
    if (typeof Paddle === "undefined") {
      if (statusEl) statusEl.textContent = "Paddle.js not loaded";
      showHelp();
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
            displayMode: "inline",
            frameTarget: "paddle-checkout-frame",
            frameInitialHeight: "680",
            frameStyle:
              "width: 100%; min-width: 312px; min-height: 680px; background: transparent; border: none;",
            theme: "dark",
            locale: document.documentElement.lang === "vi" ? "vi" : "en",
            successUrl: successUrl,
          },
        },
        eventCallback: function (ev) {
          if (!ev) return;
          if (ev.name === "checkout.error") {
            console.error("Paddle checkout.error", ev);
            showHelp();
          }
          if (ev.name === "checkout.completed") {
            window.location.href = successUrl;
          }
        },
      });
      if (statusEl) statusEl.classList.add("is-hidden");
      /* URL must keep ?_ptxn= — Paddle opens inline checkout automatically */
    } catch (err) {
      console.error(err);
      showHelp();
    }
  }

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", initPaddle);
  } else {
    initPaddle();
  }
})();
