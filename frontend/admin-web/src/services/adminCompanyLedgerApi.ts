import api from './api';
import { createCompanyLedgerApi } from '@drop/api-client';

export const adminCompanyLedgerApi = createCompanyLedgerApi(api, {
  basePath: '/admin/company-ledger',
});
