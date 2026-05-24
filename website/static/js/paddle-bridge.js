/**
 * Shared Paddle overlay checkout (used by /buy and /buy/paddle).
 */
window.BodyExporterPaddle = (function () {
  let cfg = null;
  let initialized = false;
  let widthGuardObserver = null;

  function isMobileViewport() {
    return window.matchMedia("(max-width: 720px)").matches;
  }

  function overlayCheckoutSettings() {
    const settings = { displayMode: "overlay" };
    if (isMobileViewport()) {
      settings.variant = "one-page";
    }
    return settings;
  }

  function isPaddleFrame(el) {
    if (!el || el.tagName !== "IFRAME") return false;
    const src = (el.getAttribute("src") || "").toLowerCase();
    const name = (el.getAttribute("name") || "").toLowerCase();
    return src.indexOf("paddle") !== -1 || name.indexOf("paddle") !== -1;
  }

  function constrainPaddleOverlayWidth() {
    if (!isMobileViewport()) return;
    const vw = document.documentElement.clientWidth + "px";
    document.querySelectorAll("iframe").forEach(function (frame) {
      if (!isPaddleFrame(frame)) return;
      frame.style.setProperty("max-width", vw, "important");
      frame.style.setProperty("box-sizing", "border-box", "important");
      let node = frame.parentElement;
      for (let i = 0; i < 12 && node && node !== document.documentElement; i++) {
        node.style.setProperty("max-width", vw, "important");
        node.style.setProperty("overflow-x", "clip", "important");
        node.style.setProperty("box-sizing", "border-box", "important");
        node = node.parentElement;
      }
    });
  }

  function schedulePaddleWidthFix() {
    constrainPaddleOverlayWidth();
    requestAnimationFrame(constrainPaddleOverlayWidth);
    setTimeout(constrainPaddleOverlayWidth, 80);
    setTimeout(constrainPaddleOverlayWidth, 350);
  }

  function startPaddleWidthGuard() {
    if (widthGuardObserver) return;
    widthGuardObserver = new MutationObserver(function () {
      schedulePaddleWidthFix();
    });
    widthGuardObserver.observe(document.body, {
      childList: true,
      subtree: true,
      attributes: true,
      attributeFilter: ["style", "class", "width"],
    });
    window.addEventListener("resize", schedulePaddleWidthFix);
  }

  function stopPaddleWidthGuard() {
    if (widthGuardObserver) {
      widthGuardObserver.disconnect();
      widthGuardObserver = null;
    }
    window.removeEventListener("resize", schedulePaddleWidthFix);
  }

  function setPaddleCheckoutOpen(on) {
    document.documentElement.classList.toggle("paddle-checkout-open", on);
    document.body.classList.toggle("paddle-checkout-open", on);
    if (on) {
      if (isMobileViewport()) {
        startPaddleWidthGuard();
        schedulePaddleWidthFix();
      }
    } else {
      stopPaddleWidthGuard();
    }
  }

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
          if (
            ev.name === "checkout.loaded" ||
            ev.name === "checkout.updated" ||
            ev.name === "checkout.customer.created" ||
            ev.name === "checkout.customer.updated"
          ) {
            setPaddleCheckoutOpen(true);
            if (isMobileViewport()) schedulePaddleWidthFix();
          }
          if (
            ev.name === "checkout.completed" ||
            ev.name === "checkout.closed" ||
            ev.name === "checkout.error"
          ) {
            setPaddleCheckoutOpen(false);
          }
          if (ev.name === "checkout.completed" && cfg.success_url) {
            let em = (cfg._pending_email || "").trim();
            const d = ev.data || {};
            const cust = d.customer || {};
            if (!em && cust.email) em = String(cust.email).trim();
            if (!em && d.custom_data && d.custom_data.buyer_email) {
              em = String(d.custom_data.buyer_email).trim();
            }
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
      setPaddleCheckoutOpen(true);
      if (isMobileViewport()) schedulePaddleWidthFix();
      return Promise.resolve();
    } catch (err) {
      setPaddleCheckoutOpen(false);
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

  function openWithItems(opts) {
    opts = opts || {};
    if (!cfg || !cfg.client_token || !opts.priceId) {
      return Promise.reject(new Error("not_configured"));
    }
    if (!initConfig(cfg)) {
      return Promise.reject(new Error("paddle_init_failed"));
    }
    try {
      Paddle.Checkout.open({
        items: [{ priceId: opts.priceId, quantity: opts.quantity || 1 }],
        settings: overlayCheckoutSettings(),
      });
      setPaddleCheckoutOpen(true);
      if (isMobileViewport()) schedulePaddleWidthFix();
      return Promise.resolve();
    } catch (err) {
      setPaddleCheckoutOpen(false);
      if (cfg.onError) cfg.onError(String(err), err);
      return Promise.reject(err);
    }
  }

  return {
    setConfig: function (config) {
      cfg = config;
      initialized = false;
    },
    openCheckout: openCheckout,
    startFromApi: startFromApi,
    openWithItems: openWithItems,
  };
})();
