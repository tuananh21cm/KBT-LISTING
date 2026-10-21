import api from './api';
import { createReceivingBankApi } from '@drop/api-client';

export const receivingBankApi = createReceivingBankApi(api, {
  basePath: '/receiving-bank-accounts',
});
