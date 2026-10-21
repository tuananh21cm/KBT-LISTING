import api from './api';
import type { ApiResponse } from '@drop/types';

export interface KbtCrmSyncResult {
  crmGroups: number;
  crmUsers: number;
  crmShops: number;
  usersCreated: number;
  usersUpdated: number;
  usersDeactivated: number;
  shopsCreated: number;
  shopsUpdated: number;
  ownersTransferred: number;
  skipped: string[];
  finishedAt: string;
}

/** KBT CRM → local sync (teams, users, shops). */
export const adminKbtCrmApi = {
  sync: async () => {
    const response = await api.post<ApiResponse<KbtCrmSyncResult>>('/admin/kbtcrm/sync');
    return response.data;
  },
};
