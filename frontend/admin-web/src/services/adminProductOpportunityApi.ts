import api from './api';
import { createProductOpportunityApi } from '@drop/api-client';

export const adminProductOpportunityApi = createProductOpportunityApi(api, {
  basePath: '/admin/product-opportunities',
});
