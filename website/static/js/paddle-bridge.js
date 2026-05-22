/**
 * Shared Paddle overlay checkout (used by /buy and /buy/paddle).
 */
window.BodyExporterPaddle = (function () {
  let cfg = null;
  let initialized = false;

  function formatError(ev) {
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
    return "";
  }

  function initConfig(config) {
    cfg = config || {};
    if (initialized || typeof Paddle === "undefined") return false;
    try {
      if (cfg.environment === "sandbox" && Paddle.Environment && Paddle.Environment.set) {
        Paddle.Environment.set("sandbox");
      }
      Paddle.Initialize({
        token: cfg.client_token,
        pwCustomer: {},
        checkout: {
          settings: {
            displayMode: "overlay",
            theme: "dark",
            locale: document.documentElement.lang === "vi" ? "vi" : "en",
            successUrl: cfg.success_url || "/buy/success",
          },
        },
        eventCallback: function (ev) {
          if (!ev) return;
          if (ev.name === "checkout.completed" && cfg.success_url) {
            const em = (cfg._pending_email || "").trim();
            const url =
              cfg.success_url + (em ? "?email=" + encodeURIComponent(em) : "");
            window.location.href = url;
          }
          if (ev.name === "checkout.error" && cfg.onError) {
            cfg.onError(formatError(ev), ev);
          }
          if (ev.name === "checkout.loaded" && cfg.onLoaded) {
            cfg.onLoaded(ev);
          }
        },
      });
      initialized = true;
      return true;
    } catch (err) {
      if (cfg.onError) cfg.onError(String(err), err);
      return false;
    }
  }

  function openCheckout(opts) {
    opts = opts || {};
    if (!cfg || !cfg.client_token) {
      if (opts.onError) opts.onError("Paddle not configured");
      return Promise.reject(new Error("not_configured"));
    }
    cfg._pending_email = (opts.email || "").trim();
    cfg.success_url = opts.successUrl || cfg.success_url;
    if (opts.onError) cfg.onError = opts.onError;
    if (opts.onLoaded) cfg.onLoaded = opts.onLoaded;
    if (!initConfig(cfg)) {
      return Promise.reject(new Error("paddle_init_failed"));
    }
    const payload = { transactionId: opts.transactionId };
    if (cfg._pending_email) {
      payload.customer = { email: cfg._pending_email };
    }
    try {
      Paddle.Checkout.open(payload);
      return Promise.resolve();
    } catch (err) {
      if (cfg.onError) cfg.onError(String(err), err);
      return Promise.reject(err);
    }
  }

  function startFromApi(apiUrl, body) {
    return fetch(apiUrl, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(body),
    })
      .then(function (r) {
        return r.json().then(function (data) {
          return { ok: r.ok, data: data };
        });
      })
      .then(function (res) {
        if (!res.data.ok || !res.data.transaction_id) {
          throw new Error(res.data.error || res.data.error_code || "paddle_failed");
        }
        return openCheckout({
          transactionId: res.data.transaction_id,
          email: body.email,
        });
      });
  }

  return {
    setConfig: function (config) {
      cfg = config;
      initialized = false;
    },
    openCheckout: openCheckout,
    startFromApi: startFromApi,
  };
})();
