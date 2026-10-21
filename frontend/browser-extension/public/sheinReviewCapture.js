/**
 * MAIN-world — chạy document_start trên *.shein.com.
 * Hook fetch/XHR (chỉ ĐỌC, không chặn request nào của SPA):
 * - get_goods_detail_realtime_data → rating + stock + seller + SLA + Local / Quick ship
 * - get_products_by_keywords → lưu response **mọi trang** + header/method/body làm template.
 * - get_size_guide_info → bảng size (cm + US) + hướng dẫn đo sản phẩm / cơ thể.
 *
 * Mọi trang: list lấy từ response của CHÍNH SPA. Trang 1 mở bằng `tabs.update` URL pdsearch
 * (Enter thanh địa chỉ); trang 2+ **bấm thanh phân trang của SPA** — nội dung đổi tại chỗ,
 * không load lại trang — rồi hook hứng JSON theo `page`. Không bấm được thì `tabs.update`
 * `?page=N`. Phương án cuối khi vẫn không hớt được JSON: XHR MAIN-world qua `window.fetch`
 * (lớp bọc Shein ký header theo URL mới, trước đó `history.replaceState` `?page`); isolated
 * fallback nếu MAIN im. Overlay chỉ page/keywords; 403 thì điều hướng lại đúng trang đang cần.
 * `kbt-shein-search-reset` xóa session + capture đã lưu.
 * Cache sessionStorage theo tab (không request lại, không bóc HTML).
 */
(function () {
  var STORAGE_KEY = 'kbt_shein_pdp_cache';
  var LEGACY_KEY = 'kbt_shein_review_cache';
  var MSG = 'kbt-shein-pdp-capture';
  var SEARCH_STORAGE_KEY = 'kbt_shein_search_cache';
  var SEARCH_MSG = 'kbt-shein-search-capture';
  var SESSION_STORAGE_KEY = 'kbt_shein_search_session';
  var SESSION_MSG = 'kbt-shein-search-session';
  var SIZE_GUIDE_STORAGE_KEY = 'kbt_shein_size_guide_cache';
  var SIZE_GUIDE_MSG = 'kbt-shein-size-guide-capture';
  var SIZE_GUIDE_FETCH_MSG = 'kbt-shein-size-guide-fetch';
  var SIZE_GUIDE_FETCH_RESULT_MSG = 'kbt-shein-size-guide-fetch-result';
  var SIZE_GUIDE_FETCH_TIMEOUT_MS = 8000;
  var HEADER_ALLOWLIST = {
    armortoken: 1,
    'x-csrf-token': 1,
    smdeviceid: 1,
    'x-gw-auth': 1,
    'x-cs-random': 1,
    'x-oest': 1,
    'x-ad-flag': 1,
    webversion: 1,
    'x-requested-with': 1,
    'content-type': 1,
    _ver: 1,
    _lang: 1,
    authorization: 1,
    'anti-in': 1,
    blackbox: 1,
    uat: 1,
  };
  var HEADER_DENYLIST = {
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

  function goodsIdFromUrl(url) {
    try {
      var u = new URL(url, location.href);
      var m = u.searchParams.get('goods_id');
      if (m) return String(m);
      var path = u.pathname.match(/-p-(\d+)/i);
      return path ? path[1] : null;
    } catch (e) {
      return null;
    }
  }

  function toInt(v) {
    if (typeof v === 'number' && Number.isFinite(v)) return Math.floor(v);
    if (typeof v === 'string' && v.trim()) {
      var n = parseInt(v, 10);
      return Number.isFinite(n) ? n : null;
    }
    return null;
  }

  function toFloat(v) {
    if (v == null || v === '') return null;
    var n = typeof v === 'number' ? v : parseFloat(String(v).replace(/[$,\s]/g, ''));
    return Number.isFinite(n) ? n : null;
  }

  function parsePercent(desc) {
    if (!desc) return null;
    var m = String(desc).match(/(\d+(?:\.\d+)?)\s*%/);
    if (!m) return null;
    var n = parseFloat(m[1]);
    if (!Number.isFinite(n) || n < 0 || n > 100) return null;
    return Math.round(n * 100) / 100;
  }

  /** "88% are ≤ <b>7</b> business days" → 7 */
  function parseSlaDays(desc) {
    if (!desc) return null;
    var s = String(desc);
    var m = s.match(/[≤<=]\s*(?:<[^>]*>\s*)*(\d+)/);
    if (!m) m = s.match(/(\d+)\s*(?:<\/b>)?\s*business\s+days/i);
    if (!m) return null;
    var n = parseInt(m[1], 10);
    return Number.isFinite(n) && n >= 0 ? n : null;
  }

  /** Histogram: cộng mọi bucket trừ ">N business days". */
  function sumDayPercents(list) {
    if (!list || !list.length) return null;
    var sum = 0;
    var any = false;
    for (var i = 0; i < list.length; i++) {
      var row = list[i];
      if (!row) continue;
      var day = row.day != null ? String(row.day).trim() : '';
      if (day.charAt(0) === '>') continue;
      var n = toFloat(row.percent);
      if (n == null) continue;
      sum += n;
      any = true;
    }
    if (!any) return null;
    var rounded = Math.round(sum * 100) / 100;
    if (rounded < 0 || rounded > 100) return null;
    return rounded;
  }

  function maxDayFromPercents(list) {
    if (!list || !list.length) return null;
    var maxDay = null;
    for (var i = 0; i < list.length; i++) {
      var d = list[i] && list[i].day != null ? String(list[i].day) : '';
      if (!d || d.charAt(0) === '>') continue;
      var m = d.match(/(\d+)/);
      if (!m) continue;
      var n = parseInt(m[1], 10);
      if (Number.isFinite(n) && (maxDay == null || n > maxDay)) maxDay = n;
    }
    return maxDay;
  }

  function slaFromBlock(desc, percents) {
    var descStr = desc != null && String(desc).trim() ? String(desc) : null;
    var percent = parsePercent(descStr);
    if (percent == null) percent = sumDayPercents(percents);
    var days = parseSlaDays(descStr);
    if (days == null) days = maxDayFromPercents(percents);
    if (percent == null && days == null) return null;
    return { percent: percent, days: days, desc: descStr };
  }

  function collectSlaBlocks(root, out, depth) {
    if (!root || depth > 8) return;
    if (Array.isArray(root)) {
      for (var i = 0; i < root.length; i++) collectSlaBlocks(root[i], out, depth + 1);
      return;
    }
    if (typeof root !== 'object') return;
    var hasDesc = root.dayPercentDesc != null && String(root.dayPercentDesc).trim() !== '';
    var hasPercents = root.dayPercents && root.dayPercents.length;
    if (hasDesc || hasPercents) {
      var block = slaFromBlock(root.dayPercentDesc, root.dayPercents);
      if (block) out.push(block);
    }
    for (var k in root) {
      if (!Object.prototype.hasOwnProperty.call(root, k)) continue;
      if (k === 'dayPercents' || k === 'dayPercentDesc') continue;
      collectSlaBlocks(root[k], out, depth + 1);
    }
  }

  /**
   * SLA giao hàng: `dayPercentDesc` + `dayPercents` (vd. 88% ≤ 7).
   * Không dùng `shippingDayPercentDesc` — đó là số toàn sàn (~80.34%) giống nhau mọi shop.
   */
  function pickShippingSla(ship) {
    var globalDesc =
      ship && ship.shippingDayPercentDesc
        ? String(ship.shippingDayPercentDesc).trim()
        : '';

    var direct = slaFromBlock(ship && ship.dayPercentDesc, ship && ship.dayPercents);
    var blocks = [];
    if (direct) blocks.push(direct);
    if (ship) {
      for (var k in ship) {
        if (!Object.prototype.hasOwnProperty.call(ship, k)) continue;
        if (
          k === 'dayPercents' ||
          k === 'dayPercentDesc' ||
          k === 'shippingDayPercentDesc' ||
          k === 'shippingDayPercentDescHC'
        ) {
          continue;
        }
        collectSlaBlocks(ship[k], blocks, 0);
      }
    }

    function isSevenDay(b) {
      if (!b) return false;
      if (b.days === 7) return true;
      if (!b.desc) return false;
      var plain = String(b.desc).replace(/<[^>]+>/g, ' ');
      return /[≤<=]\s*7\b/.test(plain) || /\b7\s+business\s+days/i.test(plain);
    }

    function score(b) {
      if (!b || b.percent == null) return -1;
      var seven = isSevenDay(b);
      var sameAsGlobal =
        globalDesc && b.desc && String(b.desc).trim() === globalDesc;
      if (seven && !sameAsGlobal) return 3;
      if (!seven) return 2;
      return 1;
    }

    var chosen = null;
    var best = -1;
    for (var i = 0; i < blocks.length; i++) {
      var s = score(blocks[i]);
      if (s > best) {
        best = s;
        chosen = blocks[i];
      }
    }

    var days = chosen && chosen.days != null ? chosen.days : toInt(ship && ship.shippingDays);
    if (days != null && days < 0) days = null;
    return {
      days: days,
      percent: chosen ? chosen.percent : null,
      desc: chosen && chosen.desc ? chosen.desc : null,
    };
  }

  function absUrl(v) {
    if (v == null || v === '') return null;
    var s = String(v).trim();
    if (!s) return null;
    if (s.indexOf('//') === 0) return 'https:' + s;
    return s;
  }

  function firstText() {
    for (var i = 0; i < arguments.length; i++) {
      var v = arguments[i];
      if (v == null || v === '') continue;
      var s = String(v).trim();
      if (s) return s;
    }
    return null;
  }

  function firstNumber() {
    for (var i = 0; i < arguments.length; i++) {
      var n = toFloat(arguments[i]);
      if (n != null) return n;
    }
    return null;
  }

  function firstInt() {
    for (var i = 0; i < arguments.length; i++) {
      var n = toInt(arguments[i]);
      if (n != null) return n;
    }
    return null;
  }

  /** "Sold by kcruab-kp" → "kcruab-kp" */
  function parseSoldByTitle(tips) {
    if (!tips) return null;
    var m = String(tips).match(/sold\s+by\s*[:\-]?\s*(.+)/i);
    if (!m) return null;
    var name = m[1].replace(/^["'\s]+|["'\s]+$/g, '').trim();
    return name || null;
  }

  /** true / 1 / "1" / "true" — string "0" là false (SKU-level isSupportQuickShip hay "0"). */
  function isTruthyFlag(v) {
    if (v === true || v === 1) return true;
    if (typeof v === 'string') {
      var s = v.trim().toLowerCase();
      return s === '1' || s === 'true';
    }
    return false;
  }

  function parseIsLocalProduct(ship, productInfo) {
    var tips =
      ship && ship.localMadeTips != null ? String(ship.localMadeTips).trim() : '';
    if (tips && tips.toLowerCase() === 'local') return true;
    if (
      productInfo &&
      productInfo.isLocalProduct != null &&
      productInfo.isLocalProduct !== ''
    ) {
      return isTruthyFlag(productInfo.isLocalProduct);
    }
    if (tips) return false;
    return null;
  }

  /**
   * Quick ship cấp SP: bất kỳ mall_stock.isSupportQuickShip (không dùng sku.isSupportQuickShip).
   * Ưu tiên mall_code khớp selectedMallCode; không match thì OR mọi mall.
   */
  function parseQuickShip(info, productInfo) {
    var saleAttr = (info && info.saleAttr) || {};
    var multi = saleAttr.multiLevelSaleAttribute || {};
    var skuList = multi.sku_list || info.sku_list || [];
    if (!skuList || !skuList.length) return null;

    var selectedMall =
      productInfo && productInfo.selectedMallCode != null
        ? String(productInfo.selectedMallCode).trim()
        : '';

    var matched = [];
    var all = [];
    for (var i = 0; i < skuList.length; i++) {
      var stocks = skuList[i] && skuList[i].mall_stock;
      if (!stocks || !stocks.length) continue;
      for (var j = 0; j < stocks.length; j++) {
        var ms = stocks[j];
        if (!ms) continue;
        all.push(ms);
        if (
          selectedMall &&
          ms.mall_code != null &&
          String(ms.mall_code).trim() === selectedMall
        ) {
          matched.push(ms);
        }
      }
    }
    if (!all.length) return null;
    var scope = matched.length ? matched : all;
    for (var k = 0; k < scope.length; k++) {
      if (isTruthyFlag(scope[k].isSupportQuickShip)) return true;
    }
    return false;
  }

  function parseRealtimeData(json) {
    try {
      var root = typeof json === 'string' ? JSON.parse(json) : json;
      var info = root && root.info;
      if (!info) return null;

      var overview =
        info.comment && info.comment.comments_overview;
      var reviewCount = overview ? toInt(overview.comment_num) : null;
      var averageRating = overview ? toFloat(overview.comment_rank_average) : null;
      if (reviewCount != null && reviewCount < 0) reviewCount = null;
      if (
        averageRating != null &&
        (averageRating < 0 || averageRating > 5)
      ) {
        averageRating = null;
      }

      var productInfo = info.productInfo || {};
      var stock = toInt(productInfo.stock);
      if (stock != null && stock < 0) stock = null;
      var bodyGoodsId = firstText(
        productInfo.goods_id,
        productInfo.goodsId,
        info.goods_id,
        info.goodsId,
      );

      var storeInfo = info.storeInfo || {};
      var seller =
        storeInfo.sellerInfo ||
        (info.sku_list && info.sku_list[0] && info.sku_list[0].sellerInfo) ||
        null;
      var mode = seller && seller.modeChangeSellerInfo;
      var storeCodeRaw =
        seller && seller.storeCode != null ? seller.storeCode : null;
      if (storeCodeRaw == null && storeInfo.storeCode != null) {
        storeCodeRaw = storeInfo.storeCode;
      }
      var storeCode = storeCodeRaw != null && String(storeCodeRaw).trim()
        ? String(storeCodeRaw).trim()
        : null;
      var storeLogo = absUrl(
        (mode && mode.storeLogo) ||
          (seller && seller.storeLogo) ||
          storeInfo.storeLogo,
      );
      var storeTitle = firstText(
        mode && mode.storeTitle,
        seller && seller.storeTitle,
        seller && seller.storeName,
        storeInfo.storeName,
        storeInfo.title,
      );
      var soldByTips = firstText(
        seller && seller.soldByTips,
        mode && mode.soldByTips,
        storeInfo.soldByTips,
      );
      if (!storeTitle && soldByTips) {
        storeTitle = parseSoldByTitle(soldByTips);
      }
      var shipsFromTips = firstText(
        seller && seller.shipsFromTips,
        mode && mode.shipsFromTips,
        storeInfo.shipsFromTips,
      );
      var shopRank = firstNumber(
        mode && mode.shopRank,
        seller && seller.shopRank,
        storeInfo.shopRank,
      );
      if (shopRank != null && shopRank < 0) shopRank = null;
      var storeAttention = firstInt(
        mode && mode.storeAttention,
        seller && seller.storeAttention,
        storeInfo.storeAttention,
      );
      if (storeAttention != null && storeAttention < 0) storeAttention = null;

      var ship = info.shipInfo || {};
      var sla = pickShippingSla(ship);
      var shippingDays = sla.days;
      var shippingDayPercent = sla.percent;
      var shippingDayPercentDesc = sla.desc;
      var isLocalProduct = parseIsLocalProduct(ship, productInfo);
      var isSupportQuickShip = parseQuickShip(info, productInfo);

      if (
        reviewCount == null &&
        averageRating == null &&
        stock == null &&
        !storeCode &&
        !storeTitle &&
        !soldByTips &&
        shopRank == null &&
        storeAttention == null &&
        shippingDays == null &&
        shippingDayPercent == null &&
        isLocalProduct == null &&
        isSupportQuickShip == null
      ) {
        return null;
      }

      return {
        goodsId: bodyGoodsId,
        reviewCount: reviewCount,
        averageRating: averageRating,
        stock: stock,
        storeCode: storeCode,
        storeLogo: storeLogo,
        storeTitle: storeTitle,
        soldByTips: soldByTips,
        shipsFromTips: shipsFromTips,
        shopRank: shopRank,
        storeAttention: storeAttention,
        shippingDays: shippingDays,
        shippingDayPercent: shippingDayPercent,
        shippingDayPercentDesc: shippingDayPercentDesc,
        isLocalProduct: isLocalProduct,
        isSupportQuickShip: isSupportQuickShip,
      };
    } catch (e) {
      return null;
    }
  }

  var MAX_CACHE = 24;

  function emptyMap() {
    return { byId: {}, latestId: null };
  }

  function readMap() {
    try {
      var raw = sessionStorage.getItem(STORAGE_KEY);
      if (!raw) return emptyMap();
      var obj = JSON.parse(raw);
      if (obj && obj.byId && typeof obj.byId === 'object') {
        return { byId: obj.byId, latestId: obj.latestId || null };
      }
      if (obj && typeof obj === 'object') {
        var legacyId = obj.goodsId != null ? String(obj.goodsId) : '_';
        var byId = {};
        byId[legacyId] = obj;
        return { byId: byId, latestId: legacyId === '_' ? null : legacyId };
      }
    } catch (e) {
      /* ignore */
    }
    return emptyMap();
  }

  function save(goodsId, data) {
    if (!data) return;
    var id = goodsId ? String(goodsId) : null;
    var payload = Object.assign({}, data, { goodsId: id, at: Date.now() });
    try {
      var map = readMap();
      var key = id || '_';
      map.byId[key] = payload;
      if (id) map.latestId = id;
      var ids = Object.keys(map.byId);
      if (ids.length > MAX_CACHE) {
        ids.sort(function (a, b) {
          var atA = (map.byId[a] && map.byId[a].at) || 0;
          var atB = (map.byId[b] && map.byId[b].at) || 0;
          return atA - atB;
        });
        for (var i = 0; i < ids.length - MAX_CACHE; i++) {
          delete map.byId[ids[i]];
        }
      }
      sessionStorage.setItem(STORAGE_KEY, JSON.stringify(map));
      sessionStorage.removeItem(LEGACY_KEY);
    } catch (e) {
      /* ignore quota */
    }
    try {
      window.postMessage({ source: MSG, payload: payload }, location.origin);
    } catch (e) {
      /* ignore */
    }
  }

  function toNum(v) {
    if (typeof v === 'number' && Number.isFinite(v)) return v;
    if (typeof v === 'string' && v.trim()) {
      var n = parseFloat(v.replace(/[$,\s]/g, '').replace(/,/g, ''));
      return Number.isFinite(n) ? n : null;
    }
    return null;
  }

  function soldLabelText(raw) {
    if (raw == null || raw === '') return null;
    if (typeof raw === 'number' && Number.isFinite(raw)) return String(raw);
    if (typeof raw === 'string') {
      var t = raw.trim();
      return t && t !== '[object Object]' ? t : null;
    }
    if (Array.isArray(raw)) {
      for (var i = 0; i < raw.length; i++) {
        var nested = soldLabelText(raw[i]);
        if (nested) return nested;
      }
      return null;
    }
    if (typeof raw === 'object') {
      var keys = ['labelLang', 'label_lang', 'label', 'text', 'value', 'desc'];
      for (var k = 0; k < keys.length; k++) {
        var fromObj = soldLabelText(raw[keys[k]]);
        if (fromObj) return fromObj;
      }
    }
    return null;
  }

  function parseSoldLabel(raw) {
    if (raw == null || raw === '') return null;
    if (typeof raw === 'number' && Number.isFinite(raw)) return Math.max(0, Math.floor(raw));
    var s = soldLabelText(raw);
    if (!s) return null;
    var norm = s.toLowerCase().replace(/,/g, '');
    var m = norm.match(/(\d+(?:\.\d+)?)\s*([km])?\s*\+?/i);
    if (!m) {
      var n = parseInt(norm, 10);
      return Number.isFinite(n) ? n : null;
    }
    var val = parseFloat(m[1]);
    if (!Number.isFinite(val)) return null;
    var unit = (m[2] || '').toLowerCase();
    if (unit === 'k') val *= 1000;
    if (unit === 'm') val *= 1000000;
    return Math.max(0, Math.floor(val));
  }

  function soldCountOf(row) {
    return (
      parseSoldLabel(row.salesLabel) ??
      parseSoldLabel(row.sales_label) ??
      parseSoldLabel(row.salesVolume) ??
      parseSoldLabel(row.sales_volume) ??
      parseSoldLabel(row.sales) ??
      parseSoldLabel(row.soldCount) ??
      parseSoldLabel(row.sold_count) ??
      parseSoldLabel(row.soldNum) ??
      parseSoldLabel(row.sold_num) ??
      parseSoldLabel(row.unit_sell) ??
      parseSoldLabel(row.saleNum)
    );
  }

  function pickPrice(row) {
    var sale = row.salePrice || row.sale_price || row.retailPrice || row.retail_price || row;
    if (sale && typeof sale === 'object') {
      var amount =
        toNum(sale.usdAmount) ??
        toNum(sale.amount) ??
        toNum(sale.salePrice) ??
        toNum(sale.price);
      if (amount != null) return amount;
    }
    return toNum(row.salePrice) ?? toNum(row.sale_price) ?? toNum(row.price);
  }

  function pickImage(row) {
    var img =
      row.goods_img ||
      row.goodsImg ||
      row.goods_thumb ||
      row.goodsThumb ||
      (Array.isArray(row.detail_image) && row.detail_image[0]) ||
      (Array.isArray(row.detailImage) && row.detailImage[0]);
    if (!img) return null;
    var s = String(img);
    if (s.indexOf('http') === 0) return s;
    if (s.indexOf('//') === 0) return 'https:' + s;
    return s;
  }

  function variantCountOf(row) {
    var related = row.relatedColor || row.related_color || row.relatedGoods || row.skuList;
    if (Array.isArray(related) && related.length) return related.length;
    var n =
      toNum(row.relatedColorCount) ??
      toNum(row.related_color_count) ??
      toNum(row.colorCount) ??
      toNum(row.skuCount);
    return n != null ? Math.max(0, Math.floor(n)) : null;
  }

  function flagBool(v) {
    if (v === true || v === 1 || v === '1') return true;
    if (v === false || v === 0 || v === '0') return false;
    if (typeof v === 'string' && /^(true|yes|local|quickship|club)$/i.test(v.trim())) return true;
    if (typeof v === 'string' && /^(false|no)$/i.test(v.trim())) return false;
    return undefined;
  }

  /** Local US list Search: titleLabelList[].labelType === "local_stock". */
  function parseSearchLocal(row) {
    var material = row.productMaterial || row.product_material;
    if (!material || typeof material !== 'object') return undefined;
    var list = material.titleLabelList || material.title_label_list;
    if (!Array.isArray(list)) return undefined;
    for (var i = 0; i < list.length; i++) {
      var item = list[i];
      if (!item || typeof item !== 'object') continue;
      var type = String(item.labelType || '')
        .trim()
        .toLowerCase();
      if (type === 'local_stock') return true;
    }
    return false;
  }

  function slimProduct(row) {
    if (!row || typeof row !== 'object') return null;
    var goodsId = row.goods_id != null ? String(row.goods_id) : row.goodsId != null ? String(row.goodsId) : '';
    if (!goodsId) return null;
    var name = String(row.goods_name || row.goodsName || row.goods_url_name || row.goodsUrlName || '').trim();
    var slug = String(row.goods_url_name || row.goodsUrlName || '').trim();
    var cateName = String(row.cate_name || row.cateName || row.category_name || row.categoryName || '').trim();
    var sold = soldCountOf(row);
    var rating =
      toFloat(row.comment_rank_average) ??
      toFloat(row.commentRankAverage) ??
      toFloat(row.commentRank) ??
      toFloat(row.grade) ??
      toFloat(row.goodsScore) ??
      toFloat(row.score);
    var reviews = toInt(row.comment_num) ?? toInt(row.commentNum) ?? toInt(row.comment_count);
    var isLocal = parseSearchLocal(row);
    var isQuickShip =
      flagBool(row.isSupportQuickShip) ?? flagBool(row.is_support_quick_ship) ?? flagBool(row.quickship);
    var isSheinClub = flagBool(row.sheinClub) ?? flagBool(row.isSheinClub) ?? flagBool(row.premiumFlag);
    var isUsWarehouse =
      flagBool(row.isUsWarehouse) ??
      flagBool(row.is_us_warehouse) ??
      flagBool(row.usWarehouse) ??
      flagBool(row.shipFromUs) ??
      flagBool(row.is_us_stock);
    return {
      goodsId: goodsId,
      name: name || goodsId,
      image: pickImage(row),
      price: pickPrice(row),
      rating: rating,
      reviewCount: reviews,
      soldCount: sold,
      variantCount: variantCountOf(row),
      goodsUrlName: slug || null,
      cateName: cateName || null,
      isLocal: isLocal,
      isQuickShip: isQuickShip,
      isSheinClub: isSheinClub,
      isUsWarehouse: isUsWarehouse,
    };
  }

  function extractProductsArray(json) {
    if (!json || typeof json !== 'object') return [];
    var info = json.info || json.data || json;
    var list =
      info.products ||
      info.goods ||
      info.list ||
      info.productList ||
      json.products ||
      json.goods;
    return Array.isArray(list) ? list : [];
  }

  function extractTotal(json, fallbackLen) {
    if (!json || typeof json !== 'object') return fallbackLen;
    var info = json.info || json.data || json;
    var n =
      toInt(info.num) ??
      toInt(info.total) ??
      toInt(info.totalCount) ??
      toInt(json.num) ??
      toInt(json.total);
    return n != null ? n : fallbackLen;
  }

  function collectAttrFacets(root, out, depth) {
    if (!root || depth > 7) return;
    if (Array.isArray(root)) {
      for (var i = 0; i < root.length; i++) collectAttrFacets(root[i], out, depth + 1);
      return;
    }
    if (typeof root !== 'object') return;
    var attrId = root.attr_id || root.attrId || root.attr_value_id || root.attrValueId;
    var attrValue = root.attr_value || root.attrValue || root.attr_value_en || root.name || root.label;
    if (attrId != null && attrValue) {
      var id = String(attrId).trim();
      var val = String(attrValue).trim();
      if (/^\d+_\d+$/.test(id) && val) out.push({ attrId: id, attrValue: val });
    }
    var keys = Object.keys(root);
    for (var k = 0; k < keys.length; k++) {
      var child = root[keys[k]];
      if (child && typeof child === 'object') collectAttrFacets(child, out, depth + 1);
    }
  }

  function keywordsFromUrl(url) {
    try {
      var u = new URL(url, location.href);
      return (
        u.searchParams.get('keywords') ||
        u.searchParams.get('keyword') ||
        decodeURIComponent((location.pathname.match(/\/pdsearch\/([^/]+)/i) || [])[1] || '')
          .replace(/\+/g, ' ')
          .trim()
      );
    } catch (e) {
      return '';
    }
  }

  function pageFromUrl(url) {
    try {
      var u = new URL(url, location.href);
      var p = parseInt(u.searchParams.get('page') || '', 10);
      return Number.isFinite(p) && p > 0 ? p : null;
    } catch (e) {
      return null;
    }
  }

  /** SPA gửi POST — `page` nằm trong JSON body (có thể lồng `queryObj`). */
  function pageFromBody(bodyText) {
    var raw = String(bodyText || '').trim();
    if (raw.charAt(0) !== '{') return null;
    try {
      var root = JSON.parse(raw);
      if (!root || typeof root !== 'object') return null;
      var direct = toInt(root.page);
      if (direct != null && direct > 0) return direct;
      var nested = root.queryObj && typeof root.queryObj === 'object' ? toInt(root.queryObj.page) : null;
      return nested != null && nested > 0 ? nested : null;
    } catch (e) {
      return null;
    }
  }

  function pageFromJson(json) {
    if (!json || typeof json !== 'object') return null;
    var info = json.info || json.data || json;
    return toInt(info.page) ?? toInt(info.pageNum) ?? toInt(info.pageNo) ?? toInt(json.page);
  }

  function keywordsFromJson(json) {
    if (!json || typeof json !== 'object') return '';
    var info = json.info || json.data || json;
    var k = info.keywords || info.keyword || json.keywords || json.keyword;
    return k != null ? String(k).trim() : '';
  }

  function parseSearchData(url, bodyText, pageHint) {
    try {
      var json = JSON.parse(bodyText);
      var rows = extractProductsArray(json);
      var products = [];
      var seen = {};
      for (var i = 0; i < rows.length; i++) {
        var slim = slimProduct(rows[i]);
        if (!slim || seen[slim.goodsId]) continue;
        seen[slim.goodsId] = true;
        products.push(slim);
      }
      var facets = [];
      collectAttrFacets(json, facets, 0);
      var page = pageFromJson(json);
      if (page == null || page < 1) page = pageFromUrl(url);
      if (page == null || page < 1) page = pageHint != null && pageHint > 0 ? pageHint : 1;
      return {
        keywords: keywordsFromUrl(url) || keywordsFromJson(json),
        page: page,
        total: extractTotal(json, products.length),
        products: products,
        facets: facets,
        at: Date.now(),
      };
    } catch (e) {
      return null;
    }
  }

  function saveSearch(payload) {
    if (!payload) return;
    try {
      sessionStorage.setItem(SEARCH_STORAGE_KEY, JSON.stringify(payload));
    } catch (e) {
      /* ignore quota */
    }
    try {
      window.postMessage({ source: SEARCH_MSG, payload: payload }, location.origin);
    } catch (e) {
      /* ignore */
    }
  }

  function isSearchUrl(url) {
    return /get_products_by_keywords/i.test(String(url || ''));
  }

  function shouldKeepHeader(name) {
    var n = String(name || '').trim().toLowerCase();
    if (!n || HEADER_DENYLIST[n]) return false;
    if (HEADER_ALLOWLIST[n]) return true;
    return n.indexOf('x-') === 0 || n.charAt(0) === '_';
  }

  function pickHeaders(pairs) {
    var out = {};
    if (!pairs || !pairs.length) return out;
    for (var i = 0; i < pairs.length; i++) {
      var pair = pairs[i];
      if (!pair || pair.length < 2) continue;
      var name = String(pair[0] || '').trim().toLowerCase();
      var value = pair[1];
      if (!shouldKeepHeader(name)) continue;
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

  function absRequestUrl(url) {
    try {
      return new URL(url, location.href).href;
    } catch (e) {
      return String(url || '');
    }
  }

  function captureRequestBody(raw) {
    if (raw == null || raw === '') return '';
    try {
      if (typeof raw === 'string') return raw.slice(0, 20000);
      if (typeof URLSearchParams !== 'undefined' && raw instanceof URLSearchParams) {
        return raw.toString().slice(0, 20000);
      }
      if (typeof FormData !== 'undefined' && raw instanceof FormData) return '';
      if (typeof Blob !== 'undefined' && raw instanceof Blob) return '';
      if (typeof ArrayBuffer !== 'undefined' && raw instanceof ArrayBuffer) return '';
      if (typeof raw === 'object') return JSON.stringify(raw).slice(0, 20000);
    } catch (e) {
      /* ignore */
    }
    return '';
  }

  function saveSession(url, method, headers, body) {
    if (!isSearchUrl(url)) return;
    var payload = {
      templateUrl: absRequestUrl(url),
      method: String(method || 'POST').toUpperCase(),
      headers: headers && typeof headers === 'object' ? headers : {},
      body: typeof body === 'string' ? body : '',
      at: Date.now(),
    };
    try {
      sessionStorage.setItem(SESSION_STORAGE_KEY, JSON.stringify(payload));
    } catch (e) {
      /* ignore quota */
    }
    try {
      window.postMessage({ source: SESSION_MSG, payload: payload }, location.origin);
    } catch (e) {
      /* ignore */
    }
  }

  function resetSearchCapture() {
    try {
      sessionStorage.removeItem(SESSION_STORAGE_KEY);
      sessionStorage.removeItem(SEARCH_STORAGE_KEY);
    } catch (e) {
      /* ignore */
    }
  }

  var FETCH_MSG = 'kbt-shein-search-fetch';
  var FETCH_RESULT_MSG = 'kbt-shein-search-fetch-result';
  var FETCH_TIMEOUT_MS = 45000;

  function syncPdsearchPage(page) {
    var n = Math.max(1, Math.floor(Number(page) || 0));
    if (n < 1) return;
    try {
      if (!/\/pdsearch\//i.test(location.pathname)) return;
      var next = new URL(location.href);
      var current = next.searchParams.get('page') || '1';
      if (String(n) === current) return;
      next.searchParams.set('page', String(n));
      history.replaceState(history.state, '', next.pathname + next.search + next.hash);
    } catch (e) {
      /* ignore */
    }
  }

  function replySearchFetch(id, payload) {
    try {
      var msg = { source: FETCH_RESULT_MSG, id: id };
      if (payload) {
        for (var k in payload) {
          if (Object.prototype.hasOwnProperty.call(payload, k)) msg[k] = payload[k];
        }
      }
      window.postMessage(msg, location.origin);
    } catch (e) {
      /* ignore */
    }
  }

  window.addEventListener('message', function (e) {
    if (e.source !== window) return;
    if (!e.data) return;
    if (e.data.source === 'kbt-shein-search-reset') {
      resetSearchCapture();
      return;
    }
    if (e.data.source !== FETCH_MSG) return;
    var id = e.data.id;
    var url = e.data.url;
    var method = String(e.data.method || 'POST').toUpperCase() === 'GET' ? 'GET' : 'POST';
    var body = e.data.body;
    try {
      var parsed = new URL(url, location.href);
      if (parsed.origin !== location.origin || !isSearchUrl(parsed.href)) {
        replySearchFetch(id, { ok: false, message: 'Không gọi được API Shein.' });
        return;
      }
      url = parsed.href;
    } catch (err) {
      replySearchFetch(id, { ok: false, message: 'Không gọi được API Shein.' });
      return;
    }
    syncPdsearchPage(e.data.page);
    var headers = {
      accept: 'application/json, text/plain, */*',
      'x-requested-with': 'XMLHttpRequest',
    };
    if (method !== 'GET' && body) {
      headers['content-type'] = 'application/json;charset=UTF-8';
    }
    var fetchFn = window.fetch;
    if (typeof fetchFn !== 'function') {
      replySearchFetch(id, { ok: false, message: 'Không gọi được API Shein.' });
      return;
    }
    var ac = typeof AbortController !== 'undefined' ? new AbortController() : null;
    var timer = window.setTimeout(function () {
      try {
        if (ac) ac.abort();
      } catch (err) {}
    }, FETCH_TIMEOUT_MS);
    var init = {
      method: method,
      credentials: 'include',
      headers: headers,
      body: method === 'GET' ? undefined : body || undefined,
      // Request của chính extension: hook bên dưới không được lấy nó làm template/session,
      // kẻo header anti-bot đã hớt của SPA bị ghi đè bằng 3 header trơ của mình.
      __kbtForged: true,
    };
    if (ac) init.signal = ac.signal;
    fetchFn
      .call(window, url, init)
      .then(function (res) {
        return res.text().then(function (text) {
          var json = null;
          try {
            json = JSON.parse(text);
          } catch (err) {
            json = null;
          }
          if (!res.ok) {
            replySearchFetch(id, {
              ok: false,
              status: res.status,
              json: json,
              message: 'SHEIN HTTP ' + res.status,
            });
            return;
          }
          replySearchFetch(id, { ok: true, status: res.status, json: json });
        });
      })
      .catch(function (err) {
        var aborted = !!(ac && ac.signal && ac.signal.aborted);
        replySearchFetch(id, {
          ok: false,
          message: aborted
            ? 'SHEIN search quá ' + Math.round(FETCH_TIMEOUT_MS / 1000) + 's không trả lời'
            : (err && err.message) || 'Không gọi được API Shein.',
        });
      })
      .then(function () {
        window.clearTimeout(timer);
      });
  });

  function trimText(v) {
    if (typeof v === 'number' && Number.isFinite(v)) return String(v);
    if (typeof v !== 'string') return null;
    var t = v.trim();
    return t || null;
  }

  function toSortNum(v) {
    if (typeof v === 'number' && Number.isFinite(v)) return Math.floor(v);
    if (typeof v === 'string' && v.trim()) {
      var n = parseInt(v, 10);
      if (Number.isFinite(n)) return n;
    }
    return 0;
  }

  function toAbsUrl(v) {
    var t = trimText(v);
    if (!t) return null;
    if (t.indexOf('//') === 0) return 'https:' + t;
    return t;
  }

  function parseMeasureHints(list, descKey) {
    if (!list || !list.length) return [];
    var out = [];
    for (var i = 0; i < list.length; i++) {
      var row = list[i];
      if (!row) continue;
      var name = trimText(row.name);
      var description = trimText(row[descKey]);
      if (!name && !description) continue;
      out.push({ name: name || '', description: description || '', sort: toSortNum(row.sort) });
    }
    out.sort(function (a, b) {
      return a.sort - b.sort || String(a.name).localeCompare(String(b.name));
    });
    return out;
  }

  function parseSizeGuideDimensions(sizeInfo) {
    var raw = sizeInfo && sizeInfo.dimensionAttrInfo;
    if (!raw || !raw.length) return [];
    var out = [];
    for (var i = 0; i < raw.length; i++) {
      var row = raw[i];
      if (!row) continue;
      var nameRaw = trimText(row.attr_name);
      var name = nameRaw ? nameRaw.replace(/\s+$/, '') : trimText(row.attr_name_en);
      var nameEn = trimText(row.attr_name_en) || name;
      if (!name) continue;
      out.push({
        id: trimText(row.attr_id) || '',
        name: name,
        nameEn: nameEn || name,
        sort: toSortNum(row.sort),
      });
    }
    out.sort(function (a, b) {
      return a.sort - b.sort || String(a.name).localeCompare(String(b.name));
    });
    return out;
  }

  function buildUsSizeMap(info) {
    var map = {};
    var attrs =
      info &&
      info.multiLevelSaleAttribute &&
      info.multiLevelSaleAttribute.skc_sale_attr;
    if (!attrs || !attrs.length) return map;
    for (var i = 0; i < attrs.length; i++) {
      var attr = attrs[i];
      if (!attr || String(attr.isSize || '') !== '1') continue;
      var list = attr.attr_value_list;
      if (!list || !list.length) continue;
      for (var j = 0; j < list.length; j++) {
        var item = list[j];
        if (!item) continue;
        var sizeName = trimText(item.attr_value_name);
        if (!sizeName) continue;
        var usSize = null;
        if (trimText(item.country_code) === 'US') {
          usSize = trimText(item.attr_local_size_value);
        }
        var localSizes = item.attr_value_local_size;
        if (!usSize && localSizes && localSizes.length) {
          for (var k = 0; k < localSizes.length; k++) {
            var loc = localSizes[k];
            if (loc && trimText(loc.country_code) === 'US') {
              usSize = trimText(loc.attr_local_size_value);
              break;
            }
          }
        }
        if (!usSize) usSize = trimText(item.attr_local_size_value);
        if (usSize) map[sizeName] = usSize;
      }
    }
    return map;
  }

  function lookupMeasurement(row, dim) {
    var keys = [dim.name, dim.name + ' ', dim.nameEn, dim.nameEn + ' '];
    for (var i = 0; i < keys.length; i++) {
      var val = trimText(row[keys[i]]);
      if (val) return val;
    }
    return null;
  }

  function normalizeInchValue(raw) {
    var t = trimText(raw);
    if (!t) return null;
    var s = t.replace(/\s*(in|inch|inches)\b/gi, '').trim().replace(/\s+/g, ' ');
    return s || null;
  }

  function normalizeLengthDual(raw) {
    var t = normalizeInchValue(raw);
    if (!t) return null;
    if (t.indexOf('/') === -1) return t;
    var parts = t.split('/').map(function (p) {
      return p.trim();
    }).filter(Boolean);
    if (parts.length < 2) return t;
    var nums = [];
    for (var i = 0; i < parts.length; i++) {
      var n = parseFloat(String(parts[i]).replace(/[^\d.+-]/g, ''));
      if (isFinite(n)) nums.push(n);
    }
    if (nums.length >= 2) {
      var lo = Math.min.apply(null, nums);
      var hi = Math.max.apply(null, nums);
      return lo + '-' + hi;
    }
    return t;
  }

  function mapMeasurementColumn(dimName) {
    var n = String(dimName || '')
      .trim()
      .toLowerCase();
    if (
      n.indexOf('sleeve') !== -1 ||
      n.indexOf('shoulder') !== -1 ||
      n.indexOf('inseam') !== -1 ||
      n.indexOf('armhole') !== -1
    ) {
      return null;
    }
    if (n.indexOf('bust') !== -1 || n.indexOf('chest') !== -1) return 'bust';
    if (n.indexOf('waist') !== -1) return 'waist';
    if (n.indexOf('hip') !== -1) return 'hip';
    if (n.indexOf('length') !== -1) return 'length';
    return null;
  }

  function parseBodySizeInchMap(basicAttribute) {
    var map = {};
    var raw = basicAttribute && basicAttribute.base_code_info_inch;
    if (!raw || !raw.length) return map;
    for (var i = 0; i < raw.length; i++) {
      var row = raw[i];
      if (!row) continue;
      var size = trimText(row.size);
      if (!size) continue;
      map[size] = {
        bust: normalizeInchValue(trimText(row.Bust)),
        waist: normalizeInchValue(trimText(row['Waist Size'])),
        hip: normalizeInchValue(trimText(row['Hip Size'])),
      };
    }
    return map;
  }

  function buildChartRows(rows, dimensions, bodyMap, unit) {
    var out = [];
    for (var i = 0; i < rows.length; i++) {
      var row = rows[i];
      var chart = {
        size: row.size,
        sizeId: row.sizeId,
        usSize: row.usSize,
        bust: null,
        waist: null,
        hip: null,
        length: null,
      };
      for (var d = 0; d < dimensions.length; d++) {
        var dim = dimensions[d];
        var rawVal = row.measurements[dim.name];
        if (!rawVal) continue;
        var col = mapMeasurementColumn(dim.name);
        if (col === 'bust') chart.bust = unit === 'inch' ? normalizeInchValue(rawVal) : rawVal;
        else if (col === 'waist') chart.waist = unit === 'inch' ? normalizeInchValue(rawVal) : rawVal;
        else if (col === 'hip') chart.hip = unit === 'inch' ? normalizeInchValue(rawVal) : rawVal;
        else if (col === 'length') chart.length = unit === 'inch' ? normalizeLengthDual(rawVal) : rawVal;
      }
      var body = bodyMap[row.size];
      if (body && body.hip && !chart.hip) chart.hip = body.hip;
      out.push(chart);
    }
    return out;
  }

  function parseRowsFromArray(raw, dimensions, usSizeMap) {
    if (!raw || !raw.length) return [];
    var out = [];
    for (var i = 0; i < raw.length; i++) {
      var row = raw[i];
      if (!row) continue;
      var size = trimText(row.attr_value_name) || trimText(row.attr_value_name_en);
      if (!size) continue;
      var measurements = {};
      for (var d = 0; d < dimensions.length; d++) {
        var dim = dimensions[d];
        var val = lookupMeasurement(row, dim);
        if (val) measurements[dim.name] = val;
      }
      out.push({
        size: size,
        sizeId: trimText(row.attr_value_id) || '',
        usSize: usSizeMap[size] || null,
        measurements: measurements,
      });
    }
    return out;
  }

  function parseMeasureNote(info) {
    var tips = info && info.sizeGuideLanguageTips;
    if (!tips) return null;
    return trimText(tips.manualMeasurementNote);
  }

  function parseSizeGuideRows(sizeInfo, dimensions, usSizeMap) {
    return parseRowsFromArray(sizeInfo && sizeInfo.sizeInfo, dimensions, usSizeMap);
  }

  function parseSizeGuideData(bodyText, goodsIdHint) {
    var root;
    try {
      root = JSON.parse(bodyText);
    } catch (e) {
      return null;
    }
    if (!root || String(root.code || '') !== '0' || !root.info) return null;
    var info = root.info;
    var sizeInfo = info.sizeInfo || null;
    var sizeTemplate = info.sizeTemplate || null;
    var basicAttribute = sizeInfo && sizeInfo.basicAttribute ? sizeInfo.basicAttribute : null;
    var dimensions = parseSizeGuideDimensions(sizeInfo);
    var usSizeMap = buildUsSizeMap(info);
    var bodyMap = parseBodySizeInchMap(basicAttribute);
    var inchRaw = sizeInfo && sizeInfo.sizeInfoInch;
    var hasInch = inchRaw && inchRaw.length;
    var unit = hasInch ? 'inch' : 'cm';
    var rows = hasInch
      ? parseRowsFromArray(inchRaw, dimensions, usSizeMap)
      : parseRowsFromArray(sizeInfo && sizeInfo.sizeInfo, dimensions, usSizeMap);
    var chartRows =
      unit === 'inch' && rows.length ? buildChartRows(rows, dimensions, bodyMap, 'inch') : [];
    var productMeasureGuide = parseMeasureHints(
      sizeTemplate && sizeTemplate.description_multi,
      'description',
    );
    var bodyMeasureGuide = parseMeasureHints(
      basicAttribute && basicAttribute.attribute_info,
      'desc',
    );
    var measureNote = parseMeasureNote(info);
    if (
      !chartRows.length &&
      !rows.length &&
      !productMeasureGuide.length &&
      !bodyMeasureGuide.length
    ) {
      return null;
    }
    return {
      goodsId: trimText(goodsIdHint) || '',
      unit: unit,
      dimensions: dimensions,
      rows: rows,
      chartRows: chartRows,
      productMeasureGuide: productMeasureGuide,
      bodyMeasureGuide: bodyMeasureGuide,
      measureNote: measureNote,
      productMeasureImageUrl: toAbsUrl(sizeTemplate && sizeTemplate.image_url),
      bodyMeasureImageUrl: toAbsUrl(basicAttribute && basicAttribute.image_url),
      capturedAt: new Date().toISOString(),
    };
  }

  function readSizeGuideMap() {
    try {
      var raw = sessionStorage.getItem(SIZE_GUIDE_STORAGE_KEY);
      if (!raw) return { byId: {}, latestId: null };
      var obj = JSON.parse(raw);
      if (obj && obj.byId && typeof obj.byId === 'object') {
        return { byId: obj.byId, latestId: obj.latestId || null };
      }
    } catch (e) {
      /* ignore */
    }
    return { byId: {}, latestId: null };
  }

  function saveSizeGuide(goodsId, data) {
    if (!data) return;
    var id = goodsId ? String(goodsId) : null;
    var payload = Object.assign({}, data, { goodsId: id || data.goodsId || '', at: Date.now() });
    try {
      var map = readSizeGuideMap();
      var key = id || '_';
      map.byId[key] = payload;
      if (id) map.latestId = id;
      var ids = Object.keys(map.byId);
      if (ids.length > MAX_CACHE) {
        ids.sort(function (a, b) {
          var atA = (map.byId[a] && map.byId[a].at) || 0;
          var atB = (map.byId[b] && map.byId[b].at) || 0;
          return atA - atB;
        });
        for (var i = 0; i < ids.length - MAX_CACHE; i++) {
          delete map.byId[ids[i]];
        }
      }
      sessionStorage.setItem(SIZE_GUIDE_STORAGE_KEY, JSON.stringify(map));
    } catch (e) {
      /* ignore quota */
    }
    try {
      window.postMessage({ source: SIZE_GUIDE_MSG, payload: payload }, location.origin);
    } catch (e) {
      /* ignore */
    }
  }

  function isSizeGuideUrl(url) {
    return /get_size_guide_info/i.test(String(url || ''));
  }

  function goodsIdFromSizeGuideUrl(url) {
    try {
      var u = new URL(url, location.href);
      var gid = u.searchParams.get('goodsId') || u.searchParams.get('goods_id');
      return gid ? String(gid) : null;
    } catch (e) {
      return null;
    }
  }

  function handleSizeGuideResponse(url, bodyText) {
    if (!isSizeGuideUrl(url)) return;
    var gid = goodsIdFromSizeGuideUrl(url) || goodsIdFromUrl(location.href) || null;
    var data = parseSizeGuideData(bodyText, gid);
    if (!data) return;
    if (!data.goodsId && gid) data.goodsId = gid;
    saveSizeGuide(data.goodsId || gid, data);
  }

  function replySizeGuideFetch(id, payload) {
    try {
      var msg = { source: SIZE_GUIDE_FETCH_RESULT_MSG, id: id };
      if (payload) {
        for (var k in payload) {
          if (Object.prototype.hasOwnProperty.call(payload, k)) msg[k] = payload[k];
        }
      }
      window.postMessage(msg, location.origin);
    } catch (e) {
      /* ignore */
    }
  }

  window.addEventListener('message', function (e) {
    if (e.source !== window) return;
    if (!e.data) return;
    if (e.data.source !== SIZE_GUIDE_FETCH_MSG) return;
    var id = e.data.id;
    var goodsId = trimText(e.data.goodsId);
    if (!goodsId) {
      replySizeGuideFetch(id, { ok: false, message: 'Thiếu goodsId.' });
      return;
    }
    var url;
    try {
      var origin = location.origin.replace(/\/$/, '');
      url =
        origin +
        '/bff-api/products-api/get_size_guide_info?_ver=1.1.8&_lang=en&goodsId=' +
        encodeURIComponent(goodsId);
    } catch (err) {
      replySizeGuideFetch(id, { ok: false, message: 'Không gọi được API Shein.' });
      return;
    }
    var fetchFn = window.fetch;
    if (typeof fetchFn !== 'function') {
      replySizeGuideFetch(id, { ok: false, message: 'Không gọi được API Shein.' });
      return;
    }
    var ac = typeof AbortController !== 'undefined' ? new AbortController() : null;
    var timer = window.setTimeout(function () {
      try {
        if (ac) ac.abort();
      } catch (err) {}
    }, SIZE_GUIDE_FETCH_TIMEOUT_MS);
    fetchFn
      .call(window, url, {
        method: 'GET',
        credentials: 'include',
        headers: {
          accept: 'application/json, text/plain, */*',
          'x-requested-with': 'XMLHttpRequest',
        },
        __kbtForged: true,
        signal: ac ? ac.signal : undefined,
      })
      .then(function (res) {
        return res.text().then(function (text) {
          var json = null;
          try {
            json = JSON.parse(text);
          } catch (err) {
            json = null;
          }
          if (!res.ok) {
            replySizeGuideFetch(id, {
              ok: false,
              status: res.status,
              json: json,
              message: 'SHEIN HTTP ' + res.status,
            });
            return;
          }
          handleSizeGuideResponse(url, text);
          var parsed = parseSizeGuideData(text, goodsId);
          replySizeGuideFetch(id, { ok: true, status: res.status, json: json, guide: parsed });
        });
      })
      .catch(function (err) {
        var aborted = !!(ac && ac.signal && ac.signal.aborted);
        replySizeGuideFetch(id, {
          ok: false,
          message: aborted
            ? 'SHEIN size guide quá ' + Math.round(SIZE_GUIDE_FETCH_TIMEOUT_MS / 1000) + 's'
            : (err && err.message) || 'Không gọi được API Shein.',
        });
      })
      .then(function () {
        window.clearTimeout(timer);
      });
  });

  function isPdpUrl(url) {
    return /get_goods_detail_realtime_data/i.test(String(url || ''));
  }

  function handleResponse(url, bodyText) {
    if (!isPdpUrl(url)) return;
    var data = parseRealtimeData(bodyText);
    if (!data) return;
    var gid =
      data.goodsId || goodsIdFromUrl(url) || goodsIdFromUrl(location.href) || null;
    save(gid, data);
  }

  /**
   * JSON search: SPA hoặc XHR MAIN-world (`window.fetch` sau replaceState `?page`).
   * Isolated fallback không đi qua hook này. Background dồn theo `page`.
   */
  function handleSearchResponse(url, bodyText, pageHint) {
    if (!isSearchUrl(url)) return;
    var data = parseSearchData(url, bodyText, pageHint);
    if (!data || !data.products.length) return;
    saveSearch(data);
  }

  var origFetch = window.fetch;
  if (typeof origFetch === 'function') {
    window.fetch = function () {
      var args = arguments;
      var input = args[0];
      var init = args[1];
      var url =
        typeof input === 'string'
          ? input
          : input && input.url
            ? input.url
            : String(input);
      var searchPage = null;
      try {
        if (isSearchUrl(url)) {
          var method =
            (init && init.method) ||
            (typeof Request !== 'undefined' && input instanceof Request && input.method) ||
            'POST';
          var body = '';
          try {
            if (init && init.body != null) body = captureRequestBody(init.body);
          } catch (e) {
            /* ignore */
          }
          searchPage = pageFromUrl(url) || pageFromBody(body);
          // Chỉ SPA mới được làm template: request forge của mình không mang header anti-bot,
          // lưu lại sẽ ghi đè session tốt bằng 3 header trơ rồi replay nào cũng 403.
          if (!(init && init.__kbtForged)) {
            saveSession(url, method, headersFromFetch(input, init), body);
          }
        }
      } catch (e) {
        /* ignore */
      }
      return origFetch.apply(this, args).then(function (res) {
        try {
          if (isPdpUrl(url) || isSearchUrl(url) || isSizeGuideUrl(url)) {
            res
              .clone()
              .text()
              .then(function (t) {
                handleResponse(url, t);
                handleSearchResponse(url, t, searchPage);
                handleSizeGuideResponse(url, t);
              })
              .catch(function () {});
          }
        } catch (e) {
          /* ignore */
        }
        return res;
      });
    };
  }

  var XO = window.XMLHttpRequest;
  if (XO && XO.prototype) {
    var open = XO.prototype.open;
    var setHeader = XO.prototype.setRequestHeader;
    var send = XO.prototype.send;
    XO.prototype.open = function (method, url) {
      this.__kbtSheinUrl = url;
      this.__kbtSheinMethod = method;
      this.__kbtSheinHeaders = {};
      return open.apply(this, arguments);
    };
    XO.prototype.setRequestHeader = function (name, value) {
      try {
        var key = String(name || '').trim().toLowerCase();
        if (shouldKeepHeader(key) && this.__kbtSheinHeaders) {
          this.__kbtSheinHeaders[key] = String(value);
        }
      } catch (e) {
        /* ignore */
      }
      return setHeader.apply(this, arguments);
    };
    XO.prototype.send = function (body) {
      var xhr = this;
      try {
        if (isSearchUrl(xhr.__kbtSheinUrl)) {
          var captured = captureRequestBody(body);
          xhr.__kbtSheinPage = pageFromUrl(xhr.__kbtSheinUrl) || pageFromBody(captured);
          saveSession(
            xhr.__kbtSheinUrl,
            xhr.__kbtSheinMethod || 'POST',
            xhr.__kbtSheinHeaders || {},
            captured,
          );
        }
      } catch (e) {
        /* ignore */
      }
      xhr.addEventListener('load', function () {
        try {
          handleResponse(xhr.__kbtSheinUrl, xhr.responseText);
          handleSearchResponse(xhr.__kbtSheinUrl, xhr.responseText, xhr.__kbtSheinPage);
          handleSizeGuideResponse(xhr.__kbtSheinUrl, xhr.responseText);
        } catch (e) {
          /* ignore */
        }
      });
      return send.apply(this, arguments);
    };
  }
})();
