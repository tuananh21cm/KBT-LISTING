import api from './api';
import { createListingApi } from '@drop/api-client';

export const adminListingApi = createListingApi(api, { basePath: '/admin/listings' });
