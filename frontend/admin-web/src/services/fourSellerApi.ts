import api from './api';
import { createFourSellerApi } from '@drop/api-client';

export const fourSellerApi = createFourSellerApi(api, { basePath: '/admin/fourseller' });
