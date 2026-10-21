import api from './api';
import { createDashboardApi } from '@drop/api-client';

export const adminDashboardApi = createDashboardApi(api, { basePath: '/admin/dashboard' });
