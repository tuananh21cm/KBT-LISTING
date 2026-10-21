export const getMutationErrorMessage = (error: unknown): string => {
  const axiosError = error as {
    response?: { status?: number; data?: { message?: string; errors?: string[] } };
  };

  if (axiosError.response?.status === 403) {
    return 'Không có quyền thực hiện. Hãy tải lại trang hoặc đăng nhập lại.';
  }

  const data = axiosError.response?.data;
  if (data?.errors?.length) {
    return data.errors.join(', ');
  }

  return data?.message ?? (error instanceof Error ? error.message : 'Có lỗi xảy ra.');
};
