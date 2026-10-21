import api from './api';
import { createShopApi } from '@drop/api-client';

export const shopApi = createShopApi(api, { basePath: '/shops' });
