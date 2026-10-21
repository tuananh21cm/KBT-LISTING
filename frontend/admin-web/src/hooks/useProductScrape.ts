import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import axios from 'axios';
import { adminProductScrapeApi } from '../services/adminProductScrapeApi';
import type {
  FourSellerCrawlUrlsResponse,
  FourSellerMoveDraftPageRequest,
  FourSellerSaveToPlatformRequest,
  FourSellerScheduleProgress,
} from '../types/productScrape';

const scrapeErrorMessage = (error: unknown, fallback: string) => {
  if (axios.isAxiosError(error)) {
    const apiMessage = error.response?.data?.message;
    if (typeof apiMessage === 'string' && apiMessage.trim()) {
      return apiMessage;
    }
  }
  if (error instanceof Error && error.message) {
    return error.message;
  }
  return fallback;
};

const handleScrapeError = (error: unknown, fallback: string) => {
  const msg = scrapeErrorMessage(error, fallback);
  const lower = msg.toLowerCase();
  // Only remap explicit auth failures — data:[] is NOT cookie expiry.
  if (
    lower.includes('4001') ||
    lower.includes('login.back.validation_failed') ||
    msg.includes('Chưa cấu hình cookie 4Seller') ||
    msg.includes('Cookie 4Seller thiếu') ||
    msg.includes('Cookie 4Seller có thể đã hết hạn')
  ) {
    message.error(
      'Cookie 4Seller thiếu/hết hạn (code 4001). Vào Quản lý Người dùng → cập nhật 4Seller Cookie.',
    );
    return;
  }
  message.error(msg);
};

/** Normalize source URL for matching scrape results (Shein -p-id, else origin+path). */
export const sourceUrlKey = (url: string) => {
  try {
    const parsed = new URL(url);
    const productId = parsed.pathname.match(/-p-(\d+)/i)?.[1];
    if (productId) return `p:${productId}`;
    return `${parsed.origin}${parsed.pathname}`.toLowerCase();
  } catch {
    return url.trim().toLowerCase();
  }
};

export const useMoveDrafts = (request: FourSellerMoveDraftPageRequest, enabled = true) =>
  useQuery({
    queryKey: [
      'productScrapeDrafts',
      request.pageCurrent,
      request.pageSize,
      request.searchValue,
      request.batchId,
      request.mineOnly,
      request.createdById,
    ],
    queryFn: async () => {
      const response = await adminProductScrapeApi.getDrafts(request);
      if (!response.success || !response.data) {
        throw new Error(response.message || 'Không tải được danh sách draft scrape.');
      }
      return response.data;
    },
    enabled,
    refetchOnWindowFocus: false,
  });

/** Submit URLs — returns batchId. */
export const useSubmitCrawlUrls = () =>
  useMutation({
    mutationFn: async (urls: string[]): Promise<FourSellerCrawlUrlsResponse> => {
      const response = await adminProductScrapeApi.submitUrls({ urls });
      if (!response.success || !response.data?.batchId) {
        throw new Error(response.message || 'Không gửi được URL scrape.');
      }
      return response.data;
    },
    onError: (error) => handleScrapeError(error, 'Không gửi được URL scrape.'),
  });

export const useSaveToPlatform = () =>
  useMutation({
    mutationFn: async (request: FourSellerSaveToPlatformRequest) => {
      const response = await adminProductScrapeApi.saveToPlatform(request);
      if (!response.success || !response.data?.scheduleCode) {
        throw new Error(response.message || 'Không đẩy được draft lên platform.');
      }
      return response.data;
    },
    onSuccess: (data) => {
      message.success(`Đã tạo schedule: ${data.scheduleCode}`);
    },
    onError: (error) => handleScrapeError(error, 'Không đẩy được draft lên platform.'),
  });

export const useDeleteMoveDrafts = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: async (moveDraftIds: number[]) => {
      const response = await adminProductScrapeApi.deleteDrafts(moveDraftIds);
      if (!response.success) {
        throw new Error(response.message || 'Không xóa được draft.');
      }
      return response.data;
    },
    onSuccess: (_data, ids) => {
      message.success(ids.length === 1 ? 'Đã xóa draft.' : `Đã xóa ${ids.length} draft.`);
      queryClient.invalidateQueries({ queryKey: ['productScrapeDrafts'] });
    },
    onError: (error) => handleScrapeError(error, 'Không xóa được draft.'),
  });
};

export const pollScheduleProgress = async (
  scheduleCode: string,
  options?: {
    intervalMs?: number;
    timeoutMs?: number;
    onProgress?: (progress: FourSellerScheduleProgress) => void;
    signal?: AbortSignal;
  },
): Promise<FourSellerScheduleProgress> => {
  const intervalMs = options?.intervalMs ?? 2000;
  const timeoutMs = options?.timeoutMs ?? 120_000;
  const started = Date.now();

  while (true) {
    if (options?.signal?.aborted) {
      throw new Error('Đã hủy theo dõi tiến trình.');
    }

    const response = await adminProductScrapeApi.getSchedule(scheduleCode);
    if (!response.success || !response.data) {
      throw new Error(response.message || 'Không lấy được tiến trình schedule.');
    }

    const progress = response.data;
    options?.onProgress?.(progress);

    if (progress.total > 0 && progress.succeed + progress.failed >= progress.total) {
      return progress;
    }

    if (Date.now() - started >= timeoutMs) {
      return progress;
    }

    await new Promise<void>((resolve, reject) => {
      const timer = setTimeout(resolve, intervalMs);
      options?.signal?.addEventListener(
        'abort',
        () => {
          clearTimeout(timer);
          reject(new Error('Đã hủy theo dõi tiến trình.'));
        },
        { once: true },
      );
    });
  }
};
