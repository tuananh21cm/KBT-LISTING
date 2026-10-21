import api from './api';
import { createOrderApi } from '@drop/api-client';

export const orderApi = createOrderApi(api, { basePath: '/orders' });
