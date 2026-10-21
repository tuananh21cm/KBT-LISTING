import api from './api';
import { createShopTaskApi } from '@drop/api-client';

export const shopTaskApi = createShopTaskApi(api, { basePath: '/shop-tasks' });
