const SIZE_GUIDE_STYLE_ID = 'kbt-size-guide-resize';
const DIALOG_WRAPPER_SELECTOR = 'div.bsc-size-guide__dialog-wrapper';

const STYLE_CSS = `
${DIALOG_WRAPPER_SELECTOR} .sui-dialog__ctn.sui-animation__dialog_W720 {
  width: auto !important;
  max-width: none !important;
}
${DIALOG_WRAPPER_SELECTOR} .sui-dialog__ctn > div {
  width: 900px !important;
  max-width: 90vw !important;
}
${DIALOG_WRAPPER_SELECTOR} .sui-dialog__body {
  max-height: min(80vh, 720px);
  overflow: auto;
}
${DIALOG_WRAPPER_SELECTOR} .bsc-size-guide__feature.SizeTools,
${DIALOG_WRAPPER_SELECTOR} .bsc-size-guide__feature[class*="SizeTools"] {
  display: none !important;
}
${DIALOG_WRAPPER_SELECTOR} .bsc-common-size-table__top,
${DIALOG_WRAPPER_SELECTOR} .bsc-common-size-table__top_tabs {
  display: none !important;
}
${DIALOG_WRAPPER_SELECTOR} .bsc-size-guide__feature.ProductDesc,
${DIALOG_WRAPPER_SELECTOR} .bsc-size-guide__feature[class*="ProductDesc"] {
  display: none !important;
}
`;

function isSheinHost(): boolean {
  return /(^|\.)shein\.com$/i.test(location.hostname);
}

function findOpenSizeGuideDialog(): HTMLElement | null {
  const wrappers = document.querySelectorAll<HTMLElement>(DIALOG_WRAPPER_SELECTOR);
  for (const wrapper of wrappers) {
    const style = window.getComputedStyle(wrapper);
    if (style.display === 'none' || style.visibility === 'hidden') continue;
    if (style.opacity === '0') continue;
    const rect = wrapper.getBoundingClientRect();
    if (rect.width < 8 || rect.height < 8) continue;
    return wrapper;
  }
  return null;
}

function applySizeGuideStyles(): void {
  let style = document.getElementById(SIZE_GUIDE_STYLE_ID) as HTMLStyleElement | null;
  if (!style) {
    style = document.createElement('style');
    style.id = SIZE_GUIDE_STYLE_ID;
    document.head.appendChild(style);
  }
  if (style.textContent !== STYLE_CSS) {
    style.textContent = STYLE_CSS;
  }
}

function removeSizeGuideStyles(): void {
  document.getElementById(SIZE_GUIDE_STYLE_ID)?.remove();
}

let pollTimer: ReturnType<typeof window.setInterval> | null = null;
let closeWatchTimer: ReturnType<typeof window.setInterval> | null = null;
let pollAttempts = 0;

const POLL_MS = 250;
const POLL_MAX = 48; // ~12s
const CLOSE_CHECK_MS = 400;

function stopSizeGuideWatch(): void {
  if (pollTimer !== null) {
    window.clearInterval(pollTimer);
    pollTimer = null;
  }
  if (closeWatchTimer !== null) {
    window.clearInterval(closeWatchTimer);
    closeWatchTimer = null;
  }
  pollAttempts = 0;
  removeSizeGuideStyles();
}

function startCloseWatch(): void {
  if (closeWatchTimer !== null) return;
  closeWatchTimer = window.setInterval(() => {
    if (!findOpenSizeGuideDialog()) {
      stopSizeGuideWatch();
    }
  }, CLOSE_CHECK_MS);
}

function tickWaitForDialog(): void {
  pollAttempts += 1;
  const dialog = findOpenSizeGuideDialog();
  if (dialog) {
    applySizeGuideStyles();
    if (pollTimer !== null) {
      window.clearInterval(pollTimer);
      pollTimer = null;
    }
    startCloseWatch();
    return;
  }
  if (pollAttempts >= POLL_MAX) {
    stopSizeGuideWatch();
  }
}

function onSizeHelpActivated(): void {
  stopSizeGuideWatch();
  pollAttempts = 0;
  pollTimer = window.setInterval(tickWaitForDialog, POLL_MS);
  window.setTimeout(tickWaitForDialog, 0);
  window.setTimeout(tickWaitForDialog, POLL_MS);
  window.setTimeout(tickWaitForDialog, POLL_MS * 3);
}

/** Widen SHEIN size guide modal and hide clutter — only after user opens it (no global observer). */
export function initSheinSizeGuideModalResize(): void {
  if (!isSheinHost()) return;

  document.addEventListener(
    'click',
    (event) => {
      const target = event.target;
      if (!(target instanceof Element)) return;
      if (!target.closest('.product-intro__size-help')) return;
      // Bubble phase: để SHEIN xử lý click/mở modal trước, rồi mới chỉnh CSS.
      window.setTimeout(onSizeHelpActivated, 0);
    },
    false,
  );
}
