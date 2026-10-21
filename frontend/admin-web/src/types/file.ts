export interface UserFile {
  id: string;
  originalFileName: string;
  storagePath: string;
  publicUrl?: string;
  contentType: string;
  fileSizeBytes: number;
  fileHash: string;
  metadata?: string;
  linkedEntityType?: string;
  linkedEntityId?: string;
  isDuplicate: boolean;
  createdAt: string;
}
