import { createFulfilApi, type FulfilApi } from '@drop/api-client';
import api from './api';

/** Admin surface — all catalogue / stats / bulk methods present. */
export type AdminFulfilApi = Required<FulfilApi>;

/** Admin fulfil API — `/admin/fulfil` (accounts/cards + transactions + stats). */
export const adminFulfilApi = createFulfilApi(api, {
  basePath: '/admin/fulfil',
}) as AdminFulfilApi;
