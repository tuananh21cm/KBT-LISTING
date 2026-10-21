import { Cascader } from 'antd';
import type { DefaultOptionType } from 'antd/es/cascader';
import { useEffect, useRef, useState, type ComponentProps, type MouseEvent, type ReactElement } from 'react';
import { adminCategoryApi } from '../../services/adminCategoryApi';
import type { CategoryTreeNode } from '../../types/category';
import './categories.css';

export type CategoryCascaderSelection = { id: string; name: string; pathLabel?: string };

interface CategoryCascaderProps {
  site?: string;
  /** Selected path(s) of external IDs (only the last id is required — ancestors resolve lazily). */
  value?: string[] | string[][];
  multiple?: boolean;
  /** `panel` = menus always open (category picker modal). */
  variant?: 'select' | 'panel';
  onChange?: (
    path: string[] | string[][] | undefined,
    selected?: CategoryCascaderSelection | CategoryCascaderSelection[] | null,
  ) => void;
  disabled?: boolean;
  placeholder?: string;
}

const toOption = (n: CategoryTreeNode): DefaultOptionType => ({
  value: n.value,
  label: n.label,
  isLeaf: n.isLeaf,
  children: n.children && n.children.length ? n.children.map(toOption) : undefined,
});

/**
 * Legacy helper kept for callers that still resolve a path from an in-memory tree.
 * The Cascader itself no longer needs the full tree (lazy children endpoint).
 */
export const findCategoryPath = (
  nodes: CategoryTreeNode[],
  targetId: string,
  trail: string[] = [],
): string[] | undefined => {
  for (const node of nodes) {
    const next = [...trail, node.value];
    if (node.value === targetId) {
      return next;
    }
    if (node.children?.length) {
      const found = findCategoryPath(node.children, targetId, next);
      if (found) {
        return found;
      }
    }
  }
  return undefined;
};

/** Immutably attach loaded children onto the node identified by its value path. */
const attachChildren = (
  options: DefaultOptionType[],
  path: string[],
  children: DefaultOptionType[],
): DefaultOptionType[] =>
  options.map((opt) => {
    if (String(opt.value) !== path[0]) return opt;
    if (path.length === 1) {
      return { ...opt, children, loading: false };
    }
    return {
      ...opt,
      children: opt.children
        ? attachChildren(opt.children, path.slice(1), children)
        : opt.children,
    };
  });

const mergeOptions = (left: DefaultOptionType[], right: DefaultOptionType[]): DefaultOptionType[] => {
  const byValue = new Map<string, DefaultOptionType>();
  for (const opt of left) byValue.set(String(opt.value), { ...opt });
  for (const opt of right) {
    const key = String(opt.value);
    const existing = byValue.get(key);
    if (!existing) {
      byValue.set(key, { ...opt });
      continue;
    }
    const childrenA = (existing.children ?? []) as DefaultOptionType[];
    const childrenB = (opt.children ?? []) as DefaultOptionType[];
    if (childrenA.length || childrenB.length) {
      existing.children = mergeOptions(childrenA, childrenB);
      existing.isLeaf = false;
    }
  }
  return [...byValue.values()];
};

const extractLeafIds = (value: string[] | string[][] | undefined, multiple: boolean): string[] => {
  if (!value || value.length === 0) return [];
  if (multiple) {
    return (value as string[][])
      .map((path) => (path.length ? String(path[path.length - 1]) : ''))
      .filter(Boolean);
  }
  const path = value as string[];
  return path.length ? [String(path[path.length - 1])] : [];
};

const resolveKey = (site: string, leafIds: string[]) => `${site}::${[...leafIds].sort().join(',')}`;

const popupContainer = (trigger: HTMLElement) =>
  (trigger.closest('.ant-modal-wrap') as HTMLElement | null) ?? document.body;

/** antd 6 Cascader overloads reject `multiple: boolean` with mixed single/multi value. */
type CascaderLooseProps = Omit<ComponentProps<typeof Cascader>, 'multiple' | 'value' | 'onChange'> & {
  multiple?: boolean;
  value?: string[] | string[][];
  onChange?: (path: unknown, selectedOptions: unknown) => void;
};
const LooseCascader = Cascader as unknown as (props: CascaderLooseProps) => ReactElement;

/** Checkbox on a parent only selects; also expand so children load like a normal row click. */
const expandParentOnCheckboxClick = (event: MouseEvent) => {
  const target = event.target as HTMLElement;
  if (!target.closest('.ant-cascader-checkbox')) return;
  const item = target.closest('li.ant-cascader-menu-item-expand') as HTMLElement | null;
  if (!item || item.classList.contains('ant-cascader-menu-item-disabled')) return;
  queueMicrotask(() => item.click());
};

const selectionFromPath = (
  path: string[],
  selectedOptions?: DefaultOptionType[],
): CategoryCascaderSelection => {
  const id = String(path[path.length - 1] ?? '');
  const pathLabel = (selectedOptions ?? [])
    .map((opt) => String(opt.label ?? ''))
    .filter(Boolean)
    .join(' / ');
  const name = String(selectedOptions?.[selectedOptions.length - 1]?.label ?? id);
  return { id, name, pathLabel: pathLabel || name };
};

/**
 * TikTok category picker with on-demand loading: only the clicked level's children
 * are fetched (4Seller-style), so opening a listing never ships the whole tree.
 */
export const CategoryCascader = ({
  site = 'US',
  value,
  multiple = false,
  onChange,
  disabled,
  placeholder = 'Chọn category TikTok',
  variant = 'select',
}: CategoryCascaderProps) => {
  const [options, setOptions] = useState<DefaultOptionType[]>([]);
  const [selectedPath, setSelectedPath] = useState<string[] | string[][] | undefined>(value);
  const [loading, setLoading] = useState(false);
  const resolvedKeyRef = useRef<string | null>(null);
  const requestTokenRef = useRef(0);

  const leafIds = extractLeafIds(value, multiple);
  const selectionKey = resolveKey(site, leafIds);
  const isPanel = variant === 'panel';

  useEffect(() => {
    if (!site) {
      setOptions([]);
      setSelectedPath(undefined);
      resolvedKeyRef.current = null;
      return;
    }
    if (selectionKey === resolvedKeyRef.current) return;

    const token = ++requestTokenRef.current;
    setLoading(true);
    void (async () => {
      try {
        if (leafIds.length > 0) {
          const responses = await Promise.all(
            leafIds.map((id) => adminCategoryApi.getPath({ site, externalId: id })),
          );
          if (token !== requestTokenRef.current) return;
          let merged: DefaultOptionType[] = [];
          const paths: string[][] = [];
          responses.forEach((resp, index) => {
            const id = leafIds[index] ?? '';
            if (resp.success && resp.data && resp.data.options.length) {
              merged = mergeOptions(merged, resp.data.options.map(toOption));
              paths.push(resp.data.pathIds);
            } else if (id) {
              paths.push([id]);
            }
          });
          if (merged.length === 0) {
            const roots = await adminCategoryApi.getChildren({ site, parentId: '' });
            if (token !== requestTokenRef.current) return;
            merged = roots.success && roots.data ? roots.data.map(toOption) : [];
          }
          setOptions(merged);
          setSelectedPath(multiple ? paths : paths[0]);
          resolvedKeyRef.current = selectionKey;
          return;
        }

        const roots = await adminCategoryApi.getChildren({ site, parentId: '' });
        if (token !== requestTokenRef.current) return;
        setOptions(roots.success && roots.data ? roots.data.map(toOption) : []);
        setSelectedPath(undefined);
        resolvedKeyRef.current = selectionKey;
      } catch {
        if (token !== requestTokenRef.current) return;
        setOptions([]);
        setSelectedPath(multiple ? (leafIds.length ? leafIds.map((id) => [id]) : undefined) : leafIds);
        resolvedKeyRef.current = selectionKey;
      } finally {
        if (token === requestTokenRef.current) setLoading(false);
      }
    })();
  }, [site, selectionKey, multiple]);

  const loadData = (selectedOptions: DefaultOptionType[]) => {
    const target = selectedOptions[selectedOptions.length - 1];
    if (!target || target.isLeaf) return;
    if (target.children && target.children.length) return;
    const path = selectedOptions.map((o) => String(o.value));
    target.loading = true;
    void (async () => {
      try {
        const resp = await adminCategoryApi.getChildren({
          site,
          parentId: String(target.value),
        });
        const children = resp.success && resp.data ? resp.data.map(toOption) : [];
        setOptions((prev) => attachChildren(prev, path, children));
      } catch {
        setOptions((prev) => attachChildren(prev, path, []));
      }
    })();
  };

  const cascader = (
    <LooseCascader
      style={{ width: '100%' }}
      options={options}
      value={selectedPath}
      loading={loading}
      disabled={disabled}
      placeholder={placeholder}
      multiple={multiple}
      maxTagCount={multiple ? 'responsive' : undefined}
      showCheckedStrategy={multiple ? Cascader.SHOW_PARENT : undefined}
      changeOnSelect={multiple}
      loadData={loadData}
      open={isPanel ? true : undefined}
      onOpenChange={isPanel ? () => undefined : undefined}
      popupRender={
        multiple
          ? (menu) => <div onClickCapture={expandParentOnCheckboxClick}>{menu}</div>
          : undefined
      }
      displayRender={multiple ? undefined : (labels) => labels.join(' / ')}
      getPopupContainer={(trigger) => {
        if (isPanel) {
          return (
            (trigger.closest('.category-cascader-panel') as HTMLElement | null) ??
            trigger.parentElement ??
            document.body
          );
        }
        return popupContainer(trigger);
      }}
      styles={{ popup: { root: { zIndex: isPanel ? 1 : 1100 } } }}
      notFoundContent={loading ? 'Đang tải…' : 'Chưa có category. Sync catalog trước.'}
      onChange={(path, selectedOptions) => {
        if (multiple) {
          const paths = ((path as (string | number)[][] | undefined) ?? []).map((p) => p.map(String));
          if (paths.length === 0) {
            setSelectedPath(undefined);
            resolvedKeyRef.current = resolveKey(site, []);
            onChange?.(undefined, []);
            return;
          }
          const optionRows = (selectedOptions as DefaultOptionType[][] | undefined) ?? [];
          const selected = paths.map((p, i) => selectionFromPath(p, optionRows[i]));
          setSelectedPath(paths);
          resolvedKeyRef.current = resolveKey(site, selected.map((s) => s.id));
          onChange?.(paths, selected);
          return;
        }

        const single = (path as (string | number)[] | undefined) ?? [];
        if (single.length === 0) {
          setSelectedPath(undefined);
          resolvedKeyRef.current = resolveKey(site, []);
          onChange?.(undefined, undefined);
          return;
        }
        const strPath = single.map(String);
        const selected = selectionFromPath(
          strPath,
          selectedOptions as DefaultOptionType[] | undefined,
        );
        setSelectedPath(strPath);
        resolvedKeyRef.current = resolveKey(site, [selected.id]);
        onChange?.(strPath, selected);
      }}
    />
  );

  return isPanel ? <div className="category-cascader-panel">{cascader}</div> : cascader;
};
