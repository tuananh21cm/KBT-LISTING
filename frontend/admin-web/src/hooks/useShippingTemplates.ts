import { useMemo } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import {
  loadShippingTemplatesCache,
  saveShippingTemplatesCache,
} from '../features/products/shippingTemplatesStorage';
import { shippingTemplateApi } from '../services/shippingTemplateApi';
import { useAuthStore } from '../stores/authStore';
import type {
  CreateShippingTemplateRequest,
  ShippingTemplate,
  UpdateShippingTemplateRequest,
} from '../types/shippingTemplate';

export const shippingTemplateKeys = {
  all: ['shippingTemplates'] as const,
  byUser: (userId: string) => [...shippingTemplateKeys.all, userId] as const,
};

const SHIPPING_TEMPLATES_STALE_MS = 5 * 60 * 1000;

function persistTemplates(userId: string | undefined, templates: ShippingTemplate[]) {
  if (!userId) return;
  saveShippingTemplatesCache(userId, templates);
}

/** Lists shipping templates owned by the signed-in user only (`CreatedById` = JWT). */
export const useShippingTemplates = (enabled = true) => {
  const userId = useAuthStore((s) => s.user?.id);
  const cacheSnapshot = useMemo(
    () => (userId ? loadShippingTemplatesCache(userId) : null),
    [userId],
  );

  return useQuery({
    queryKey: userId ? shippingTemplateKeys.byUser(userId) : shippingTemplateKeys.all,
    queryFn: async () => {
      const response = await shippingTemplateApi.getAll();
      if (!response.success) throw new Error(response.message);
      const data = response.data ?? [];
      persistTemplates(userId, data);
      return data;
    },
    enabled: enabled && !!userId,
    staleTime: SHIPPING_TEMPLATES_STALE_MS,
    // Instant select / modal: hydrate from localStorage while network refreshes in background.
    initialData: cacheSnapshot?.templates,
    initialDataUpdatedAt: cacheSnapshot?.savedAt
      ? Date.parse(cacheSnapshot.savedAt)
      : undefined,
  });
};

export const useCreateShippingTemplate = () => {
  const queryClient = useQueryClient();
  const userId = useAuthStore((s) => s.user?.id);
  return useMutation({
    mutationFn: (data: CreateShippingTemplateRequest) => shippingTemplateApi.create(data),
    onSuccess: (res) => {
      if (res.success) {
        message.success(res.message || 'Đã tạo shipping template.');
        if (res.data && userId) {
          queryClient.setQueryData<ShippingTemplate[]>(shippingTemplateKeys.byUser(userId), (prev) => {
            const next = [...(prev ?? []), res.data!];
            persistTemplates(userId, next);
            return next;
          });
        }
        if (userId) {
          void queryClient.invalidateQueries({ queryKey: shippingTemplateKeys.byUser(userId) });
        }
      } else {
        message.error(res.message || 'Tạo template thất bại.');
      }
    },
    onError: (err: Error) => message.error(err.message || 'Tạo template thất bại.'),
  });
};

export const useUpdateShippingTemplate = () => {
  const queryClient = useQueryClient();
  const userId = useAuthStore((s) => s.user?.id);
  return useMutation({
    mutationFn: ({ id, data }: { id: string; data: UpdateShippingTemplateRequest }) =>
      shippingTemplateApi.update(id, data),
    onSuccess: (res, vars) => {
      if (res.success) {
        message.success(res.message || 'Đã cập nhật shipping template.');
        if (res.data && userId) {
          queryClient.setQueryData<ShippingTemplate[]>(shippingTemplateKeys.byUser(userId), (prev) => {
            const next = (prev ?? []).map((t) => (t.id === vars.id ? res.data! : t));
            persistTemplates(userId, next);
            return next;
          });
        }
        if (userId) {
          void queryClient.invalidateQueries({ queryKey: shippingTemplateKeys.byUser(userId) });
        }
      } else {
        message.error(res.message || 'Cập nhật template thất bại.');
      }
    },
    onError: (err: Error) => message.error(err.message || 'Cập nhật template thất bại.'),
  });
};

export const useDeleteShippingTemplate = () => {
  const queryClient = useQueryClient();
  const userId = useAuthStore((s) => s.user?.id);
  return useMutation({
    mutationFn: (id: string) => shippingTemplateApi.delete(id),
    onSuccess: (res, id) => {
      if (res.success) {
        message.success(res.message || 'Đã xóa shipping template.');
        if (userId) {
          queryClient.setQueryData<ShippingTemplate[]>(shippingTemplateKeys.byUser(userId), (prev) => {
            const next = (prev ?? []).filter((t) => t.id !== id);
            persistTemplates(userId, next);
            return next;
          });
          void queryClient.invalidateQueries({ queryKey: shippingTemplateKeys.byUser(userId) });
        }
      } else {
        message.error(res.message || 'Xóa template thất bại.');
      }
    },
    onError: (err: Error) => message.error(err.message || 'Xóa template thất bại.'),
  });
};
