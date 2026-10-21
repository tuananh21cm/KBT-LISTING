import api from './api';
import type { ApiResponse } from '../types/api';
import type {
  FourSellerCrawlUrlsRequest,
  FourSellerCrawlUrlsResponse,
  FourSellerMoveDraftPage,
  FourSellerMoveDraftPageRequest,
  FourSellerSaveToPlatformRequest,
  FourSellerSaveToPlatformResponse,
  FourSellerScheduleProgress,
} from '../types/productScrape';

export const adminProductScrapeApi = {
  submitUrls: async (
    request: FourSellerCrawlUrlsRequest,
  ): Promise<ApiResponse<FourSellerCrawlUrlsResponse>> => {
    const response = await api.post<ApiResponse<FourSellerCrawlUrlsResponse>>(
      '/admin/fourseller/crawl/urls',
      request,
    );
    return response.data;
  },

  getDrafts: async (
    request: FourSellerMoveDraftPageRequest,
  ): Promise<ApiResponse<FourSellerMoveDraftPage>> => {
    const response = await api.post<ApiResponse<FourSellerMoveDraftPage>>(
      '/admin/fourseller/crawl/drafts',
      request,
    );
    return response.data;
  },

  saveToPlatform: async (
    request: FourSellerSaveToPlatformRequest,
  ): Promise<ApiResponse<FourSellerSaveToPlatformResponse>> => {
    const response = await api.post<ApiResponse<FourSellerSaveToPlatformResponse>>(
      '/admin/fourseller/crawl/save-to-platform',
      request,
    );
    return response.data;
  },

  getSchedule: async (scheduleCode: string): Promise<ApiResponse<FourSellerScheduleProgress>> => {
    const response = await api.get<ApiResponse<FourSellerScheduleProgress>>(
      '/admin/fourseller/crawl/schedule',
      { params: { scheduleCode } },
    );
    return response.data;
  },

  deleteDrafts: async (
    moveDraftIds: number[],
  ): Promise<ApiResponse<{ deleted: number }>> => {
    const response = await api.post<ApiResponse<{ deleted: number }>>(
      '/admin/fourseller/crawl/delete-drafts',
      { moveDraftIds },
    );
    return response.data;
  },
};
