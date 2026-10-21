import api from './api';
import { createProductOpportunityApi } from '@drop/api-client';

export const productOpportunityApi = createProductOpportunityApi(api, {
  basePath: '/product-opportunities',
});
