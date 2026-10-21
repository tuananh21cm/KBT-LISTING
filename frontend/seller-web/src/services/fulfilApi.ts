import { createFulfilApi } from '@drop/api-client';
import api from './api';

/** Seller fulfil API — `/fulfil` (owner-scoped transactions + options). */
export const fulfilApi = createFulfilApi(api, { basePath: '/fulfil' });
