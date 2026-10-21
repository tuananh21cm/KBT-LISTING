import api from './api';
import { createOrderApi, type OrderDeleteResult } from '@drop/api-client';

export type { OrderDeleteResult };

export const adminOrderApi = createOrderApi(api, { basePath: '/admin/orders' });
