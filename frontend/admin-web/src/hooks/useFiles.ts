import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { message } from 'antd';
import { fileApi } from '../services/fileApi';

export const useFiles = (enabled = true) =>
  useQuery({
    queryKey: ['files'],
    enabled,
    refetchOnMount: 'always',
    queryFn: async () => {
      const response = await fileApi.getAll();
      if (!response.success) {
        throw new Error(response.message || 'Không tải được thư viện file.');
      }
      return response.data ?? [];
    },
  });

export const useUploadFile = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: ({
      file,
      linkedEntityType,
      linkedEntityId,
      metadata,
    }: {
      file: File;
      linkedEntityType?: string;
      linkedEntityId?: string;
      metadata?: string;
    }) => fileApi.upload(file, { linkedEntityType, linkedEntityId, metadata }),
    onSuccess: (response) => {
      if (response.success) {
        queryClient.invalidateQueries({ queryKey: ['files'] });
      }
    },
    onError: (error: Error) => message.error(error.message || 'Upload thất bại.'),
  });
};

export const useDeleteFile = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => fileApi.delete(id),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Đã xóa file.');
        queryClient.invalidateQueries({ queryKey: ['files'] });
      } else {
        message.error(response.message || 'Xóa file thất bại.');
      }
    },
    onError: (error: Error) => message.error(error.message || 'Xóa file thất bại.'),
  });
};
