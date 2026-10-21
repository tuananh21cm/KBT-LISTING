import { useMemo } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import {
  loadPricingTemplatesCache,
  savePricingTemplatesCache,
} from '../features/products/pricingTemplatesStorage';
import { pricingTemplateApi } from '../services/pricingTemplateApi';
import { useAuthStore } from '../stores/authStore';
import type {
  CreatePricingTemplateRequest,
  PricingTemplate,
  UpdatePricingTemplateRequest,
} from '../types/pricingTemplate';

export const pricingTemplateKeys = {
  all: ['pricingTemplates'] as const,
  byUser: (userId: string) => [...pricingTemplateKeys.all, userId] as const,
};

const PRICING_TEMPLATES_STALE_MS = 5 * 60 * 1000;

function persistTemplates(userId: string | undefined, templates: PricingTemplate[]) {
  if (!userId) return;
  savePricingTemplatesCache(userId, templates);
}

/** Lists pricing templates owned by the signed-in user only (`CreatedById` = JWT). */
export const usePricingTemplates = (enabled = true) => {
  const userId = useAuthStore((s) => s.user?.id);
  const cacheSnapshot = useMemo(
    () => (userId ? loadPricingTemplatesCache(userId) : null),
    [userId],
  );

  return useQuery({
    queryKey: userId ? pricingTemplateKeys.byUser(userId) : pricingTemplateKeys.all,
    queryFn: async () => {
      const response = await pricingTemplateApi.getAll();
      if (!response.success) throw new Error(response.message);
      const data = response.data ?? [];
      persistTemplates(userId, data);
      return data;
    },
    enabled: enabled && !!userId,
    staleTime: PRICING_TEMPLATES_STALE_MS,
    // Instant modal open: hydrate from localStorage while network refreshes in background.
    initialData: cacheSnapshot?.templates,
    initialDataUpdatedAt: cacheSnapshot?.savedAt
      ? Date.parse(cacheSnapshot.savedAt)
      : undefined,
  });
};

export const useCreatePricingTemplate = () => {
  const queryClient = useQueryClient();
  const userId = useAuthStore((s) => s.user?.id);
  return useMutation({
    mutationFn: (data: CreatePricingTemplateRequest) => pricingTemplateApi.create(data),
    onSuccess: (res) => {
      if (res.success) {
        message.success(res.message || 'Đã tạo pricing template.');
        if (res.data && userId) {
          queryClient.setQueryData<PricingTemplate[]>(pricingTemplateKeys.byUser(userId), (prev) => {
            const next = [...(prev ?? []), res.data!];
            persistTemplates(userId, next);
            return next;
          });
        }
        if (userId) {
          void queryClient.invalidateQueries({ queryKey: pricingTemplateKeys.byUser(userId) });
        }
      } else {
        message.error(res.message || 'Tạo template thất bại.');
      }
    },
    onError: (err: Error) => message.error(err.message || 'Tạo template thất bại.'),
  });
};

export const useUpdatePricingTemplate = () => {
  const queryClient = useQueryClient();
  const userId = useAuthStore((s) => s.user?.id);
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdatePricingTemplateRequest }) =>
      pricingTemplateApi.update(id, data),
    onSuccess: (res, vars) => {
      if (res.success) {
        message.success(res.message || 'Đã cập nhật pricing template.');
        if (res.data && userId) {
          queryClient.setQueryData<PricingTemplate[]>(pricingTemplateKeys.byUser(userId), (prev) => {
            const next = (prev ?? []).map((t) => (t.id === vars.id ? res.data! : t));
            persistTemplates(userId, next);
            return next;
          });
        }
        if (userId) {
          void queryClient.invalidateQueries({ queryKey: pricingTemplateKeys.byUser(userId) });
        }
      } else {
        message.error(res.message || 'Cập nhật template thất bại.');
      }
    },
    onError: (err: Error) => message.error(err.message || 'Cập nhật template thất bại.'),
  });
};

export const useDeletePricingTemplate = () => {
  const queryClient = useQueryClient();
  const userId = useAuthStore((s) => s.user?.id);
  return useMutation({
    mutationFn: (id: string) => pricingTemplateApi.delete(id),
    onSuccess: (res, id) => {
      if (res.success) {
        message.success(res.message || 'Đã xóa pricing template.');
        if (userId) {
          queryClient.setQueryData<PricingTemplate[]>(pricingTemplateKeys.byUser(userId), (prev) => {
            const next = (prev ?? []).filter((t) => t.id !== id);
            persistTemplates(userId, next);
            return next;
          });
          void queryClient.invalidateQueries({ queryKey: pricingTemplateKeys.byUser(userId) });
        }
      } else {
        message.error(res.message || 'Xóa template thất bại.');
      }
    },
    onError: (err: Error) => message.error(err.message || 'Xóa template thất bại.'),
  });
};
