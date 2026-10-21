import api from './api';
import { createShopApi } from '@drop/api-client';

export const adminShopApi = createShopApi(api, { basePath: '/admin/shops' });
