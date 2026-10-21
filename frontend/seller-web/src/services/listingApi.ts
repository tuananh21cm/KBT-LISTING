import api from './api';
import { createListingApi } from '@drop/api-client';

export const listingApi = createListingApi(api, { basePath: '/listings' });
