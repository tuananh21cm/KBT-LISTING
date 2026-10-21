import {
  startTransition,
  useDeferredValue,
  useEffect,
  useMemo,
  useRef,
  useState,
  type Dispatch,
  type MouseEvent as ReactMouseEvent,
  type MutableRefObject,
  type PointerEvent as ReactPointerEvent,
  type ReactNode,
  type SetStateAction,
} from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useQuery, useQueryClient } from "@tanstack/react-query";
import {
  Alert,
  Avatar,
  Button,
  Card,
  Checkbox,
  Col,
  Dropdown,
  Form,
  Image,
  Input,
  InputNumber,
  Popover,
  Radio,
  Row,
  Select,
  Space,
  Switch,
  Table,
  Tag,
  Tooltip,
  Typography,
  message,
} from "antd";
import { Modal, PageBootSpin, useIsMobile } from "@drop/ui";
import {
  listingRewriteTitleTooltip,
  parseListingRewriteTitleMode,
  type ListingRewriteTitleMode,
} from "@drop/ui";
import { useDeferredMount } from "./useDeferredMount";
import { DeferredSection } from "./DeferredSection";
import { ListingDescriptionCard } from "./ListingDescriptionCard";
import {
  ArrowLeftOutlined,
  CheckOutlined,
  CloseOutlined,
  CopyOutlined,
  DeleteOutlined,
  DownOutlined,
  ExclamationCircleFilled,
  HolderOutlined,
  MinusCircleOutlined,
  PlusCircleFilled,
  PlayCircleOutlined,
  PlusOutlined,
  SearchOutlined,
  SendOutlined,
  SettingOutlined,
  ShopOutlined,
  SyncOutlined,
  ThunderboltOutlined,
  UndoOutlined,
  UpOutlined,
  LoadingOutlined,
  ZoomInOutlined,
} from "@ant-design/icons";
import type { ColumnsType } from "antd/es/table";
import type { FormListFieldData } from "antd/es/form/FormList";
import { useListingsDeps } from "./ListingsDepsContext";
import type { ListingsPortalConfig } from "./portalConfig";
import { SHOP_REGIONS } from "./localTypes";
import {
  NO_BRAND_ID,
  NO_BRAND_NAME,
  type MapListingSpecificsResult,
  type MappedListingSpecific,
} from "@drop/types/listing";
import type {
  ListingAttributeRow,
  ListingCertificationRow,
  ListingImageRow,
  ListingVariationRow,
} from "./productFormTypes";
import {
  COLOR_PRESETS,
  NUMBER_OF_ITEMS_PRESETS,
  SIZE_PRESETS,
  createEmptyVariationRow,
  extractSelectedSpecs,
  listingNeedsFourSellerRefresh,
  formatSyncErrorDisplay,
  parseSyncErrorMissingSpecifics,
  rebuildVariationsFromSpecs,
  resolveVariationRowAttrs,
  validateProductImages,
  validateVariationMatrix,
  upgradeHttpImageUrls,
  upgradeHttpToHttpsUrl,
  type ListingFormValues,
  type VariationSpecKey,
} from "./listingFormUtils";
import {
  deepEqual,
  ensureTypedMediaFromSource,
  mergeListingFormRemoteDiff,
} from "./listingFormMerge";
import {
  formToMirrorListingPayload,
  isBrandAttribute,
  isPublishBlockedAttribute,
  mirrorListingToForm,
  needsAutoMapSpecifics,
  normalizeStringList,
} from "./mirrorListingFormUtils";
import {
  clearListingEditDraft,
  loadListingEditDraft,
  saveListingEditDraft,
} from "./listingEditDraftStorage";
import {
  buildListingAppliedPricingPayload,
  loadListingAppliedPricing,
  saveListingAppliedPricing,
  snapshotFromAppliedPricing,
} from "./listingAppliedPricingStorage";
import { BatchSkuModal } from "./BatchSkuModal";
import { BatchPricingModal } from "./BatchPricingModal";
import { ManageShippingTemplateModal } from "./ManageShippingTemplateModal";
import {
  DEFAULT_PRICING_FORMULA,
  collectVariationProfits,
  fillMissingSalePrices,
  formatProfitLabel,
  getDefaultPricingTemplate,
  resolvePromoPercent,
  roundMoney,
  templateToFormulaInput,
} from "./pricingBatchUtils";
import type { PricingFormulaInput } from "@drop/types/pricingTemplate";
import { AddProductImageModal } from "./AddProductImageModal";
import { ProductPreviewMeta } from "./productPreviewMeta";
import { ProductPreviewName } from "./productPreviewName";
import {
  getDefaultShippingTemplate,
  type ShippingTemplate,
} from "@drop/types/shippingTemplate";
import { getApiErrorDetails } from "./apiErrorUtils";
import { formatDateTimeVn } from "@drop/utils/datetime";
import {
  DIMENSION_UNIT_OPTIONS,
  normalizeDimensionUnit,
  normalizeWeightUnit,
  WEIGHT_UNIT_OPTIONS,
} from "./shippingUnits";
import "./products.css";

const { Title, Text } = Typography;

const YES_NO = [
  { label: "No", value: "No" },
  { label: "Yes", value: "Yes" },
];

const VARIANT_IMAGE_MAX = 9;
const VARIANT_TABLE_PREVIEW_COUNT = 2;
const SPECIFICS_PREVIEW_COUNT = 2;
/** Warn / color when sale price is at most cost + this margin (USD). */
const SALE_OVER_COST_MARGIN_USD = 10;

function attrEquals(a?: string | null, b?: string | null): boolean {
  return (a ?? "").trim().toLowerCase() === (b ?? "").trim().toLowerCase();
}

/** Match variation to preview Color / Size / Number of Items selection. */
function variationMatchesPreview(
  v: ListingVariationRow,
  color: string | null,
  size: string | null,
  item: string | null,
): boolean {
  const attrs = v.attrs ?? {};
  if (color && !attrEquals(attrs.Color, color)) return false;
  if (size && !attrEquals(attrs.Size, size)) return false;
  if (item && !attrEquals(attrs["Number of Items"], item)) return false;
  return true;
}

function formatPreviewPriceRange(prices: number[]): string {
  if (prices.length === 0) return "—";
  const min = Math.min(...prices);
  const max = Math.max(...prices);
  if (min === max) return `$${min.toFixed(2)}`;
  return `$${min.toFixed(2)} – $${max.toFixed(2)}`;
}

type SaleVsCostTone = "danger" | "warning" | null;

/** Red: sale ≤ cost. Yellow: cost < sale ≤ cost+$10. Else ok. */
function getSaleVsCostTone(
  salePrice: number | null | undefined,
  costPrice: number | null | undefined,
): SaleVsCostTone {
  const sale = Number(salePrice);
  const cost = Number(costPrice);
  if (!Number.isFinite(sale)) return "danger";
  const costSafe = Number.isFinite(cost) ? cost : 0;
  if (sale <= costSafe) return "danger";
  if (sale <= costSafe + SALE_OVER_COST_MARGIN_USD) return "warning";
  return null;
}

function VariationSalePriceInput({
  variationIndex,
  value,
  onChange,
  placeholder = "Enter",
}: {
  variationIndex: number;
  value?: number | null;
  onChange?: (v: number | null) => void;
  placeholder?: string;
}) {
  const costPrice = Form.useWatch(["variations", variationIndex, "costPrice"]);
  const tone = getSaleVsCostTone(value, costPrice as number | null | undefined);
  return (
    <InputNumber
      size="small"
      min={0}
      step={0.01}
      value={value}
      onChange={(v) => onChange?.(typeof v === "number" ? v : null)}
      placeholder={placeholder}
      status={
        tone === "danger" ? "error" : tone === "warning" ? "warning" : undefined
      }
      style={{ width: "100%" }}
    />
  );
}

const listingStatusDotColor = (status?: string | null): string => {
  const key = (status || "").toLowerCase();
  if (key === "active") return "#52c41a";
  if (key === "draft" || key === "pending" || key === "incomplete")
    return "#faad14";
  if (key === "inactive" || key === "private") return "#8c8c8c";
  if (
    key === "suspended" ||
    key === "removed" ||
    key === "reject" ||
    key === "unsaleable"
  ) {
    return "#ff4d4f";
  }
  return "#1677ff";
};

function colorToCss(name: string): string {
  const key = name.trim().toLowerCase();
  const map: Record<string, string> = {
    white: "#f5f5f5",
    black: "#1f1f1f",
    pink: "#f8bbd0",
    red: "#e53935",
    wine: "#722f37",
    blue: "#1e88e5",
    gold: "#f6c343",
    green: "#43a047",
    yellow: "#fdd835",
    orange: "#fb8c00",
    purple: "#8e24aa",
    brown: "#8d6e63",
    gray: "#9e9e9e",
    grey: "#9e9e9e",
    beige: "#d7ccc8",
    navy: "#1a237e",
    chocolate: "#5d4037",
  };
  return map[key] ?? "#d9d9d9";
}

function isSharedProductImage(img?: {
  mediaType?: string;
  colorValue?: string;
  listingSkuId?: string;
}) {
  if (!img) return false;
  if (img.colorValue?.trim()) return false;
  if (img.listingSkuId) return false;
  return (
    !img.mediaType || img.mediaType === "Main" || img.mediaType === "Detail"
  );
}

function isVariantColorImage(
  img?: { mediaType?: string; colorValue?: string },
  color?: string,
) {
  if (!img?.colorValue?.trim()) return false;
  if (
    color &&
    img.colorValue.trim().toLowerCase() !== color.trim().toLowerCase()
  ) {
    return false;
  }
  return (
    !img.mediaType || img.mediaType === "Main" || img.mediaType === "Detail"
  );
}

function isGifImageUrl(url?: string) {
  if (!url?.trim()) return false;
  try {
    const path = new URL(url, "https://local.invalid").pathname;
    return /\.gif$/i.test(path);
  } catch {
    return /\.gif(?:$|\?|#)/i.test(url);
  }
}

function syncListingImagesOrder(list: ListingImageRow[]): ListingImageRow[] {
  const products = list.filter((img) => isSharedProductImage(img));
  const variantColors = list.filter((img) => isVariantColorImage(img));
  const extras = list.filter(
    (img) => !isSharedProductImage(img) && !isVariantColorImage(img),
  );
  return [
    ...products.map((img, i) => ({
      ...img,
      mediaType: i === 0 ? ("Main" as const) : ("Detail" as const),
      sortOrder: i,
      colorValue: undefined,
    })),
    ...variantColors.map((img, i) => ({
      ...img,
      mediaType: "Detail" as const,
      sortOrder: products.length + i,
    })),
    ...extras.map((img, i) => ({
      ...img,
      sortOrder: products.length + variantColors.length + i,
    })),
  ];
}

function reorderImageSubset(
  list: ListingImageRow[],
  matches: (img: ListingImageRow) => boolean,
  fromLocal: number,
  toLocal: number,
): ListingImageRow[] | null {
  if (fromLocal === toLocal || fromLocal < 0 || toLocal < 0) return null;
  const globalIndices = list
    .map((img, i) => (matches(img) ? i : -1))
    .filter((i) => i >= 0);
  const fromGlobal = globalIndices[fromLocal];
  const toGlobal = globalIndices[toLocal];
  if (fromGlobal == null || toGlobal == null) return null;
  const next = [...list];
  const [moved] = next.splice(fromGlobal, 1);
  if (!moved) return null;
  next.splice(toGlobal, 0, moved);
  return syncListingImagesOrder(next);
}

/** Last path segment (file name) of an image URL, lower-cased for comparison. */
function imageFileNameKey(url?: string): string {
  const raw = String(url ?? "").trim();
  if (!raw) return "";
  try {
    const path = new URL(raw, "https://local.invalid").pathname;
    return (path.split("/").filter(Boolean).pop() || "").toLowerCase();
  } catch {
    return (
      raw.split("/").filter(Boolean).pop()?.toLowerCase() ?? raw.toLowerCase()
    );
  }
}

/** Human-readable image file name (last path segment, URL-decoded) for preview captions. */
function imageDisplayName(url?: string): string {
  const raw = String(url ?? "").trim();
  if (!raw) return "";
  try {
    const path = new URL(raw, "https://local.invalid").pathname;
    const seg = path.split("/").filter(Boolean).pop() || "";
    return decodeURIComponent(seg);
  } catch {
    const seg =
      raw.split("?")[0]?.split("#")[0]?.split("/").filter(Boolean).pop() || raw;
    return seg;
  }
}

/** Keep head + tail of a long file name (middle ellipsis). */
function truncateMiddle(text: string, maxChars: number): string {
  const t = text.trim();
  if (maxChars <= 0 || t.length <= maxChars) return t;
  if (maxChars <= 3) return t.slice(0, maxChars);
  const keep = maxChars - 1; // …
  const head = Math.ceil(keep / 2);
  const tail = Math.floor(keep / 2);
  return `${t.slice(0, head)}…${t.slice(t.length - tail)}`;
}

/** Filename caption that middle-truncates to the available preview width. */
function PreviewFilenameCaption({ name }: { name: string }) {
  const ref = useRef<HTMLDivElement>(null);
  const [shown, setShown] = useState(name);

  useEffect(() => {
    const el = ref.current;
    if (!el || !name) {
      setShown(name);
      return;
    }

    const previewRoot = el.closest(".fs-gallery-preview-root");

    const update = () => {
      const previewImg = previewRoot?.querySelector(
        ".ant-image-preview-img",
      ) as HTMLElement | null;
      // Prefer image width — do not use el.clientWidth (shrink-wrap feedback loop).
      const targetWidth =
        previewImg && previewImg.clientWidth > 40
          ? previewImg.clientWidth
          : Math.min(window.innerWidth * 0.9, 560);
      el.style.width = `${Math.floor(targetWidth)}px`;
      el.style.maxWidth = `${Math.floor(targetWidth)}px`;
      // ~5.5px/char at 11px; keep more of the name visible.
      const maxChars = Math.max(24, Math.floor((targetWidth - 24) / 5.5));
      setShown(truncateMiddle(name, maxChars));
    };

    update();
    const ro = new ResizeObserver(update);
    if (previewRoot) ro.observe(previewRoot);
    const previewImg = previewRoot?.querySelector(
      ".ant-image-preview-img",
    ) as HTMLElement | null;
    if (previewImg) {
      ro.observe(previewImg);
      previewImg.addEventListener("load", update);
    }
    window.addEventListener("resize", update);
    return () => {
      ro.disconnect();
      previewImg?.removeEventListener("load", update);
      window.removeEventListener("resize", update);
    };
  }, [name]);

  if (!name) return null;
  return (
    <div ref={ref} className="fs-preview-filename" title={name}>
      {shown}
    </div>
  );
}

/**
 * Pull http(s) image URLs from 4Seller syncError text
 * (e.g. 【Publish Failed】product image upload fail，http://…webp，upload file is invalid).
 * Stops at whitespace / CJK commas / ASCII punctuation used as separators.
 */
function extractImageUrlsFromSyncError(text?: string | null): string[] {
  const raw = String(text ?? "").trim();
  if (!raw) return [];
  const re = /https?:\/\/[^\s，,;；、\u3000<>"']+/gi;
  const found: string[] = [];
  const seen = new Set<string>();
  for (const match of raw.matchAll(re)) {
    let url = match[0].replace(/[)\]}》」』。.]+$/g, "");
    if (!url) continue;
    // Match gallery tiles after http→https upgrade.
    url = upgradeHttpToHttpsUrl(url);
    const key = url.toLowerCase();
    if (seen.has(key)) continue;
    seen.add(key);
    found.push(url);
  }
  return found;
}

/** Basename without extension / CDN size suffixes — for matching error URL ↔ form URL. */
function imageFileStemKey(url?: string): string {
  const name = imageFileNameKey(url);
  if (!name) return "";
  return name
    .replace(/\.(webp|jpe?g|png|gif|bmp|avif)$/i, "")
    .replace(/_thumbnail_\d+x$/i, "")
    .replace(/_\d+x\d+$/i, "")
    .toLowerCase();
}

/**
 * Shein/ltwebstatic content id = phần trước `_thumbnail_900x` (vd.
 * `1772183982da4805766df27805ced8bed8dc40e976_thumbnail_900x.webp` → hash).
 * Dùng để bôi đỏ mọi size/variant cùng ảnh gốc.
 */
function imageContentIdKey(url?: string): string {
  const raw = String(url ?? "").trim();
  if (!raw) return "";
  const name = imageFileNameKey(raw);
  const fromName = name.match(/^(.+?)_thumbnail_\d+x/i);
  if (fromName?.[1]) return fromName[1].toLowerCase();
  const fromPath = raw.match(/\/([a-z0-9]+)(?:_thumbnail_\d+x)(?:\.[a-z0-9]+)?(?:$|[?#])/i);
  if (fromPath?.[1]) return fromPath[1].toLowerCase();
  // Fallback: long hex/alnum token in basename (no thumbnail suffix).
  const bare = name
    .replace(/\.(webp|jpe?g|png|gif|bmp|avif)$/i, "")
    .toLowerCase();
  if (/^[a-z0-9]{20,}$/i.test(bare)) return bare;
  return imageFileStemKey(raw);
}

/** Match keys for a URL: full URL / path / file name / content-id trước `_thumbnail_`. */
function buildFailedUploadImageKeys(urls: string[]): Set<string> {
  const keys = new Set<string>();
  for (const url of urls) {
    const trimmed = url.trim();
    if (!trimmed) continue;
    keys.add(trimmed.toLowerCase());
    try {
      const u = new URL(trimmed, "https://local.invalid");
      keys.add(`${u.host}${u.pathname}`.toLowerCase());
    } catch {
      /* ignore */
    }
    const name = imageFileNameKey(trimmed);
    if (name) keys.add(name);
    const stem = imageFileStemKey(trimmed);
    if (stem) keys.add(`stem:${stem}`);
    const contentId = imageContentIdKey(trimmed);
    if (contentId) {
      keys.add(`id:${contentId}`);
      keys.add(`stem:${contentId}`);
    }
  }
  return keys;
}

function isFailedUploadImageUrl(
  url: string | undefined | null,
  failedKeys: Set<string>,
): boolean {
  if (!url?.trim() || failedKeys.size === 0) return false;
  const trimmed = url.trim();
  if (failedKeys.has(trimmed.toLowerCase())) return true;
  try {
    const u = new URL(trimmed, "https://local.invalid");
    if (failedKeys.has(`${u.host}${u.pathname}`.toLowerCase())) return true;
  } catch {
    /* ignore */
  }
  const name = imageFileNameKey(trimmed);
  if (name && failedKeys.has(name)) return true;
  const stem = imageFileStemKey(trimmed);
  if (stem && failedKeys.has(`stem:${stem}`)) return true;
  const contentId = imageContentIdKey(trimmed);
  if (contentId && failedKeys.has(`id:${contentId}`)) return true;
  // Form URL khác host nhưng vẫn chứa cùng content id trong path/filename.
  if (name) {
    for (const key of failedKeys) {
      if (!key.startsWith("id:")) continue;
      const id = key.slice(3);
      if (id.length >= 16 && name.includes(id)) return true;
    }
  }
  return false;
}

/**
 * Identity for duplicate detection: Shein content-id before `_thumbnail_Nx`,
 * else basename. Same photo at 405x vs 900x must count as one identity.
 */
function imageIdentityKey(url?: string | null): string {
  const raw = url ?? undefined;
  return imageContentIdKey(raw) || imageFileNameKey(raw);
}

type FailedUploadImageScope =
  | { kind: "main" }
  | { kind: "color"; color: string }
  | { kind: "media"; mediaType: string };

function countIdentityInScope(
  list: ListingImageRow[],
  identity: string,
  scope: FailedUploadImageScope,
): number {
  if (!identity) return 0;
  let n = 0;
  for (const img of list) {
    const url = String(img.url ?? "").trim();
    if (!url || imageIdentityKey(url) !== identity) continue;
    if (scope.kind === "main") {
      if (isSharedProductImage(img)) n += 1;
    } else if (scope.kind === "color") {
      if (isVariantColorImage(img, scope.color)) n += 1;
    } else if (img.mediaType === scope.mediaType) {
      n += 1;
    }
  }
  return n;
}

/**
 * Tile stays red only while the syncError-matched identity is still duplicated
 * in the same gallery scope. After the user (or auto-dedupe) leaves a single
 * copy, clear the “xóa trùng” highlight — republish clears server syncError.
 */
function isActiveFailedUploadImage(
  url: string | undefined | null,
  failedKeys: Set<string>,
  images: ListingImageRow[],
  scope: FailedUploadImageScope,
): boolean {
  if (!isFailedUploadImageUrl(url, failedKeys)) return false;
  const id = imageIdentityKey(url);
  if (!id) return true;
  return countIdentityInScope(images, id, scope) >= 2;
}

/** True when any syncError-matched identity still appears ≥2 times in one scope. */
function hasFailedUploadDupes(
  images: ListingImageRow[],
  failedKeys: Set<string>,
): boolean {
  if (failedKeys.size === 0) return false;
  const identities = new Set<string>();
  for (const img of images) {
    const url = String(img.url ?? "").trim();
    if (!url || !isFailedUploadImageUrl(url, failedKeys)) continue;
    const id = imageIdentityKey(url);
    if (id) identities.add(id);
  }
  for (const id of identities) {
    if (countIdentityInScope(images, id, { kind: "main" }) >= 2) return true;
    const colors = new Set<string>();
    for (const img of images) {
      if (!isVariantColorImage(img)) continue;
      if (imageIdentityKey(img.url) !== id) continue;
      colors.add(String(img.colorValue ?? "").trim().toLowerCase());
    }
    for (const color of colors) {
      if (countIdentityInScope(images, id, { kind: "color", color }) >= 2) {
        return true;
      }
    }
    for (const mediaType of ["SizeChart", "Video"] as const) {
      if (countIdentityInScope(images, id, { kind: "media", mediaType }) >= 2) {
        return true;
      }
    }
  }
  return false;
}

/** Split syncError so image URLs render as clickable links (activate + scroll to tile). */
function renderSyncErrorWithImageLinks(
  text: string,
  onUrlClick: (url: string) => void,
): ReactNode {
  const urls = extractImageUrlsFromSyncError(text);
  if (urls.length === 0) return text;
  const nodes: ReactNode[] = [];
  let rest = text;
  let key = 0;
  for (const url of urls) {
    const idx = rest.indexOf(url);
    if (idx < 0) continue;
    if (idx > 0) nodes.push(rest.slice(0, idx));
    nodes.push(
      <a
        key={`fail-img-${key++}`}
        href={url}
        className="fs-sync-error-image-link"
        onClick={(e) => {
          e.preventDefault();
          onUrlClick(url);
        }}
        title={`${url}\nNhấn để tìm ảnh lỗi trên gallery`}
      >
        {imageFileNameKey(url) || url}
      </a>,
    );
    rest = rest.slice(idx + url.length);
  }
  if (rest) nodes.push(rest);
  return nodes.length > 0 ? nodes : text;
}

function hasListingSizeChart(images: ListingImageRow[] | undefined): boolean {
  return (images ?? []).some(
    (img) =>
      String(img?.mediaType ?? "").toLowerCase() === "sizechart" &&
      String(img?.url ?? "").trim(),
  );
}

function showPublishMissingSizeChartWarning(): void {
  Modal.warning({
    title: "Chưa có Size Chart trước khi Publish",
    content: (
      <p style={{ marginBottom: 0 }}>
        Tải ảnh bảng size tại mục <strong>Size Chart</strong> (card Sản phẩm),
        rồi Publish lại.
      </p>
    ),
    okText: "Đã hiểu",
  });
}

function imageLoadFailKey(url?: string | null): string {
  return String(url ?? "").trim().toLowerCase();
}

function isLoadFailedImage(
  url: string | undefined | null,
  failedLoadKeys: Set<string>,
): boolean {
  const key = imageLoadFailKey(url);
  return Boolean(key) && failedLoadKeys.has(key);
}

function hasLoadFailedImages(
  images: ListingImageRow[] | undefined,
  failedLoadKeys: Set<string>,
): boolean {
  if (failedLoadKeys.size === 0) return false;
  return (images ?? []).some((img) => isLoadFailedImage(img.url, failedLoadKeys));
}

/** 4Seller rejects attribute/product image upload when URL is explicit http:// (Invalid Para). */
function isHttpImageUrl(url?: string | null): boolean {
  const trimmed = String(url ?? "").trim();
  if (!trimmed) return false;
  return trimmed.toLowerCase().startsWith("http://");
}

function hasHttpImages(images?: ListingImageRow[]): boolean {
  return (images ?? []).some((img) => isHttpImageUrl(img.url));
}

/** Dupes 4Seller đã từ chối, URL http://, hoặc ảnh trên gallery không tải được (ô đỏ). */
function hasListingFailedImages(
  images: ListingImageRow[] | undefined,
  failedUploadKeys: Set<string>,
  failedLoadKeys: Set<string>,
): boolean {
  const list = images ?? [];
  return (
    hasHttpImages(list) ||
    hasFailedUploadDupes(list, failedUploadKeys) ||
    hasLoadFailedImages(list, failedLoadKeys)
  );
}

function showPublishFailedImagesWarning(): void {
  Modal.warning({
    title: "Còn ảnh lỗi trước khi Publish",
    content: (
      <p style={{ marginBottom: 0 }}>
        Gallery còn ảnh lỗi (ô đỏ, dấu !): URL dùng{" "}
        <strong>http://</strong> (4Seller báo Invalid Para), ảnh trùng, hoặc
        không tải được. Đổi sang <strong>https://</strong> hoặc xóa tại{" "}
        <strong>Variant Image</strong> / gallery Default, rồi Publish lại.
      </p>
    ),
    okText: "Đã hiểu",
  });
}

function failedListingImageTitle(
  httpUrl: boolean,
  uploadFailed: boolean,
  loadFailed: boolean,
  fallback: string,
): string {
  if (httpUrl) {
    return "URL dùng http:// — 4Seller từ chối (Invalid Para). Đổi sang https:// hoặc xóa ảnh";
  }
  if (uploadFailed) return "Ảnh trùng trong cùng nhóm — xóa bản thừa";
  if (loadFailed) return "Ảnh không tải được — xóa hoặc thay ảnh này";
  return fallback;
}

/**
 * 4Seller rejects publish when a variant color gallery holds the same image twice
 * (【Publish Failed】product image upload fail …). Drop later duplicates within each color
 * group, matching by content-id / file name so CDN size suffixes don't hide a dupe.
 */
function dedupeVariantColorImages(list: ListingImageRow[]): ListingImageRow[] {
  const seenByColor = new Map<string, Set<string>>();
  const kept: ListingImageRow[] = [];
  let removed = 0;
  for (const img of list) {
    if (isVariantColorImage(img)) {
      const color = String(img.colorValue ?? "")
        .trim()
        .toLowerCase();
      const identity = imageIdentityKey(img.url);
      if (identity) {
        let seen = seenByColor.get(color);
        if (!seen) {
          seen = new Set<string>();
          seenByColor.set(color, seen);
        }
        if (seen.has(identity)) {
          removed += 1;
          continue;
        }
        seen.add(identity);
      }
    }
    kept.push(img);
  }
  return removed > 0 ? syncListingImagesOrder(kept) : list;
}

/**
 * Isolate `Form.useWatch("images")` so image edits (delete/reorder) only re-render
 * the media subtree — not Variations table / Product Info / Specifics (~full page).
 * Supports optimistic list for instant tile removal before form commit.
 */
function ImagesWatchIsolator({
  failedLoadKeysRef,
  children,
}: {
  failedLoadKeysRef: MutableRefObject<Set<string>>;
  children: (ctx: {
    images: ListingImageRow[];
    watchedImages: ListingImageRow[];
    setOptimisticImages: Dispatch<SetStateAction<ListingImageRow[] | null>>;
    sizeChartUrl: string;
    videoUrl: string;
    failedLoadKeys: Set<string>;
    markImageLoadFailed: (url?: string | null) => void;
    markImageLoadOk: (url?: string | null) => void;
  }) => ReactNode;
}) {
  const form = Form.useFormInstance();
  const watchedImages = (Form.useWatch("images", form) ??
    []) as ListingImageRow[];
  const [optimisticImages, setOptimisticImages] = useState<
    ListingImageRow[] | null
  >(null);
  const [failedLoadKeys, setFailedLoadKeys] = useState<Set<string>>(
    () => new Set(),
  );
  const images = optimisticImages ?? watchedImages;
  failedLoadKeysRef.current = failedLoadKeys;

  useEffect(() => {
    setOptimisticImages(null);
  }, [watchedImages]);

  const markImageLoadFailed = (url?: string | null) => {
    const key = imageLoadFailKey(url);
    if (!key) return;
    setFailedLoadKeys((prev) => {
      if (prev.has(key)) return prev;
      const next = new Set(prev);
      next.add(key);
      return next;
    });
  };

  const markImageLoadOk = (url?: string | null) => {
    const key = imageLoadFailKey(url);
    if (!key) return;
    setFailedLoadKeys((prev) => {
      if (!prev.has(key)) return prev;
      const next = new Set(prev);
      next.delete(key);
      return next;
    });
  };

  const sizeChartUrl = useMemo(() => {
    const hit = images.find(
      (img) => img?.mediaType === "SizeChart" && String(img.url ?? "").trim(),
    );
    return hit?.url?.trim() || "";
  }, [images]);

  const videoUrl = useMemo(() => {
    const hit = images.find(
      (img) => img?.mediaType === "Video" && String(img.url ?? "").trim(),
    );
    return hit?.url?.trim() || "";
  }, [images]);

  return (
    <>
      {children({
        images,
        watchedImages,
        setOptimisticImages,
        sizeChartUrl,
        videoUrl,
        failedLoadKeys,
        markImageLoadFailed,
        markImageLoadOk,
      })}
    </>
  );
}

function FailedUploadImageAutofocus({
  mediaReady,
  syncError,
  failedKeys,
  images,
  onFocus,
  focusedRef,
}: {
  mediaReady: boolean;
  syncError?: string | null;
  failedKeys: Set<string>;
  images: ListingImageRow[];
  onFocus: () => boolean;
  focusedRef: MutableRefObject<string | null>;
}) {
  const onFocusRef = useRef(onFocus);
  onFocusRef.current = onFocus;

  useEffect(() => {
    if (!mediaReady) return;
    if (hasHttpImages(images)) {
      const key = "http-url";
      if (focusedRef.current !== key && onFocusRef.current()) {
        focusedRef.current = key;
      }
      return;
    }
    const err = syncError?.trim() ?? "";
    if (!err || failedKeys.size === 0) return;
    if (focusedRef.current === err) return;
    // Only auto-focus while local duplicates of the failed identity remain.
    if (!hasFailedUploadDupes(images, failedKeys)) return;
    if (onFocusRef.current()) {
      focusedRef.current = err;
    }
  }, [mediaReady, syncError, failedKeys, images, focusedRef]);

  return null;
}

/** Keeps status-card Alert in sync without Form.useWatch("images") on the full page. */
function FailedUploadDupeStatusSync({
  images,
  failedKeys,
  onChange,
}: {
  images: ListingImageRow[];
  failedKeys: Set<string>;
  onChange: (dupesRemain: boolean) => void;
}) {
  useEffect(() => {
    if (failedKeys.size === 0) {
      onChange(false);
      return;
    }
    onChange(hasFailedUploadDupes(images, failedKeys));
  }, [images, failedKeys, onChange]);
  return null;
}

type VariantPhotoDragScope = "__main__" | string;

type VariantPhotoDragPos = {
  scope: VariantPhotoDragScope;
  index: number;
};

/** Avoid re-render storms: native `dragover` fires every frame while hovering. */
function sameVariantPhotoDragPos(
  a: VariantPhotoDragPos | null,
  b: VariantPhotoDragPos | null,
): boolean {
  if (a === b) return true;
  if (!a || !b) return false;
  return a.scope === b.scope && a.index === b.index;
}

/**
 * Live DnD slot shift: dragged item slides to `to`, siblings between `from`/`to`
 * slide the other way so a gap opens at the insert point.
 */
function dragReorderSlotShift(
  from: number,
  to: number,
  index: number,
): number {
  if (from < 0 || to < 0 || from === to) return 0;
  if (index === from) return to - from;
  if (from < to) {
    if (index > from && index <= to) return -1;
  } else if (index >= to && index < from) {
    return 1;
  }
  return 0;
}

/** Horizontal/vertical translate using self size + gap (wrap-safe enough for ≤9 tiles). */
function dragReorderTransform(
  shift: number,
  axis: "x" | "y",
  gapPx: number,
): string | undefined {
  if (shift === 0) return undefined;
  const translate = axis === "x" ? "translateX" : "translateY";
  return `${translate}(calc(${shift} * (100% + ${gapPx}px)))`;
}

/** Snapshot layout boxes before live transforms — hit-testing against shifted tiles oscillates. */
function snapshotDragReorderRects(
  container: HTMLElement,
  itemSelector: string,
): DOMRect[] {
  return Array.from(container.querySelectorAll(itemSelector)).map((el) =>
    (el as HTMLElement).getBoundingClientRect(),
  );
}

/**
 * Map pointer to a slot using pre-drag rects (ignores CSS transforms applied during live sort).
 * Picks the closest slot center so gaps / partial overlaps stay stable.
 */
function dragReorderIndexFromRects(
  clientX: number,
  clientY: number,
  rects: DOMRect[],
): number | null {
  if (rects.length === 0) return null;
  let best = 0;
  let bestDist = Number.POSITIVE_INFINITY;
  for (let i = 0; i < rects.length; i++) {
    const r = rects[i]!;
    const cx = r.left + r.width / 2;
    const cy = r.top + r.height / 2;
    const dist = (clientX - cx) ** 2 + (clientY - cy) ** 2;
    if (dist < bestDist) {
      bestDist = dist;
      best = i;
    }
  }
  return best;
}

export type ListingEditPageProps = {
  config: ListingsPortalConfig;
};

export const ListingEditPage = ({ config }: ListingEditPageProps) => {
  const deps = useListingsDeps();
  const { CategoryCascader } = deps;
  const { listingId = "" } = useParams();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const canManage = deps.hasPermission(config.managePermission);
  const isMobile = useIsMobile();
  const { data: apiListing, isLoading } = deps.useListingDetail(listingId);
  const saveDraftListing = deps.useSaveDraftListing();
  const publishListing = deps.usePublishListing();
  const updateListing = deps.useUpdateListing();
  const activateListing = deps.useActivateListing();
  const deactivateListing = deps.useDeactivateListing();
  const duplicateListing = deps.useDuplicateListing();
  const { data: shops = [] } = deps.useShopOptions();
  const [form] = Form.useForm<ListingFormValues>();
  const [saving, setSaving] = useState(false);
  const [refreshingRemote, setRefreshingRemote] = useState(false);
  const [categorySite, setCategorySite] = useState("US");
  const [categoryPath, setCategoryPath] = useState<string[] | undefined>();
  /** Baseline form before background 4Seller refresh (three-way merge). */
  const baselineFormRef = useRef<ListingFormValues | null>(null);
  const hydratedListingIdRef = useRef<string | null>(null);
  const remoteRefreshDoneRef = useRef<string | null>(null);
  const syncInFlightRef = useRef(false);
  const syncGenerationRef = useRef(0);
  /** listingId that finished local hydrate — triggers auto-sync without listing object identity. */
  const [hydratedForId, setHydratedForId] = useState<string | null>(null);

  const [selectedColors, setSelectedColors] = useState<string[]>([]);
  const [selectedSizes, setSelectedSizes] = useState<string[]>([]);
  const [selectedItems, setSelectedItems] = useState<string[]>([]);
  /** Latest specs for rebuild — avoids stale closure when tick Color then Size quickly. */
  const selectedColorsRef = useRef<string[]>([]);
  const selectedSizesRef = useRef<string[]>([]);
  const selectedItemsRef = useRef<string[]>([]);
  selectedColorsRef.current = selectedColors;
  selectedSizesRef.current = selectedSizes;
  selectedItemsRef.current = selectedItems;
  const [activeSpecs, setActiveSpecs] = useState<Set<VariationSpecKey>>(
    new Set(),
  );
  const [customColor, setCustomColor] = useState("");
  const [customSize, setCustomSize] = useState("");
  const [customItem, setCustomItem] = useState("");
  const [addingSpec, setAddingSpec] = useState<VariationSpecKey | null>(null);
  const [colorOptions, setColorOptions] = useState<string[]>(COLOR_PRESETS);
  const [sizeOptions, setSizeOptions] = useState<string[]>(SIZE_PRESETS);
  const [itemOptions, setItemOptions] = useState<string[]>(
    NUMBER_OF_ITEMS_PRESETS,
  );

  const [batchSkuModalOpen, setBatchSkuModalOpen] = useState(false);
  const [batchPricingModalOpen, setBatchPricingModalOpen] = useState(false);
  const [pricingFormula, setPricingFormula] = useState<PricingFormulaInput>(
    DEFAULT_PRICING_FORMULA,
  );
  const [selectedPricingTemplateId, setSelectedPricingTemplateId] = useState<
    string | undefined
  >();
  const [pricingFormulaApplied, setPricingFormulaApplied] = useState(false);
  const pricingFormulaInitializedRef = useRef(false);
  const [batchModal, setBatchModal] = useState<{
    field: "sellerSku" | "quantity" | "salePrice" | "costPrice";
    title: string;
  } | null>(null);
  const [batchDraftNumber, setBatchDraftNumber] = useState<number | null>(null);

  const [activeImageIndex, setActiveImageIndex] = useState(0);
  const [galleryColor, setGalleryColor] = useState<string | null>(null);
  const [previewSize, setPreviewSize] = useState<string | null>(null);
  const [previewItem, setPreviewItem] = useState<string | null>(null);
  const [addingMedia, setAddingMedia] = useState<"product" | null>(null);
  const [uploadingExtraMedia, setUploadingExtraMedia] = useState<
    "sizeChart" | "video" | null
  >(null);
  const [generatingSizeChart, setGeneratingSizeChart] = useState(false);
  const [applyingCapturedSizeChart, setApplyingCapturedSizeChart] =
    useState(false);
  const sizeChartInputRef = useRef<HTMLInputElement>(null);
  const videoInputRef = useRef<HTMLInputElement>(null);
  const [addingVariantColor, setAddingVariantColor] = useState<string | null>(
    null,
  );
  const [variantImageOn, setVariantImageOn] = useState(false);
  const [dragImageIndex, setDragImageIndex] = useState<number | null>(null);
  const [dragOverImageIndex, setDragOverImageIndex] = useState<number | null>(
    null,
  );
  const [variantPhotoDrag, setVariantPhotoDrag] =
    useState<VariantPhotoDragPos | null>(null);
  const [variantPhotoDragOver, setVariantPhotoDragOver] =
    useState<VariantPhotoDragPos | null>(null);
  /** Only commit when scope/index change — `dragover` otherwise re-renders whole page ~60fps. */
  const setVariantPhotoDragOverSafe = (
    next: VariantPhotoDragPos | null,
  ) => {
    setVariantPhotoDragOver((prev) =>
      sameVariantPhotoDragPos(prev, next) ? prev : next,
    );
  };
  /** Pre-transform tile boxes for stable hit-testing while live-sorting. */
  const galleryDragRectsRef = useRef<DOMRect[]>([]);
  const dragHitRectsRef = useRef<{
    container: HTMLElement | null;
    rects: DOMRect[];
  }>({ container: null, rects: [] });
  const colorBlockDragRectsRef = useRef<{
    container: HTMLElement | null;
    rects: DOMRect[];
  }>({ container: null, rects: [] });
  const ensureDragHitRects = (
    container: HTMLElement,
    itemSelector: string,
  ): DOMRect[] => {
    if (dragHitRectsRef.current.container !== container) {
      dragHitRectsRef.current = {
        container,
        rects: snapshotDragReorderRects(container, itemSelector),
      };
    }
    return dragHitRectsRef.current.rects;
  };
  const clearDragHitRects = () => {
    dragHitRectsRef.current = { container: null, rects: [] };
  };
  const [colorBlockDragIndex, setColorBlockDragIndex] = useState<number | null>(
    null,
  );
  const [colorBlockDragOverIndex, setColorBlockDragOverIndex] = useState<
    number | null
  >(null);
  /** Color-block handle hovering over Default (mặc định) drop zone. */
  const [colorBlockDragOverDefault, setColorBlockDragOverDefault] =
    useState(false);
  const [variantPreviewOpen, setVariantPreviewOpen] = useState(false);
  const [variantPreviewUrls, setVariantPreviewUrls] = useState<string[]>([]);
  const [variantPreviewIndex, setVariantPreviewIndex] = useState(0);
  const skipVariantPhotoClickRef = useRef(false);
  /** Avoid re-focusing the same syncError after user navigates gallery. */
  const focusedFailedSyncErrorRef = useRef<string | null>(null);
  /** URLs whose <img> fired onError — Isolator writes here so Publish can read without extra page render. */
  const failedLoadImageKeysRef = useRef<Set<string>>(new Set());
  const prevSyncErrorForDupesRef = useRef<string | null | undefined>(undefined);
  /** Local form still has ≥2 copies of a syncError-matched image in one scope. */
  const [failedUploadDupesRemain, setFailedUploadDupesRemain] = useState(false);
  /** Became true once we observed local dupes for this syncError (gates “resolved” Alert). */
  const [sawFailedUploadDupes, setSawFailedUploadDupes] = useState(false);
  const onFailedUploadDupesRemainChange = useMemo(
    () => (dupesRemain: boolean) => {
      if (dupesRemain) {
        setSawFailedUploadDupes(true);
      }
      setFailedUploadDupesRemain((prev) =>
        prev === dupesRemain ? prev : dupesRemain,
      );
    },
    [],
  );
  const [galleryPreviewOpen, setGalleryPreviewOpen] = useState(false);
  const [galleryPreviewIndex, setGalleryPreviewIndex] = useState(0);
  const [videoPreviewOpen, setVideoPreviewOpen] = useState(false);
  const [variationsTableExpanded, setVariationsTableExpanded] = useState(false);
  const [specificsListExpanded, setSpecificsListExpanded] = useState(false);
  const [shippingTemplateModalOpen, setShippingTemplateModalOpen] =
    useState(false);
  const [selectedShippingTemplateId, setSelectedShippingTemplateId] = useState<
    string | undefined
  >();
  const [shopPickerOpen, setShopPickerOpen] = useState(false);
  const [shopSearch, setShopSearch] = useState("");
  const shopMetaAppliedRef = useRef<string | null>(null);

  const [hazardous, setHazardous] = useState("No");
  const [prop65Repro, setProp65Repro] = useState("No");
  const [prop65Carcinogen, setProp65Carcinogen] = useState("No");
  /** Previous descriptionHtml before last AI rewrite — enables Hoàn tác. */
  const [rewriteUndoHtml, setRewriteUndoHtml] = useState<string | null>(null);
  /** Previous productName before last AI rewrite — restored by the same Hoàn tác. */
  const [rewriteUndoTitle, setRewriteUndoTitle] = useState<string | null>(null);
  /** Previous searchTerms before last AI rewrite — restored by the same Hoàn tác. */
  const [rewriteUndoSearchTerms, setRewriteUndoSearchTerms] = useState<
    string[] | null
  >(null);
  /** Previous productHighlights before last AI rewrite — restored by the same Hoàn tác. */
  const [rewriteUndoProductHighlights, setRewriteUndoProductHighlights] =
    useState<string[] | null>(null);
  /** Publish gate: must Rewrite at least once. Insert Images is optional. */
  const [descriptionRewritten, setDescriptionRewritten] = useState(false);
  const [descriptionImagesInserted, setDescriptionImagesInserted] =
    useState(false);
  const [mapSpecificsOpen, setMapSpecificsOpen] = useState(false);
  const [mapSpecificsResult, setMapSpecificsResult] =
    useState<MapListingSpecificsResult | null>(null);
  const [selectedMapKeys, setSelectedMapKeys] = useState<string[]>([]);
  const [autoSpecificsRunning, setAutoSpecificsRunning] = useState(false);
  const autoSpecificsDoneRef = useRef<string | null>(null);
  const autoShippingDoneRef = useRef<string | null>(null);
  const autoPricingDoneRef = useRef<string | null>(null);
  /** Bumps when background 4Seller sync finishes so auto-Specifics can start. */
  const [remoteSyncGeneration, setRemoteSyncGeneration] = useState(0);

  const recommendCategory = deps.useRecommendCategory();
  const rewriteDescription = deps.useRewriteListingDescription();
  const saveDescriptionPrep = deps.useSaveListingDescriptionPrep();
  const rewriteConfigQuery = useQuery({
    queryKey: ["listingRewriteConfig"],
    queryFn: async () => {
      const res = await deps.listingApi.getRewriteConfig();
      if (!res.success || !res.data) {
        throw new Error(res.message || "Không tải được cấu hình Rewrite.");
      }
      return res.data;
    },
    staleTime: 30_000,
    refetchOnMount: "always",
    retry: 2,
  });
  const rewriteAllowRepeat = Boolean(rewriteConfigQuery.data?.allowRepeat);
  const specificsAllowRepeat = Boolean(
    rewriteConfigQuery.data?.allowSpecificsRepeat,
  );
  const autoGenerateSizeChart = Boolean(
    rewriteConfigQuery.data?.autoGenerateSizeChart,
  );
  const rewriteTitleMode: ListingRewriteTitleMode | null = useMemo(() => {
    const raw = rewriteConfigQuery.data?.titleMode;
    return raw == null ? null : parseListingRewriteTitleMode(raw);
  }, [rewriteConfigQuery.data?.titleMode]);
  const rewriteLocked = descriptionRewritten && !rewriteAllowRepeat;
  const rewriteTitleHint = listingRewriteTitleTooltip(
    rewriteTitleMode,
    descriptionRewritten,
    rewriteAllowRepeat,
  );
  const mapSpecifics = deps.useMapListingSpecifics(listingId);
  const { data: sourceOptions = [] } = deps.useProductSourceOptions(true);
  const { data: shippingTemplates = [] } = deps.useShippingTemplates(true);
  const { data: pricingTemplates = [] } = deps.usePricingTemplates(true);
  const listing = apiListing ?? null;
  const hasSizeGuide = Boolean(
    listing?.sizeGuideJson?.chartRows?.length ||
      listing?.sizeGuideJson?.rows?.length,
  );
  const isPublished = listing?.pageType === "published";
  const siteStatus = listing?.siteStatus?.toLowerCase();
  const isActive = siteStatus === "active";
  const isInactive = siteStatus === "inactive";
  /** Active/Inactive listings are already bound to a shop — do not allow reassignment. */
  const shopChangeLocked = isActive || isInactive;
  const statusDisplay = listing?.siteStatus || listing?.pageType || "";
  const syncErrorDisplay = listing?.syncError
    ? formatSyncErrorDisplay(listing.syncError)
    : null;
  const syncMissingSpecifics = useMemo(
    () => parseSyncErrorMissingSpecifics(listing?.syncError),
    [listing?.syncError],
  );
  /** Synced 4Seller listings: only show actual Color/Size values — no presets or custom add. */
  const variationSpecsFromFourSeller = Boolean(listing?.fourSellerListingId);

  // Restore applied pricing: localStorage (instant) → overwrite from DB when detail arrives.
  useEffect(() => {
    if (!listingId) return;
    pricingFormulaInitializedRef.current = false;
    focusedFailedSyncErrorRef.current = null;
    prevSyncErrorForDupesRef.current = undefined;
    setFailedUploadDupesRemain(false);
    setSawFailedUploadDupes(false);
    setPricingFormulaApplied(false);
    setPricingFormula(DEFAULT_PRICING_FORMULA);
    setSelectedPricingTemplateId(undefined);

    const saved = loadListingAppliedPricing(listingId);
    if (saved) {
      setPricingFormula(saved.formula);
      setSelectedPricingTemplateId(saved.templateId);
      setPricingFormulaApplied(true);
      pricingFormulaInitializedRef.current = true;
    }
  }, [listingId]);

  // New publish failure from server → re-arm duplicate highlight / Alert.
  // Skip the first observation so hydrate auto-dedupe can mark “resolved”.
  useEffect(() => {
    const err = listing?.syncError ?? null;
    if (prevSyncErrorForDupesRef.current === undefined) {
      prevSyncErrorForDupesRef.current = err;
      return;
    }
    if (prevSyncErrorForDupesRef.current === err) return;
    prevSyncErrorForDupesRef.current = err;
    setFailedUploadDupesRemain(false);
    setSawFailedUploadDupes(false);
    focusedFailedSyncErrorRef.current = null;
  }, [listing?.syncError]);

  // Description prep checklist — source of truth is DB (survives refresh / Sync / multi-device).
  // Only hydrate when the listing row first binds; Sync updates flags explicitly in syncFromRemote.
  useEffect(() => {
    if (!listingId || !listing) return;
    setDescriptionRewritten(Boolean(listing.descriptionRewritten));
    setDescriptionImagesInserted(Boolean(listing.descriptionImagesInserted));
  }, [listingId, listing?.id]);

  useEffect(() => {
    if (!listingId || !listing?.appliedPricing) return;
    const snap = snapshotFromAppliedPricing(listingId, listing.appliedPricing);
    if (!snap) return;
    setPricingFormula(snap.formula);
    setSelectedPricingTemplateId(snap.templateId);
    setPricingFormulaApplied(true);
    pricingFormulaInitializedRef.current = true;
    saveListingAppliedPricing(listingId, snap.formula, snap.templateId);
    // Depend on appliedAt so React Query identity churn does not re-run hydrate.
  }, [listingId, listing?.appliedPricing?.appliedAt, listing?.appliedPricing?.templateId]);

  useEffect(() => {
    if (
      !listingId ||
      pricingFormulaInitializedRef.current ||
      pricingTemplates.length === 0
    )
      return;
    const defaults = getDefaultPricingTemplate(pricingTemplates);
    if (!defaults) return;
    setPricingFormula(templateToFormulaInput(defaults));
    setSelectedPricingTemplateId(defaults.id);
    pricingFormulaInitializedRef.current = true;
  }, [listingId, pricingTemplates]);

  const variations = Form.useWatch("variations", form) ?? [];
  /** Mount Variations / gallery after first paint so Product Info paints immediately. */
  const mediaReady = useDeferredMount(!!listingId, 0);
  const watchedAttributes =
    (Form.useWatch("attributes", form) as ListingAttributeRow[] | undefined) ??
    [];
  // Lock after Specifics are filled (beyond Brand/cert) unless Settings allow repeat.
  const autoSpecificsDisabled =
    !needsAutoMapSpecifics(watchedAttributes) && !specificsAllowRepeat;
  const productType = Form.useWatch("productType", form) ?? "MultiSku";
  const productName = Form.useWatch("productName", form) ?? "";
  const deferredProductName = useDeferredValue(productName);
  const listingSourceUrl = Form.useWatch("listingSourceUrl", form) ?? "";
  const watchedShopId = Form.useWatch("shopId", form) as string | undefined;
  const watchedWarehouseId = Form.useWatch("warehouseId", form) as
    string | undefined;

  const failedUploadImageKeys = useMemo(
    () =>
      buildFailedUploadImageKeys(
        extractImageUrlsFromSyncError(listing?.syncError),
      ),
    [listing?.syncError],
  );

  const isFailedUploadImage = (
    url: string | undefined | null,
    images: ListingImageRow[],
    scope: FailedUploadImageScope,
  ) => isActiveFailedUploadImage(url, failedUploadImageKeys, images, scope);

  const focusFailedUploadImage = (targetUrl?: string) => {
    const loadKeys = failedLoadImageKeysRef.current;
    const list =
      (form.getFieldValue("images") as ListingImageRow[] | undefined) ?? [];
    const preferHttp = list.find((img) => isHttpImageUrl(img.url));
    if (
      failedUploadImageKeys.size === 0 &&
      loadKeys.size === 0 &&
      !targetUrl?.trim() &&
      !preferHttp
    ) {
      return false;
    }
    const keys = targetUrl?.trim()
      ? buildFailedUploadImageKeys([targetUrl.trim()])
      : failedUploadImageKeys;
    const urlLooksFailed = (url: string | undefined | null) =>
      isHttpImageUrl(url) ||
      isFailedUploadImageUrl(url, keys) ||
      isLoadFailedImage(url, loadKeys);

    const preferDupe = list.find((img) => {
      const url = String(img.url ?? "").trim();
      if (!url || !urlLooksFailed(url)) return false;
      const id = imageIdentityKey(url);
      if (!id) return true;
      if (isVariantColorImage(img)) {
        return (
          countIdentityInScope(list, id, {
            kind: "color",
            color: String(img.colorValue ?? ""),
          }) >= 2
        );
      }
      if (isSharedProductImage(img)) {
        return countIdentityInScope(list, id, { kind: "main" }) >= 2;
      }
      return false;
    });
    const hit =
      preferHttp ??
      preferDupe ??
      list.find(
        (img) =>
          String(img.url ?? "").trim() && urlLooksFailed(img.url),
      );
    if (!hit?.url) return false;

    if (isVariantColorImage(hit)) {
      const color = String(hit.colorValue ?? "").trim();
      const colorImgs = list.filter(
        (img) =>
          isVariantColorImage(img, color) && String(img.url ?? "").trim(),
      );
      const idx = colorImgs.findIndex((img) => urlLooksFailed(img.url));
      setVariantImageOn(true);
      setGalleryColor(color || null);
      if (idx >= 0) setActiveImageIndex(idx);
    } else if (hit.mediaType === "SizeChart") {
      setGalleryColor(null);
    } else {
      const products = list.filter(
        (img) =>
          isSharedProductImage(img) && String(img.url ?? "").trim(),
      );
      const idx = products.findIndex((img) => urlLooksFailed(img.url));
      setGalleryColor(null);
      if (idx >= 0) setActiveImageIndex(idx);
    }

    queueMicrotask(() => {
      window.setTimeout(() => {
        document
          .querySelector<HTMLElement>(
            ".fs-gallery-thumb.is-upload-failed, .fs-photo-tile.is-upload-failed",
          )
          ?.scrollIntoView({ behavior: "smooth", block: "center" });
      }, 80);
    });
    return true;
  };

  // Start shop-metadata as soon as listing.shopId is known — do not wait for form hydrate.
  const metadataShopId = watchedShopId || listing?.shopId || undefined;

  const {
    data: shopMetadata,
    isFetching: shopMetadataLoading,
    isError: shopMetadataError,
    error: shopMetadataErr,
    refetch: refetchShopMetadata,
  } = deps.useListingShopMetadata(listingId, metadataShopId);

  // After shop metadata loads: pick default warehouse when current is missing / invalid for shop.
  useEffect(() => {
    if (!metadataShopId || !shopMetadata) return;
    if (shopMetadata.shopId !== metadataShopId) return;

    const warehouses = shopMetadata.warehouses ?? [];
    const currentWarehouse = String(
      form.getFieldValue("warehouseId") ?? "",
    ).trim();
    const stillValid = warehouses.some(
      (w) => w.warehouseId === currentWarehouse,
    );
    const appliedKey = `${metadataShopId}:${shopMetadata.defaultWarehouseId ?? ""}:${warehouses.length}`;

    if (stillValid && shopMetaAppliedRef.current === appliedKey) return;

    if (!stillValid) {
      const nextWarehouse =
        shopMetadata.defaultWarehouseId ||
        warehouses.find((w) => w.isDefault)?.warehouseId ||
        warehouses.find(
          (w) =>
            w.warehouseEffectStatus == null || w.warehouseEffectStatus === 1,
        )?.warehouseId ||
        warehouses[0]?.warehouseId;
      form.setFieldsValue({ warehouseId: nextWarehouse || undefined });
    }

    shopMetaAppliedRef.current = appliedKey;
  }, [metadataShopId, shopMetadata, form]);

  const shopMetadataReady =
    !!metadataShopId &&
    !!shopMetadata &&
    shopMetadata.shopId === metadataShopId &&
    !shopMetadataLoading;

  const categorySiteOptions = useMemo(
    () => SHOP_REGIONS.map((r) => ({ label: r.label, value: r.value })),
    [],
  );

  const publishShopOptions = useMemo(() => {
    const ownerId = listing?.ownerId;
    const selectedId = watchedShopId || listing?.shopId || undefined;
    const shopSelectLabel = (shop: (typeof shops)[number]) => {
      const profile = shop.profileName?.trim();
      const shopName = shop.name?.trim() || "Shop";
      return profile ? `${profile} (${shopName})` : shopName;
    };
    const toShopOption = (shop: (typeof shops)[number]) => ({
      value: shop.id,
      label: shopSelectLabel(shop),
      disabled: !shop.fourSellerShopId,
      title: shop.fourSellerShopId ? undefined : "Shop chưa kết nối 4Seller",
    });
    const mapped = shops
      .filter((s) => s.status === "Active")
      .filter((s) => !ownerId || s.ownerId === ownerId)
      .filter((s) => {
        if (!listing?.platform) return true;
        return (
          (s.platformCode || "").toLowerCase() ===
          listing.platform.toLowerCase()
        );
      })
      .map(toShopOption);

    // Keep listing's current shop visible even if filtered (Inactive / platform mismatch).
    if (selectedId && !mapped.some((o) => o.value === selectedId)) {
      const shop = shops.find((s) => s.id === selectedId);
      if (shop) {
        mapped.unshift(toShopOption(shop));
      }
    }

    return mapped;
  }, [
    shops,
    listing?.ownerId,
    listing?.platform,
    listing?.shopId,
    watchedShopId,
  ]);

  const filteredPublishShopOptions = useMemo(() => {
    const q = shopSearch.trim().toLowerCase();
    if (!q) return publishShopOptions;
    return publishShopOptions.filter((o) =>
      String(o.label).toLowerCase().includes(q),
    );
  }, [publishShopOptions, shopSearch]);

  const selectedPublishShop = useMemo(
    () => shops.find((s) => s.id === watchedShopId),
    [shops, watchedShopId],
  );

  const derivedCategorySite = useMemo(() => {
    if (!listing) return "US";
    const shop = shops.find((s) => s.id === (watchedShopId || listing.shopId));
    return shop?.region || listing.siteId || listing.site || "US";
  }, [listing, shops, watchedShopId]);

  const initialValues = useMemo((): ListingFormValues | null => {
    if (!listing) return null;
    return mirrorListingToForm(listing);
  }, [listing]);

  const previewColor =
    galleryColor && selectedColors.includes(galleryColor)
      ? galleryColor
      : (selectedColors[0] ?? null);
  const previewSizeActive =
    previewSize && selectedSizes.includes(previewSize)
      ? previewSize
      : (selectedSizes[0] ?? null);
  const previewItemActive =
    previewItem && selectedItems.includes(previewItem)
      ? previewItem
      : (selectedItems[0] ?? null);

  const previewVariations = useMemo(() => {
    const rows = variations as ListingVariationRow[];
    if (rows.length === 0) return rows;
    const color = selectedColors.length > 0 ? previewColor : null;
    const size = selectedSizes.length > 0 ? previewSizeActive : null;
    const item = selectedItems.length > 0 ? previewItemActive : null;
    if (!color && !size && !item) return rows;
    const matched = rows.filter((v) =>
      variationMatchesPreview(v, color, size, item),
    );
    return matched.length > 0 ? matched : rows;
  }, [
    variations,
    selectedColors.length,
    selectedSizes.length,
    selectedItems.length,
    previewColor,
    previewSizeActive,
    previewItemActive,
  ]);

  const previewPriceLabel = useMemo(() => {
    const prices = previewVariations
      .map((v) => v.salePrice)
      .filter(
        (p): p is number =>
          p != null && !Number.isNaN(Number(p)) && Number(p) > 0,
      )
      .map(Number);
    return formatPreviewPriceRange(prices);
  }, [previewVariations]);

  const previewDiscountPriceLabel = useMemo(() => {
    const promoPercent = resolvePromoPercent(
      pricingFormula.discountPercent,
      pricingFormula.fastSalePercent,
    );
    if (!pricingFormulaApplied || promoPercent <= 0) return null;

    const discountedPrices = previewVariations
      .map((v) => v.salePrice)
      .filter(
        (p): p is number =>
          p != null && !Number.isNaN(Number(p)) && Number(p) > 0,
      )
      .map((price) => roundMoney(Number(price) * (1 - promoPercent / 100)));

    if (discountedPrices.length === 0) return null;
    return formatPreviewPriceRange(discountedPrices);
  }, [previewVariations, pricingFormula, pricingFormulaApplied]);

  const previewPromoLabel = useMemo(() => {
    if (!pricingFormulaApplied) return null;
    const promoPercent = resolvePromoPercent(
      pricingFormula.discountPercent,
      pricingFormula.fastSalePercent,
    );
    if (promoPercent <= 0) return null;
    const normalized = Number.isInteger(promoPercent)
      ? String(promoPercent)
      : promoPercent.toFixed(2).replace(/\.?0+$/, "");
    return `-${normalized}%`;
  }, [pricingFormula, pricingFormulaApplied]);

  const previewProfitLabel = useMemo(() => {
    if (!pricingFormulaApplied) return null;
    const profits = collectVariationProfits(previewVariations, pricingFormula);
    return formatProfitLabel(profits);
  }, [previewVariations, pricingFormula, pricingFormulaApplied]);

  const previewSkuLabel = useMemo(() => {
    const first = previewVariations.find((v) => v.sellerSku?.trim());
    return first?.sellerSku?.trim() || "—";
  }, [previewVariations]);

  const isProductMediaType = (
    mediaType?: string,
    colorValue?: string,
    listingSkuId?: string,
  ) =>
    !colorValue?.trim() &&
    !listingSkuId &&
    (!mediaType || mediaType === "Main" || mediaType === "Detail");

  const pruneColorImages = (colors: string[]) => {
    const list = [
      ...((form.getFieldValue("images") as ListingImageRow[]) ?? []),
    ];
    const colorSet = new Set(colors.map((c) => c.trim().toLowerCase()));
    const next = list.filter((img) => {
      const c = img.colorValue?.trim();
      return !c || colorSet.has(c.toLowerCase());
    });
    if (next.length !== list.length) {
      form.setFieldValue(
        "images",
        next.length > 0 ? next : [{ mediaType: "Main", url: "", sortOrder: 0 }],
      );
    }
  };

  const upsertTypedMedia = (
    mediaType: "SizeChart" | "Video",
    url: string,
    materialId?: string | number | null,
  ) => {
    const list = [
      ...((form.getFieldValue("images") as ListingImageRow[]) ?? []),
    ];
    const idx = list.findIndex(
      (img) => img.mediaType === mediaType && !img.colorValue,
    );
    if (idx >= 0) {
      list[idx] = { ...list[idx], url, mediaType, sortOrder: idx };
    } else {
      list.push({ mediaType, url, sortOrder: list.length });
    }
    form.setFieldValue("images", list);
    const idStr =
      materialId == null || materialId === "" ? "" : String(materialId);
    if (mediaType === "SizeChart" && idStr) {
      form.setFieldValue("sizeChartId", idStr);
    }
    if (mediaType === "Video" && idStr) {
      form.setFieldValue("erpVideoId", idStr);
    }
  };

  const removeTypedMedia = (mediaType: "SizeChart" | "Video") => {
    if (mediaType === "Video") setVideoPreviewOpen(false);
    if (mediaType === "SizeChart") form.setFieldValue("sizeChartId", "");
    if (mediaType === "Video") form.setFieldValue("erpVideoId", "");
    const list = (
      (form.getFieldValue("images") as ListingImageRow[]) ?? []
    ).filter((img) => img.mediaType !== mediaType);
    form.setFieldValue(
      "images",
      list.length > 0 ? list : [{ mediaType: "Main", url: "", sortOrder: 0 }],
    );
  };

  const uploadExtraMediaToFourSeller = async (
    kind: "sizeChart" | "video",
    file: File,
  ) => {
    if (!listingId) {
      message.error("Thiếu listing id.");
      return;
    }
    const isVideo = kind === "video";
    const maxMb = isVideo ? 10 : 5;
    if (file.size > maxMb * 1024 * 1024) {
      message.error(`${file.name}: tối đa ${maxMb} MB`);
      return;
    }
    if (isVideo && !file.type.startsWith("video/")) {
      message.error(`${file.name}: chỉ hỗ trợ file video`);
      return;
    }
    if (!isVideo && !file.type.startsWith("image/")) {
      message.error(`${file.name}: chỉ hỗ trợ file ảnh PNG/JPG`);
      return;
    }

    setUploadingExtraMedia(kind);
    try {
      const referenceType = (listing?.platform || "tiktok").toLowerCase();
      const res = await deps.listingApi.cosUpload(
        listingId,
        file,
        referenceType,
      );
      if (!res.success || !res.data?.url?.trim()) {
        message.error(res.message || "Upload 4Seller thất bại");
        return;
      }
      upsertTypedMedia(
        isVideo ? "Video" : "SizeChart",
        res.data.url.trim(),
        res.data.localMaterialId,
      );
      message.success(res.message || "Upload 4Seller thành công.");
    } catch (err) {
      message.error(
        err instanceof Error ? err.message : "Upload 4Seller thất bại",
      );
    } finally {
      setUploadingExtraMedia(null);
    }
  };

  const generateSizeChartWithAi = async () => {
    if (!listingId) {
      message.error("Thiếu listing id.");
      return;
    }
    if (!autoGenerateSizeChart) {
      message.warning("Tạo Size Chart tự động đang tắt trong Settings.");
      return;
    }
    if (!hasSizeGuide) {
      message.warning(
        "Listing chưa có bảng size SHEIN. Thêm sản phẩm qua extension trước.",
      );
      return;
    }

    setGeneratingSizeChart(true);
    try {
      const res = await deps.listingApi.generateSizeChart(listingId);
      if (!res.success || !res.data?.url?.trim()) {
        message.error(res.message || "Tạo Size Chart thất bại");
        return;
      }
      upsertTypedMedia(
        "SizeChart",
        res.data.url.trim(),
        res.data.localMaterialId,
      );
      message.success(res.message || "Tạo Size Chart thành công.");
    } catch (err) {
      const { message: errMsg } = getApiErrorDetails(
        err,
        "Tạo Size Chart thất bại",
      );
      message.error(errMsg);
    } finally {
      setGeneratingSizeChart(false);
    }
  };

  const applyCapturedSizeChart = async () => {
    const captureUrl = listing?.sizeChartCaptureUrl?.trim();
    if (!listingId) {
      message.error("Thiếu listing id.");
      return;
    }
    if (!captureUrl) {
      message.warning("Chưa có ảnh Size Chart từ extension.");
      return;
    }

    setApplyingCapturedSizeChart(true);
    try {
      const res = await fetch(captureUrl);
      if (!res.ok) {
        message.error(`Không tải được ảnh (${res.status}).`);
        return;
      }
      const blob = await res.blob();
      if (!blob.type.startsWith("image/")) {
        message.error("File capture không phải ảnh hợp lệ.");
        return;
      }
      const ext = blob.type.includes("png") ? "png" : "jpg";
      const file = new File([blob], `size-chart-capture.${ext}`, {
        type: blob.type || "image/png",
      });
      await uploadExtraMediaToFourSeller("sizeChart", file);
    } catch (err) {
      message.error(
        err instanceof Error ? err.message : "Áp dụng Size Chart thất bại.",
      );
    } finally {
      setApplyingCapturedSizeChart(false);
    }
  };

  const appendProductImageUrls = (urls: string[]) => {
    if (urls.length === 0) return;
    const list = [
      ...((form.getFieldValue("images") as ListingImageRow[]) ?? []),
    ];
    const extras = list.filter(
      (img) => !isSharedProductImage(img) && !isVariantColorImage(img),
    );
    const variantColorImgs = list.filter((img) => isVariantColorImage(img));
    let products = list.filter((img) => isSharedProductImage(img));
    const filledCount = products.filter((img) =>
      String(img?.url ?? "").trim(),
    ).length;
    const remainingSlots = VARIANT_IMAGE_MAX - filledCount;
    if (remainingSlots <= 0) {
      message.warning(`Default tối đa ${VARIANT_IMAGE_MAX} ảnh.`);
      return;
    }
    const urlsToAdd = urls.slice(0, remainingSlots);
    let lastProductIndex = Math.min(
      activeImageIndex,
      Math.max(products.length - 1, 0),
    );

    for (const url of urlsToAdd) {
      const upgraded = upgradeHttpToHttpsUrl(url);
      const emptyIdx = products.findIndex(
        (img) => !String(img?.url ?? "").trim(),
      );
      if (emptyIdx >= 0) {
        products[emptyIdx] = {
          ...products[emptyIdx],
          url: upgraded,
          mediaType: emptyIdx === 0 ? "Main" : "Detail",
          sortOrder: emptyIdx,
          colorValue: undefined,
        };
        lastProductIndex = emptyIdx;
      } else {
        products.push({
          mediaType: products.length === 0 ? "Main" : "Detail",
          url: upgraded,
          sortOrder: products.length,
        });
        lastProductIndex = products.length - 1;
      }
    }

    products = products.map((img, i) => ({
      ...img,
      mediaType: i === 0 ? "Main" : "Detail",
      sortOrder: i,
      colorValue: undefined,
    }));
    form.setFieldValue("images", [
      ...products,
      ...variantColorImgs,
      ...extras.map((img, i) => ({
        ...img,
        sortOrder: products.length + variantColorImgs.length + i,
      })),
    ]);
    setActiveImageIndex(lastProductIndex);
    if (urls.length > remainingSlots) {
      message.warning(
        `Chỉ thêm được ${remainingSlots} ảnh (tối đa ${VARIANT_IMAGE_MAX}).`,
      );
    }
  };

  const appendVariantColorImageUrls = (color: string, urls: string[]) => {
    if (!color || urls.length === 0) return;
    const list = [
      ...((form.getFieldValue("images") as ListingImageRow[]) ?? []),
    ];
    const existingForColor = list.filter((img) =>
      isVariantColorImage(img, color),
    );
    const remainingSlots = VARIANT_IMAGE_MAX - existingForColor.length;
    if (remainingSlots <= 0) {
      message.warning(`Mỗi màu tối đa ${VARIANT_IMAGE_MAX} ảnh.`);
      return;
    }
    const toAdd = urls.slice(0, remainingSlots).map((url, i) => ({
      mediaType: "Detail" as const,
      url: upgradeHttpToHttpsUrl(url),
      sortOrder: list.length + i,
      colorValue: color,
    }));
    form.setFieldValue("images", [...list, ...toAdd]);
    setGalleryColor(color);
    setActiveImageIndex(0);
    if (urls.length > remainingSlots) {
      message.warning(
        `Chỉ thêm được ${remainingSlots} ảnh (tối đa ${VARIANT_IMAGE_MAX}/màu).`,
      );
    }
  };

  const removeVariantColorImage = (
    color: string,
    localIndex: number,
    setOptimisticImages?: Dispatch<SetStateAction<ListingImageRow[] | null>>,
  ) => {
    const list = [
      ...((form.getFieldValue("images") as ListingImageRow[]) ?? []),
    ];
    let seen = 0;
    const next = list.filter((img) => {
      if (isVariantColorImage(img, color) && String(img.url ?? "").trim()) {
        if (seen === localIndex) {
          seen += 1;
          return false;
        }
        seen += 1;
      }
      return true;
    });
    const payload =
      next.length > 0
        ? next
        : [{ mediaType: "Main" as const, url: "", sortOrder: 0 }];
    // Paint tile removal immediately; commit form in transition (avoids blocking click).
    setOptimisticImages?.(payload);
    startTransition(() => {
      form.setFieldValue("images", payload);
    });
  };

  const removeDefaultImage = (
    localIndex: number,
    setOptimisticImages?: Dispatch<SetStateAction<ListingImageRow[] | null>>,
  ) => {
    const list = [
      ...((form.getFieldValue("images") as ListingImageRow[]) ?? []),
    ];
    let seen = 0;
    const next = list.filter((img) => {
      if (isSharedProductImage(img) && String(img.url ?? "").trim()) {
        if (seen === localIndex) {
          seen += 1;
          return false;
        }
        seen += 1;
      }
      return true;
    });
    const products = next
      .filter(
        (img) => isSharedProductImage(img) && String(img.url ?? "").trim(),
      )
      .map((img, i) => ({
        ...img,
        mediaType: (i === 0 ? "Main" : "Detail") as "Main" | "Detail",
        sortOrder: i,
        colorValue: undefined as string | undefined,
      }));
    const variantColors = next.filter((img) => isVariantColorImage(img));
    const extras = next.filter(
      (img) => !isSharedProductImage(img) && !isVariantColorImage(img),
    );
    const payload =
      products.length + variantColors.length + extras.length > 0
        ? syncListingImagesOrder([...products, ...variantColors, ...extras])
        : [{ mediaType: "Main" as const, url: "", sortOrder: 0 }];
    setOptimisticImages?.(payload);
    startTransition(() => {
      form.setFieldValue("images", payload);
      setActiveImageIndex((prev) =>
        Math.min(prev, Math.max(products.length - 1, 0)),
      );
    });
  };

  const reorderVariantPhotoScope = (
    scope: VariantPhotoDragScope,
    fromLocal: number,
    toLocal: number,
  ) => {
    const list = (form.getFieldValue("images") as ListingImageRow[]) ?? [];
    const matches =
      scope === "__main__"
        ? (img: ListingImageRow) =>
            Boolean(isSharedProductImage(img) && String(img.url ?? "").trim())
        : (img: ListingImageRow) =>
            Boolean(
              isVariantColorImage(img, scope) && String(img.url ?? "").trim(),
            );
    const reordered = reorderImageSubset(list, matches, fromLocal, toLocal);
    if (!reordered) return;
    form.setFieldValue("images", reordered);
    if (scope === "__main__") {
      setActiveImageIndex(toLocal);
    } else {
      setGalleryColor(scope);
      setActiveImageIndex(toLocal);
    }
  };

  /** Reorder color blocks → display order + first color becomes Default/main variant. */
  const reorderSelectedColors = (from: number, to: number) => {
    if (from === to || from < 0 || to < 0) return;
    const nextColors = [...selectedColorsRef.current];
    const [moved] = nextColors.splice(from, 1);
    if (!moved) return;
    nextColors.splice(to, 0, moved);
    setSelectedColors(nextColors);
    syncVariationsFromSpecs(nextColors);

    const list = (form.getFieldValue("images") as ListingImageRow[]) ?? [];
    const products = list.filter((img) => isSharedProductImage(img));
    const extras = list.filter(
      (img) => !isSharedProductImage(img) && !isVariantColorImage(img),
    );
    const orderedVariant = nextColors.flatMap((color) =>
      list.filter((img) => isVariantColorImage(img, color)),
    );
    const known = new Set(nextColors.map((c) => c.trim().toLowerCase()));
    const orphanVariant = list.filter(
      (img) =>
        isVariantColorImage(img) &&
        !known.has(
          String(img.colorValue ?? "")
            .trim()
            .toLowerCase(),
        ),
    );
    form.setFieldValue(
      "images",
      syncListingImagesOrder([
        ...products,
        ...orderedVariant,
        ...orphanVariant,
        ...extras,
      ]),
    );
    if (galleryColor && !nextColors.includes(galleryColor)) {
      setGalleryColor(null);
    }
  };

  /** Replace shared product (Default) gallery with a color's variant images (copy URLs). */
  const setDefaultImagesFromColor = (color: string) => {
    const list = (form.getFieldValue("images") as ListingImageRow[]) ?? [];
    const colorImgs = list.filter(
      (img) => isVariantColorImage(img, color) && String(img.url ?? "").trim(),
    );
    if (colorImgs.length === 0) {
      message.warning(`Màu ${color} chưa có ảnh để đặt làm Default.`);
      return false;
    }
    const extras = list.filter(
      (img) => !isSharedProductImage(img) && !isVariantColorImage(img),
    );
    const variantColors = list.filter((img) => isVariantColorImage(img));
    const products = colorImgs.slice(0, VARIANT_IMAGE_MAX).map((img, i) => ({
      mediaType: (i === 0 ? "Main" : "Detail") as "Main" | "Detail",
      url: img.url,
      sortOrder: i,
      colorValue: undefined as string | undefined,
    }));
    form.setFieldValue(
      "images",
      syncListingImagesOrder([...products, ...variantColors, ...extras]),
    );
    setGalleryColor(null);
    setActiveImageIndex(0);
    return true;
  };

  /**
   * Drop color block onto Default: promote that color to Main (index 0)
   * and copy its images into the shared product Default gallery.
   */
  const promoteColorToDefault = (fromColorIndex: number) => {
    const color = selectedColors[fromColorIndex];
    if (!color) return;
    if (fromColorIndex !== 0) {
      reorderSelectedColors(fromColorIndex, 0);
    }
    if (setDefaultImagesFromColor(color)) {
      message.success(`Đã đặt ảnh màu ${color} làm Default (mặc định).`);
    }
  };

  /** Copy one photo from a color scope into Default gallery at `toLocal` (or append). */
  const copyVariantPhotoToDefault = (
    fromColor: string,
    fromLocal: number,
    toLocal?: number,
  ) => {
    const list = (form.getFieldValue("images") as ListingImageRow[]) ?? [];
    const colorImgs = list.filter(
      (img) =>
        isVariantColorImage(img, fromColor) && String(img.url ?? "").trim(),
    );
    const source = colorImgs[fromLocal];
    if (!source?.url) return;

    let products: ListingImageRow[] = list
      .filter(
        (img) => isSharedProductImage(img) && String(img.url ?? "").trim(),
      )
      .map((img) => ({ ...img, colorValue: undefined }));
    const extras = list.filter(
      (img) => !isSharedProductImage(img) && !isVariantColorImage(img),
    );
    const variantColors = list.filter((img) => isVariantColorImage(img));

    const insertAt =
      toLocal == null || toLocal < 0 || toLocal > products.length
        ? products.length
        : toLocal;
    if (products.length >= VARIANT_IMAGE_MAX && insertAt >= products.length) {
      message.warning(`Default tối đa ${VARIANT_IMAGE_MAX} ảnh.`);
      return;
    }

    const nextProduct: ListingImageRow = {
      mediaType: "Detail",
      url: source.url,
      sortOrder: insertAt,
      colorValue: undefined,
    };
    if (products.length >= VARIANT_IMAGE_MAX) {
      products = [...products];
      products[insertAt] = nextProduct;
    } else {
      products = [
        ...products.slice(0, insertAt),
        nextProduct,
        ...products.slice(insertAt),
      ];
    }
    form.setFieldValue(
      "images",
      syncListingImagesOrder([...products, ...variantColors, ...extras]),
    );
    setGalleryColor(null);
    setActiveImageIndex(Math.min(insertAt, VARIANT_IMAGE_MAX - 1));
  };

  const openVariantPhotoPreview = (urls: string[], index: number) => {
    if (skipVariantPhotoClickRef.current) {
      skipVariantPhotoClickRef.current = false;
      return;
    }
    if (!urls[index]) return;
    setVariantPreviewUrls(urls);
    setVariantPreviewIndex(index);
    setVariantPreviewOpen(true);
  };

  /** Ignore preview when user aims for the × delete control (incl. near-miss corner). */
  const shouldIgnoreVariantPhotoPreview = (
    e: ReactMouseEvent<HTMLElement>,
  ) => {
    const t = e.target;
    if (t instanceof Element && t.closest("button.remove")) return true;
    const rect = e.currentTarget.getBoundingClientRect();
    const zone = 32;
    return e.clientX >= rect.right - zone && e.clientY <= rect.top + zone;
  };

  const handleVariantPhotoRemovePointerDown = (
    e: ReactMouseEvent | ReactPointerEvent,
  ) => {
    e.preventDefault();
    e.stopPropagation();
    skipVariantPhotoClickRef.current = true;
  };

  const pruneSelectedToUsed = (selected: string[], used: string[]) => {
    const usedSet = new Set(used.map(String));
    return selected.map(String).filter((v) => usedSet.has(v));
  };

  const applyVariationTable = (
    next: ListingVariationRow[],
    nextColors: string[],
    nextSizes: string[],
    nextItems: string[],
    options?: { pruneEmptySpecs?: boolean },
  ) => {
    selectedColorsRef.current = nextColors;
    selectedSizesRef.current = nextSizes;
    selectedItemsRef.current = nextItems;
    setSelectedColors(nextColors);
    setSelectedSizes(nextSizes);
    setSelectedItems(nextItems);
    setActiveSpecs((prev) => {
      const specs = new Set(prev);
      if (options?.pruneEmptySpecs) {
        if (nextColors.length === 0) specs.delete("Color");
        if (nextSizes.length === 0) specs.delete("Size");
        if (nextItems.length === 0) specs.delete("Number of Items");
      }
      if (nextColors.length) specs.add("Color");
      if (nextSizes.length) specs.add("Size");
      if (nextItems.length) specs.add("Number of Items");
      return specs;
    });
    const hasMulti = nextColors.length + nextSizes.length + nextItems.length > 0;
    form.setFieldsValue({
      variations: next,
      mainSpecification: nextSizes[0]
        ? "Size"
        : nextColors[0]
          ? "Color"
          : nextItems[0]
            ? "Number of Items"
            : "Color",
      productType:
        hasMulti && next.length > 1
          ? "MultiSku"
          : hasMulti
            ? form.getFieldValue("productType")
            : "SingleSku",
    });
    pruneColorImages(nextColors);
    if (nextColors.length === 0) setVariantImageOn(false);
    setGalleryColor((prev) =>
      prev && nextColors.includes(prev) ? prev : null,
    );
  };

  /**
   * Rebuild variations from chips. Missing axes fall back to refs so rapid Color/Size
   * toggles never close over a stale render (common on slow machines). Passes the
   * previous chip snapshot so holes the user deleted are not filled back in.
   */
  const syncVariationsFromSpecs = (
    colors?: string[],
    sizes?: string[],
    items?: string[],
  ) => {
    const prevColors = selectedColorsRef.current;
    const prevSizes = selectedSizesRef.current;
    const prevItems = selectedItemsRef.current;
    const nextColors = colors ?? prevColors;
    const nextSizes = sizes ?? prevSizes;
    const nextItems = items ?? prevItems;
    const existing =
      (form.getFieldValue("variations") as ListingVariationRow[]) ?? [];
    const next = rebuildVariationsFromSpecs(
      nextColors,
      nextSizes,
      nextItems,
      existing,
      { colors: prevColors, sizes: prevSizes, itemCounts: prevItems },
    );
    applyVariationTable(next, nextColors, nextSizes, nextItems);
  };

  const enableSpec = (spec: VariationSpecKey) => {
    setActiveSpecs((prev) => new Set(prev).add(spec));
  };

  const removeSpec = (spec: VariationSpecKey) => {
    setActiveSpecs((prev) => {
      const next = new Set(prev);
      next.delete(spec);
      return next;
    });
    if (spec === "Color") {
      setSelectedColors([]);
      syncVariationsFromSpecs([], undefined, undefined);
    } else if (spec === "Size") {
      setSelectedSizes([]);
      syncVariationsFromSpecs(undefined, [], undefined);
    } else {
      setSelectedItems([]);
      syncVariationsFromSpecs(undefined, undefined, []);
    }
  };

  const selectedValuesForSpec = (spec: VariationSpecKey) =>
    spec === "Color"
      ? selectedColorsRef.current
      : spec === "Size"
        ? selectedSizesRef.current
        : selectedItemsRef.current;

  /**
   * Drop every variant that uses this chip value. Incremental rebuild keeps holes
   * on the remaining values rather than filling the cartesian product.
   */
  const removeSpecValue = (spec: VariationSpecKey, value: string) => {
    const next = selectedValuesForSpec(spec)
      .map(String)
      .filter((v) => v !== value);
    if (next.length === 0) {
      removeSpec(spec);
      return;
    }
    if (spec === "Color") {
      setSelectedColors(next);
      syncVariationsFromSpecs(next);
    } else if (spec === "Size") {
      setSelectedSizes(next);
      syncVariationsFromSpecs(undefined, next);
    } else {
      setSelectedItems(next);
      syncVariationsFromSpecs(undefined, undefined, next);
    }
  };

  const requestSpecValueRemoval = (spec: VariationSpecKey, value: string) => {
    const rows =
      (form.getFieldValue("variations") as ListingVariationRow[]) ?? [];
    const fallbackMain =
      String(form.getFieldValue("mainSpecification") ?? "") || "Color";
    const affected = rows.filter(
      (r) =>
        String(resolveVariationRowAttrs(r, fallbackMain)[spec] ?? "").trim() ===
        value,
    ).length;
    const dropsAxis = selectedValuesForSpec(spec).length <= 1;
    Modal.confirm({
      title: `Xóa ${spec} "${value}"?`,
      content: dropsAxis
        ? `${spec} chỉ còn giá trị này — xóa sẽ tắt luôn ${spec} và bảng variant còn 1 dòng mặc định.`
        : `Sẽ xóa ${affected} variant dùng ${spec} "${value}".`,
      okText: "Xóa",
      okButtonProps: { danger: true },
      cancelText: "Hủy",
      onOk: () => removeSpecValue(spec, value),
    });
  };

  /** Splice one combo only. Chips stay on unless no remaining row uses that value. */
  const requestVariantRemoval = (index: number) => {
    const rows =
      (form.getFieldValue("variations") as ListingVariationRow[]) ?? [];
    if (!rows[index]) return;
    const remaining = rows.filter((_, i) => i !== index);
    const next = remaining.length
      ? remaining.map((row, i) => ({ ...row, sortOrder: i }))
      : [createEmptyVariationRow(0)];
    const defaultIdx = next.findIndex((row) => row.isDefault);
    const resolved = next.map((row, i) => ({
      ...row,
      isDefault: defaultIdx >= 0 ? i === defaultIdx : i === 0,
    }));
    const used = extractSelectedSpecs(resolved);
    applyVariationTable(
      resolved,
      pruneSelectedToUsed(selectedColorsRef.current, used.colors),
      pruneSelectedToUsed(selectedSizesRef.current, used.sizes),
      pruneSelectedToUsed(selectedItemsRef.current, used.itemCounts),
      { pruneEmptySpecs: true },
    );
  };

  const applyAiCategory = async () => {
    const title = String(form.getFieldValue("productName") ?? "").trim();
    if (!title) {
      message.warning("Nhập Product name trước khi AI gợi ý category.");
      return;
    }
    const categoryShop = shops.find(
      (s) => s.id === (watchedShopId || listing?.shopId),
    );
    const rawFsShopId = categoryShop?.fourSellerShopId;
    const fourSellerShopId =
      rawFsShopId != null && String(rawFsShopId).trim() !== ""
        ? Number(rawFsShopId)
        : undefined;
    const res = await recommendCategory.mutateAsync({
      site: categorySite,
      title,
      ownerUserId: listing?.ownerId,
      fourSellerShopId:
        fourSellerShopId != null && Number.isFinite(fourSellerShopId) && fourSellerShopId > 0
          ? fourSellerShopId
          : undefined,
    });
    if (!res.success || !res.data) {
      message.error(res.message || "AI không gợi ý được category.");
      return;
    }
    const { categoryId, categoryName, pathIds, nodePath, isValidated, validationWarning,
      replacedFromCategoryId, replacedFromCategoryName, replacementReason } =
      res.data;
    const path = pathIds?.length > 0 ? pathIds : [categoryId];
    setCategoryPath(path);
    form.setFieldsValue({ categoryId, categoryName });
    const label = nodePath ? `Category: ${nodePath}` : `Category: ${categoryName}`;
    if (isValidated === false) {
      message.warning(`${label} — ${validationWarning || "category chưa được kiểm chứng."}`);
      return;
    }
    if (replacedFromCategoryId) {
      message.info(
        replacementReason
          || `Đã tránh category bị chặn ${replacedFromCategoryId}${replacedFromCategoryName ? ` (${replacedFromCategoryName})` : ""} → ${categoryName}.`,
      );
      return;
    }
    message.success(label);
  };

  /**
   * Curated listing data the prompt may cite as fact — without it the model is told
   * not to invent specs, so the copy stays generic and thin on keywords.
   */
  const buildRewriteContext = () => {
    const categoryName = String(
      form.getFieldValue("categoryName") ?? "",
    ).trim();
    const brandName = String(form.getFieldValue("brandName") ?? "").trim();
    const attributes = (
      (form.getFieldValue("attributes") as ListingAttributeRow[]) ?? []
    )
      .filter((a) => !isBrandAttribute(a))
      .map((a) => ({
        name: String(a.attrName ?? "").trim(),
        value: String(a.attrValue ?? "").trim(),
      }))
      .filter((a) => a.name && a.value);

    return {
      categoryName: categoryName || undefined,
      brandName:
        brandName && brandName !== NO_BRAND_NAME ? brandName : undefined,
      colors: selectedColors.length > 0 ? selectedColors : undefined,
      sizes: selectedSizes.length > 0 ? selectedSizes : undefined,
      attributes: attributes.length > 0 ? attributes : undefined,
    };
  };

  const applyAiRewriteDescription = async () => {
    const title = String(form.getFieldValue("productName") ?? "").trim();
    const description = String(
      form.getFieldValue("descriptionHtml") ?? "",
    ).trim();
    if (!title) {
      message.warning("Nhập Product name trước khi AI viết lại mô tả.");
      return;
    }
    const plain = description
      .replace(/<[^>]*>/g, " ")
      .replace(/&nbsp;/g, " ")
      .trim();
    if (!plain) {
      message.warning("Nhập Description trước khi AI viết lại mô tả.");
      return;
    }
    try {
      const res = await rewriteDescription.mutateAsync({
        title,
        description,
        ...buildRewriteContext(),
      });
      if (!res.success || !res.data?.descriptionHtml) {
        message.error(res.message || "AI không viết lại được mô tả.");
        return;
      }
      // Apply straight into the editor — Hoàn tác restores title, HTML, search terms, highlights.
      const nextHtml = res.data.descriptionHtml;
      const nextTitle = String(res.data.title ?? "").trim();
      const prevTerms = normalizeStringList(
        Array.isArray(form.getFieldValue("searchTerms"))
          ? (form.getFieldValue("searchTerms") as string[])
          : [],
      );
      const prevHighlights = normalizeStringList(
        Array.isArray(form.getFieldValue("productHighlights"))
          ? (form.getFieldValue("productHighlights") as string[])
          : [],
      );
      const nextTerms = normalizeStringList(res.data.searchTerms);
      const nextHighlights = normalizeStringList(res.data.productHighlights);
      setRewriteUndoHtml(description);
      setRewriteUndoTitle(title);
      setRewriteUndoSearchTerms(prevTerms);
      setRewriteUndoProductHighlights(prevHighlights);
      form.setFieldsValue({
        descriptionHtml: nextHtml,
        ...(nextTitle ? { productName: nextTitle } : {}),
        ...(nextTerms.length > 0 ? { searchTerms: nextTerms } : {}),
        ...(nextHighlights.length > 0 ? { productHighlights: nextHighlights } : {}),
      });
      setDescriptionRewritten(true);
      setDescriptionImagesInserted(false);
      const appliedMeta = nextTerms.length > 0 || nextHighlights.length > 0;
      message.success(
        appliedMeta
          ? "Đã áp dụng tiêu đề, mô tả, search terms và highlights."
          : nextTitle
            ? "Đã áp dụng tiêu đề + mô tả AI."
            : "Đã áp dụng mô tả AI.",
      );
      try {
        await saveDescriptionPrep.mutateAsync({
          id: listingId,
          data: {
            descriptionHtml: nextHtml,
            descriptionRewritten: true,
            descriptionImagesInserted: false,
            ...(nextTitle ? { productName: nextTitle } : {}),
            ...(nextTerms.length > 0 ? { searchTerms: nextTerms } : {}),
            ...(nextHighlights.length > 0
              ? { productHighlights: nextHighlights }
              : {}),
          },
        });
        const draftValues = {
          ...(form.getFieldsValue(true) as ListingFormValues),
          descriptionHtml: nextHtml,
          ...(nextTitle ? { productName: nextTitle } : {}),
          ...(nextTerms.length > 0 ? { searchTerms: nextTerms } : {}),
          ...(nextHighlights.length > 0
            ? { productHighlights: nextHighlights }
            : {}),
        };
        persistLocalDraft(draftValues);
      } catch {
        message.warning(
          "Nội dung AI đã áp dụng trên form nhưng chưa lưu được lên server — Save Draft để giữ.",
        );
      }
    } catch {
      // toast handled by mutation onError
    }
  };

  const undoAiRewriteDescription = async () => {
    if (rewriteUndoHtml === null) return;
    const restored = rewriteUndoHtml;
    const restoredTitle = (rewriteUndoTitle ?? "").trim();
    const restoredTerms = rewriteUndoSearchTerms ?? [];
    const restoredHighlights = rewriteUndoProductHighlights ?? [];
    form.setFieldsValue({
      descriptionHtml: restored,
      ...(restoredTitle ? { productName: restoredTitle } : {}),
      searchTerms: restoredTerms,
      productHighlights: restoredHighlights,
    });
    setRewriteUndoHtml(null);
    setRewriteUndoTitle(null);
    setRewriteUndoSearchTerms(null);
    setRewriteUndoProductHighlights(null);
    setDescriptionRewritten(false);
    setDescriptionImagesInserted(false);
    message.success(
      "Đã khôi phục tiêu đề, mô tả, search terms và highlights trước khi AI viết lại.",
    );
    try {
      await saveDescriptionPrep.mutateAsync({
        id: listingId,
        data: {
          descriptionHtml: restored,
          descriptionRewritten: false,
          descriptionImagesInserted: false,
          ...(restoredTitle ? { productName: restoredTitle } : {}),
          searchTerms: restoredTerms,
          productHighlights: restoredHighlights,
        },
      });
      const draftValues = {
        ...(form.getFieldsValue(true) as ListingFormValues),
        descriptionHtml: restored,
        ...(restoredTitle ? { productName: restoredTitle } : {}),
        searchTerms: restoredTerms,
        productHighlights: restoredHighlights,
      };
      persistLocalDraft(draftValues);
    } catch {
      message.warning(
        "Đã hoàn tác trên form nhưng chưa lưu được lên server — Save Draft để giữ.",
      );
    }
  };

  const markDescriptionImagesInserted = async () => {
    setDescriptionImagesInserted(true);
    const html = String(form.getFieldValue("descriptionHtml") ?? "");
    try {
      await saveDescriptionPrep.mutateAsync({
        id: listingId,
        data: {
          descriptionHtml: html,
          descriptionRewritten: true,
          descriptionImagesInserted: true,
        },
      });
      const draftValues = {
        ...(form.getFieldsValue(true) as ListingFormValues),
        descriptionHtml: html,
      };
      persistLocalDraft(draftValues);
    } catch {
      message.warning(
        "Đã chèn ảnh trên form nhưng chưa lưu được lên server — Save Draft để giữ.",
      );
    }
  };

  const mapSpecificsRowKey = (row: MappedListingSpecific) =>
    `${row.attrId}::${row.sourceName}::${row.sourceValue}`;

  /** Schema attr only — TikTok/4Seller "Washing Instruction(s)". Care* is a description synonym. */
  const isWashingInstructionAttr = (name?: string | null) => {
    const n = String(name ?? "")
      .trim()
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, "");
    return (
      n === "washinginstruction" ||
      n === "washinginstructions" ||
      n.includes("washinginstruction")
    );
  };

  const pickAutoApplyMatches = (result: MapListingSpecificsResult) =>
    (result.matched ?? []).filter((m) => {
      if (m.skippedExisting) return false;
      if (isPublishBlockedAttribute(m.attrName)) return false;
      if (!String(m.attrValue ?? "").trim()) return false;
      return (
        m.confidence >= 0.85 || isWashingInstructionAttr(m.attrName)
      );
    });

  /** Merge map result into current Specifics rows (Brand kept; never overwrite filled values). */
  const mergeMappedSpecificsIntoAttributes = (
    currentRows: ListingAttributeRow[],
    toApply: MappedListingSpecific[],
  ): ListingAttributeRow[] => {
    const current = currentRows.map((a) => ({ ...a }));
    if (!current.some((a) => isBrandAttribute(a))) {
      current.unshift({
        attrId: "Brand",
        attrName: "Brand",
        attrValue: NO_BRAND_NAME,
        isRequired: true,
      });
    }
    const byId = new Map(
      current
        .filter((a) => String(a.attrId ?? "").trim())
        .map((a) => [String(a.attrId).trim().toLowerCase(), a] as const),
    );

    const toValues = (m: MappedListingSpecific) => {
      if (m.values?.length) {
        return m.values
          .filter((v) => String(v.valueName ?? "").trim())
          .map((v) => ({
            valueId: v.valueId ?? undefined,
            valueName: String(v.valueName ?? "").trim(),
          }));
      }
      if (String(m.attrValue ?? "").trim()) {
        return [
          {
            valueId: m.valueId ?? undefined,
            valueName: String(m.attrValue).trim(),
          },
        ];
      }
      return [];
    };

    for (const m of toApply) {
      if (isPublishBlockedAttribute(m.attrName)) continue;
      const mappedValues = toValues(m);
      // Never inject empty Specifics — empty Washing Instruction → 4Seller
      // 【Unpublishable】Product attributes are unavailable.
      if (mappedValues.length === 0) continue;

      const key = m.attrId.trim().toLowerCase();
      const existing = byId.get(key);
      const forceRequired =
        m.isRequired || isWashingInstructionAttr(m.attrName);
      const attrValue = mappedValues.map((v) => v.valueName).join(", ");
      const valueId = mappedValues[0]?.valueId;
      if (existing && String(existing.attrValue ?? "").trim()) {
        if (forceRequired) existing.isRequired = true;
        continue;
      }
      if (existing) {
        existing.attrId = m.attrId;
        existing.attrName = m.attrName;
        existing.attrValue = attrValue;
        existing.valueId = valueId;
        existing.values = mappedValues;
        existing.isRequired = forceRequired;
        continue;
      }
      const row: ListingAttributeRow = {
        attrId: m.attrId,
        attrName: m.attrName,
        attrValue,
        valueId,
        values: mappedValues,
        isRequired: forceRequired,
      };
      current.push(row);
      byId.set(key, row);
    }

    return current;
  };

  const buildMapSpecificsRequest = () => {
    const categoryId = String(form.getFieldValue("categoryId") ?? "").trim();
    const categoryName = String(
      form.getFieldValue("categoryName") ?? "",
    ).trim();
    const shopId = String(
      form.getFieldValue("shopId") ?? listing?.shopId ?? "",
    ).trim();
    const title = String(form.getFieldValue("productName") ?? "").trim();
    const existingAttributes = (
      (form.getFieldValue("attributes") as ListingAttributeRow[]) ?? []
    )
      .filter((a) => String(a.attrId ?? a.attrName ?? "").trim())
      .map((a) => ({
        attrId: a.attrId || a.attrName || "",
        attrName: a.attrName,
        attrValue: a.attrValue,
        valueId: a.valueId,
        values: a.values?.length
          ? a.values.map((v) => ({
              valueId: v.valueId,
              valueName: v.valueName,
            }))
          : undefined,
        valueType: a.valueType,
        isRequired: a.isRequired,
      }));

    // No description: the server parses the supplier snapshot it stored at sync time. Sending
    // the editor contents made Auto Specifics fail once Rewrite had replaced the Key: Value
    // rows, which is why it used to have to run first.
    return {
      categoryId,
      categoryName,
      shopId,
      title,
      existingAttributes,
    };
  };

  /** Silent Save Draft / Update after background Specifics map — stay on page. */
  const persistSpecificsInBackground = async (
    nextAttributes: ListingAttributeRow[],
  ) => {
    if (!canManage || !listingId || !listing?.hasRawPayload) return false;

    const values = {
      ...(form.getFieldsValue(true) as ListingFormValues),
      attributes: nextAttributes,
    };
    const selectedShopId = shopChangeLocked
      ? listing.shopId || values.shopId || undefined
      : values.shopId || listing.shopId || undefined;
    if (!selectedShopId) return false;
    const selectedShop = shops.find((s) => s.id === selectedShopId);
    if (!selectedShop?.fourSellerShopId) return false;

    const formRows = (values.variations ?? []) as ListingVariationRow[];
    let rawVariations: ListingVariationRow[];
    if (values.productType === "SingleSku") {
      rawVariations = [
        {
          ...(formRows[0] ?? createEmptyVariationRow(0)),
          mainSpec: values.mainSpecification || "Color",
          specValue: formRows[0]?.specValue || "Default",
          isDefault: true,
        },
      ];
    } else {
      rawVariations = formRows.map((v, i) => ({
        ...v,
        sortOrder: v.sortOrder ?? i,
      }));
    }

    const certByType = new Map(
      (values.certifications ?? []).map((c) => [c.certType, c]),
    );
    const certifications: ListingCertificationRow[] = [
      { certType: "Compliance", name: hazardous || "No" },
      { certType: "ProductCert", name: prop65Repro || "No" },
      { certType: "ResponsiblePerson", name: prop65Carcinogen || "No" },
    ].map((c) => ({
      ...certByType.get(c.certType),
      certType: c.certType,
      name: c.name === "Yes" ? "Yes" : "No",
      fileUrls: certByType.get(c.certType)?.fileUrls ?? "[]",
    }));

    const payloadValues: ListingFormValues = {
      ...values,
      shopId: selectedShopId,
      variations: rawVariations,
      certifications,
    };

    // Cache browser draft so refresh keeps mapped Specifics (do not close tab).
    saveListingEditDraft(listingId, payloadValues, {
      hazardous,
      prop65Repro,
      prop65Carcinogen,
      variantImageOn,
    });
    const data = formToMirrorListingPayload(payloadValues, listing, {
      appliedPricing: pricingFormulaApplied
        ? buildListingAppliedPricingPayload(
            pricingFormula,
            selectedPricingTemplateId,
          )
        : undefined,
      descriptionRewritten,
      descriptionImagesInserted,
    });
    const response = isPublished
      ? await deps.listingApi.pushUpdate(listingId, data)
      : await deps.listingApi.saveDraft(listingId, data);
    if (!response.success) {
      const err = new Error(
        response.message ||
          (isPublished ? "Update thất bại." : "Save Draft thất bại."),
      ) as Error & { apiErrors?: string[] };
      err.apiErrors = response.errors ?? [];
      throw err;
    }
    if (!isPublished) {
      saveListingEditDraft(listingId, payloadValues, {
        hazardous,
        prop65Repro,
        prop65Carcinogen,
        variantImageOn,
      });
    }
    baselineFormRef.current = payloadValues;
    deps.invalidateListingsCache(queryClient);
    void queryClient.invalidateQueries({ queryKey: deps.listingDetailQueryKeyPrefix });
    return true;
  };

  const openAutoMapSpecifics = async () => {
    const req = buildMapSpecificsRequest();
    if (!req.categoryId) {
      message.warning("Chọn Category trước khi Auto map Specifics.");
      return;
    }
    if (!req.shopId) {
      message.warning("Chọn shop Publish trước khi Auto map Specifics.");
      return;
    }

    try {
      const res = await mapSpecifics.mutateAsync({
        categoryId: req.categoryId,
        categoryName: req.categoryName || undefined,
        shopId: req.shopId,
        title: req.title || undefined,
        existingAttributes: req.existingAttributes,
      });
      if (!res.success || !res.data) {
        message.error(res.message || "Auto map Specifics thất bại.");
        return;
      }
      if (res.data.parsedPairCount === 0) {
        message.info(
          "Mô tả nhà cung cấp không còn cặp Key: Value — bỏ qua Auto Specifics, điền tay nếu cần.",
        );
        return;
      }
      const applicable = pickAutoApplyMatches(res.data);
      setMapSpecificsResult(res.data);
      setSelectedMapKeys(applicable.map(mapSpecificsRowKey));
      setMapSpecificsOpen(true);
      if ((res.data.matched?.length ?? 0) === 0) {
        message.warning(
          res.message || "Không map được Specifics nào đủ tin cậy.",
        );
      }
    } catch {
      // toast handled by mutation onError
    }
  };

  const applyMappedSpecifics = () => {
    if (!mapSpecificsResult) {
      setMapSpecificsOpen(false);
      return;
    }

    const selected = new Set(selectedMapKeys);
    const toApply = (mapSpecificsResult.matched ?? []).filter(
      (m) =>
        !m.skippedExisting &&
        !isPublishBlockedAttribute(m.attrName) &&
        selected.has(mapSpecificsRowKey(m)) &&
        String(m.attrValue ?? "").trim(),
    );
    for (const m of mapSpecificsResult.matched ?? []) {
      if (
        !m.skippedExisting &&
        isWashingInstructionAttr(m.attrName) &&
        String(m.attrValue ?? "").trim() &&
        !toApply.some((x) => x.attrId === m.attrId)
      ) {
        toApply.push(m);
      }
    }

    if (toApply.length === 0) {
      message.warning("Chọn ít nhất một Specifics (có giá trị) để áp dụng.");
      return;
    }

    const current = (
      (form.getFieldValue("attributes") as ListingAttributeRow[]) ?? []
    ) as ListingAttributeRow[];
    const next = mergeMappedSpecificsIntoAttributes(current, toApply);
    form.setFieldsValue({ attributes: next });
    setMapSpecificsOpen(false);
    setMapSpecificsResult(null);

    const washingStillMissing = (mapSpecificsResult.missingRequired ?? []).some(
      (m) => isWashingInstructionAttr(m.attrName),
    );
    message.success(
      `Đã áp dụng ${toApply.length} Specifics vào form — nhớ Save Draft.`,
    );
    if (washingStillMissing) {
      message.warning(
        "Chưa map được Washing Instruction — điền thủ công trước khi Publish (tránh giá trị trống).",
      );
    }
  };

  /**
   * The enum ids that Auto Specifics resolved travel in hidden fields next to the value. Editing the
   * text without clearing them would let the backend re-resolve the row by its stale id and put the
   * old value back, so a hand-typed value is judged by its text alone.
   */
  const dropResolvedSpecificValueId = (index: number) => {
    const row = form.getFieldValue(["attributes", index]) as
      | ListingAttributeRow
      | undefined;
    if (!row?.valueId && !row?.values?.length) return;
    form.setFieldValue(["attributes", index, "valueId"], "");
    form.setFieldValue(["attributes", index, "values"], undefined);
  };

  /**
   * Background: when Specifics empty / only Brand → map + apply (≥85% + Washing) + silent save.
   * No modal; stay on edit page.
   */
  const maybeBackgroundAutoSpecifics = async () => {
    if (!canManage || !listingId || !listing) return;
    if (autoSpecificsDoneRef.current === listingId) return;
    if (hydratedListingIdRef.current !== listingId) return;
    if (remoteRefreshDoneRef.current !== listingId) return;
    if (autoSpecificsRunning || mapSpecificsOpen) return;

    const currentAttrs =
      (form.getFieldValue("attributes") as ListingAttributeRow[]) ?? [];
    if (!needsAutoMapSpecifics(currentAttrs)) {
      autoSpecificsDoneRef.current = listingId;
      return;
    }

    const req = buildMapSpecificsRequest();
    if (!req.categoryId || !req.shopId) {
      // Prerequisites not ready yet (shop/category) — retry later.
      return;
    }
    if (!listing.hasRawPayload) {
      autoSpecificsDoneRef.current = listingId;
      return;
    }

    autoSpecificsDoneRef.current = listingId;
    setAutoSpecificsRunning(true);
    try {
      const res = await deps.listingApi.mapSpecifics(listingId, {
        categoryId: req.categoryId,
        categoryName: req.categoryName || undefined,
        shopId: req.shopId,
        title: req.title || undefined,
        existingAttributes: req.existingAttributes,
      });
      if (!res.success || !res.data) return;

      const toApply = pickAutoApplyMatches(res.data);
      if (toApply.length === 0) {
        const washingMissing = (res.data.missingRequired ?? []).some((m) =>
          isWashingInstructionAttr(m.attrName),
        );
        if (washingMissing) {
          message.warning(
            "Auto Specifics: chưa map được Washing Instruction từ mô tả — điền thủ công nếu category yêu cầu.",
          );
        }
        return;
      }

      const next = mergeMappedSpecificsIntoAttributes(currentAttrs, toApply);
      form.setFieldsValue({ attributes: next });

      try {
        const saved = await persistSpecificsInBackground(next);
        const washingStillMissing = (res.data.missingRequired ?? []).some((m) =>
          isWashingInstructionAttr(m.attrName),
        );
        // Defer past form/query re-render so antd message paints on a settled frame (crisper glyphs).
        requestAnimationFrame(() => {
          message.success(
            saved
              ? `Đã tự động map & lưu ${toApply.length} Specifics.`
              : `Đã tự động map ${toApply.length} Specifics — nhớ Save Draft.`,
          );
          if (washingStillMissing) {
            message.warning(
              "Chưa map được Washing Instruction — điền thủ công trước khi Publish.",
            );
          }
        });
      } catch {
        requestAnimationFrame(() => {
          message.warning(
            `Đã tự động map ${toApply.length} Specifics nhưng lưu thất bại — nhớ Save Draft.`,
          );
        });
      }
    } catch {
      // Silent — user can still run Auto map manually.
    } finally {
      setAutoSpecificsRunning(false);
    }
  };

  const applyFormHydration = (
    values: ListingFormValues,
    listingForVariantFlag?: typeof listing,
  ) => {
    const beforeImages = upgradeHttpImageUrls(
      (values.images ?? []) as ListingImageRow[],
    );
    const dedupedImages = dedupeVariantColorImages(beforeImages);
    const syncErr =
      listingForVariantFlag?.syncError ?? listing?.syncError ?? null;
    const failKeys = buildFailedUploadImageKeys(
      extractImageUrlsFromSyncError(syncErr),
    );
    if (
      failKeys.size > 0 &&
      hasFailedUploadDupes(beforeImages, failKeys) &&
      !hasFailedUploadDupes(dedupedImages, failKeys)
    ) {
      setSawFailedUploadDupes(true);
      setFailedUploadDupesRemain(false);
    }
    form.setFieldsValue({
      ...values,
      images: dedupedImages,
      weightUnit: normalizeWeightUnit(values.weightUnit),
      dimensionUnit: normalizeDimensionUnit(values.dimensionUnit),
    });
    const extracted = extractSelectedSpecs(values.variations ?? []);
    selectedColorsRef.current = extracted.colors;
    selectedSizesRef.current = extracted.sizes;
    selectedItemsRef.current = extracted.itemCounts;
    setSelectedColors(extracted.colors);
    setSelectedSizes(extracted.sizes);
    setSelectedItems(extracted.itemCounts);
    const specsFromFourSeller =
      variationSpecsFromFourSeller ||
      Boolean(listingForVariantFlag?.fourSellerListingId);
    if (specsFromFourSeller) {
      setColorOptions(extracted.colors.map(String));
      setSizeOptions(extracted.sizes.map(String));
      setItemOptions(extracted.itemCounts.map(String));
    } else {
      setColorOptions((prev) => [
        ...new Set([...prev, ...extracted.colors].map(String)),
      ]);
      setSizeOptions((prev) => [
        ...new Set([...prev, ...extracted.sizes].map(String)),
      ]);
      setItemOptions((prev) => [
        ...new Set([...prev, ...extracted.itemCounts].map(String)),
      ]);
    }
    const specs = new Set<VariationSpecKey>();
    if (extracted.colors.length) specs.add("Color");
    if (extracted.sizes.length) specs.add("Size");
    if (extracted.itemCounts.length) specs.add("Number of Items");
    setActiveSpecs(specs);
    const hasColorImages = dedupedImages.some((img) => img.colorValue?.trim());
    const hasVariationMainImage = (
      listingForVariantFlag?.variations ?? []
    ).some((v) => Boolean(v.mainImage?.trim()));
    setVariantImageOn(
      extracted.colors.length > 0 && (hasColorImages || hasVariationMainImage),
    );

    const certs = values.certifications ?? [];
    const findCert = (type: string) =>
      certs.find((c) => c.certType === type)?.name ?? "No";
    setHazardous(findCert("Compliance") === "Yes" ? "Yes" : "No");
    setProp65Repro(findCert("ProductCert") === "Yes" ? "Yes" : "No");
    setProp65Carcinogen(findCert("ResponsiblePerson") === "Yes" ? "Yes" : "No");
  };

  const persistLocalDraft = (values: ListingFormValues) => {
    if (!listingId) return;
    saveListingEditDraft(listingId, values, {
      hazardous,
      prop65Repro,
      prop65Carcinogen,
      variantImageOn,
    });
  };

  useEffect(() => {
    if (derivedCategorySite) setCategorySite(derivedCategorySite);
  }, [derivedCategorySite]);

  // Reset bookkeeping when navigating to another listing — must run before hydrate.
  useEffect(() => {
    autoSpecificsDoneRef.current = null;
    autoShippingDoneRef.current = null;
    autoPricingDoneRef.current = null;
    hydratedListingIdRef.current = null;
    remoteRefreshDoneRef.current = null;
    baselineFormRef.current = null;
    syncInFlightRef.current = false;
    syncGenerationRef.current += 1;
    setHydratedForId(null);
    setRemoteSyncGeneration(0);
    setRefreshingRemote(false);
    setColorOptions([]);
    setSizeOptions([]);
    setItemOptions([]);
  }, [listingId]);

  // Prefer browser local draft, else local DB form — once per listingId.
  useEffect(() => {
    if (!listing || !initialValues) return;
    if (hydratedListingIdRef.current === listingId) return;

    const draft = loadListingEditDraft(listingId);
    // Hydrate synchronously (high priority) so DB values — variants included — bind immediately.
    if (draft?.formValues) {
      // Draft may predate Video hydrate / shop Publish field — backfill from DB snapshot.
      const merged = ensureTypedMediaFromSource(
        draft.formValues,
        initialValues,
      );
      applyFormHydration(
        {
          ...merged,
          shopId: merged.shopId || initialValues.shopId,
        },
        listing,
      );
      if (draft.hazardous) setHazardous(draft.hazardous);
      if (draft.prop65Repro) setProp65Repro(draft.prop65Repro);
      if (draft.prop65Carcinogen) setProp65Carcinogen(draft.prop65Carcinogen);
      if (typeof draft.variantImageOn === "boolean")
        setVariantImageOn(draft.variantImageOn);
    } else {
      applyFormHydration(initialValues, listing);
    }
    // Baseline = local DB snapshot so 4Seller diffs don't wipe unsynced browser draft edits.
    baselineFormRef.current = initialValues;
    hydratedListingIdRef.current = listingId;
    remoteRefreshDoneRef.current = null;
    setHydratedForId(listingId);
  }, [form, initialValues, listing, listingId]);

  // Late backfill: listing has shop but form/draft left shopId empty (or shops loaded after hydrate).
  useEffect(() => {
    if (!listing?.shopId || hydratedListingIdRef.current !== listingId) return;
    const current = form.getFieldValue("shopId") as string | undefined;
    if (current) return;
    form.setFieldsValue({ shopId: listing.shopId });
  }, [form, listing?.shopId, listingId]);

  /** Pull 4Seller detail → three-way merge into form (keeps in-progress edits). */
  const syncFromRemote = async (options?: { silent?: boolean }) => {
    if (!listingId) return;
    // Silent auto-sync never stacks; manual Sync preempts an in-flight silent pull.
    if (syncInFlightRef.current) {
      if (options?.silent) return;
      syncGenerationRef.current += 1;
      syncInFlightRef.current = false;
    }

    const generation = ++syncGenerationRef.current;
    syncInFlightRef.current = true;
    // Background auto-sync must not spin the Sync button / lock topbar UX.
    const showSyncUi = !options?.silent;
    if (showSyncUi) setRefreshingRemote(true);
    try {
      const refreshed = await deps.refreshListingFromFourSeller?.(listingId);
      if (generation !== syncGenerationRef.current) return;
      if (!refreshed) {
        remoteRefreshDoneRef.current = listingId;
        setRemoteSyncGeneration((n) => n + 1);
        if (showSyncUi) message.warning("Không nhận được dữ liệu sync.");
        return;
      }

      // Build merge before mutating query cache / form — bail if superseded mid-flight.
      const remoteForm = mirrorListingToForm(refreshed);
      const baseline = baselineFormRef.current ?? remoteForm;
      const current = {
        ...baseline,
        ...(form.getFieldsValue(true) as ListingFormValues),
      };
      const merged = mergeListingFormRemoteDiff(baseline, remoteForm, current);
      if (generation !== syncGenerationRef.current) return;

      queryClient.setQueryData(deps.listingDetailQueryKey(listingId), refreshed);

      setDescriptionRewritten(Boolean(refreshed.descriptionRewritten));
      setDescriptionImagesInserted(Boolean(refreshed.descriptionImagesInserted));

      if (!deepEqual(merged, current)) {
        if (generation !== syncGenerationRef.current) return;
        applyFormHydration(merged, refreshed);
      }
      if (generation !== syncGenerationRef.current) return;
      baselineFormRef.current = remoteForm;
      remoteRefreshDoneRef.current = listingId;
      setRemoteSyncGeneration((n) => n + 1);
      if (showSyncUi) {
        message.success("Đã sync listing.");
      }
    } catch (err) {
      // Best-effort on auto-sync — keep local hydrate; do not block editing.
      if (generation !== syncGenerationRef.current) return;
      remoteRefreshDoneRef.current = listingId;
      setRemoteSyncGeneration((n) => n + 1);
      if (showSyncUi) {
        message.error(err instanceof Error ? err.message : "Sync thất bại.");
      }
    } finally {
      if (generation === syncGenerationRef.current) {
        syncInFlightRef.current = false;
        if (showSyncUi) setRefreshingRemote(false);
      }
    }
  };

  // Upgrade any leftover http:// image URLs after hydrate (covers HMR / old draft).
  useEffect(() => {
    if (!listingId || hydratedForId !== listingId) return;
    const list =
      (form.getFieldValue("images") as ListingImageRow[] | undefined) ?? [];
    const upgraded = upgradeHttpImageUrls(list);
    if (upgraded !== list) {
      form.setFieldValue("images", upgraded);
    }
  }, [listingId, hydratedForId, form]);

  // Background enrich on open — only when local mirror looks incomplete.
  // Scheduled sync (page + batch-detail) often already has RawPayload + media; skip
  // per-id refresh then. Thin / missing raw still silent-pulls `?refresh=true`.
  // Depends on hydratedForId (not listing object) so setQueryData cannot re-arm a second sync.
  useEffect(() => {
    if (!listingId || hydratedForId !== listingId) return;
    if (remoteRefreshDoneRef.current === listingId) return;

    // Local already rich enough — mark remote done so auto specifics/pricing can run.
    if (listing && !listingNeedsFourSellerRefresh(listing)) {
      remoteRefreshDoneRef.current = listingId;
      setRemoteSyncGeneration((n) => n + 1);
      return;
    }

    let cancelled = false;
    const timer = setTimeout(() => {
      if (cancelled) return;
      void syncFromRemote({ silent: true });
    }, 350);
    return () => {
      cancelled = true;
      clearTimeout(timer);
      // Do not bump syncGeneration here — timer cancel must not orphan an in-flight
      // manual sync. listingId reset already invalidates flights on navigate.
    };
    // Intentional: run once after local hydrate; manual Sync uses syncFromRemote directly.
    // eslint-disable-next-line react-hooks/exhaustive-deps -- auto-sync once per listingId hydrate
  }, [listingId, hydratedForId]);

  // After sync: auto-map Specifics when empty / only Brand — apply + silent save, no modal.
  useEffect(() => {
    if (!listingId || !listing) return;
    if (hydratedListingIdRef.current !== listingId) return;
    if (remoteRefreshDoneRef.current !== listingId) return;
    void maybeBackgroundAutoSpecifics();
    // eslint-disable-next-line react-hooks/exhaustive-deps -- intentional trigger after sync / shop / attrs ready
  }, [
    listingId,
    listing,
    remoteSyncGeneration,
    watchedShopId,
    watchedAttributes.length,
    canManage,
  ]);

  useEffect(() => {
    // Lazy cascader resolves ancestors itself — seed with just the saved leaf id.
    setCategoryPath(listing?.categoryId ? [listing.categoryId] : undefined);
  }, [listing?.categoryId]);

  const applyShippingTemplate = (
    template: ShippingTemplate,
    options?: { silent?: boolean },
  ) => {
    form.setFieldsValue({
      packageWeight: template.packageWeight,
      packageLength: template.packageLength,
      packageWidth: template.packageWidth,
      packageHeight: template.packageHeight,
      weightUnit: normalizeWeightUnit(template.weightUnit),
      dimensionUnit: normalizeDimensionUnit(template.dimensionUnit),
    });
    setSelectedShippingTemplateId(template.id);
    if (!options?.silent) {
      message.success(`Đã áp dụng template "${template.name}".`);
    }
  };

  const needsShippingAutofill = () => {
    const weight = Number(form.getFieldValue("packageWeight"));
    const length = Number(form.getFieldValue("packageLength"));
    const width = Number(form.getFieldValue("packageWidth"));
    const height = Number(form.getFieldValue("packageHeight"));
    return !(weight > 0 && length > 0 && width > 0 && height > 0);
  };

  /** After hydrate: apply default shipping template when weight/dims missing. */
  useEffect(() => {
    if (!listingId || !listing) return;
    if (hydratedListingIdRef.current !== listingId) return;
    if (autoShippingDoneRef.current === listingId) return;
    if (shippingTemplates.length === 0) return;

    if (!needsShippingAutofill()) {
      autoShippingDoneRef.current = listingId;
      // Still select matching template in dropdown when dims already filled.
      const match = shippingTemplates.find(
        (t) =>
          Number(t.packageWeight) === Number(form.getFieldValue("packageWeight")) &&
          Number(t.packageLength) === Number(form.getFieldValue("packageLength")) &&
          Number(t.packageWidth) === Number(form.getFieldValue("packageWidth")) &&
          Number(t.packageHeight) === Number(form.getFieldValue("packageHeight")),
      );
      if (match) setSelectedShippingTemplateId(match.id);
      return;
    }

    const defaults = getDefaultShippingTemplate(shippingTemplates);
    if (!defaults) return;
    autoShippingDoneRef.current = listingId;
    applyShippingTemplate(defaults, { silent: true });
    // eslint-disable-next-line react-hooks/exhaustive-deps -- once per listing after templates load
  }, [listingId, listing, shippingTemplates]);

  /** After remote refresh: fill missing salePrice from user's default pricing template. */
  useEffect(() => {
    if (!listingId || !listing) return;
    if (hydratedListingIdRef.current !== listingId) return;
    // Wait for 4Seller auto-sync so we do not rewrite variations mid merge/hydrate.
    if (remoteRefreshDoneRef.current !== listingId) return;
    if (autoPricingDoneRef.current === listingId) return;
    if (pricingTemplates.length === 0) return;

    const rows =
      (form.getFieldValue("variations") as ListingVariationRow[]) ?? [];
    if (rows.length === 0) {
      autoPricingDoneRef.current = listingId;
      return;
    }

    const needsFill = rows.some((v) => {
      const sale = v.salePrice == null ? NaN : Number(v.salePrice);
      const cost = v.costPrice == null ? NaN : Number(v.costPrice);
      return (Number.isNaN(sale) || sale <= 0) && !Number.isNaN(cost) && cost > 0;
    });
    if (!needsFill) {
      autoPricingDoneRef.current = listingId;
      return;
    }

    const defaults = getDefaultPricingTemplate(pricingTemplates);
    if (!defaults) return;

    const formula =
      pricingFormulaApplied && pricingFormulaInitializedRef.current
        ? pricingFormula
        : templateToFormulaInput(defaults);
    const templateId =
      pricingFormulaApplied && selectedPricingTemplateId
        ? selectedPricingTemplateId
        : defaults.id;

    const { rows: next, filled } = fillMissingSalePrices(rows, formula);
    autoPricingDoneRef.current = listingId;
    if (filled === 0) return;

    form.setFieldValue("variations", next);
    setPricingFormula(formula);
    setSelectedPricingTemplateId(templateId);
    setPricingFormulaApplied(true);
    pricingFormulaInitializedRef.current = true;
    saveListingAppliedPricing(listingId, formula, templateId);
    const appliedPricing = buildListingAppliedPricingPayload(formula, templateId);
    void deps.listingApi
      .update(listingId, { appliedPricing })
      .then((res) => {
        if (res.success && res.data) {
          queryClient.setQueryData(deps.listingDetailQueryKey(listingId), res.data);
        }
      })
      .catch(() => {
        /* localStorage keeps formula until Save Draft */
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps -- once per listing after remote sync / templates
  }, [listingId, listing, pricingTemplates, variations.length, remoteSyncGeneration]);

  /** Edit mở tab mới — hủy GET đang chạy, đợi toast (nếu có), rồi đóng tab; fallback navigate nếu trình duyệt chặn close. */
  const closeEditTab = (fallbackPath: string, delayMs = 0) => {
    const doClose = () => {
      void queryClient.cancelQueries();
      window.close();
      navigate(fallbackPath);
    };
    if (delayMs > 0) {
      window.setTimeout(doClose, delayMs);
      return;
    }
    doClose();
  };

  const persist = async (mode: "draft" | "publish" | "update") => {
    if (!canManage) {
      message.warning("Bạn không có quyền sửa listing.");
      return;
    }
    // Show the button spinner immediately, then yield one frame so it paints
    // before the heavy synchronous validateFields / payload build on this huge form.
    setSaving(true);
    await new Promise<void>((resolve) => requestAnimationFrame(() => resolve()));

    const fail = (msg: string) => {
      message.error(msg);
      setSaving(false);
    };

    let values: ListingFormValues;
    try {
      values = await form.validateFields();
    } catch {
      // Field-level errors are surfaced inline by the form itself.
      setSaving(false);
      return;
    }

    // validateFields only returns registered fields; collapsed variation rows mount a
    // subset of Form.Items. Merge full store so removed SKUs stay removed and kept
    // rows keep attrs / identity for the mirror payload.
    const storeValues = form.getFieldsValue(true) as ListingFormValues;
    values = {
      ...storeValues,
      ...values,
      variations: (storeValues.variations?.length
        ? storeValues.variations
        : values.variations) as ListingFormValues["variations"],
      images: ((storeValues.images?.length
        ? storeValues.images
        : values.images) ?? []) as ListingFormValues["images"],
    };

    const selectedShopId = shopChangeLocked
      ? listing?.shopId || values.shopId || undefined
      : values.shopId || listing?.shopId || undefined;
    if (!selectedShopId) {
      fail(
        "Chọn shop Publish (góc phải card Product Info) trước khi Save Draft / Publish.",
      );
      return;
    }
    if (
      shopChangeLocked &&
      listing?.shopId &&
      values.shopId &&
      values.shopId !== listing.shopId
    ) {
      fail("Không thể đổi shop khi listing đang Active hoặc Inactive.");
      return;
    }
    const selectedShop = shops.find((s) => s.id === selectedShopId);
    if (!selectedShop?.fourSellerShopId) {
      fail(
        "Shop chưa kết nối 4Seller — chọn shop đã Connected hoặc kết nối shop trước.",
      );
      return;
    }

    if (mode !== "draft") {
      if (shopMetadataLoading || !shopMetadataReady) {
        fail("Đang tải kho mặc định của shop — đợi xong rồi thử lại.");
        return;
      }
      if (!String(values.warehouseId ?? "").trim()) {
        fail(
          "Shop chưa có warehouse mặc định từ 4Seller — kiểm tra cookie seller hoặc kết nối shop.",
        );
        return;
      }
    }

    if (mode === "publish") {
      if (!descriptionRewritten) {
        setSaving(false);
        Modal.warning({
          title: "Chưa Rewrite mô tả trước khi Publish",
          content: (
            <p style={{ marginBottom: 0 }}>
              Bấm <strong>1. Rewrite</strong> để AI viết lại tiêu đề + mô tả,
              rồi Publish lại. Chèn ảnh (2. Images) là tùy chọn.
            </p>
          ),
          okText: "Đã hiểu",
        });
        return;
      }
      if (!hasListingSizeChart(values.images)) {
        setSaving(false);
        showPublishMissingSizeChartWarning();
        return;
      }
      if (
        hasListingFailedImages(
          values.images,
          failedUploadImageKeys,
          failedLoadImageKeysRef.current,
        )
      ) {
        setSaving(false);
        showPublishFailedImagesWarning();
        focusFailedUploadImage();
        return;
      }
    }

    const formRows = (values.variations ?? []) as ListingVariationRow[];
    let rawVariations: ListingVariationRow[];
    if (values.productType === "SingleSku") {
      rawVariations = [
        {
          ...(formRows[0] ?? createEmptyVariationRow(0)),
          mainSpec: values.mainSpecification || "Color",
          specValue: formRows[0]?.specValue || "Default",
          isDefault: true,
        },
      ];
    } else {
      // Form.List already holds the live variation rows (synced when chips change).
      // Rebuilding again on save drops identity when attrs/id are missing from form values.
      // Re-derive attrs from the label so a localStorage draft that predates structured
      // attrs still pushes a full sale-prop combo instead of a bare title.
      rawVariations = formRows.map((v, i) => ({
        ...v,
        attrs: resolveVariationRowAttrs(v, values.mainSpecification || "Color"),
        sortOrder: v.sortOrder ?? i,
      }));

      const matrixCheck = validateVariationMatrix(
        rawVariations,
        {
          colors: selectedColorsRef.current,
          sizes: selectedSizesRef.current,
          itemCounts: selectedItemsRef.current,
        },
        {
          requirePricing: mode !== "draft",
          mainSpecification: values.mainSpecification,
        },
      );
      if (matrixCheck.error) {
        fail(matrixCheck.error);
        return;
      }
      if (matrixCheck.warning) {
        message.warning(matrixCheck.warning);
      }
    }

    // Always push all three Product Certification Yes/No (fashion default No) into certifications
    // so backend can upsert them into productAttributes (TikTok canonical store).
    const certByType = new Map(
      (values.certifications ?? []).map((c) => [c.certType, c]),
    );
    const certifications: ListingCertificationRow[] = [
      { certType: "Compliance", name: hazardous || "No" },
      { certType: "ProductCert", name: prop65Repro || "No" },
      { certType: "ResponsiblePerson", name: prop65Carcinogen || "No" },
    ].map((c) => ({
      ...certByType.get(c.certType),
      certType: c.certType,
      name: c.name === "Yes" ? "Yes" : "No",
      fileUrls: certByType.get(c.certType)?.fileUrls ?? "[]",
    }));

    const payloadValues: ListingFormValues = {
      ...values,
      shopId: selectedShopId,
      variations: rawVariations,
      certifications,
      images: dedupeVariantColorImages(
        upgradeHttpImageUrls((values.images ?? []) as ListingImageRow[]),
      ),
    };

    const productImages = (payloadValues.images ?? []).filter(
      (i) =>
        (i.mediaType === "Main" || i.mediaType === "Detail") &&
        !i.colorValue?.trim() &&
        String(i.url ?? "").trim(),
    );
    if (productImages.length > 0) {
      const imageError = validateProductImages(
        (payloadValues.images ?? []).filter(
          (i) =>
            (i.mediaType === "Main" || i.mediaType === "Detail") &&
            !i.colorValue?.trim(),
        ),
      );
      if (imageError) {
        fail(imageError);
        return;
      }
    }

    if (!listing?.hasRawPayload) {
      fail(
        "Listing chưa có raw detail từ 4Seller. Đồng bộ lại trước khi Save Draft / Publish.",
      );
      return;
    }

    // Always cache browser local draft so next edit opens instantly (even if 4Seller fails).
    if (mode === "draft") {
      persistLocalDraft(payloadValues);
    }

    try {
      // Single call: refresh 4Seller base → patch form onto RawPayload → push 4Seller → save local.
      const data = formToMirrorListingPayload(payloadValues, listing, {
        appliedPricing: pricingFormulaApplied
          ? buildListingAppliedPricingPayload(
              pricingFormula,
              selectedPricingTemplateId,
            )
          : undefined,
        descriptionRewritten,
        descriptionImagesInserted,
      });
      if (mode === "draft") {
        await saveDraftListing.mutateAsync({ id: listingId, data });
        persistLocalDraft(payloadValues);
        baselineFormRef.current = payloadValues;
        closeEditTab("/listings/draft", 1000);
      } else if (mode === "update") {
        await updateListing.mutateAsync({ id: listingId, data });
        clearListingEditDraft(listingId);
        closeEditTab("/listings/published", 1000);
      } else {
        await publishListing.mutateAsync({ id: listingId, data });
        clearListingEditDraft(listingId);
        // Backend moves to Published / Pending (UnderReview) on accept — open that tab.
        closeEditTab("/listings/published?siteStatus=pending", 1000);
      }
    } catch (pushError) {
      const actionLabel =
        mode === "draft"
          ? "Save Draft"
          : mode === "update"
            ? "Update"
            : "Publish";
      const fromApi = getApiErrorDetails(pushError, `${actionLabel} thất bại.`);
      const extra =
        (pushError as Error & { apiErrors?: string[] }).apiErrors ?? [];
      const errors = fromApi.errors.length > 0 ? fromApi.errors : extra;
      Modal.error({
        title: `${actionLabel} thất bại (4Seller)`,
        width: 640,
        content: (
          <div>
            <p
              style={{
                marginBottom: errors.length ? 12 : 0,
                whiteSpace: "pre-wrap",
              }}
            >
              {fromApi.message}
            </p>
            {errors.length > 0 ? (
              <ul style={{ margin: 0, paddingLeft: 20 }}>
                {errors.map((item) => (
                  <li key={item} style={{ marginBottom: 4 }}>
                    {item}
                  </li>
                ))}
              </ul>
            ) : null}
          </div>
        ),
      });
      void queryClient.invalidateQueries({
        queryKey: deps.listingDetailQueryKey(listingId),
      });
    } finally {
      setSaving(false);
    }
  };

  const applyBatch = (
    field: "sellerSku" | "quantity" | "salePrice" | "costPrice",
    value: string | number,
  ) => {
    const rows =
      (form.getFieldValue("variations") as ListingVariationRow[]) ?? [];
    form.setFieldValue(
      "variations",
      rows.map((row) => ({ ...row, [field]: value })),
    );
  };

  const openBatchModal = (
    field: "sellerSku" | "quantity" | "salePrice" | "costPrice",
    title: string,
  ) => {
    if (field === "sellerSku") {
      setBatchSkuModalOpen(true);
      return;
    }
    if (field === "salePrice") {
      setBatchPricingModalOpen(true);
      return;
    }
    setBatchDraftNumber(null);
    setBatchModal({ field, title });
  };

  const handleBatchPricingApply = (
    salePrices: Array<number | null>,
    formula: PricingFormulaInput,
    templateId?: string,
  ) => {
    setPricingFormula(formula);
    setSelectedPricingTemplateId(templateId);
    setPricingFormulaApplied(true);
    pricingFormulaInitializedRef.current = true;
    const appliedPricing = buildListingAppliedPricingPayload(formula, templateId);
    if (listingId) {
      saveListingAppliedPricing(listingId, formula, templateId);
    }
    const rows =
      (form.getFieldValue("variations") as ListingVariationRow[]) ?? [];
    const nextRows = rows.map((row, i) => {
      const price = salePrices[i];
      if (price == null) return row;
      return { ...row, salePrice: price };
    });
    form.setFieldValue("variations", nextRows);
    // Persist formula only (tiny jsonb) — sale prices go with Save Draft / Publish / Update.
    if (listingId) {
      void deps.listingApi
        .update(listingId, { appliedPricing })
        .then((res) => {
          if (res.success && res.data) {
            queryClient.setQueryData(deps.listingDetailQueryKey(listingId), res.data);
          }
        })
        .catch(() => {
          /* localStorage + next Save Draft still carry formula */
        });
    }
    message.success("Đã áp dụng giá bán batch cho các variant.");
  };

  const handlePricingFormulaChange = (
    formula: PricingFormulaInput,
    templateId?: string,
  ) => {
    setPricingFormula(formula);
    setSelectedPricingTemplateId(templateId);
    pricingFormulaInitializedRef.current = true;
  };

  const handleBatchSkuApply = (skus: string[]) => {
    const rows =
      (form.getFieldValue("variations") as ListingVariationRow[]) ?? [];
    form.setFieldValue(
      "variations",
      rows.map((row, i) => ({ ...row, sellerSku: skus[i] ?? row.sellerSku })),
    );
    message.success("Đã áp dụng SKU cho tất cả variant.");
  };

  const confirmBatchModal = () => {
    if (!batchModal) return;
    if (batchDraftNumber == null) {
      message.warning("Nhập giá trị batch.");
      return;
    }
    applyBatch(batchModal.field, batchDraftNumber);
    message.success(`Đã áp dụng ${batchModal.title} cho tất cả variant.`);
    setBatchModal(null);
  };

  const batchHeader = (
    label: string,
    field: "sellerSku" | "quantity" | "salePrice" | "costPrice",
    title: string,
  ) => (
    <span className="fs-batch-header">
      {label}
      <span className="fs-batch-sep">/</span>
      <Button
        type="link"
        size="small"
        className="fs-batch-link"
        onClick={() => openBatchModal(field, title)}
      >
        Batch
      </Button>
    </span>
  );

  const allVariationColumns: ColumnsType<FormListFieldData> = useMemo(
    () => [
      {
        key: "variant",
        title: "Variant",
        // Mobile: no fixed width — CSS shrinks column to label text so price/QTY fit without H-scroll.
        width: isMobile ? undefined : 160,
        onHeaderCell: isMobile
          ? () => ({ className: "fs-variations-col-variant" })
          : undefined,
        onCell: isMobile
          ? () => ({ className: "fs-variations-col-variant" })
          : undefined,
        render: (_, field) => {
          const row = form.getFieldValue([
            "variations",
            field.name,
          ]) as ListingVariationRow;
          return (
            <>
              <Text className={isMobile ? "fs-variations-variant-label" : undefined}>
                {row?.specValue || "—"}
              </Text>
              <Form.Item name={[field.name, "specValue"]} hidden>
                <Input />
              </Form.Item>
              <Form.Item name={[field.name, "mainSpec"]} hidden>
                <Input />
              </Form.Item>
              <Form.Item name={[field.name, "isDefault"]} hidden>
                <Input />
              </Form.Item>
              <Form.Item name={[field.name, "sortOrder"]} hidden>
                <InputNumber />
              </Form.Item>
              <Form.Item name={[field.name, "id"]} hidden>
                <Input />
              </Form.Item>
              <Form.Item name={[field.name, "fourSellerSkuId"]} hidden>
                <InputNumber />
              </Form.Item>
              {isMobile ? (
                <Form.Item name={[field.name, "sellerSku"]} hidden>
                  <Input />
                </Form.Item>
              ) : null}
            </>
          );
        },
      },
      {
        key: "sku",
        title: batchHeader("SKU", "sellerSku", "Batch SKU"),
        width: 150,
        render: (_, field) => (
          <Form.Item name={[field.name, "sellerSku"]} noStyle>
            <Input size="small" placeholder="Enter" />
          </Form.Item>
        ),
      },
      {
        key: "costPrice",
        title: batchHeader(
          isMobile ? "Nhập" : "Giá nhập",
          "costPrice",
          "Batch giá nhập",
        ),
        width: isMobile ? 84 : 110,
        render: (_, field) => (
          <Form.Item name={[field.name, "costPrice"]} noStyle>
            <InputNumber
              size="small"
              min={0}
              step={0.01}
              style={{ width: "100%" }}
              placeholder="0"
            />
          </Form.Item>
        ),
      },
      {
        key: "salePrice",
        title: batchHeader(
          isMobile ? "Bán*" : "Giá bán*",
          "salePrice",
          "Batch giá bán",
        ),
        width: isMobile ? 84 : 110,
        render: (_, field) => (
          <Form.Item
            name={[field.name, "salePrice"]}
            noStyle
            rules={[{ required: true, message: "Bắt buộc" }]}
            dependencies={[["variations", field.name, "costPrice"]]}
          >
            <VariationSalePriceInput variationIndex={field.name} />
          </Form.Item>
        ),
      },
      {
        key: "quantity",
        title: batchHeader("QTY*", "quantity", "Batch QTY"),
        width: isMobile ? 72 : 90,
        render: (_, field) => (
          <Form.Item
            name={[field.name, "quantity"]}
            noStyle
            rules={[{ required: true, message: "Bắt buộc" }]}
          >
            <InputNumber
              size="small"
              min={0}
              style={{ width: "100%" }}
              placeholder="0"
            />
          </Form.Item>
        ),
      },
      {
        key: "default",
        title: "Default",
        width: 72,
        align: "center" as const,
        render: (_, field) => {
          const isDefault = form.getFieldValue([
            "variations",
            field.name,
            "isDefault",
          ]) as boolean;
          return isDefault ? (
            <Tag color="blue">Default</Tag>
          ) : (
            <Button
              type="link"
              size="small"
              className="fs-link"
              onClick={() => {
                const rows = form.getFieldValue(
                  "variations",
                ) as ListingVariationRow[];
                form.setFieldValue(
                  "variations",
                  rows.map((row, i) => ({
                    ...row,
                    isDefault: i === field.name,
                  })),
                );
              }}
            >
              Đặt
            </Button>
          );
        },
      },
      {
        key: "action",
        title: isMobile ? "" : "Action",
        width: isMobile ? 44 : 56,
        align: "center" as const,
        fixed: "right" as const,
        render: (_, field) => (
          <Tooltip title="Remove variant">
            <Button
              type="text"
              danger
              aria-label="Remove variant"
              icon={<MinusCircleOutlined />}
              onClick={() => requestVariantRemoval(field.name)}
            />
          </Tooltip>
        ),
      },
    ],
    // batchHeader / openBatchModal are stable enough via form; rebuild on mobile / selected specs.
    // eslint-disable-next-line react-hooks/exhaustive-deps -- intentional narrow deps for Table column identity
    [isMobile, form, selectedColors, selectedSizes],
  );

  const variationColumns: ColumnsType<FormListFieldData> = useMemo(
    () =>
      isMobile
        ? allVariationColumns.filter((col) =>
            ["variant", "costPrice", "salePrice", "quantity"].includes(
              String(col.key),
            ),
          )
        : allVariationColumns,
    [allVariationColumns, isMobile],
  );

  const renderSpecPicker = (
    spec: VariationSpecKey,
    options: string[],
    selected: string[],
    setSelected: (next: string[]) => void,
    custom: string,
    setCustom: (v: string) => void,
    setOptions: Dispatch<SetStateAction<string[]>>,
    onSync: (next: string[]) => void,
    locked = false,
  ) => {
    const isAdding = !locked && addingSpec === spec;

    const commitCustom = () => {
      const v = custom.trim();
      if (!v) return;
      setOptions((p) => {
        const normalized = p.map(String);
        return normalized.includes(v) ? normalized : [...normalized, v];
      });
      const selectedNorm = selected.map(String);
      const next = selectedNorm.includes(v)
        ? selectedNorm
        : [...selectedNorm, v];
      setSelected(next);
      setCustom("");
      setAddingSpec(null);
      onSync(next);
    };

    // Dedupe after coercing numbers→string (JSON Size:47 vs "47" → same React key).
    const uniqueOptions = [...new Set(options.map(String))];
    const selectedSet = new Set(selected.map(String));

    return (
      <div className="fs-spec-block" key={spec}>
        <div className="fs-spec-head">
          <span className="name">{spec}</span>
          <Space size={0} className="fs-spec-actions" align="center">
            {!locked &&
              (isAdding ? (
                <Space.Compact size="small" className="fs-spec-add-compact">
                  <Input
                    size="small"
                    autoFocus
                    placeholder="Enter"
                    value={custom}
                    onChange={(e) => setCustom(e.target.value)}
                    onPressEnter={commitCustom}
                  />
                  <Tooltip title="Xác nhận">
                    <Button
                      type="primary"
                      size="small"
                      icon={<CheckOutlined />}
                      onClick={commitCustom}
                    />
                  </Tooltip>
                  <Tooltip title="Hủy">
                    <Button
                      size="small"
                      icon={<CloseOutlined />}
                      onClick={() => {
                        setCustom("");
                        setAddingSpec(null);
                      }}
                    />
                  </Tooltip>
                </Space.Compact>
              ) : (
                <Tooltip title="Thêm giá trị">
                  <Button
                    type="text"
                    size="small"
                    className="fs-spec-action-btn"
                    icon={<PlusOutlined />}
                    onClick={() => {
                      setAddingSpec(spec);
                      setCustom("");
                    }}
                  />
                </Tooltip>
              ))}
            <Tooltip title={`Xóa ${spec}`}>
              <Button
                type="text"
                danger
                size="small"
                className="fs-spec-action-btn"
                icon={<DeleteOutlined />}
                onClick={() => {
                  setAddingSpec((prev) => (prev === spec ? null : prev));
                  removeSpec(spec);
                }}
              />
            </Tooltip>
          </Space>
        </div>
        <div className="fs-spec-body">
          <div className="fs-check-grid">
            {uniqueOptions.map((opt) => (
              <Checkbox
                key={opt}
                checked={selectedSet.has(opt)}
                onChange={(e) => {
                  if (e.target.checked) {
                    const next = [...selected.map(String), opt];
                    setSelected(next);
                    onSync(next);
                    return;
                  }
                  requestSpecValueRemoval(spec, opt);
                }}
              >
                {opt}
              </Checkbox>
            ))}
          </div>
        </div>
      </div>
    );
  };

  // Published (already posted to a shop) → Update instead of Save Draft + Publish.
  const statusTooltip = statusDisplay
    ? statusDisplay.charAt(0).toUpperCase() +
      statusDisplay.slice(1).toLowerCase()
    : "—";
  const activating = activateListing.isPending;
  const deactivating = deactivateListing.isPending;
  const duplicating = duplicateListing.isPending;
  const canChangeShop = canManage && !shopChangeLocked;

  const handleActivate = () => {
    Modal.confirm({
      title: "Activate listing?",
      content: "Listing sẽ được active lại trên 4Seller.",
      okText: "Activate",
      cancelText: "Hủy",
      onOk: () => activateListing.mutateAsync(listingId),
    });
  };

  const handleDeactivate = () => {
    Modal.confirm({
      title: "Deactivate listing?",
      content: "Bạn có chắc chắn muốn hủy đăng listing này?",
      okText: "Deactivate",
      okButtonProps: { danger: true },
      cancelText: "Hủy",
      onOk: async () => {
        await deactivateListing.mutateAsync(listingId);
        closeEditTab("/listings/published");
      },
    });
  };

  const handleDuplicate = () => {
    Modal.confirm({
      title: "Duplicate listing?",
      content: "Bạn có chắc chắn muốn tạo bản sao mới?",
      okText: "Duplicate",
      cancelText: "Hủy",
      onOk: async () => {
        await duplicateListing.mutateAsync(listingId);
        closeEditTab("/listings/draft");
      },
    });
  };

  const handlePublish = () => {
    if (!descriptionRewritten) {
      Modal.warning({
        title: "Chưa Rewrite mô tả trước khi Publish",
        content: (
          <p style={{ marginBottom: 0 }}>
            Bấm <strong>1. Rewrite</strong> để AI viết lại tiêu đề + mô tả, rồi
            Publish lại. Chèn ảnh (2. Images) là tùy chọn.
          </p>
        ),
        okText: "Đã hiểu",
      });
      return;
    }

    const images = (form.getFieldValue("images") ?? []) as ListingImageRow[];
    if (!hasListingSizeChart(images)) {
      showPublishMissingSizeChartWarning();
      return;
    }
    if (
      hasListingFailedImages(
        images,
        failedUploadImageKeys,
        failedLoadImageKeysRef.current,
      )
    ) {
      showPublishFailedImagesWarning();
      focusFailedUploadImage();
      return;
    }

    const rows = (form.getFieldValue("variations") ??
      []) as ListingVariationRow[];
    const lowMarginRows = rows.filter((row) => {
      const tone = getSaleVsCostTone(row.salePrice, row.costPrice);
      return tone === "danger" || tone === "warning";
    });

    if (lowMarginRows.length === 0) {
      void persist("publish");
      return;
    }

    const samples = lowMarginRows
      .slice(0, 5)
      .map((row) => {
        const sku = String(row.sellerSku || "").trim() || "SKU trống";
        const sale = Number(row.salePrice);
        const cost = Number(row.costPrice);
        const saleLabel = Number.isFinite(sale) ? `$${sale.toFixed(2)}` : "—";
        const costLabel = Number.isFinite(cost) ? `$${cost.toFixed(2)}` : "—";
        const tone = getSaleVsCostTone(row.salePrice, row.costPrice);
        const hint =
          tone === "danger"
            ? "≤ giá nhập"
            : `≤ giá nhập +$${SALE_OVER_COST_MARGIN_USD}`;
        return `${sku}: bán ${saleLabel} / nhập ${costLabel} (${hint})`;
      })
      .join("\n");
    const more =
      lowMarginRows.length > 5
        ? `\n… và ${lowMarginRows.length - 5} variant khác`
        : "";

    Modal.confirm({
      title: "Giá bán thấp so với giá nhập",
      content: (
        <div>
          <p style={{ marginBottom: 8 }}>
            Có {lowMarginRows.length} variant giá bán ≤ giá nhập + $
            {SALE_OVER_COST_MARGIN_USD} USD (đỏ: ≤ giá nhập; vàng: lớn hơn giá
            nhập nhưng vẫn ≤ +$
            {SALE_OVER_COST_MARGIN_USD}). Bạn có muốn tiếp tục Publish?
          </p>
          <pre className="fs-confirm-sample">
            {samples}
            {more}
          </pre>
        </div>
      ),
      okText: "Publish",
      cancelText: "Hủy",
      onOk: () => persist("publish"),
    });
  };

  const pushDisabled =
    !listing?.hasRawPayload ||
    shopMetadataLoading ||
    !String(watchedWarehouseId ?? "").trim();

  const saveButtons = isPublished ? (
    <>
      {isInactive ? (
        <Button
          icon={<PlayCircleOutlined />}
          loading={activating}
          onClick={handleActivate}
        >
          Activate
        </Button>
      ) : null}
      {isActive ? (
        <Button danger loading={deactivating} onClick={handleDeactivate}>
          Deactivate
        </Button>
      ) : null}
      <Button
        type="primary"
        icon={<SendOutlined />}
        loading={saving}
        disabled={pushDisabled}
        onClick={() => void persist("update")}
      >
        Update
      </Button>
    </>
  ) : (
    <>
      <Button
        loading={saving}
        disabled={!listing?.hasRawPayload}
        onClick={() => void persist("draft")}
      >
        Save Draft
      </Button>
      <Button
        type="primary"
        icon={<SendOutlined />}
        loading={saving}
        disabled={pushDisabled}
        onClick={handlePublish}
      >
        Publish
      </Button>
    </>
  );

  // Keep `form` connected while loading / not-found so Form.useWatch + setFieldsValue
  // during hydrate do not warn "useForm is not connected to any Form element".
  if (isLoading && !listing) {
    return (
      <>
        <Form form={form} component={false} />
        <PageBootSpin tip="Đang tải listing…" />
      </>
    );
  }

  if (!listing) {
    return (
      <>
        <Form form={form} component={false} />
        <Text type="danger">Không tìm thấy listing.</Text>
      </>
    );
  }

  return (
    <div>
      <div className="fs-form-topbar">
        <div className="fs-form-topbar-main">
          <Button
            icon={<ArrowLeftOutlined />}
            onClick={() => navigate("/listings/draft")}
            style={{ flexShrink: 0 }}
          />
          <Title
            level={5}
            className="fs-form-topbar-title"
            ellipsis={{ tooltip: true }}
          >
            {String(deferredProductName).trim() ||
              listing.productName ||
              "Sửa listing"}
          </Title>
        </div>
        <Space wrap className="fs-form-topbar-actions">
          <Tooltip title="Đồng bộ lại từ 4Seller">
            <Button
              icon={<SyncOutlined />}
              loading={refreshingRemote}
              onClick={() => void syncFromRemote()}
            >
              Sync
            </Button>
          </Tooltip>
          {canManage ? (
            <Tooltip title="Tạo bản sao mới">
              <Button
                icon={<CopyOutlined />}
                loading={duplicating}
                disabled={!listing.fourSellerListingId}
                onClick={handleDuplicate}
              >
                Duplicate
              </Button>
            </Tooltip>
          ) : null}
          {canManage ? saveButtons : null}
        </Space>
      </div>

      {!listing.hasRawPayload ? (
        <Alert
          type="warning"
          showIcon
          style={{ marginBottom: 16 }}
          title="Listing chưa có raw detail từ 4Seller. Đồng bộ lại (có batch-detail) trước khi Save Draft / Publish."
        />
      ) : null}

      <Form
        form={form}
        layout="horizontal"
        labelCol={{ flex: "140px" }}
        wrapperCol={{ flex: 1 }}
        labelAlign="left"
        colon={false}
        disabled={!canManage}
        size="middle"
        preserve
        requiredMark={(label, { required }) => (
          <>
            {label}
            {required ? (
              <span style={{ color: "#ff4d4f", marginLeft: 2 }}> *</span>
            ) : null}
          </>
        )}
      >
        <Card
          title={
            <Space size={8} align="center">
              <Tooltip title={statusTooltip}>
                <span
                  aria-label={statusTooltip}
                  style={{
                    width: 8,
                    height: 8,
                    borderRadius: "50%",
                    background: listingStatusDotColor(statusDisplay),
                    display: "inline-block",
                    flexShrink: 0,
                  }}
                />
              </Tooltip>
              <span>Product Info</span>
            </Space>
          }
          size="small"
          style={{ marginBottom: 16 }}
          extra={
            <>
              <Form.Item
                name="shopId"
                hidden
                rules={[{ required: true, message: "Chọn shop Publish" }]}
              >
                <Input />
              </Form.Item>
              <Tooltip
                title={
                  shopChangeLocked
                    ? "Không thể đổi shop khi listing đang Active hoặc Inactive"
                    : undefined
                }
              >
                <Dropdown
                  trigger={["click"]}
                  open={canChangeShop ? shopPickerOpen : false}
                  onOpenChange={(open) => {
                    if (!canChangeShop) return;
                    setShopPickerOpen(open);
                    if (!open) setShopSearch("");
                  }}
                  disabled={!canChangeShop}
                  popupRender={() => (
                    <div
                      className="fs-shop-picker-dropdown"
                      onMouseDown={(e) => e.preventDefault()}
                    >
                      <Input
                        allowClear
                        size="small"
                        prefix={
                          <SearchOutlined className="fs-shop-picker-search-icon" />
                        }
                        placeholder="Tìm shop..."
                        value={shopSearch}
                        onChange={(e) => setShopSearch(e.target.value)}
                        style={{ marginBottom: 8 }}
                      />
                      <div style={{ maxHeight: 280, overflowY: "auto" }}>
                        {filteredPublishShopOptions.length === 0 ? (
                          <div className="fs-shop-picker-empty">
                            Không có shop phù hợp
                          </div>
                        ) : (
                          filteredPublishShopOptions.map((opt) => {
                            const shop = shops.find((s) => s.id === opt.value);
                            const selected = opt.value === watchedShopId;
                            return (
                              <div
                                key={opt.value}
                                title={opt.title}
                                className={[
                                  "fs-shop-picker-option",
                                  selected ? "is-selected" : "",
                                  opt.disabled ? "is-disabled" : "",
                                ]
                                  .filter(Boolean)
                                  .join(" ")}
                                onClick={() => {
                                  if (opt.disabled || !canChangeShop) return;
                                  const shopId = String(opt.value);
                                  const next = shops.find(
                                    (s) => s.id === shopId,
                                  );
                                  if (next?.region)
                                    setCategorySite(next.region);
                                  // Reset shop-scoped metadata like 4Seller: Brand → No brand, clear warehouse.
                                  shopMetaAppliedRef.current = null;
                                  form.setFieldsValue({
                                    shopId,
                                    brandId: NO_BRAND_ID,
                                    brandName: NO_BRAND_NAME,
                                    warehouseId: undefined,
                                  });
                                  setShopPickerOpen(false);
                                  setShopSearch("");
                                }}
                              >
                                <Avatar
                                  size={22}
                                  src={shop?.avatarUrl}
                                  icon={<ShopOutlined />}
                                  style={{
                                    flexShrink: 0,
                                    background: shop?.avatarUrl
                                      ? undefined
                                      : "#1677ff",
                                  }}
                                />
                                <span className="fs-shop-picker-option-label">
                                  {opt.label}
                                </span>
                                {selected ? (
                                  <CheckOutlined className="fs-shop-picker-check" />
                                ) : null}
                              </div>
                            );
                          })
                        )}
                      </div>
                    </div>
                  )}
                >
                  <Button
                    type="default"
                    disabled={!canChangeShop}
                    className="fs-shop-picker-trigger"
                  >
                    <Avatar
                      size={18}
                      src={selectedPublishShop?.avatarUrl}
                      icon={<ShopOutlined />}
                      style={{
                        flexShrink: 0,
                        background: selectedPublishShop?.avatarUrl
                          ? undefined
                          : "#1677ff",
                      }}
                    />
                    <span className="fs-shop-picker-trigger-label">
                      {selectedPublishShop
                        ? selectedPublishShop.profileName
                          ? `${selectedPublishShop.name} (${selectedPublishShop.profileName})`
                          : selectedPublishShop.name
                        : "Chọn shop Publish"}
                    </span>
                    {!shopChangeLocked ? (
                      <DownOutlined className="fs-shop-picker-caret" />
                    ) : null}
                  </Button>
                </Dropdown>
              </Tooltip>
            </>
          }
        >
          <Form.Item label="Product name" required>
            {/* Flex row rather than Input addonAfter: an input-group addon is its own bordered
                cell and renders 2px taller than the field beside it. Same shape as Category. */}
            <div
              style={{
                display: "flex",
                width: "100%",
                gap: 8,
                alignItems: "center",
              }}
            >
              <Form.Item
                name="productName"
                noStyle
                rules={[
                  { required: true, message: "Nhập tên sản phẩm" },
                  { max: 255 },
                ]}
              >
                <Input
                  maxLength={255}
                  style={{ flex: 1, minWidth: 0 }}
                  suffix={
                    <Text type="secondary" style={{ fontSize: 12 }}>
                      {String(productName).length} / 255
                    </Text>
                  }
                />
              </Form.Item>
              {/* Same action as the card's 1. Rewrite — mirrored here because the title it
                  overwrites is the field the seller is looking at. Shares all its state. */}
              <Button
                type="default"
                icon={<ThunderboltOutlined />}
                loading={rewriteDescription.isPending}
                disabled={rewriteLocked}
                onClick={() => void applyAiRewriteDescription()}
                title={rewriteTitleHint}
                className={rewriteLocked ? undefined : "fs-ai-action-btn"}
                style={
                  descriptionRewritten
                    ? {
                        flexShrink: 0,
                        color: "#389e0d",
                        borderColor: "#b7eb8f",
                        background: "#f6ffed",
                      }
                    : undefined
                }
              >
                {descriptionRewritten ? "Rewrite ✓" : "Rewrite"}
              </Button>
              {rewriteUndoHtml !== null ? (
                <Button
                  type="default"
                  icon={<UndoOutlined />}
                  onClick={() => void undoAiRewriteDescription()}
                  title="Khôi phục tiêu đề, mô tả, search terms và highlights trước khi AI viết lại"
                  style={{ flexShrink: 0 }}
                />
              ) : null}
            </div>
          </Form.Item>
          <Form.Item name="categoryId" hidden>
            <Input />
          </Form.Item>
          <Form.Item name="categoryName" hidden>
            <Input />
          </Form.Item>
          {/* Brand luôn cố định No brand — không hiển thị picker. */}
          <Form.Item name="brandId" hidden initialValue={NO_BRAND_ID}>
            <Input />
          </Form.Item>
          <Form.Item name="brandName" hidden initialValue={NO_BRAND_NAME}>
            <Input />
          </Form.Item>
          <Form.Item label="Category" required>
            <div
              style={{
                display: "flex",
                width: "100%",
                gap: 8,
                alignItems: "center",
              }}
            >
              <Select
                value={categorySite}
                style={{ width: 100, flexShrink: 0 }}
                options={categorySiteOptions}
                onChange={(site) => {
                  setCategorySite(site);
                  setCategoryPath(undefined);
                  form.setFieldsValue({
                    categoryId: undefined,
                    categoryName: undefined,
                  });
                }}
              />
              <div style={{ flex: 1, minWidth: 0 }}>
                <CategoryCascader
                  site={categorySite}
                  value={categoryPath}
                  onChange={(path, selected) => {
                    setCategoryPath(path);
                    form.setFieldsValue({
                      categoryId: selected?.id,
                      categoryName: selected?.name,
                    });
                  }}
                />
              </div>
              <Button
                type="default"
                icon={<ThunderboltOutlined />}
                loading={recommendCategory.isPending}
                onClick={() => void applyAiCategory()}
                title="AI recommend category"
                className="fs-ai-action-btn"
              >
                AI
              </Button>
            </div>
          </Form.Item>
          <Form.Item name="listingSourceUrl" hidden rules={[{ max: 500 }]}>
            <Input />
          </Form.Item>
          <Form.Item
            name="productType"
            label="Variations"
            rules={[{ required: true }]}
          >
            <Radio.Group>
              <Radio value="SingleSku">No Variations</Radio>
              <Radio value="MultiSku">Has Variations</Radio>
            </Radio.Group>
          </Form.Item>
        </Card>

        {!mediaReady ? (
          <>
            <DeferredSection title="Variations" minHeight={300} rows={8} />
            <DeferredSection title="Sản phẩm" minHeight={400} rows={10} />
          </>
        ) : (
          <>
        {productType === "MultiSku" ? (
          <Card
            title={
              <div className="fs-variations-head">
                <span>
                  {`Variations${
                    selectedColors.length +
                      selectedSizes.length +
                      selectedItems.length >
                    0
                      ? ` (${(variations as ListingVariationRow[]).filter((v) => v.specValue).length})`
                      : ""
                  }`}
                </span>
                {!variationSpecsFromFourSeller && (
                  <Space wrap size={4} className="fs-variations-quick-add">
                    {!activeSpecs.has("Color") && (
                      <Button
                        type="text"
                        size="small"
                        className="fs-quick-spec-btn"
                        icon={<PlusCircleFilled />}
                        onClick={() => enableSpec("Color")}
                      >
                        Color
                      </Button>
                    )}
                    {!activeSpecs.has("Size") && (
                      <Button
                        type="text"
                        size="small"
                        className="fs-quick-spec-btn"
                        icon={<PlusCircleFilled />}
                        onClick={() => enableSpec("Size")}
                      >
                        Size
                      </Button>
                    )}
                    {!activeSpecs.has("Number of Items") && (
                      <Button
                        type="text"
                        size="small"
                        className="fs-quick-spec-btn"
                        icon={<PlusCircleFilled />}
                        onClick={() => enableSpec("Number of Items")}
                      >
                        Number of Items
                      </Button>
                    )}
                    {activeSpecs.size === 0 && (
                      <Button
                        type="link"
                        size="small"
                        className="fs-link"
                        icon={<PlusOutlined />}
                        onClick={() => enableSpec("Color")}
                      >
                        Add Variation
                      </Button>
                    )}
                  </Space>
                )}
              </div>
            }
            size="small"
            style={{ marginBottom: 16 }}
          >
            <Form.Item name="mainSpecification" hidden>
              <Input />
            </Form.Item>

            {(activeSpecs.has("Color") ||
              activeSpecs.has("Size") ||
              activeSpecs.has("Number of Items")) && (
              <div className="fs-spec-list">
                {activeSpecs.has("Color") &&
                  renderSpecPicker(
                    "Color",
                    colorOptions,
                    selectedColors,
                    setSelectedColors,
                    customColor,
                    setCustomColor,
                    setColorOptions,
                    (next) => syncVariationsFromSpecs(next),
                    variationSpecsFromFourSeller,
                  )}
                {activeSpecs.has("Size") &&
                  renderSpecPicker(
                    "Size",
                    sizeOptions,
                    selectedSizes,
                    setSelectedSizes,
                    customSize,
                    setCustomSize,
                    setSizeOptions,
                    (next) => syncVariationsFromSpecs(undefined, next),
                    variationSpecsFromFourSeller,
                  )}
                {activeSpecs.has("Number of Items") &&
                  renderSpecPicker(
                    "Number of Items",
                    itemOptions,
                    selectedItems,
                    setSelectedItems,
                    customItem,
                    setCustomItem,
                    setItemOptions,
                    (next) =>
                      syncVariationsFromSpecs(undefined, undefined, next),
                    variationSpecsFromFourSeller,
                  )}
              </div>
            )}

            {activeSpecs.size === 0 && (
              <Text type="secondary">
                Chọn Color, Size hoặc Number of Items ở trên để tạo variant.
              </Text>
            )}

            {activeSpecs.size > 0 &&
              (selectedColors.length > 0 ||
                selectedSizes.length > 0 ||
                selectedItems.length > 0) && (
                <Form.List name="variations">
                  {(fields) => {
                    const hasMore = fields.length > VARIANT_TABLE_PREVIEW_COUNT;
                    const visibleFields = variationsTableExpanded
                      ? fields
                      : fields.slice(0, VARIANT_TABLE_PREVIEW_COUNT);

                    return (
                      <>
                        {/* Collapsed rows still need mounted Form.Items or validateFields drops them. */}
                        {fields
                          .filter(
                            (f) => !visibleFields.some((v) => v.key === f.key),
                          )
                          .map((field) => (
                            <div
                              key={`preserve-${field.key}`}
                              style={{ display: "none" }}
                              aria-hidden
                            >
                              <Form.Item name={[field.name, "id"]} hidden>
                                <Input />
                              </Form.Item>
                              <Form.Item
                                name={[field.name, "fourSellerSkuId"]}
                                hidden
                              >
                                <InputNumber />
                              </Form.Item>
                              <Form.Item
                                name={[field.name, "specValue"]}
                                hidden
                              >
                                <Input />
                              </Form.Item>
                              <Form.Item name={[field.name, "mainSpec"]} hidden>
                                <Input />
                              </Form.Item>
                              <Form.Item
                                name={[field.name, "sellerSku"]}
                                hidden
                              >
                                <Input />
                              </Form.Item>
                              <Form.Item
                                name={[field.name, "costPrice"]}
                                hidden
                              >
                                <InputNumber />
                              </Form.Item>
                              <Form.Item
                                name={[field.name, "salePrice"]}
                                hidden
                              >
                                <InputNumber />
                              </Form.Item>
                              <Form.Item
                                name={[field.name, "originalPrice"]}
                                hidden
                              >
                                <InputNumber />
                              </Form.Item>
                              <Form.Item name={[field.name, "quantity"]} hidden>
                                <InputNumber />
                              </Form.Item>
                              <Form.Item name={[field.name, "barcode"]} hidden>
                                <Input />
                              </Form.Item>
                              <Form.Item
                                name={[field.name, "isDefault"]}
                                hidden
                              >
                                <Input />
                              </Form.Item>
                              <Form.Item
                                name={[field.name, "sortOrder"]}
                                hidden
                              >
                                <InputNumber />
                              </Form.Item>
                            </div>
                          ))}
                        <Table
                          className="fs-variations-table"
                          rowKey="key"
                          size="small"
                          pagination={false}
                          scroll={isMobile ? undefined : { x: 900 }}
                          dataSource={visibleFields}
                          columns={variationColumns}
                        />
                        {hasMore ? (
                          <div style={{ textAlign: "center", marginTop: 8 }}>
                            <Button
                              type="link"
                              size="small"
                              onClick={() =>
                                setVariationsTableExpanded((open) => !open)
                              }
                            >
                              {variationsTableExpanded ? (
                                <>
                                  Thu gọn <UpOutlined />
                                </>
                              ) : (
                                <>
                                  Xem thêm{" "}
                                  {fields.length - VARIANT_TABLE_PREVIEW_COUNT}{" "}
                                  variant <DownOutlined />
                                </>
                              )}
                            </Button>
                          </div>
                        ) : null}
                      </>
                    );
                  }}
                </Form.List>
              )}
          </Card>
        ) : (
          <Card title="Giá & tồn" size="small" style={{ marginBottom: 16 }}>
            <Form.List name="variations">
              {(fields) => {
                const field = fields[0];
                if (!field) return null;
                return (
                  <Row gutter={16}>
                    <Col xs={24} md={6}>
                      <Form.Item
                        name={[field.name, "sellerSku"]}
                        label="SKU"
                        labelCol={{ span: 24 }}
                        wrapperCol={{ span: 24 }}
                      >
                        <Input size="small" />
                      </Form.Item>
                    </Col>
                    <Col xs={24} md={6}>
                      <Form.Item
                        name={[field.name, "costPrice"]}
                        label="Giá nhập"
                        labelCol={{ span: 24 }}
                        wrapperCol={{ span: 24 }}
                      >
                        <InputNumber
                          size="small"
                          min={0}
                          step={0.01}
                          style={{ width: "100%" }}
                        />
                      </Form.Item>
                    </Col>
                    <Col xs={24} md={6}>
                      <Form.Item
                        name={[field.name, "salePrice"]}
                        label="Giá bán"
                        labelCol={{ span: 24 }}
                        wrapperCol={{ span: 24 }}
                        dependencies={[["variations", field.name, "costPrice"]]}
                      >
                        <VariationSalePriceInput variationIndex={field.name} />
                      </Form.Item>
                    </Col>
                    <Col xs={24} md={6}>
                      <Form.Item
                        name={[field.name, "quantity"]}
                        label="Số lượng"
                        labelCol={{ span: 24 }}
                        wrapperCol={{ span: 24 }}
                      >
                        <InputNumber
                          size="small"
                          min={0}
                          style={{ width: "100%" }}
                        />
                      </Form.Item>
                    </Col>
                    <Form.Item name={[field.name, "mainSpec"]} hidden>
                      <Input />
                    </Form.Item>
                    <Form.Item name={[field.name, "specValue"]} hidden>
                      <Input />
                    </Form.Item>
                    <Form.Item name={[field.name, "isDefault"]} hidden>
                      <Input />
                    </Form.Item>
                    <Form.Item name={[field.name, "sortOrder"]} hidden>
                      <InputNumber />
                    </Form.Item>
                    <Form.Item name={[field.name, "id"]} hidden>
                      <Input />
                    </Form.Item>
                    <Form.Item name={[field.name, "fourSellerSkuId"]} hidden>
                      <InputNumber />
                    </Form.Item>
                  </Row>
                );
              }}
            </Form.List>
          </Card>
        )}

        <ImagesWatchIsolator
          key={listingId}
          failedLoadKeysRef={failedLoadImageKeysRef}
        >
          {({
            images: displayImages,
            watchedImages,
            setOptimisticImages,
            sizeChartUrl,
            videoUrl,
            failedLoadKeys,
            markImageLoadFailed,
            markImageLoadOk,
          }) => (
            <>
              <FailedUploadImageAutofocus
                mediaReady={mediaReady}
                syncError={listing?.syncError}
                failedKeys={failedUploadImageKeys}
                images={watchedImages}
                onFocus={focusFailedUploadImage}
                focusedRef={focusedFailedSyncErrorRef}
              />
              <FailedUploadDupeStatusSync
                images={displayImages}
                failedKeys={failedUploadImageKeys}
                onChange={onFailedUploadDupesRemainChange}
              />
        <Card
          title="Sản phẩm"
          size="small"
          style={{ marginBottom: 16 }}
          extra={<Text type="secondary">Ảnh đầu = Main</Text>}
        >
          <div className="fs-media-variations">
            {hasHttpImages(displayImages) &&
            (selectedColors.length === 0 || !variantImageOn) ? (
              <Alert
                type="error"
                showIcon
                className="fs-default-image-count-alert"
                title="Có ảnh URL dùng http:// — 4Seller từ chối (Invalid Para). Đổi sang https:// hoặc xóa ảnh đó (ô đỏ)."
                style={{ marginBottom: 8 }}
              />
            ) : null}
            <div className="fs-gallery">
              <Form.List name="images">
                {(fields, { remove, move }) => {
                  const productFields = fields.filter((f) =>
                    isProductMediaType(
                      watchedImages[f.name]?.mediaType as string | undefined,
                      watchedImages[f.name]?.colorValue as string | undefined,
                      watchedImages[f.name]?.listingSkuId as string | undefined,
                    ),
                  );
                  const variantFieldsForColor = galleryColor
                    ? fields.filter((f) =>
                        isVariantColorImage(
                          {
                            mediaType: watchedImages[f.name]?.mediaType as
                              string | undefined,
                            colorValue: watchedImages[f.name]?.colorValue as
                              string | undefined,
                          },
                          galleryColor,
                        ),
                      )
                    : [];
                  const viewingVariant =
                    !!galleryColor &&
                    variantFieldsForColor.some((f) =>
                      String(watchedImages[f.name]?.url ?? "").trim(),
                    );
                  const displayFields = viewingVariant
                    ? variantFieldsForColor
                    : productFields;
                  const safeIndex = Math.min(
                    activeImageIndex,
                    Math.max(displayFields.length - 1, 0),
                  );
                  const activeField = displayFields[safeIndex];
                  const activeUrl =
                    (activeField
                      ? (watchedImages[activeField.name]?.url as
                          string | undefined)
                      : undefined) || "";
                  const galleryPreviewUrls = displayFields
                    .map((f) => String(watchedImages[f.name]?.url ?? "").trim())
                    .filter(Boolean);

                  const syncImageMeta = () => {
                    const list = [
                      ...((form.getFieldValue("images") as ListingImageRow[]) ??
                        []),
                    ];
                    const products = list.filter((img) =>
                      isSharedProductImage(img),
                    );
                    const variantColors = list.filter((img) =>
                      isVariantColorImage(img),
                    );
                    const extras = list.filter(
                      (img) =>
                        !isSharedProductImage(img) && !isVariantColorImage(img),
                    );
                    form.setFieldValue("images", [
                      ...products.map((img, i) => ({
                        ...img,
                        mediaType: i === 0 ? "Main" : "Detail",
                        sortOrder: i,
                        colorValue: undefined,
                      })),
                      ...variantColors.map((img, i) => ({
                        ...img,
                        mediaType: "Detail",
                        sortOrder: products.length + i,
                      })),
                      ...extras.map((img, i) => ({
                        ...img,
                        sortOrder: products.length + variantColors.length + i,
                      })),
                    ]);
                  };

                  const openAddForGallery = () => {
                    if (viewingVariant && galleryColor)
                      setAddingVariantColor(galleryColor);
                    else setAddingMedia("product");
                  };

                  const handleDropReorder = (
                    fromLocal: number,
                    toLocal: number,
                  ) => {
                    if (fromLocal === toLocal || fromLocal < 0 || toLocal < 0)
                      return;
                    const fromField = displayFields[fromLocal];
                    const toField = displayFields[toLocal];
                    if (!fromField || !toField) return;
                    move(fromField.name, toField.name);
                    queueMicrotask(() => {
                      syncImageMeta();
                      setActiveImageIndex(toLocal);
                    });
                  };

                  const removeDisplayAt = (localIdx: number) => {
                    const target = displayFields[localIdx];
                    if (!target) return;
                    if (viewingVariant) {
                      remove(target.name);
                      queueMicrotask(() => {
                        syncImageMeta();
                        const remaining = (
                          (form.getFieldValue("images") as ListingImageRow[]) ??
                          []
                        ).filter(
                          (img) =>
                            galleryColor &&
                            isVariantColorImage(img, galleryColor) &&
                            String(img?.url ?? "").trim(),
                        );
                        if (remaining.length === 0) {
                          setGalleryColor(null);
                          setActiveImageIndex(0);
                          setGalleryPreviewOpen(false);
                          return;
                        }
                        setActiveImageIndex((prev) => {
                          if (prev === localIdx) return Math.max(0, prev - 1);
                          if (prev > localIdx) return prev - 1;
                          return prev;
                        });
                      });
                      return;
                    }
                    if (displayFields.length <= 1) {
                      form.setFieldValue(["images", target.name, "url"], "");
                      setGalleryPreviewOpen(false);
                      return;
                    }
                    remove(target.name);
                    queueMicrotask(() => {
                      syncImageMeta();
                      setActiveImageIndex((prev) => {
                        if (prev === localIdx) return Math.max(0, prev - 1);
                        if (prev > localIdx) return prev - 1;
                        return prev;
                      });
                    });
                  };

                  return (
                    <>
                      <div className="fs-gallery-stage">
                        <div
                          className={[
                            "fs-gallery-thumbs",
                            dragImageIndex != null &&
                            dragOverImageIndex != null &&
                            dragImageIndex !== dragOverImageIndex
                              ? "is-live-sorting"
                              : "",
                          ]
                            .filter(Boolean)
                            .join(" ")}
                          onDragOver={(e) => {
                            if (dragImageIndex == null) return;
                            e.preventDefault();
                            e.dataTransfer.dropEffect = "move";
                            const next = dragReorderIndexFromRects(
                              e.clientX,
                              e.clientY,
                              galleryDragRectsRef.current,
                            );
                            if (
                              next != null &&
                              dragOverImageIndex !== next
                            ) {
                              setDragOverImageIndex(next);
                            }
                          }}
                          onDrop={(e) => {
                            e.preventDefault();
                            const from = Number(
                              e.dataTransfer.getData("text/plain"),
                            );
                            const to =
                              dragReorderIndexFromRects(
                                e.clientX,
                                e.clientY,
                                galleryDragRectsRef.current,
                              ) ?? dragOverImageIndex;
                            if (to != null) handleDropReorder(from, to);
                            galleryDragRectsRef.current = [];
                            setDragImageIndex(null);
                            setDragOverImageIndex(null);
                          }}
                        >
                          {displayFields.map((field, idx) => {
                            const url =
                              (watchedImages[field.name]?.url as
                                | string
                                | undefined) || "";
                            const uploadFailed = isFailedUploadImage(
                              url,
                              displayImages,
                              galleryColor
                                ? { kind: "color", color: galleryColor }
                                : { kind: "main" },
                            );
                            const loadFailed = isLoadFailedImage(
                              url,
                              failedLoadKeys,
                            );
                            const httpUrl = isHttpImageUrl(url);
                            const imageFailed =
                              httpUrl || uploadFailed || loadFailed;
                            const slotShift =
                              dragImageIndex != null &&
                              dragOverImageIndex != null
                                ? dragReorderSlotShift(
                                    dragImageIndex,
                                    dragOverImageIndex,
                                    idx,
                                  )
                                : 0;
                            const thumbTransform = dragReorderTransform(
                              slotShift,
                              "y",
                              8,
                            );
                            return (
                              <div
                                key={field.key}
                                className={[
                                  "fs-gallery-thumb",
                                  idx === safeIndex ? "is-active" : "",
                                  imageFailed ? "is-upload-failed" : "",
                                  loadFailed ? "is-load-failed" : "",
                                  dragImageIndex === idx ? "is-dragging" : "",
                                  dragOverImageIndex === idx &&
                                  dragImageIndex !== idx
                                    ? "is-drag-over"
                                    : "",
                                ]
                                  .filter(Boolean)
                                  .join(" ")}
                                style={
                                  thumbTransform
                                    ? { transform: thumbTransform }
                                    : undefined
                                }
                                draggable={displayFields.length > 1}
                                title={
                                  imageFailed
                                    ? failedListingImageTitle(
                                        httpUrl,
                                        uploadFailed,
                                        loadFailed,
                                        "",
                                      )
                                    : displayFields.length > 1
                                      ? "Nhấn giữ để đổi thứ tự"
                                      : undefined
                                }
                                onClick={() => setActiveImageIndex(idx)}
                                onDragStart={(e) => {
                                  const parent = e.currentTarget.parentElement;
                                  if (parent) {
                                    galleryDragRectsRef.current =
                                      snapshotDragReorderRects(
                                        parent,
                                        ".fs-gallery-thumb:not(.fs-gallery-thumb-add)",
                                      );
                                  }
                                  setDragImageIndex(idx);
                                  setDragOverImageIndex(idx);
                                  e.dataTransfer.effectAllowed = "move";
                                  e.dataTransfer.setData(
                                    "text/plain",
                                    String(idx),
                                  );
                                }}
                                onDragEnd={() => {
                                  galleryDragRectsRef.current = [];
                                  setDragImageIndex(null);
                                  setDragOverImageIndex(null);
                                }}
                              >
                                {url ? (
                                  <img
                                    src={url}
                                    alt=""
                                    draggable={false}
                                    onError={() => markImageLoadFailed(url)}
                                    onLoad={() => markImageLoadOk(url)}
                                  />
                                ) : (
                                  <span className="fs-gallery-thumb-empty">
                                    URL
                                  </span>
                                )}
                                {imageFailed ? (
                                  <span
                                    className="fs-gallery-thumb-fail-badge"
                                    aria-label="Ảnh lỗi upload"
                                  >
                                    !
                                  </span>
                                ) : null}
                                {!viewingVariant && idx === 0 && (
                                  <span className="fs-gallery-thumb-badge">
                                    Main
                                  </span>
                                )}
                                <div className="fs-gallery-thumb-actions">
                                  {displayFields.length > 1 && (
                                    <span
                                      className="fs-gallery-thumb-drag"
                                      aria-hidden
                                    >
                                      <HolderOutlined />
                                    </span>
                                  )}
                                  {(displayFields.length > 1 || !!url) && (
                                    <button
                                      type="button"
                                      className="fs-gallery-thumb-delete"
                                      title="Xóa"
                                      onClick={(e) => {
                                        e.stopPropagation();
                                        removeDisplayAt(idx);
                                      }}
                                    >
                                      <CloseOutlined />
                                    </button>
                                  )}
                                </div>
                              </div>
                            );
                          })}
                          <button
                            type="button"
                            className="fs-gallery-thumb fs-gallery-thumb-add"
                            onClick={openAddForGallery}
                            title={
                              viewingVariant && galleryColor
                                ? `Thêm ảnh ${galleryColor}`
                                : "Thêm ảnh"
                            }
                          >
                            <PlusOutlined />
                          </button>
                        </div>

                        <div className="fs-gallery-main">
                          {activeUrl ? (
                            <Image.PreviewGroup
                              items={galleryPreviewUrls}
                              classNames={{
                                popup: { root: "fs-gallery-preview-root" },
                              }}
                              preview={{
                                open: galleryPreviewOpen,
                                current: galleryPreviewIndex,
                                onOpenChange: (open) => {
                                  if (open) {
                                    const idx =
                                      galleryPreviewUrls.indexOf(activeUrl);
                                    setGalleryPreviewIndex(idx >= 0 ? idx : 0);
                                  }
                                  setGalleryPreviewOpen(open);
                                },
                                onChange: (current) => {
                                  setGalleryPreviewIndex(current);
                                  const urlFields = displayFields.filter((f) =>
                                    String(
                                      watchedImages[f.name]?.url ?? "",
                                    ).trim(),
                                  );
                                  const target = urlFields[current];
                                  if (target) {
                                    const fieldIdx = displayFields.findIndex(
                                      (f) => f.key === target.key,
                                    );
                                    if (fieldIdx >= 0)
                                      setActiveImageIndex(fieldIdx);
                                  }
                                },
                                actionsRender: (originalNode, info) => {
                                  const current =
                                    typeof info.current === "number"
                                      ? info.current
                                      : galleryPreviewIndex;
                                  return (
                                    <Space
                                      size={12}
                                      className="fs-gallery-preview-actions"
                                    >
                                      {originalNode}
                                      <Tooltip title="Xóa ảnh">
                                        <DeleteOutlined
                                          className="fs-gallery-preview-delete"
                                          onClick={(e) => {
                                            e.stopPropagation();
                                            const urlFields =
                                              displayFields.filter((f) =>
                                                String(
                                                  watchedImages[f.name]?.url ??
                                                    "",
                                                ).trim(),
                                              );
                                            const target = urlFields[current];
                                            if (!target) return;
                                            const fieldIdx =
                                              displayFields.findIndex(
                                                (f) => f.key === target.key,
                                              );
                                            removeDisplayAt(fieldIdx);
                                            queueMicrotask(() => {
                                              const remaining = (
                                                (form.getFieldValue(
                                                  "images",
                                                ) as ListingImageRow[]) ?? []
                                              ).filter((img) =>
                                                viewingVariant && galleryColor
                                                  ? isVariantColorImage(
                                                      img,
                                                      galleryColor,
                                                    ) &&
                                                    String(
                                                      img?.url ?? "",
                                                    ).trim()
                                                  : isSharedProductImage(img) &&
                                                    String(
                                                      img?.url ?? "",
                                                    ).trim(),
                                              );
                                              if (remaining.length === 0) {
                                                setGalleryPreviewOpen(false);
                                                setGalleryPreviewIndex(0);
                                                return;
                                              }
                                              setGalleryPreviewIndex(
                                                Math.min(
                                                  current,
                                                  remaining.length - 1,
                                                ),
                                              );
                                            });
                                          }}
                                        />
                                      </Tooltip>
                                    </Space>
                                  );
                                },
                              }}
                            >
                              <Image
                                src={activeUrl}
                                alt={
                                  viewingVariant && galleryColor
                                    ? galleryColor
                                    : "Product"
                                }
                                className="fs-gallery-main-img"
                                style={{
                                  width: "100%",
                                  height: "100%",
                                  objectFit: "contain",
                                }}
                              />
                            </Image.PreviewGroup>
                          ) : (
                            <button
                              type="button"
                              className="fs-gallery-main-empty"
                              onClick={openAddForGallery}
                            >
                              <PlusOutlined
                                style={{ fontSize: 28, marginBottom: 8 }}
                              />
                              <span>
                                {viewingVariant && galleryColor
                                  ? `Thêm ảnh ${galleryColor}`
                                  : "Thêm ảnh sản phẩm"}
                              </span>
                            </button>
                          )}
                        </div>
                      </div>

                      {fields.map(({ key, name, ...restField }) => {
                        const mediaType = watchedImages[name]
                          ?.mediaType as string | undefined;
                        const colorValue = watchedImages[name]
                          ?.colorValue as string | undefined;
                        const optional =
                          mediaType === "SizeChart" ||
                          mediaType === "Video" ||
                          !!colorValue?.trim();
                        return (
                          <div
                            key={`hidden-${key}`}
                            style={{ display: "none" }}
                          >
                            <Form.Item
                              {...restField}
                              name={[name, "url"]}
                              rules={
                                optional
                                  ? undefined
                                  : [
                                      {
                                        required: true,
                                        message: "Nhập URL ảnh",
                                      },
                                    ]
                              }
                            >
                              <Input />
                            </Form.Item>
                            <Form.Item
                              {...restField}
                              name={[name, "mediaType"]}
                            >
                              <Input />
                            </Form.Item>
                            <Form.Item
                              {...restField}
                              name={[name, "colorValue"]}
                            >
                              <Input />
                            </Form.Item>
                            <Form.Item
                              {...restField}
                              name={[name, "listingSkuId"]}
                            >
                              <Input />
                            </Form.Item>
                            <Form.Item
                              {...restField}
                              name={[name, "tikTokImageId"]}
                            >
                              <Input />
                            </Form.Item>
                            <Form.Item
                              {...restField}
                              name={[name, "sortOrder"]}
                            >
                              <InputNumber />
                            </Form.Item>
                          </div>
                        );
                      })}
                    </>
                  );
                }}
              </Form.List>
            </div>

            <div className="fs-variations-preview">
              <div className="fs-product-preview-head">
                <ProductPreviewName
                  value={String(productName)}
                  onChange={(v) => form.setFieldValue("productName", v)}
                />
                <ProductPreviewMeta
                  skuLabel={previewSkuLabel}
                  sourceUrl={String(listingSourceUrl).trim() || undefined}
                  sourceOptions={sourceOptions}
                />
                <div className="fs-product-preview-price-row">
                  {previewDiscountPriceLabel ? (
                    <>
                      <span className="fs-product-preview-price">
                        {previewDiscountPriceLabel}
                      </span>
                      <span className="fs-product-preview-price-original">
                        {previewPriceLabel}
                      </span>
                    </>
                  ) : (
                    <span className="fs-product-preview-price">
                      {previewPriceLabel}
                    </span>
                  )}
                  {previewPromoLabel && (
                    <span className="fs-product-preview-promo">
                      {previewPromoLabel}
                    </span>
                  )}
                  {previewProfitLabel && (
                    <Tooltip title="Ước tính tham khảo từ công thức batch — không dùng để tính doanh số / profit báo cáo.">
                      <span className="fs-product-preview-profit">
                        {previewProfitLabel}
                      </span>
                    </Tooltip>
                  )}
                </div>
              </div>
              {productType !== "MultiSku" ? (
                <Text type="secondary">Sản phẩm không có variations.</Text>
              ) : selectedColors.length === 0 &&
                selectedSizes.length === 0 &&
                selectedItems.length === 0 ? (
                <Text type="secondary">
                  Chưa chọn Color / Size / Number of Items ở trên.
                </Text>
              ) : (
                <div className="fs-variations-preview-body">
                  {selectedColors.length > 0 && (
                    <div className="fs-preview-spec">
                      <div className="fs-preview-spec-label">
                        Color:{" "}
                        <strong>{galleryColor ?? selectedColors[0]}</strong>
                      </div>
                      <div className="fs-preview-color-grid">
                        {selectedColors.map((color) => {
                          const colorImgs = (
                            watchedImages as ListingImageRow[]
                          ).filter(
                            (img) =>
                              isVariantColorImage(img, color) &&
                              String(img.url ?? "").trim(),
                          );
                          const sharedFirst = (
                            watchedImages as ListingImageRow[]
                          ).find(
                            (img) =>
                              isSharedProductImage(img) &&
                              String(img.url ?? "").trim(),
                          );
                          const thumbUrl =
                            colorImgs[0]?.url || sharedFirst?.url || "";
                          const isActive =
                            galleryColor === color ||
                            (!galleryColor && color === selectedColors[0]);
                          const selectColor = () => {
                            if (galleryColor === color) {
                              setGalleryColor(null);
                            } else {
                              setGalleryColor(color);
                            }
                            setActiveImageIndex(0);
                            setGalleryPreviewOpen(false);
                          };
                          return (
                            <div
                              key={color}
                              role="button"
                              tabIndex={0}
                              className={`fs-preview-color-swatch${isActive ? " is-active" : ""}`}
                              title={
                                colorImgs.length > 0
                                  ? `Xem ảnh ${color}`
                                  : `${color} (chưa có Variant Image)`
                              }
                              onClick={selectColor}
                              onKeyDown={(e) => {
                                if (e.key !== "Enter" && e.key !== " ") return;
                                e.preventDefault();
                                selectColor();
                              }}
                            >
                              {thumbUrl ? (
                                <img src={thumbUrl} alt={color} />
                              ) : (
                                <span
                                  className="fs-preview-color-fallback"
                                  style={{ background: colorToCss(color) }}
                                />
                              )}
                              <span className="fs-preview-color-name">
                                {color}
                              </span>
                            </div>
                          );
                        })}
                      </div>
                    </div>
                  )}

                  {selectedSizes.length > 0 && (
                    <div className="fs-preview-spec">
                      <div className="fs-preview-spec-label">
                        <strong>
                          {previewColor
                            ? `${previewColor}/ ${previewSizeActive}`
                            : previewSizeActive}
                        </strong>
                      </div>
                      <div className="fs-preview-size-row">
                        {selectedSizes.map((size) => {
                          const isActive = size === previewSizeActive;
                          const selectSize = () => setPreviewSize(size);
                          return (
                            <span
                              key={size}
                              role="button"
                              tabIndex={0}
                              className={`fs-preview-size-pill${isActive ? " is-active" : ""}`}
                              onClick={selectSize}
                              onKeyDown={(e) => {
                                if (e.key !== "Enter" && e.key !== " ") return;
                                e.preventDefault();
                                selectSize();
                              }}
                            >
                              {size}
                            </span>
                          );
                        })}
                      </div>
                    </div>
                  )}

                  {selectedItems.length > 0 && (
                    <div className="fs-preview-spec">
                      <div className="fs-preview-spec-label">
                        Number of Items:{" "}
                        <strong>{previewItemActive}</strong>
                      </div>
                      <div className="fs-preview-size-row">
                        {selectedItems.map((item) => {
                          const isActive = item === previewItemActive;
                          const selectItem = () => setPreviewItem(item);
                          return (
                            <span
                              key={item}
                              role="button"
                              tabIndex={0}
                              className={`fs-preview-size-pill${isActive ? " is-active" : ""}`}
                              onClick={selectItem}
                              onKeyDown={(e) => {
                                if (e.key !== "Enter" && e.key !== " ") return;
                                e.preventDefault();
                                selectItem();
                              }}
                            >
                              {item}
                            </span>
                          );
                        })}
                      </div>
                    </div>
                  )}
                </div>
              )}

              <div className="fs-extra-media">
                <div className="fs-extra-media-block">
                  <div className="fs-extra-media-label">
                    <div className="fs-extra-media-label-row">
                      <span>Size Chart</span>
                      {autoGenerateSizeChart ? (
                        <Tooltip
                          title={
                            hasSizeGuide
                              ? "Tạo Size Chart tự động (Grok AI)"
                              : "Cần SizeGuideJson từ extension SHEIN"
                          }
                        >
                          <Button
                            type="text"
                            size="small"
                            className="fs-extra-media-ai-btn"
                            icon={<ThunderboltOutlined />}
                            disabled={
                              !canManage ||
                              !hasSizeGuide ||
                              generatingSizeChart ||
                              uploadingExtraMedia === "sizeChart"
                            }
                            loading={generatingSizeChart}
                            aria-label="Tạo Size Chart tự động"
                            onClick={() => void generateSizeChartWithAi()}
                          />
                        </Tooltip>
                      ) : null}
                    </div>
                    <Text type="secondary" className="fs-extra-media-hint">
                      PNG/JPG · tối đa 5 MB
                    </Text>
                    {listing?.sizeChartCaptureUrl?.trim() && !sizeChartUrl ? (
                      <div className="fs-extra-media-capture-row">
                        <Popover
                          content={
                            <img
                              src={listing.sizeChartCaptureUrl.trim()}
                              alt="Size chart capture preview"
                              referrerPolicy="no-referrer"
                              className="fs-extra-media-capture-preview"
                            />
                          }
                          trigger="hover"
                          placement="right"
                          mouseEnterDelay={0.25}
                          arrow={false}
                          styles={{ container: { padding: 4 } }}
                        >
                          <span className="fs-extra-media-capture-thumb-wrap">
                            <Image
                              src={listing.sizeChartCaptureUrl.trim()}
                              alt="Size chart capture"
                              width={36}
                              height={36}
                              style={{
                                width: 36,
                                height: 36,
                                objectFit: "cover",
                                borderRadius: 4,
                              }}
                              preview={false}
                            />
                          </span>
                        </Popover>
                        <Button
                          type="link"
                          size="small"
                          className="fs-extra-media-capture-btn"
                          disabled={
                            !canManage ||
                            applyingCapturedSizeChart ||
                            uploadingExtraMedia === "sizeChart" ||
                            generatingSizeChart
                          }
                          loading={applyingCapturedSizeChart}
                          onClick={() => void applyCapturedSizeChart()}
                        >
                          Dùng ảnh đã chụp
                        </Button>
                      </div>
                    ) : null}
                  </div>
                  <div className="fs-photo-grid">
                    {sizeChartUrl ? (
                      <div
                        className={[
                          "fs-photo-tile",
                          "fs-extra-media-thumb",
                          isHttpImageUrl(sizeChartUrl) ||
                          isFailedUploadImage(sizeChartUrl, displayImages, {
                            kind: "media",
                            mediaType: "SizeChart",
                          }) ||
                          isLoadFailedImage(sizeChartUrl, failedLoadKeys)
                            ? "is-upload-failed is-active"
                            : "",
                          isLoadFailedImage(sizeChartUrl, failedLoadKeys)
                            ? "is-load-failed"
                            : "",
                        ]
                          .filter(Boolean)
                          .join(" ")}
                        title={
                          isHttpImageUrl(sizeChartUrl)
                            ? failedListingImageTitle(true, false, false, "")
                            : undefined
                        }
                      >
                        <Image
                          src={sizeChartUrl}
                          alt="Size chart"
                          width={72}
                          height={72}
                          style={{ width: 72, height: 72, objectFit: "cover" }}
                          classNames={{
                            popup: { root: "fs-gallery-preview-root" },
                          }}
                          onError={() => markImageLoadFailed(sizeChartUrl)}
                          onLoad={() => markImageLoadOk(sizeChartUrl)}
                        />
                        <button
                          type="button"
                          className="remove"
                          aria-label="Remove size chart"
                          onClick={(e) => {
                            e.stopPropagation();
                            removeTypedMedia("SizeChart");
                          }}
                        >
                          <CloseOutlined />
                        </button>
                      </div>
                    ) : (
                      <button
                        type="button"
                        className="fs-photo-add"
                        disabled={
                          uploadingExtraMedia === "sizeChart" || !canManage
                        }
                        onClick={() => sizeChartInputRef.current?.click()}
                        title="Thêm Size Chart"
                      >
                        {uploadingExtraMedia === "sizeChart" ? (
                          <LoadingOutlined style={{ fontSize: 20 }} />
                        ) : (
                          <PlusOutlined style={{ fontSize: 20 }} />
                        )}
                      </button>
                    )}
                  </div>
                  <input
                    ref={sizeChartInputRef}
                    type="file"
                    accept="image/jpeg,image/jpg,image/png"
                    className="fs-extra-media-file-input"
                    tabIndex={-1}
                    aria-hidden
                    onChange={(e) => {
                      const file = e.target.files?.[0];
                      e.target.value = "";
                      if (file)
                        void uploadExtraMediaToFourSeller("sizeChart", file);
                    }}
                  />
                  <Form.Item name="sizeChartId" hidden noStyle>
                    <Input />
                  </Form.Item>
                </div>

                <div className="fs-extra-media-block">
                  <div className="fs-extra-media-label">
                    <span>Product video</span>
                    <Text type="secondary" className="fs-extra-media-hint">
                      MP4/WEBM · tối đa 10 MB
                    </Text>
                  </div>
                  <div className="fs-photo-grid">
                    {videoUrl ? (
                      <div
                        className="fs-photo-tile fs-extra-media-thumb fs-extra-media-video-thumb"
                        role="button"
                        tabIndex={0}
                        title="Xem video"
                        onClick={() => setVideoPreviewOpen(true)}
                        onKeyDown={(e) => {
                          if (e.key === "Enter" || e.key === " ") {
                            e.preventDefault();
                            setVideoPreviewOpen(true);
                          }
                        }}
                      >
                        <video
                          src={videoUrl}
                          muted
                          preload="metadata"
                          playsInline
                        />
                        <span className="fs-extra-media-play" aria-hidden>
                          <PlayCircleOutlined />
                        </span>
                        <button
                          type="button"
                          className="remove"
                          aria-label="Remove video"
                          onClick={(e) => {
                            e.stopPropagation();
                            removeTypedMedia("Video");
                          }}
                        >
                          <CloseOutlined />
                        </button>
                      </div>
                    ) : (
                      <button
                        type="button"
                        className="fs-photo-add"
                        disabled={uploadingExtraMedia === "video" || !canManage}
                        onClick={() => videoInputRef.current?.click()}
                        title="Thêm Product video"
                      >
                        {uploadingExtraMedia === "video" ? (
                          <LoadingOutlined style={{ fontSize: 20 }} />
                        ) : (
                          <PlusOutlined style={{ fontSize: 20 }} />
                        )}
                      </button>
                    )}
                  </div>
                  <input
                    ref={videoInputRef}
                    type="file"
                    accept="video/mp4,video/webm,video/quicktime"
                    className="fs-extra-media-file-input"
                    tabIndex={-1}
                    aria-hidden
                    onChange={(e) => {
                      const file = e.target.files?.[0];
                      e.target.value = "";
                      if (file)
                        void uploadExtraMediaToFourSeller("video", file);
                    }}
                  />
                  <Form.Item name="erpVideoId" hidden noStyle>
                    <Input />
                  </Form.Item>
                </div>
              </div>
            </div>
          </div>

          <Modal
            open={videoPreviewOpen && !!videoUrl}
            onCancel={() => setVideoPreviewOpen(false)}
            footer={null}
            centered
            destroyOnClose
            width="auto"
            closable
            maskClosable
            keyboard
            classNames={{ root: "fs-video-preview-root" }}
            className="fs-video-preview-modal"
            styles={{
              mask: { background: "rgba(0, 0, 0, 0.45)" },
              container: { padding: 0 },
              body: { padding: 0, background: "transparent", lineHeight: 0 },
            }}
          >
            {videoUrl ? (
              <div className="fs-video-preview-body">
                <video
                  src={videoUrl}
                  controls
                  autoPlay
                  className="fs-video-preview-player"
                />
              </div>
            ) : null}
          </Modal>

          <AddProductImageModal
            open={addingMedia === "product"}
            title="Thêm ảnh Default"
            onCancel={() => setAddingMedia(null)}
            onAdd={(urls) => {
              appendProductImageUrls(urls);
              setAddingMedia(null);
            }}
          />
          <AddProductImageModal
            open={!!addingVariantColor}
            title={
              addingVariantColor
                ? `Thêm ảnh Variant — ${addingVariantColor}`
                : "Thêm ảnh Variant"
            }
            onCancel={() => setAddingVariantColor(null)}
            onAdd={(urls) => {
              if (addingVariantColor)
                appendVariantColorImageUrls(addingVariantColor, urls);
              setAddingVariantColor(null);
            }}
          />
        </Card>

        {selectedColors.length > 0 && (
          <Card
            size="small"
            style={{ marginBottom: 16 }}
            title={
              <Space>
                <span>Variant Image</span>
                <Switch
                  size="small"
                  checked={variantImageOn}
                  onChange={setVariantImageOn}
                  style={{ background: variantImageOn ? "#00897b" : undefined }}
                />
              </Space>
            }
          >
            {variantImageOn ? (
              <>
                {hasHttpImages(displayImages) ? (
                  <Alert
                    type="error"
                    showIcon
                    className="fs-default-image-count-alert"
                    title="Có ảnh URL dùng http:// — 4Seller từ chối (Invalid Para). Đổi sang https:// hoặc xóa ảnh đó (ô đỏ)."
                    style={{ marginBottom: 8 }}
                  />
                ) : null}
                {(() => {
                  const mainImgs = (displayImages as ListingImageRow[]).filter(
                    (img) =>
                      isSharedProductImage(img) && String(img.url ?? "").trim(),
                  );
                  const canAcceptColorBlock = colorBlockDragIndex != null;
                  const isDefaultDragOver =
                    colorBlockDragOverDefault ||
                    (variantPhotoDrag != null &&
                      variantPhotoDrag.scope !== "__main__" &&
                      variantPhotoDragOver?.scope === "__main__");
                  return (
                    <div
                      className={[
                        "fs-variant-color-block",
                        "fs-variant-color-block--default-drop",
                        canAcceptColorBlock
                          ? "fs-variant-color-block--sortable"
                          : "",
                        isDefaultDragOver ? "is-drag-over" : "",
                      ]
                        .filter(Boolean)
                        .join(" ")}
                      onDragOver={(e) => {
                        const draggingColor = colorBlockDragIndex != null;
                        const draggingColorPhoto =
                          variantPhotoDrag != null &&
                          variantPhotoDrag.scope !== "__main__";
                        if (!draggingColor && !draggingColorPhoto) return;
                        e.preventDefault();
                        e.dataTransfer.dropEffect = "move";
                        if (draggingColor && !colorBlockDragOverDefault) {
                          setColorBlockDragOverDefault(true);
                          setColorBlockDragOverIndex(null);
                        }
                        if (draggingColorPhoto) {
                          setVariantPhotoDragOverSafe({
                            scope: "__main__",
                            index: Math.max(mainImgs.length - 1, 0),
                          });
                        }
                      }}
                      onDragLeave={(e) => {
                        const related = e.relatedTarget as Node | null;
                        if (related && e.currentTarget.contains(related))
                          return;
                        setColorBlockDragOverDefault(false);
                      }}
                      onDrop={(e) => {
                        e.preventDefault();
                        try {
                          const colorPayloadRaw = e.dataTransfer.getData(
                            "application/x-variant-color",
                          );
                          if (colorPayloadRaw) {
                            const payload = JSON.parse(colorPayloadRaw) as {
                              type: string;
                              index: number;
                            };
                            if (payload.type === "color-block") {
                              promoteColorToDefault(payload.index);
                            }
                          } else {
                            const photoPayload = JSON.parse(
                              e.dataTransfer.getData("text/plain"),
                            ) as {
                              scope: VariantPhotoDragScope;
                              index: number;
                            };
                            if (
                              photoPayload.scope &&
                              photoPayload.scope !== "__main__"
                            ) {
                              copyVariantPhotoToDefault(
                                photoPayload.scope,
                                photoPayload.index,
                                mainImgs.length,
                              );
                            }
                          }
                        } catch {
                          /* ignore invalid */
                        }
                        setColorBlockDragIndex(null);
                        setColorBlockDragOverIndex(null);
                        setColorBlockDragOverDefault(false);
                        setVariantPhotoDrag(null);
                        setVariantPhotoDragOverSafe(null);
                      }}
                    >
                      <Text strong>
                        Default (mặc định){" "}
                        <Tag
                          color={
                            mainImgs.length >= VARIANT_IMAGE_MAX
                              ? "success"
                              : "warning"
                          }
                          style={{ marginInlineStart: 4 }}
                        >
                          {mainImgs.length}/{VARIANT_IMAGE_MAX}
                        </Tag>
                        {canAcceptColorBlock ? (
                          <Text
                            type="secondary"
                            style={{ fontWeight: 400, fontSize: 12 }}
                          >
                            — thả màu vào đây để đặt làm ảnh mặc định
                          </Text>
                        ) : null}
                      </Text>
                      {mainImgs.length < VARIANT_IMAGE_MAX ? (
                        <Alert
                          type="warning"
                          showIcon
                          className="fs-default-image-count-alert"
                          title={`Phải có đủ ${VARIANT_IMAGE_MAX} ảnh mặc định mới lên Good. Hiện có ${mainImgs.length}/${VARIANT_IMAGE_MAX}.`}
                        />
                      ) : null}
                      <div
                        className={[
                          "fs-photo-grid",
                          variantPhotoDrag?.scope === "__main__" &&
                          variantPhotoDragOver?.scope === "__main__" &&
                          variantPhotoDrag.index !== variantPhotoDragOver.index
                            ? "is-live-sorting"
                            : "",
                        ]
                          .filter(Boolean)
                          .join(" ")}
                        style={{ marginTop: 8 }}
                        onDragOver={(e) => {
                          e.preventDefault();
                          e.stopPropagation();
                          e.dataTransfer.dropEffect = "move";
                          if (variantPhotoDrag == null) return;
                          const rects = ensureDragHitRects(
                            e.currentTarget,
                            ".fs-photo-tile.fs-variant-draggable",
                          );
                          const next = dragReorderIndexFromRects(
                            e.clientX,
                            e.clientY,
                            rects,
                          );
                          if (next != null) {
                            setVariantPhotoDragOverSafe({
                              scope: "__main__",
                              index: next,
                            });
                          }
                        }}
                        onDrop={(e) => {
                          e.preventDefault();
                          e.stopPropagation();
                          const rects = ensureDragHitRects(
                            e.currentTarget,
                            ".fs-photo-tile.fs-variant-draggable",
                          );
                          const toIdx =
                            dragReorderIndexFromRects(
                              e.clientX,
                              e.clientY,
                              rects,
                            ) ??
                            (variantPhotoDragOver?.scope === "__main__"
                              ? variantPhotoDragOver.index
                              : null);
                          try {
                            const payload = JSON.parse(
                              e.dataTransfer.getData("text/plain"),
                            ) as {
                              scope: VariantPhotoDragScope;
                              index: number;
                            };
                            if (payload.scope === "__main__" && toIdx != null) {
                              reorderVariantPhotoScope(
                                "__main__",
                                payload.index,
                                toIdx,
                              );
                            } else if (payload.scope && toIdx != null) {
                              copyVariantPhotoToDefault(
                                payload.scope,
                                payload.index,
                                toIdx,
                              );
                            }
                          } catch {
                            /* ignore */
                          }
                          clearDragHitRects();
                          setVariantPhotoDrag(null);
                          setVariantPhotoDragOverSafe(null);
                          setColorBlockDragOverDefault(false);
                        }}
                      >
                        {mainImgs.map((img, idx) => {
                          const canReorder = mainImgs.length > 1;
                          const isDragging =
                            variantPhotoDrag?.scope === "__main__" &&
                            variantPhotoDrag.index === idx;
                          const isDragOver =
                            variantPhotoDragOver?.scope === "__main__" &&
                            variantPhotoDragOver.index === idx &&
                            !(
                              variantPhotoDrag?.scope === "__main__" &&
                              variantPhotoDrag.index === idx
                            );
                          const slotShift =
                            variantPhotoDrag?.scope === "__main__" &&
                            variantPhotoDragOver?.scope === "__main__"
                              ? dragReorderSlotShift(
                                  variantPhotoDrag.index,
                                  variantPhotoDragOver.index,
                                  idx,
                                )
                              : 0;
                          const previewUrls = mainImgs.map((m) => m.url);
                          const uploadFailed = isFailedUploadImage(
                            img.url,
                            displayImages,
                            { kind: "main" },
                          );
                          const loadFailed = isLoadFailedImage(
                            img.url,
                            failedLoadKeys,
                          );
                          const httpUrl = isHttpImageUrl(img.url);
                          const imageFailed =
                            httpUrl || uploadFailed || loadFailed;
                          const tileTransform = dragReorderTransform(
                            slotShift,
                            "x",
                            8,
                          );
                          return (
                            <div
                              key={`main-${img.url}-${idx}`}
                              className={[
                                "fs-photo-tile",
                                "fs-variant-previewable",
                                "fs-variant-draggable",
                                isGifImageUrl(img.url)
                                  ? "fs-photo-tile--gif"
                                  : "",
                                imageFailed ? "is-upload-failed is-active" : "",
                                loadFailed ? "is-load-failed" : "",
                                isDragging ? "is-dragging" : "",
                                isDragOver ? "is-drag-over" : "",
                                slotShift !== 0 && !isDragging
                                  ? "is-slot-shifting"
                                  : "",
                              ]
                                .filter(Boolean)
                                .join(" ")}
                              style={
                                tileTransform
                                  ? { transform: tileTransform }
                                  : undefined
                              }
                              draggable={canReorder}
                              role="button"
                              tabIndex={0}
                              title={failedListingImageTitle(
                                httpUrl,
                                uploadFailed,
                                loadFailed,
                                canReorder
                                  ? "Hover: xem lớn / xóa · giữ để đổi thứ tự · thả ảnh màu vào đây để thêm"
                                  : "Hover: xem lớn / xóa · thả ảnh màu vào đây để thêm",
                              )}
                              onClick={(e) => {
                                // Single-click reserved for delete / preview btn / drag.
                                if (shouldIgnoreVariantPhotoPreview(e)) {
                                  e.preventDefault();
                                  e.stopPropagation();
                                }
                              }}
                              onDoubleClick={(e) => {
                                if (shouldIgnoreVariantPhotoPreview(e)) return;
                                if (
                                  e.target instanceof Element &&
                                  e.target.closest(".fs-photo-preview-btn")
                                ) {
                                  return;
                                }
                                openVariantPhotoPreview(previewUrls, idx);
                              }}
                              onKeyDown={(e) => {
                                if (e.key === "Enter" || e.key === " ") {
                                  e.preventDefault();
                                  openVariantPhotoPreview(previewUrls, idx);
                                }
                              }}
                              onDragStart={(e) => {
                                skipVariantPhotoClickRef.current = true;
                                const parent = e.currentTarget.parentElement;
                                if (parent) {
                                  ensureDragHitRects(
                                    parent,
                                    ".fs-photo-tile.fs-variant-draggable",
                                  );
                                }
                                setVariantPhotoDrag({
                                  scope: "__main__",
                                  index: idx,
                                });
                                setVariantPhotoDragOverSafe({
                                  scope: "__main__",
                                  index: idx,
                                });
                                e.dataTransfer.effectAllowed = "move";
                                e.dataTransfer.setData(
                                  "text/plain",
                                  JSON.stringify({
                                    scope: "__main__",
                                    index: idx,
                                  }),
                                );
                                const thumb =
                                  e.currentTarget.querySelector("img");
                                if (thumb)
                                  e.dataTransfer.setDragImage(thumb, 36, 36);
                              }}
                              onDragEnd={() => {
                                clearDragHitRects();
                                setVariantPhotoDrag(null);
                                setVariantPhotoDragOverSafe(null);
                              }}
                            >
                              <img
                                src={img.url}
                                alt="Main image"
                                draggable={false}
                                referrerPolicy="no-referrer"
                                onError={() => markImageLoadFailed(img.url)}
                                onLoad={() => markImageLoadOk(img.url)}
                              />
                              <span className="fs-photo-index" aria-hidden>
                                {idx + 1 + slotShift}
                              </span>
                              {canReorder ? (
                                <span
                                  className="fs-variant-drag-hint"
                                  aria-hidden
                                >
                                  <HolderOutlined />
                                </span>
                              ) : null}
                              <button
                                type="button"
                                className="fs-photo-preview-btn"
                                aria-label="Xem ảnh lớn"
                                title="Xem ảnh lớn"
                                onPointerDown={(e) => e.stopPropagation()}
                                onMouseDown={(e) => e.stopPropagation()}
                                onClick={(e) => {
                                  e.preventDefault();
                                  e.stopPropagation();
                                  openVariantPhotoPreview(previewUrls, idx);
                                }}
                              >
                                <ZoomInOutlined />
                              </button>
                              <button
                                type="button"
                                className="remove"
                                aria-label="Xóa ảnh Default"
                                onPointerDown={
                                  handleVariantPhotoRemovePointerDown
                                }
                                onMouseDown={
                                  handleVariantPhotoRemovePointerDown
                                }
                                onClick={(e) => {
                                  e.preventDefault();
                                  e.stopPropagation();
                                  skipVariantPhotoClickRef.current = true;
                                  removeDefaultImage(
                                    idx,
                                    setOptimisticImages,
                                  );
                                }}
                              >
                                <CloseOutlined />
                              </button>
                            </div>
                          );
                        })}
                        {mainImgs.length < VARIANT_IMAGE_MAX && (
                          <button
                            type="button"
                            className="fs-photo-add"
                            title="Thêm ảnh Default"
                            onClick={() => setAddingMedia("product")}
                          >
                            <PlusOutlined style={{ fontSize: 20 }} />
                          </button>
                        )}
                      </div>
                    </div>
                  );
                })()}
                {selectedColors.map((color, colorIdx) => {
                  const colorImgs = (displayImages as ListingImageRow[]).filter(
                    (img) =>
                      isVariantColorImage(img, color) &&
                      String(img.url ?? "").trim(),
                  );
                  const canReorderColors = selectedColors.length > 1;
                  const isColorDragging = colorBlockDragIndex === colorIdx;
                  const isColorDragOver =
                    colorBlockDragOverIndex === colorIdx &&
                    colorBlockDragIndex !== colorIdx;
                  const colorBlockShift =
                    colorBlockDragIndex != null &&
                    colorBlockDragOverIndex != null
                      ? dragReorderSlotShift(
                          colorBlockDragIndex,
                          colorBlockDragOverIndex,
                          colorIdx,
                        )
                      : 0;
                  const colorBlockTransform = dragReorderTransform(
                    colorBlockShift,
                    "y",
                    12,
                  );
                  const liveSortingPhotos =
                    variantPhotoDrag?.scope === color &&
                    variantPhotoDragOver?.scope === color &&
                    variantPhotoDrag.index !== variantPhotoDragOver.index;
                  return (
                    <div
                      key={color}
                      className={[
                        "fs-variant-color-block",
                        canReorderColors
                          ? "fs-variant-color-block--sortable"
                          : "",
                        colorBlockDragIndex != null ? "is-live-sorting" : "",
                        isColorDragging ? "is-dragging" : "",
                        isColorDragOver ? "is-drag-over" : "",
                        colorBlockShift !== 0 && !isColorDragging
                          ? "is-slot-shifting"
                          : "",
                      ]
                        .filter(Boolean)
                        .join(" ")}
                      style={
                        colorBlockTransform
                          ? { transform: colorBlockTransform }
                          : undefined
                      }
                      onDragOver={(e) => {
                        if (!canReorderColors || colorBlockDragIndex == null)
                          return;
                        e.preventDefault();
                        e.dataTransfer.dropEffect = "move";
                        if (colorBlockDragOverDefault)
                          setColorBlockDragOverDefault(false);
                        const rects = colorBlockDragRectsRef.current.rects;
                        const next =
                          rects.length > 0
                            ? dragReorderIndexFromRects(
                                e.clientX,
                                e.clientY,
                                rects,
                              )
                            : colorIdx;
                        if (next != null && colorBlockDragOverIndex !== next) {
                          setColorBlockDragOverIndex(next);
                        }
                      }}
                      onDrop={(e) => {
                        e.preventDefault();
                        const rects = colorBlockDragRectsRef.current.rects;
                        const toIdx =
                          (rects.length > 0
                            ? dragReorderIndexFromRects(
                                e.clientX,
                                e.clientY,
                                rects,
                              )
                            : null) ??
                          colorBlockDragOverIndex ??
                          colorIdx;
                        try {
                          const payload = JSON.parse(
                            e.dataTransfer.getData(
                              "application/x-variant-color",
                            ),
                          ) as { type: string; index: number };
                          if (payload.type === "color-block") {
                            reorderSelectedColors(payload.index, toIdx);
                          }
                        } catch {
                          /* ignore photo drops / invalid */
                        }
                        colorBlockDragRectsRef.current = {
                          container: null,
                          rects: [],
                        };
                        setColorBlockDragIndex(null);
                        setColorBlockDragOverIndex(null);
                      }}
                    >
                      <div className="fs-variant-color-block-head">
                        {canReorderColors ? (
                          <span
                            className="fs-variant-color-drag-handle"
                            draggable
                            title="Kéo để đổi thứ tự màu · thả lên Default để đặt ảnh mặc định"
                            onDragStart={(e) => {
                              e.stopPropagation();
                              const block = e.currentTarget.closest(
                                ".fs-variant-color-block",
                              );
                              const parent = block?.parentElement;
                              if (parent) {
                                colorBlockDragRectsRef.current = {
                                  container: parent,
                                  rects: snapshotDragReorderRects(
                                    parent,
                                    ".fs-variant-color-block--sortable:not(.fs-variant-color-block--default-drop)",
                                  ),
                                };
                              }
                              setColorBlockDragIndex(colorIdx);
                              setColorBlockDragOverIndex(colorIdx);
                              setColorBlockDragOverDefault(false);
                              e.dataTransfer.effectAllowed = "move";
                              e.dataTransfer.setData(
                                "application/x-variant-color",
                                JSON.stringify({
                                  type: "color-block",
                                  index: colorIdx,
                                }),
                              );
                              e.dataTransfer.setData(
                                "text/plain",
                                `color:${color}`,
                              );
                            }}
                            onDragEnd={() => {
                              colorBlockDragRectsRef.current = {
                                container: null,
                                rects: [],
                              };
                              setColorBlockDragIndex(null);
                              setColorBlockDragOverIndex(null);
                              setColorBlockDragOverDefault(false);
                            }}
                          >
                            <HolderOutlined />
                          </span>
                        ) : null}
                        <Text strong>
                          {color}{" "}
                          {colorIdx === 0 ? (
                            <Tag
                              style={{
                                marginInlineStart: 4,
                                background: "var(--fs-teal-soft)",
                                borderColor: "var(--fs-teal)",
                                color: "var(--fs-teal)",
                              }}
                            >
                              Main
                            </Tag>
                          ) : null}
                          {colorImgs.length === 0 ? (
                            <Text type="secondary" style={{ fontWeight: 400 }}>
                              (mặc định)
                            </Text>
                          ) : null}
                        </Text>
                      </div>
                      <div
                        className={[
                          "fs-photo-grid",
                          liveSortingPhotos ? "is-live-sorting" : "",
                        ]
                          .filter(Boolean)
                          .join(" ")}
                        style={{ marginTop: 8 }}
                        onDragOver={(e) => {
                          e.preventDefault();
                          e.stopPropagation();
                          e.dataTransfer.dropEffect = "move";
                          // Same-color reorder only for live shift; cross-scope copy uses Default drop.
                          if (
                            variantPhotoDrag != null &&
                            variantPhotoDrag.scope !== color
                          ) {
                            return;
                          }
                          const rects = ensureDragHitRects(
                            e.currentTarget,
                            ".fs-photo-tile.fs-variant-draggable",
                          );
                          const next = dragReorderIndexFromRects(
                            e.clientX,
                            e.clientY,
                            rects,
                          );
                          if (next != null) {
                            setVariantPhotoDragOverSafe({
                              scope: color,
                              index: next,
                            });
                          }
                        }}
                        onDrop={(e) => {
                          e.preventDefault();
                          e.stopPropagation();
                          const rects = ensureDragHitRects(
                            e.currentTarget,
                            ".fs-photo-tile.fs-variant-draggable",
                          );
                          const toIdx =
                            dragReorderIndexFromRects(
                              e.clientX,
                              e.clientY,
                              rects,
                            ) ??
                            (variantPhotoDragOver?.scope === color
                              ? variantPhotoDragOver.index
                              : null);
                          try {
                            const payload = JSON.parse(
                              e.dataTransfer.getData("text/plain"),
                            ) as {
                              scope: VariantPhotoDragScope;
                              index: number;
                            };
                            if (payload.scope === color && toIdx != null) {
                              reorderVariantPhotoScope(
                                color,
                                payload.index,
                                toIdx,
                              );
                            }
                          } catch {
                            /* ignore */
                          }
                          clearDragHitRects();
                          setVariantPhotoDrag(null);
                          setVariantPhotoDragOverSafe(null);
                        }}
                      >
                        {colorImgs.map((img, idx) => {
                          const canReorder = colorImgs.length > 1;
                          const canDragToDefault = colorImgs.length > 0;
                          const isDragging =
                            variantPhotoDrag?.scope === color &&
                            variantPhotoDrag.index === idx;
                          const isDragOver =
                            variantPhotoDragOver?.scope === color &&
                            variantPhotoDragOver.index === idx &&
                            variantPhotoDrag?.index !== idx;
                          const slotShift =
                            variantPhotoDrag?.scope === color &&
                            variantPhotoDragOver?.scope === color
                              ? dragReorderSlotShift(
                                  variantPhotoDrag.index,
                                  variantPhotoDragOver.index,
                                  idx,
                                )
                              : 0;
                          const previewUrls = colorImgs.map((m) => m.url);
                          const uploadFailed = isFailedUploadImage(
                            img.url,
                            displayImages,
                            { kind: "color", color },
                          );
                          const loadFailed = isLoadFailedImage(
                            img.url,
                            failedLoadKeys,
                          );
                          const httpUrl = isHttpImageUrl(img.url);
                          const imageFailed =
                            httpUrl || uploadFailed || loadFailed;
                          const tileTransform = dragReorderTransform(
                            slotShift,
                            "x",
                            8,
                          );
                          return (
                            <div
                              key={`${color}-${img.url}-${idx}`}
                              className={[
                                "fs-photo-tile",
                                "fs-variant-previewable",
                                canDragToDefault ? "fs-variant-draggable" : "",
                                isGifImageUrl(img.url)
                                  ? "fs-photo-tile--gif"
                                  : "",
                                imageFailed ? "is-upload-failed is-active" : "",
                                loadFailed ? "is-load-failed" : "",
                                isDragging ? "is-dragging" : "",
                                isDragOver ? "is-drag-over" : "",
                                slotShift !== 0 && !isDragging
                                  ? "is-slot-shifting"
                                  : "",
                              ]
                                .filter(Boolean)
                                .join(" ")}
                              style={
                                tileTransform
                                  ? { transform: tileTransform }
                                  : undefined
                              }
                              draggable={canDragToDefault}
                              role="button"
                              tabIndex={0}
                              title={failedListingImageTitle(
                                httpUrl,
                                uploadFailed,
                                loadFailed,
                                canReorder
                                  ? "Hover: xem lớn / xóa · giữ để đổi thứ tự · kéo lên Default để thêm"
                                  : "Hover: xem lớn / xóa · kéo lên Default để thêm vào ảnh mặc định",
                              )}
                              onClick={(e) => {
                                if (shouldIgnoreVariantPhotoPreview(e)) {
                                  e.preventDefault();
                                  e.stopPropagation();
                                }
                              }}
                              onDoubleClick={(e) => {
                                if (shouldIgnoreVariantPhotoPreview(e)) return;
                                if (
                                  e.target instanceof Element &&
                                  e.target.closest(".fs-photo-preview-btn")
                                ) {
                                  return;
                                }
                                openVariantPhotoPreview(previewUrls, idx);
                              }}
                              onKeyDown={(e) => {
                                if (e.key === "Enter" || e.key === " ") {
                                  e.preventDefault();
                                  openVariantPhotoPreview(previewUrls, idx);
                                }
                              }}
                              onDragStart={(e) => {
                                skipVariantPhotoClickRef.current = true;
                                const parent = e.currentTarget.parentElement;
                                if (parent) {
                                  ensureDragHitRects(
                                    parent,
                                    ".fs-photo-tile.fs-variant-draggable",
                                  );
                                }
                                setVariantPhotoDrag({
                                  scope: color,
                                  index: idx,
                                });
                                setVariantPhotoDragOverSafe({
                                  scope: color,
                                  index: idx,
                                });
                                e.dataTransfer.effectAllowed = "move";
                                e.dataTransfer.setData(
                                  "text/plain",
                                  JSON.stringify({ scope: color, index: idx }),
                                );
                                const thumb =
                                  e.currentTarget.querySelector("img");
                                if (thumb)
                                  e.dataTransfer.setDragImage(thumb, 36, 36);
                              }}
                              onDragEnd={() => {
                                clearDragHitRects();
                                setVariantPhotoDrag(null);
                                setVariantPhotoDragOverSafe(null);
                                setColorBlockDragOverDefault(false);
                              }}
                            >
                              <img
                                src={img.url}
                                alt={color}
                                draggable={false}
                                referrerPolicy="no-referrer"
                                onError={() => markImageLoadFailed(img.url)}
                                onLoad={() => markImageLoadOk(img.url)}
                              />
                              <span className="fs-photo-index" aria-hidden>
                                {idx + 1 + slotShift}
                              </span>
                              {canDragToDefault ? (
                                <span
                                  className="fs-variant-drag-hint"
                                  aria-hidden
                                >
                                  <HolderOutlined />
                                </span>
                              ) : null}
                              <button
                                type="button"
                                className="fs-photo-preview-btn"
                                aria-label="Xem ảnh lớn"
                                title="Xem ảnh lớn"
                                onPointerDown={(e) => e.stopPropagation()}
                                onMouseDown={(e) => e.stopPropagation()}
                                onClick={(e) => {
                                  e.preventDefault();
                                  e.stopPropagation();
                                  openVariantPhotoPreview(previewUrls, idx);
                                }}
                              >
                                <ZoomInOutlined />
                              </button>
                              <button
                                type="button"
                                className="remove"
                                aria-label={`Remove ${color} image`}
                                onPointerDown={
                                  handleVariantPhotoRemovePointerDown
                                }
                                onMouseDown={
                                  handleVariantPhotoRemovePointerDown
                                }
                                onClick={(e) => {
                                  e.preventDefault();
                                  e.stopPropagation();
                                  skipVariantPhotoClickRef.current = true;
                                  removeVariantColorImage(
                                    color,
                                    idx,
                                    setOptimisticImages,
                                  );
                                }}
                              >
                                <CloseOutlined />
                              </button>
                            </div>
                          );
                        })}
                        {colorImgs.length < VARIANT_IMAGE_MAX && (
                          <button
                            type="button"
                            className="fs-photo-add"
                            title={`Thêm ảnh ${color}`}
                            onClick={() => setAddingVariantColor(color)}
                          >
                            <PlusOutlined style={{ fontSize: 20 }} />
                          </button>
                        )}
                      </div>
                    </div>
                  );
                })}
                {variantPreviewUrls.length > 0 ? (
                  <Image.PreviewGroup
                    items={variantPreviewUrls}
                    classNames={{ popup: { root: "fs-gallery-preview-root" } }}
                    preview={{
                      open: variantPreviewOpen,
                      current: variantPreviewIndex,
                      onOpenChange: (open) => {
                        setVariantPreviewOpen(open);
                        if (!open) {
                          setVariantPreviewUrls([]);
                          setVariantPreviewIndex(0);
                        }
                      },
                      onChange: (current) => setVariantPreviewIndex(current),
                      actionsRender: (originalNode) => {
                        const name = imageDisplayName(
                          variantPreviewUrls[variantPreviewIndex] ?? "",
                        );
                        return (
                          <div className="fs-preview-toolbar">
                            <PreviewFilenameCaption name={name} />
                            {originalNode}
                          </div>
                        );
                      },
                    }}
                  >
                    <Image
                      src={
                        variantPreviewUrls[variantPreviewIndex] ??
                        variantPreviewUrls[0]
                      }
                      style={{ display: "none" }}
                      alt=""
                    />
                  </Image.PreviewGroup>
                ) : null}
              </>
            ) : (
              <Text type="secondary" style={{ fontSize: 12 }}>
                Bật để gán bộ ảnh riêng cho từng màu.
              </Text>
            )}
          </Card>
        )}
            </>
          )}
        </ImagesWatchIsolator>
          </>
        )}

        <Card
          title="Specifics"
          size="small"
          style={{ marginBottom: 16 }}
          className="fs-specifics-card"
          extra={
            <Button
              size="small"
              icon={<ThunderboltOutlined />}
              loading={mapSpecifics.isPending || autoSpecificsRunning}
              disabled={autoSpecificsDisabled || autoSpecificsRunning}
              onClick={() => void openAutoMapSpecifics()}
              title={
                autoSpecificsDisabled
                  ? "Đã có Specifics — bật «Cho phép tạo lại Specifics» trong Settings để chạy lại"
                  : specificsAllowRepeat &&
                      !needsAutoMapSpecifics(watchedAttributes)
                    ? "Auto Specifics — có thể chạy lại (tốn token)"
                    : "Auto Specifics"
              }
              className={autoSpecificsDisabled ? undefined : "fs-ai-action-btn"}
            >
              Auto Specifics
            </Button>
          }
        >
          {syncMissingSpecifics.length > 0 ? (
            <Alert
              type="warning"
              showIcon
              style={{ marginBottom: 12 }}
              title="Specifics thiếu khi publish"
              description={
                <>
                  Thiếu:{" "}
                  <Text strong>{syncMissingSpecifics.join(", ")}</Text>. Bấm{" "}
                  <Text strong>Auto Specifics</Text> hoặc{" "}
                  <Text strong>Save Draft</Text> — hệ thống sẽ tự điền Condition
                  mặc định (New / Brand new) nếu category yêu cầu.
                </>
              }
            />
          ) : null}
          <Form.List name="attributes">
            {(fields, { remove }) => {
              const hasMore = fields.length > SPECIFICS_PREVIEW_COUNT;
              const visibleKeys = new Set(
                (specificsListExpanded
                  ? fields
                  : fields.slice(0, SPECIFICS_PREVIEW_COUNT)
                ).map((f) => f.key),
              );

              return (
              <>
                {fields.length > 0 ? (
                  <div className="fs-specifics-header" aria-hidden="true">
                    <Text type="secondary">Tên thuộc tính</Text>
                    <Text type="secondary">Giá trị</Text>
                    <span />
                  </div>
                ) : null}
                <div className="fs-specifics-list">
                  {fields.map(({ key, name, ...restField }) => {
                    const row = form.getFieldValue([
                      "attributes",
                      name,
                    ]) as ListingAttributeRow | undefined;
                    const isDefaultBrand =
                      String(row?.attrId ?? "")
                        .trim()
                        .toLowerCase() === "brand" ||
                      String(row?.attrName ?? "")
                        .trim()
                        .toLowerCase() === "brand";
                    const collapsed = !visibleKeys.has(key);
                    return (
                      <div
                        className={`fs-specifics-row${isDefaultBrand ? " fs-specifics-row-required" : ""}`}
                        key={key}
                        style={collapsed ? { display: "none" } : undefined}
                        aria-hidden={collapsed || undefined}
                      >
                        <Form.Item
                          {...restField}
                          name={[name, "attrName"]}
                          rules={[
                            { required: true, message: "Nhập tên thuộc tính" },
                          ]}
                          className="fs-specifics-name"
                        >
                          <Input
                            placeholder="Ví dụ: Material"
                            disabled={isDefaultBrand}
                            suffix={
                              isDefaultBrand ? (
                                <Tag color="blue">Bắt buộc</Tag>
                              ) : undefined
                            }
                          />
                        </Form.Item>
                        <Form.Item
                          {...restField}
                          name={[name, "attrValue"]}
                          className="fs-specifics-value"
                        >
                          <Input
                            placeholder="Ví dụ: Cotton"
                            disabled={isDefaultBrand}
                            onChange={() => dropResolvedSpecificValueId(name)}
                          />
                        </Form.Item>
                        <Form.Item
                          {...restField}
                          name={[name, "attrId"]}
                          hidden
                        >
                          <Input />
                        </Form.Item>
                        <Form.Item
                          {...restField}
                          name={[name, "valueId"]}
                          hidden
                        >
                          <Input />
                        </Form.Item>
                        <div className="fs-specifics-remove">
                          <Button
                            danger
                            type="text"
                            icon={<DeleteOutlined />}
                            onClick={() => remove(name)}
                            disabled={isDefaultBrand}
                            aria-label="Remove attribute"
                            title={
                              isDefaultBrand
                                ? "Brand = No brand là thuộc tính bắt buộc"
                                : "Remove attribute"
                            }
                          />
                        </div>
                      </div>
                    );
                  })}
                </div>
                {hasMore ? (
                  <div style={{ textAlign: "center", marginTop: 8 }}>
                    <Button
                      type="link"
                      size="small"
                      onClick={() =>
                        setSpecificsListExpanded((open) => !open)
                      }
                    >
                      {specificsListExpanded ? (
                        <>
                          Thu gọn <UpOutlined />
                        </>
                      ) : (
                        <>
                          Xem thêm{" "}
                          {fields.length - SPECIFICS_PREVIEW_COUNT} thuộc tính{" "}
                          <DownOutlined />
                        </>
                      )}
                    </Button>
                  </div>
                ) : null}
              </>
              );
            }}
          </Form.List>
        </Card>

        <ListingDescriptionCard
          form={form}
          selectedColors={selectedColors}
          rewritePending={rewriteDescription.isPending}
          onAiRewrite={() => void applyAiRewriteDescription()}
          canUndoRewrite={rewriteUndoHtml !== null}
          onUndoRewrite={() => void undoAiRewriteDescription()}
          onImagesInserted={() => void markDescriptionImagesInserted()}
          rewriteDone={descriptionRewritten}
          rewriteLocked={rewriteLocked}
          imagesInsertedDone={descriptionImagesInserted}
          enabled={mediaReady}
          rewriteTitleTooltip={rewriteTitleHint}
        />

        <Card
          title="Shipping"
          size="small"
          style={{ marginBottom: 16 }}
          className="fs-field-card"
          extra={
            <div className="fs-field-card-extra">
              <Select
                allowClear
                placeholder="Shipping Template"
                value={selectedShippingTemplateId}
                options={shippingTemplates.map((t) => ({
                  label: t.isDefault
                    ? `${t.name} (Default · ${t.packageWeight}${t.weightUnit})`
                    : `${t.name} (${t.packageWeight}${t.weightUnit})`,
                  value: t.id,
                }))}
                onChange={(id) => {
                  if (!id) {
                    setSelectedShippingTemplateId(undefined);
                    return;
                  }
                  const template = shippingTemplates.find((t) => t.id === id);
                  if (template) applyShippingTemplate(template);
                }}
              />
              <Button
                icon={<SettingOutlined />}
                onClick={() => setShippingTemplateModalOpen(true)}
                aria-label="Quản lý"
              >
                Quản lý
              </Button>
            </div>
          }
        >
          {/* Warehouse ẩn — auto gán kho mặc định của shop khi đổi shop / load metadata. */}
          <Form.Item name="warehouseId" hidden>
            <Input />
          </Form.Item>
          {shopMetadataError ? (
            <Alert
              type="error"
              showIcon
              style={{ marginBottom: 12 }}
              title={
                (shopMetadataErr as Error)?.message ||
                "Không tải được kho mặc định — kiểm tra cookie 4Seller của seller."
              }
              action={
                <Button size="small" onClick={() => void refetchShopMetadata()}>
                  Thử lại
                </Button>
              }
            />
          ) : null}
          <Form.Item
            label="Weight with Package"
            required
            labelCol={{ flex: "0 0 240px" }}
            wrapperCol={{ flex: "1 1 auto" }}
          >
            <Space.Compact className="fs-field-compact">
              <Form.Item
                name="packageWeight"
                noStyle
                rules={[{ required: true, message: "Can not be empty" }]}
              >
                <InputNumber
                  min={0}
                  step={0.01}
                  placeholder="Enter the product weight"
                />
              </Form.Item>
              <Form.Item name="weightUnit" noStyle initialValue="lb">
                <Select options={[...WEIGHT_UNIT_OPTIONS]} />
              </Form.Item>
            </Space.Compact>
          </Form.Item>
          <Form.Item
            label="Product Dimensions"
            required
            labelCol={{ flex: "0 0 240px" }}
            wrapperCol={{ flex: "1 1 auto" }}
          >
            <Space.Compact className="fs-field-compact fs-field-compact--dims">
              <Form.Item
                name="packageLength"
                noStyle
                rules={[{ required: true, message: "Can not be empty" }]}
              >
                <InputNumber min={0} placeholder="Length" />
              </Form.Item>
              <Form.Item
                name="packageWidth"
                noStyle
                rules={[{ required: true, message: "Can not be empty" }]}
              >
                <InputNumber min={0} placeholder="Width" />
              </Form.Item>
              <Form.Item
                name="packageHeight"
                noStyle
                rules={[{ required: true, message: "Can not be empty" }]}
              >
                <InputNumber min={0} placeholder="Height" />
              </Form.Item>
              <Form.Item name="dimensionUnit" noStyle initialValue="inch">
                <Select options={[...DIMENSION_UNIT_OPTIONS]} />
              </Form.Item>
            </Space.Compact>
          </Form.Item>
        </Card>

        <Card
          title="Product Certification"
          size="small"
          style={{ marginBottom: 16 }}
          className="fs-field-card"
        >
          <Form.Item
            label="Dangerous Goods Or Hazardous Materials"
            labelCol={{ flex: "0 0 240px" }}
            wrapperCol={{ flex: "1 1 auto" }}
          >
            <Select
              className="fs-field-select"
              value={hazardous}
              onChange={setHazardous}
              options={YES_NO}
            />
          </Form.Item>
          <Form.Item
            label="CA Prop 65: Repro. Chems"
            labelCol={{ flex: "0 0 240px" }}
            wrapperCol={{ flex: "1 1 auto" }}
          >
            <Select
              className="fs-field-select"
              value={prop65Repro}
              onChange={setProp65Repro}
              options={YES_NO}
            />
          </Form.Item>
          <Form.Item
            label="CA Prop 65: Carcinogens"
            labelCol={{ flex: "0 0 240px" }}
            wrapperCol={{ flex: "1 1 auto" }}
          >
            <Select
              className="fs-field-select"
              value={prop65Carcinogen}
              onChange={setProp65Carcinogen}
              options={YES_NO}
            />
          </Form.Item>
        </Card>

        <Form.Item name="safetyWarning" hidden>
          <Input />
        </Form.Item>
        <Form.Item name="fourSellerGroupId" hidden>
          <Input />
        </Form.Item>
        <Form.Item name="fourSellerGroupName" hidden>
          <Input />
        </Form.Item>
        <Form.Item name="appliedRules" hidden>
          <Input />
        </Form.Item>
        <Form.Item name="refListingId" hidden>
          <Input />
        </Form.Item>
        <Form.Item name="refShopId" hidden>
          <Input />
        </Form.Item>
        <Form.Item name="siteId" hidden>
          <Input />
        </Form.Item>

        <Card
          title="Status (4Seller)"
          size="small"
          style={{ marginBottom: 16 }}
        >
          <Space
            orientation="vertical"
            size={4}
            style={{ width: "100%" }}
            styles={{ item: { width: "100%" } }}
          >
            <Text>
              Page: <Tag>{listing.pageType}</Tag>
              Site status: <Tag>{listing.siteStatus || "—"}</Tag>
              Publish: <Tag>{listing.publishStatus || "—"}</Tag>
            </Text>
            <Text type="secondary">
              Last sync:{" "}
              {formatDateTimeVn(listing.lastSyncedAt)}
              {listing.lastPushAt
                ? ` · Last push: ${formatDateTimeVn(listing.lastPushAt)}`
                : ""}
            </Text>
            {listing.tikTokProductId ? (
              <Text type="secondary">
                TikTok product: {listing.tikTokProductId}
              </Text>
            ) : null}
            {syncErrorDisplay ? (
              <Alert
                type={
                  sawFailedUploadDupes && !failedUploadDupesRemain
                    ? "info"
                    : "warning"
                }
                showIcon
                className="fs-sync-error-alert"
                icon={
                  sawFailedUploadDupes && !failedUploadDupesRemain ? (
                    <CheckOutlined style={{ color: "#1677ff", fontSize: 14 }} />
                  ) : (
                    <ExclamationCircleFilled style={{ color: "#ff4d4f", fontSize: 14 }} />
                  )
                }
                style={{ marginTop: 8 }}
                title={
                  sawFailedUploadDupes && !failedUploadDupesRemain ? (
                    <Tooltip
                      title={
                        <span style={{ whiteSpace: "pre-wrap" }}>
                          Lỗi gốc:{" "}
                          {renderSyncErrorWithImageLinks(
                            syncErrorDisplay.body,
                            (url) => {
                              focusFailedUploadImage(url);
                            },
                          )}
                        </span>
                      }
                    >
                      <span className="fs-sync-error-alert__text">
                        Đã hết ảnh trùng trên form — Lưu & Publish lại để 4Seller
                        xóa cảnh báo
                      </span>
                    </Tooltip>
                  ) : (
                    <Tooltip title={syncErrorDisplay.body}>
                      <span className="fs-sync-error-alert__text">
                        {renderSyncErrorWithImageLinks(
                          syncErrorDisplay.body,
                          (url) => {
                            focusFailedUploadImage(url);
                          },
                        )}
                      </span>
                    </Tooltip>
                  )
                }
              />
            ) : null}
            <Space>
              <Avatar shape="square" size={24}>
                {(listing.ownerName || "?").charAt(0)}
              </Avatar>
              <Text type="secondary">
                Owner: {listing.ownerName || "—"} · Shop:{" "}
                {shops.find((s) => s.id === (watchedShopId || listing.shopId))
                  ?.name ||
                  listing.shopName ||
                  "—"}
              </Text>
            </Space>
          </Space>
        </Card>
      </Form>

      <div className="fs-form-footer">
        <Button onClick={() => navigate("/listings/draft")}>Hủy</Button>
        {canManage ? <Space wrap>{saveButtons}</Space> : null}
      </div>

      <Modal
        title={batchModal?.title ?? "Batch"}
        open={!!batchModal}
        onCancel={() => setBatchModal(null)}
        onOk={confirmBatchModal}
        okText="Áp dụng"
        cancelText="Hủy"
        destroyOnHidden
      >
        <InputNumber
          autoFocus
          style={{ width: "100%" }}
          min={0}
          step={batchModal?.field === "quantity" ? 1 : 0.01}
          placeholder={
            batchModal?.field === "quantity"
              ? "Nhập số lượng"
              : batchModal?.field === "costPrice"
                ? "Nhập giá nhập"
                : "Nhập giá bán"
          }
          value={batchDraftNumber}
          onChange={(v) => setBatchDraftNumber(v)}
          onPressEnter={confirmBatchModal}
        />
      </Modal>

      <BatchSkuModal
        open={batchSkuModalOpen}
        variations={variations as ListingVariationRow[]}
        externalProductId={
          listing.fourSellerListingId != null
            ? String(listing.fourSellerListingId)
            : undefined
        }
        onCancel={() => setBatchSkuModalOpen(false)}
        onApply={handleBatchSkuApply}
      />

      <BatchPricingModal
        open={batchPricingModalOpen}
        variations={variations as ListingVariationRow[]}
        activeFormula={pricingFormula}
        activeTemplateId={selectedPricingTemplateId}
        onActiveFormulaChange={handlePricingFormulaChange}
        onCancel={() => setBatchPricingModalOpen(false)}
        onApply={handleBatchPricingApply}
      />

      <ManageShippingTemplateModal
        open={shippingTemplateModalOpen}
        onClose={() => setShippingTemplateModalOpen(false)}
        seed={{
          packageWeight: form.getFieldValue("packageWeight"),
          packageLength: form.getFieldValue("packageLength"),
          packageWidth: form.getFieldValue("packageWidth"),
          packageHeight: form.getFieldValue("packageHeight"),
          weightUnit: form.getFieldValue("weightUnit"),
          dimensionUnit: form.getFieldValue("dimensionUnit"),
        }}
      />

      <Modal
        title="Auto map Specifics — duyệt kết quả"
        open={mapSpecificsOpen}
        onCancel={() => {
          setMapSpecificsOpen(false);
          setMapSpecificsResult(null);
        }}
        onOk={applyMappedSpecifics}
        okText="Áp dụng đã chọn"
        cancelText="Hủy"
        width={860}
        destroyOnHidden
      >
        {mapSpecificsResult ? (
          <Space orientation="vertical" size={12} style={{ width: "100%" }}>
            <Text type="secondary">
              Parsed {mapSpecificsResult.parsedPairCount} cặp · Schema{" "}
              {mapSpecificsResult.schemaAttributeCount} attrs · Matched{" "}
              {mapSpecificsResult.matched.length} · Unmatched{" "}
              {mapSpecificsResult.unmatched.length}
            </Text>
            {mapSpecificsResult.missingRequired.length > 0 ? (
              <Alert
                type="warning"
                showIcon
                title={`Còn thiếu ${mapSpecificsResult.missingRequired.length} thuộc tính bắt buộc`}
                description={mapSpecificsResult.missingRequired
                  .map((m) => m.attrName)
                  .join(", ")}
              />
            ) : null}
            {mapSpecificsResult.matched.length > 0 ? (
              <>
                <div
                  style={{
                    display: "flex",
                    justifyContent: "space-between",
                    gap: 8,
                  }}
                >
                  <Text strong>
                    Matched (chọn để áp dụng — mặc định ≥85%; dưới 85% hiện để chọn tay)
                  </Text>
                  <Space>
                    <Button
                      size="small"
                      type="link"
                      onClick={() =>
                        setSelectedMapKeys(
                          mapSpecificsResult.matched
                            .filter((m) => !m.skippedExisting)
                            .map(mapSpecificsRowKey),
                        )
                      }
                    >
                      Chọn tất cả
                    </Button>
                    <Button
                      size="small"
                      type="link"
                      onClick={() =>
                        setSelectedMapKeys(
                          mapSpecificsResult.matched
                            .filter(
                              (m) => !m.skippedExisting && m.confidence >= 0.85,
                            )
                            .map(mapSpecificsRowKey),
                        )
                      }
                    >
                      Chỉ ≥85%
                    </Button>
                    <Button
                      size="small"
                      type="link"
                      onClick={() => setSelectedMapKeys([])}
                    >
                      Bỏ chọn
                    </Button>
                  </Space>
                </div>
                <Checkbox.Group
                  style={{ width: "100%" }}
                  value={selectedMapKeys}
                  onChange={(keys) => setSelectedMapKeys(keys as string[])}
                >
                  <Space
                    orientation="vertical"
                    style={{ width: "100%" }}
                    size={8}
                  >
                    {mapSpecificsResult.matched.map((m) => {
                      const key = mapSpecificsRowKey(m);
                      return (
                        <div
                          key={key}
                          className="fs-map-specifics-row"
                          style={{
                            opacity: m.skippedExisting ? 0.55 : 1,
                          }}
                        >
                          <Checkbox value={key} disabled={m.skippedExisting} />
                          <div style={{ flex: 1, minWidth: 0 }}>
                            <Space wrap size={4}>
                              <Text strong>{m.attrName}</Text>
                              <Tag>{m.matchMethod}</Tag>
                              <Tag
                                color={
                                  m.confidence >= 0.9
                                    ? "green"
                                    : m.confidence >= 0.85
                                      ? "blue"
                                      : "orange"
                                }
                              >
                                {(m.confidence * 100).toFixed(0)}%
                              </Tag>
                              {m.isRequired ? (
                                <Tag color="orange">Required</Tag>
                              ) : null}
                              {m.skippedExisting ? (
                                <Tag>Giữ giá trị hiện có</Tag>
                              ) : null}
                            </Space>
                            <div>
                              <Text type="secondary">
                                {m.sourceName}: {m.sourceValue}
                              </Text>
                            </div>
                            <div>
                              <Text>
                                → {m.attrValue}
                                {m.valueId ? ` (valueId ${m.valueId})` : ""} ·
                                attrId {m.attrId}
                              </Text>
                            </div>
                          </div>
                        </div>
                      );
                    })}
                  </Space>
                </Checkbox.Group>
              </>
            ) : (
              <Alert
                type="info"
                showIcon
                title="Không có cặp nào map đủ tin cậy."
              />
            )}
            {mapSpecificsResult.unmatched.length > 0 ? (
              <>
                <Text strong>Unmatched</Text>
                <ul style={{ margin: 0, paddingLeft: 18 }}>
                  {mapSpecificsResult.unmatched.map((u) => (
                    <li key={`${u.sourceName}:${u.sourceValue}`}>
                      <Text>
                        {u.sourceName}: {u.sourceValue}
                      </Text>{" "}
                      <Text type="secondary">— {u.reason}</Text>
                    </li>
                  ))}
                </ul>
              </>
            ) : null}
          </Space>
        ) : null}
      </Modal>
    </div>
  );
};
