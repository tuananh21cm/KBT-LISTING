import api from './api';
import { createSupplierSellerApi } from '@drop/api-client';

export const supplierSellerApi = createSupplierSellerApi(api, {
  basePath: '/supplier-sellers',
});
