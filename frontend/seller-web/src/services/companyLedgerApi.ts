import api from './api';
import { createCompanyLedgerApi } from '@drop/api-client';

export const companyLedgerApi = createCompanyLedgerApi(api, { basePath: '/company-ledger' });
