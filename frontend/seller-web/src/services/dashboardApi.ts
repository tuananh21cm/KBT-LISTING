import api from './api';
import { createDashboardApi } from '@drop/api-client';

export const dashboardApi = createDashboardApi(api, { basePath: '/dashboard' });
