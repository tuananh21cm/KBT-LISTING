/**
 * MAIN world — chạy document_start trên seller-us.tiktok.com.
 *
 * Gateway `/api/v1/product/*` từ chối request dựng ở isolated world (`code=10000`, không data,
 * không message): thiếu `x-secsdk-csrf-token` thật + `X-Bogus`/`_signature`/`msToken` mà
 * webmssdk sinh theo từng URL. Không bịa được mấy thứ đó, nên bridge làm 3 việc:
 *
 * 1. Hook fetch/XHR **chỉ đọc** để lấy template header từ request của CHÍNH trang.
 * 2. Hớt response của các endpoint **không tham số** (`HARVEST_TARGETS`) khi trang tự gọi —
 *    bước sync dùng lại payload đó thì không tốn request nào và không bị gateway chặn.
 * 3. Kênh `kb-tts-product-fetch`: gọi bằng `window.fetch` **đọc tại thời điểm gọi** để lớp bọc
 *    của TikTok tự ký URL mới; isolated world chỉ dùng làm fallback.
 *
 * Chữ ký gắn với URL (`X-Bogus`, `_signature`, `msToken`, `x-gorgon`…) **không bao giờ** được
 * replay sang URL khác — bê sang là tự khai sai phiên, đúng thứ TikTok bắt được.
 */
(function () {
  var w = window;
  if (w.__kbTtsProductBridge) return;
  w.__kbTtsProductBridge = true;

  var TEMPLATE_STORAGE_KEY = 'kb_tts_product_session';
  var HARVEST_STORAGE_KEY = 'kb_tts_harvest';
  var PRELOAD_MSG = 'kb-tts-preload-capture';
  var PING_MSG = 'kb-tts-product-bridge-ping';
  var PONG_MSG = 'kb-tts-product-bridge-pong';
  var FETCH_MSG = 'kb-tts-product-fetch';
  var FETCH_RESULT_MSG = 'kb-tts-product-fetch-result';
  var HARVEST_GET_MSG = 'kb-tts-harvest-get';
  var HARVEST_RESULT_MSG = 'kb-tts-harvest-result';
  /** Nhỏ hơn timeout chờ của content script để lỗi được báo ra thay vì hết hạn im lặng. */
  var FETCH_TIMEOUT_MS = 6000;
  /** Payload lớn hơn thì chỉ giữ trong RAM — sessionStorage là của TikTok, đừng chiếm quota. */
  var HARVEST_MAX_ENTRY_CHARS = 400000;
  var HARVEST_MAX_TOTAL_CHARS = 2000000;
  /** Khớp `HARVEST_TTL_MS` ở content: entry hết hạn bị dọn để reply không phải chở payload chết. */
  var HARVEST_TTL_MS = 10 * 60 * 1000;

  /** Header phiên (dùng lại được cho URL khác); tên message khớp `src/content/index.ts`. */
  var HEADER_ALLOWLIST = {
    'x-secsdk-csrf-token': 1,
    'x-tt-oec-region': 1,
    'x-tt-store-region': 1,
    'x-tt-entry-source': 1,
    'x-use-ppe': 1,
    'x-use-boe': 1,
    'x-tt-env': 1,
    'content-type': 1,
  };

  /**
   * Chữ ký một-lần + header trình duyệt tự quản. Replay chữ ký = tự khai sai phiên;
   * set header trình duyệt tự quản thì fetch ném lỗi hoặc bị bỏ im lặng.
   */
  var HEADER_DENYLIST = {
    'x-bogus': 1,
    'x-gorgon': 1,
    'x-khronos': 1,
    'x-argus': 1,
    'x-ladon': 1,
    'x-ss-stub': 1,
    'x-tt-trace-id': 1,
    'x-tt-logid': 1,
    _signature: 1,
    host: 1,
    origin: 1,
    referer: 1,
    connection: 1,
    'content-length': 1,
    accept: 1,
    'accept-encoding': 1,
    'accept-language': 1,
    'user-agent': 1,
    cookie: 1,
    'sec-fetch-site': 1,
    'sec-fetch-mode': 1,
    'sec-fetch-dest': 1,
    'sec-fetch-user': 1,
    'sec-ch-ua': 1,
    'sec-ch-ua-mobile': 1,
    'sec-ch-ua-platform': 1,
    priority: 1,
    'cache-control': 1,
    pragma: 1,
  };

  /** Query chỉ đúng cho một URL — xóa trước khi tái dùng template. */
  var SIGNED_QUERY_PARAMS = ['X-Bogus', 'x-bogus', '_signature', 'msToken', 'msToken_'];

  function isProductApiUrl(url) {
    return /\/api\/v\d+\/product\//i.test(String(url || ''));
  }

  function isPreloadUrl(url) {
    return /\/product\/product_creation\/preload/i.test(String(url || ''));
  }

  function queryParam(url, name) {
    try {
      return new URL(url, location.href).searchParams.get(name);
    } catch (e) {
      return null;
    }
  }

  /** Tham số vắng mặt **không** tính là khớp: payload mặc định của trang có thể khác. */
  function paramIs(url, name, expected) {
    return queryParam(url, name) === expected;
  }

  function readCookie(name) {
    try {
      var match = new RegExp('(?:^|; )' + name + '=([^;]*)').exec(document.cookie || '');
      return match ? decodeURIComponent(match[1]) : '';
    } catch (e) {
      return '';
    }
  }

  /** Cùng thứ tự ưu tiên với `resolveSellerId` ở content script. */
  function currentSellerId() {
    return (
      readCookie('oec_seller_id_unified_seller_env') ||
      readCookie('global_seller_id_unified_seller_env') ||
      ''
    );
  }

  function hasPath(data, path) {
    var node = data;
    for (var i = 0; i < path.length; i++) {
      if (!node || typeof node !== 'object') return false;
      node = node[path[i]];
      if (node == null) return false;
    }
    return true;
  }

  /**
   * Endpoint **không tham số** — payload trang tự gọi giống payload extension tự gọi, nên hớt
   * là dùng được luôn. `usable` là chốt chặn: payload thiếu field mapper cần, hoặc request của
   * trang khác biến thể (tab khác, body khác) thì coi như không hớt được, sync tự gọi lại.
   * Nhóm có tham số (payout / health / analytics / lead) cố ý **không** nằm đây: payload phụ
   * thuộc trang và khoảng ngày nên phải khớp fingerprint mới dùng được.
   */
  var HARVEST_TARGETS = [
    {
      key: 'seller_common',
      test: function (u) {
        return u.indexOf('/api/v3/seller/common/get') >= 0;
      },
      usable: function (raw) {
        return hasPath(raw.data, ['seller', 'seller_id']);
      },
    },
    {
      key: 'passport',
      test: function (u) {
        return u.indexOf('/passport/account/info/v2/') >= 0;
      },
      // Endpoint này không trả `code`; `message` là chốt duy nhất nói payload lành.
      usable: function (raw) {
        return String(raw.message || '').toLowerCase() === 'success' && !!raw.data;
      },
    },
    {
      key: 'onboard',
      test: function (u) {
        return u.indexOf('/api/v1/seller/onboard/v2/config/get') >= 0;
      },
      usable: function (raw) {
        return hasPath(raw.data, ['customized_config', 'product_categories']);
      },
    },
    {
      key: 'preload',
      test: isPreloadUrl,
      usable: function (raw) {
        return hasPath(raw.data, ['publish_limit']);
      },
    },
    {
      key: 'pay_onboard',
      test: function (u) {
        return u.indexOf('/api/v1/pay/onboard_info/get') >= 0;
      },
      // Payload đổi theo user_type / biz_type nên chỉ nhận đúng body extension vẫn gửi.
      usable: function (raw, url, body) {
        if (!hasPath(raw.data, ['enterprise'])) return false;
        try {
          var parsed = JSON.parse(body || '');
          return Number(parsed.user_type) === 1 && Number(parsed.biz_type) === 1;
        } catch (e) {
          return false;
        }
      },
    },
    {
      key: 'settlement',
      test: function (u) {
        return u.indexOf('/api/v1/seller/settlement/account/get') >= 0;
      },
      usable: function (raw) {
        return !!raw.data;
      },
    },
    {
      key: 'statement_stat',
      test: function (u) {
        return u.indexOf('/api/v1/pay/statement/stat/info') >= 0;
      },
      // `amount_stat_type` đổi thì «to settle» mang nghĩa khác — lấy sai là sai số tiền.
      usable: function (raw, url) {
        return (
          paramIs(url, 'amount_stat_type', '1') &&
          paramIs(url, 'statement_version', '1') &&
          !!raw.data
        );
      },
    },
    {
      key: 'products_list',
      test: function (u) {
        return u.indexOf('/api/v1/product/local/products/list') >= 0;
      },
      // Chỉ tab 19 mới mang `suspend_reason`; tab khác sẽ khiến identity lock bị suy ra là
      // «không bị khóa», mà cờ đó đẩy shop sang Suspended — sai một chiều rất đắt.
      usable: function (raw, url) {
        return queryParam(url, 'tab_id') === '19';
      },
    },
    {
      key: 'ahr',
      test: function (u) {
        return u.indexOf('/api/v1/account/ahr/score') >= 0;
      },
      // Điểm theo `account_type`/`account_level`: lấy nhầm scope là ghi sai điểm vào DB.
      usable: function (raw, url) {
        return (
          paramIs(url, 'account_type', '2') &&
          paramIs(url, 'account_level', '1') &&
          hasPath(raw.data, ['score'])
        );
      },
    },
    {
      key: 'ses',
      test: function (u) {
        return u.indexOf('/api/v1/seller/experience-score/score-overview') >= 0;
      },
      // `shop_id` rỗng = shop mặc định (đúng thứ extension lấy); có shop_id cụ thể thì
      // có thể là shop khác của cùng seller nên bỏ.
      usable: function (raw, url) {
        if (!paramIs(url, 'ses_version', 'beta')) return false;
        if (queryParam(url, 'shop_id')) return false;
        return (
          hasPath(raw.data, ['overview', 'score']) || hasPath(raw.data, ['metric', 'metricGroups'])
        );
      },
    },
  ];

  function findHarvestTarget(url) {
    var u = String(url || '');
    for (var i = 0; i < HARVEST_TARGETS.length; i++) {
      if (HARVEST_TARGETS[i].test(u)) return HARVEST_TARGETS[i];
    }
    return null;
  }

  function absUrl(url) {
    try {
      return new URL(String(url || ''), location.href).href;
    } catch (e) {
      return String(url || '');
    }
  }

  function shouldKeepHeader(name) {
    var n = String(name || '')
      .trim()
      .toLowerCase();
    if (!n || HEADER_DENYLIST[n]) return false;
    return !!HEADER_ALLOWLIST[n];
  }

  function pickHeaders(pairs) {
    var out = {};
    if (!pairs || !pairs.length) return out;
    for (var i = 0; i < pairs.length; i++) {
      var pair = pairs[i];
      if (!pair || pair.length < 2) continue;
      var name = String(pair[0] || '')
        .trim()
        .toLowerCase();
      if (!shouldKeepHeader(name)) continue;
      var value = pair[1];
      if (value == null || value === '') continue;
      out[name] = String(value);
    }
    return out;
  }

  function pushHeaderInit(h, collected) {
    if (!h) return;
    if (typeof Headers !== 'undefined' && h instanceof Headers) {
      h.forEach(function (v, k) {
        collected.push([k, v]);
      });
      return;
    }
    if (Array.isArray(h)) {
      for (var i = 0; i < h.length; i++) collected.push(h[i]);
      return;
    }
    if (typeof h === 'object') {
      for (var k in h) {
        if (Object.prototype.hasOwnProperty.call(h, k)) collected.push([k, h[k]]);
      }
    }
  }

  function headersFromFetch(input, init) {
    var collected = [];
    if (typeof Request !== 'undefined' && input instanceof Request) {
      pushHeaderInit(input.headers, collected);
    }
    if (init) pushHeaderInit(init.headers, collected);
    return pickHeaders(collected);
  }

  /**
   * Template = header phiên gộp dần từ mọi request product của trang. Gộp thay vì ghi đè:
   * mỗi endpoint gửi một tập con, không lần nào đủ cả bộ.
   */
  var template = { headers: {}, at: 0 };

  function readStoredTemplate() {
    try {
      var raw = sessionStorage.getItem(TEMPLATE_STORAGE_KEY);
      if (!raw) return;
      var obj = JSON.parse(raw);
      if (obj && obj.headers && typeof obj.headers === 'object') {
        template = { headers: obj.headers, at: Number(obj.at) || 0 };
      }
    } catch (e) {
      /* ignore */
    }
  }

  function saveTemplate(headers) {
    var keys = Object.keys(headers || {});
    if (!keys.length) return;
    var changed = false;
    for (var i = 0; i < keys.length; i++) {
      var k = keys[i];
      if (template.headers[k] === headers[k]) continue;
      template.headers[k] = headers[k];
      changed = true;
    }
    if (!changed) return;
    template.at = Date.now();
    try {
      sessionStorage.setItem(TEMPLATE_STORAGE_KEY, JSON.stringify(template));
    } catch (e) {
      /* ignore quota */
    }
  }

  function captureTemplate(url, headers) {
    if (!isProductApiUrl(url)) return;
    saveTemplate(headers);
  }

  /**
   * Payload hớt được, khoá theo `HARVEST_TARGETS[].key`. Mirror sang sessionStorage để sống qua
   * F5 của chính tab; entry quá cỡ chỉ nằm trong RAM (mất khi reload) thay vì chiếm quota.
   */
  var harvest = {};

  function readStoredHarvest() {
    try {
      var stored = sessionStorage.getItem(HARVEST_STORAGE_KEY);
      if (!stored) return;
      var obj = JSON.parse(stored);
      if (obj && typeof obj === 'object') harvest = obj;
    } catch (e) {
      /* ignore */
    }
  }

  function saveHarvest() {
    try {
      // Entry quá cỡ không được kéo cả store vượt cap rồi làm mọi entry khác mất chỗ.
      var persistable = {};
      for (var key in harvest) {
        if (!Object.prototype.hasOwnProperty.call(harvest, key)) continue;
        if (harvest[key] && harvest[key].oversize) continue;
        persistable[key] = harvest[key];
      }
      var text = JSON.stringify(persistable);
      if (text.length > HARVEST_MAX_TOTAL_CHARS) return;
      sessionStorage.setItem(HARVEST_STORAGE_KEY, text);
    } catch (e) {
      /* ignore quota */
    }
  }

  function pruneHarvest() {
    var now = Date.now();
    var dropped = false;
    for (var key in harvest) {
      if (!Object.prototype.hasOwnProperty.call(harvest, key)) continue;
      var entry = harvest[key];
      if (entry && entry.at && now - entry.at <= HARVEST_TTL_MS) continue;
      delete harvest[key];
      dropped = true;
    }
    if (dropped) saveHarvest();
  }

  /**
   * Chỉ nhận payload lành: `code` vắng hoặc `= 0` và có `data`, cộng thêm chốt `usable` riêng
   * của từng endpoint. Payload lỗi giữ nguyên entry cũ — thà dùng số cũ còn tươi hơn ghi đè
   * bằng response bị gateway chặn.
   */
  function harvestResponse(url, bodyText, requestBody) {
    var target = findHarvestTarget(url);
    if (!target) return;
    var raw;
    try {
      raw = JSON.parse(bodyText);
    } catch (e) {
      return;
    }
    if (!raw || typeof raw !== 'object') return;
    if (raw.code != null && Number(raw.code) !== 0) return;
    if (!raw.data || typeof raw.data !== 'object') return;
    try {
      if (!target.usable(raw, url, requestBody || '')) return;
    } catch (e) {
      return;
    }

    var at = Date.now();
    var sellerId = currentSellerId();
    if (String(bodyText || '').length <= HARVEST_MAX_ENTRY_CHARS) {
      harvest[target.key] = { raw: raw, at: at, sellerId: sellerId };
      saveHarvest();
    } else {
      harvest[target.key] = { raw: raw, at: at, sellerId: sellerId, oversize: true };
    }

    // publish_limit đẩy luôn sang background để side panel thấy số ngay, không đợi lượt sync.
    if (target.key !== 'preload') return;
    try {
      w.postMessage({ source: PRELOAD_MSG, raw: raw, at: at }, location.origin);
    } catch (e) {
      /* ignore */
    }
  }

  /**
   * URL của request do extension tự gọi — `pageIdleHook.js` đọc set này để không đếm vào
   * `inflight` (nếu đếm, logic page-ready của nhiệm vụ tưởng trang còn đang tải).
   * Không dùng cờ trên `init`: lớp bọc TikTok có thể dựng lại `Request` và mất property.
   */
  if (!w.__kbTtsForgedUrls) w.__kbTtsForgedUrls = {};

  function markForged(url, delta) {
    var key = absUrl(url);
    var map = w.__kbTtsForgedUrls;
    var next = (Number(map[key]) || 0) + delta;
    if (next > 0) map[key] = next;
    else delete map[key];
  }

  function reply(id, payload, source) {
    try {
      var msg = { source: source || FETCH_RESULT_MSG, id: id };
      if (payload) {
        for (var k in payload) {
          if (Object.prototype.hasOwnProperty.call(payload, k)) msg[k] = payload[k];
        }
      }
      w.postMessage(msg, location.origin);
    } catch (e) {
      /* ignore */
    }
  }

  function buildRequestUrl(rawUrl) {
    var u = new URL(rawUrl, location.href);
    if (u.origin !== location.origin || !isProductApiUrl(u.href)) return null;
    for (var i = 0; i < SIGNED_QUERY_PARAMS.length; i++) {
      u.searchParams.delete(SIGNED_QUERY_PARAMS[i]);
    }
    return u.href;
  }

  w.addEventListener('message', function (event) {
    if (event.source !== w) return;
    var data = event.data;
    if (!data) return;

    // Ping/pong để content script biết script MAIN có được nạp hay không: rẻ hơn nhiều so với
    // gửi request thật rồi chờ hết timeout mới biết là không ai nghe.
    if (data.source === PING_MSG) {
      reply(data.id, { ok: true }, PONG_MSG);
      return;
    }
    // Content script hỏi một lần cho cả lượt sync rồi tự lọc sellerId + `isUsable` phía nó.
    if (data.source === HARVEST_GET_MSG) {
      pruneHarvest();
      reply(data.id, { ok: true, entries: harvest }, HARVEST_RESULT_MSG);
      return;
    }
    if (data.source !== FETCH_MSG) return;

    var id = data.id;
    var method = String(data.method || 'GET').toUpperCase() === 'POST' ? 'POST' : 'GET';
    var url;
    try {
      url = buildRequestUrl(String(data.url || ''));
    } catch (e) {
      url = null;
    }
    if (!url) {
      reply(id, { ok: false, message: 'URL không thuộc namespace product của seller-us.' });
      return;
    }

    var headers = { accept: '*/*' };
    for (var k in template.headers) {
      if (!Object.prototype.hasOwnProperty.call(template.headers, k)) continue;
      if (k === 'content-type') continue;
      headers[k] = template.headers[k];
    }
    if (method === 'POST') headers['content-type'] = 'application/json';

    // Đọc `window.fetch` ngay lúc gọi: lớp bọc ký của TikTok nạp sau document_start nên nó
    // bọc ngoài hook này — gọi qua đó thì URL mới được ký đúng.
    var fetchFn = w.fetch;
    if (typeof fetchFn !== 'function') {
      reply(id, { ok: false, message: 'Trang seller-us không còn window.fetch.' });
      return;
    }

    var ac = typeof AbortController !== 'undefined' ? new AbortController() : null;
    var timer = w.setTimeout(function () {
      try {
        if (ac) ac.abort();
      } catch (e) {
        /* ignore */
      }
    }, FETCH_TIMEOUT_MS);

    var init = {
      method: method,
      credentials: 'include',
      headers: headers,
      body: method === 'POST' ? data.body || '{}' : undefined,
    };
    if (ac) init.signal = ac.signal;

    markForged(url, 1);
    var pending;
    try {
      pending = fetchFn.call(w, url, init);
    } catch (e) {
      // Lớp bọc của TikTok có thể ném ngay (chặn URL, thiếu tham số); phải nhả cờ forged kẻo
      // URL đó bị coi là request của extension mãi mãi.
      w.clearTimeout(timer);
      markForged(url, -1);
      reply(id, { ok: false, message: (e && e.message) || 'Bridge không gọi được product API.' });
      return;
    }
    if (!pending || typeof pending.then !== 'function') {
      w.clearTimeout(timer);
      markForged(url, -1);
      reply(id, { ok: false, message: 'window.fetch của trang không trả Promise.' });
      return;
    }

    pending
      .then(function (res) {
        return res.text().then(function (text) {
          var json = null;
          try {
            json = JSON.parse(text);
          } catch (e) {
            json = null;
          }
          reply(id, { ok: res.ok, status: res.status, raw: json });
        });
      })
      .catch(function (err) {
        var aborted = !!(ac && ac.signal && ac.signal.aborted);
        reply(id, {
          ok: false,
          message: aborted
            ? 'Timeout ' + Math.round(FETCH_TIMEOUT_MS / 1000) + 's — bridge không trả lời.'
            : (err && err.message) || 'Bridge gọi product API thất bại.',
        });
      })
      .then(function () {
        w.clearTimeout(timer);
        markForged(url, -1);
      });
  });

  readStoredTemplate();
  readStoredHarvest();

  var origFetch = w.fetch;
  if (typeof origFetch === 'function') {
    w.fetch = function kbTtsBridgeFetch(input, init) {
      var url = '';
      try {
        if (typeof input === 'string') url = input;
        else if (input instanceof URL) url = input.href;
        else if (input && typeof input.url === 'string') url = input.url;
      } catch (e) {
        url = '';
      }
      url = absUrl(url);

      // Request của chính extension không được làm template: header trơ của mình sẽ ghi đè
      // token phiên đã hớt của trang, rồi lần replay nào cũng trượt. Response của nó cũng
      // không hớt — đã trả thẳng về content qua kênh reply, hớt nữa là nhãn nguồn nói sai.
      var forged = !!w.__kbTtsForgedUrls[url];
      if (!forged && isProductApiUrl(url)) {
        try {
          captureTemplate(url, headersFromFetch(input, init));
        } catch (e) {
          /* ignore */
        }
      }

      var result = origFetch.apply(this, arguments);
      if (forged || !findHarvestTarget(url) || !result || typeof result.then !== 'function') {
        return result;
      }
      // Body request chỉ đọc khi là string: `Request` bọc stream, muốn đọc phải clone rồi
      // await — không đáng, endpoint nào cần body (pay_onboard) sẽ tự trượt `usable`.
      var reqBody = init && typeof init.body === 'string' ? init.body : '';
      return result.then(function (res) {
        try {
          res
            .clone()
            .text()
            .then(function (text) {
              harvestResponse(url, text, reqBody);
            })
            .catch(function () {});
        } catch (e) {
          /* ignore */
        }
        return res;
      });
    };
  }

  var XO = w.XMLHttpRequest;
  if (XO && XO.prototype) {
    var origOpen = XO.prototype.open;
    var origSetHeader = XO.prototype.setRequestHeader;
    var origSend = XO.prototype.send;

    XO.prototype.open = function kbTtsBridgeOpen(method, url) {
      this.__kbTtsBridgeUrl = absUrl(url);
      this.__kbTtsBridgeHeaders = {};
      return origOpen.apply(this, arguments);
    };

    XO.prototype.setRequestHeader = function kbTtsBridgeSetHeader(name, value) {
      try {
        var key = String(name || '')
          .trim()
          .toLowerCase();
        if (shouldKeepHeader(key) && this.__kbTtsBridgeHeaders) {
          this.__kbTtsBridgeHeaders[key] = String(value);
        }
      } catch (e) {
        /* ignore */
      }
      return origSetHeader.apply(this, arguments);
    };

    XO.prototype.send = function kbTtsBridgeSend(body) {
      var xhr = this;
      var url = xhr.__kbTtsBridgeUrl || '';
      var forged = !!w.__kbTtsForgedUrls[url];
      try {
        if (!forged && isProductApiUrl(url)) {
          captureTemplate(url, xhr.__kbTtsBridgeHeaders || {});
        }
      } catch (e) {
        /* ignore */
      }
      if (!forged && findHarvestTarget(url)) {
        var reqBody = typeof body === 'string' ? body : '';
        xhr.addEventListener(
          'load',
          function () {
            try {
              harvestResponse(url, xhr.responseText, reqBody);
            } catch (e) {
              /* ignore */
            }
          },
          { once: true },
        );
      }
      return origSend.apply(this, arguments);
    };
  }
})();
