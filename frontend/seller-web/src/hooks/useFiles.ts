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
        throw new Error(response.message);
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
        if (response.data?.isDuplicate) {
          message.info(response.message || 'File đã tồn tại (trùng hash).');
        } else {
          message.success(response.message || 'Upload file thành công.');
        }
        queryClient.invalidateQueries({ queryKey: ['files'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};

export const useDeleteFile = () => {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (id: string) => fileApi.delete(id),
    onSuccess: (response) => {
      if (response.success) {
        message.success(response.message || 'Xóa file thành công.');
        queryClient.invalidateQueries({ queryKey: ['files'] });
      } else {
        message.error(response.message);
      }
    },
    onError: (error: Error) => message.error(error.message),
  });
};
