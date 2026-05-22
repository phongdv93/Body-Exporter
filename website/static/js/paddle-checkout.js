(function () {
  const cfgEl = document.getElementById("paddle-config");
  const txnEl = document.getElementById("paddle-txn");
  const statusEl = document.getElementById("paddle-status");
  const detailEl = document.getElementById("paddle-error-detail");
  if (!cfgEl || !txnEl || !window.BodyExporterPaddle) return;

  let cfg = {};
  let txnId = "";
  try {
    cfg = JSON.parse(cfgEl.textContent || "{}");
    txnId = JSON.parse(txnEl.textContent || '""');
  } catch (e) {
    return;
  }

  const params = new URLSearchParams(window.location.search);
  const email = (params.get("email") || cfg.customer_email || "").trim();
  if (!txnId) {
    txnId = (params.get("txn") || params.get("_ptxn") || "").trim();
  }
  if (!cfg.client_token || !txnId) return;

  function showErr(msg) {
    if (detailEl) {
      detailEl.textContent = msg || "";
      detailEl.classList.toggle("is-hidden", !msg);
    }
    if (statusEl) statusEl.classList.add("is-hidden");
  }

  window.BodyExporterPaddle.setConfig(cfg);
  window.BodyExporterPaddle.openCheckout({
    transactionId: txnId,
    email: email,
    onError: function (msg) {
      showErr(msg);
    },
    onLoaded: function () {
      if (statusEl) statusEl.classList.add("is-hidden");
    },
  });
})();
