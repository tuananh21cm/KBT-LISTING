import axios from 'axios';
import { API_BASE_URL } from '../config/apiBaseUrl';
import type { OrderSyncHealthResponse } from '../types/orderSyncHealth';

/** Anonymous liveness + order-sync runtime snapshot (also pulses sync when due). */
export const healthApi = {
  get: async (): Promise<OrderSyncHealthResponse> => {
    const { data } = await axios.get<OrderSyncHealthResponse>(`${API_BASE_URL}/health`, {
      timeout: 15_000,
    });
    return data;
  },
};
