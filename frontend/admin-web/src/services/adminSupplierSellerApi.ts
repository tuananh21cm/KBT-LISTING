import api from './api';
import { createSupplierSellerApi } from '@drop/api-client';

export const adminSupplierSellerApi = createSupplierSellerApi(api, {
  basePath: '/admin/supplier-sellers',
});
