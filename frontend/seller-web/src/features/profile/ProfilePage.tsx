import { Button, Card, Descriptions, Form, Input, Space, Spin, Tag, Typography } from 'antd';
import { useAuthStore } from '../../stores/authStore';
import {
  useCheckFourSellerLogin,
  useCurrentUser,
  useUpdateFourSellerCookie,
} from '../../hooks/useAuth';

const { Title, Text } = Typography;

const COOKIE_HINT =
  'Dán session cookie từ trình duyệt đã đăng nhập 4Seller (cần có userToken). Cookie không được hiển thị lại sau khi lưu.';

export const ProfilePage = () => {
  const user = useAuthStore((s) => s.user);
  const { isLoading } = useCurrentUser();
  const [cookieForm] = Form.useForm<{ fourSellerCookie?: string }>();
  const updateCookie = useUpdateFourSellerCookie();
  const checkLogin = useCheckFourSellerLogin();

  if (isLoading && !user) {
    return <Spin style={{ display: 'block', margin: '40px auto' }} />;
  }

  const onSaveCookie = (values: { fourSellerCookie?: string }) => {
    updateCookie.mutate(
      { fourSellerCookie: values.fourSellerCookie?.trim() ?? '' },
      {
        onSuccess: (response) => {
          if (response.success) {
            cookieForm.resetFields();
            // Lưu xong trạng thái là `unchecked` — xác minh ngay bằng cookie đã lưu để lên `logged_in`.
            if (response.data?.hasFourSellerCookie) checkLogin.mutate({});
          }
        },
      },
    );
  };

  const onCheckLogin = () => {
    const draft = cookieForm.getFieldValue('fourSellerCookie')?.trim() as string | undefined;
    checkLogin.mutate(draft ? { fourSellerCookie: draft } : {});
  };

  return (
    <div>
      <Title level={5} style={{ marginTop: 0 }}>
        Hồ sơ cá nhân
      </Title>
      <Card size="small" style={{ maxWidth: 640, borderRadius: 6 }}>
        <Descriptions column={1} size="small" bordered>
          <Descriptions.Item label="Tên đăng nhập">{user?.username}</Descriptions.Item>
          <Descriptions.Item label="Họ và tên">{user?.fullName}</Descriptions.Item>
          <Descriptions.Item label="Email">{user?.email}</Descriptions.Item>
          <Descriptions.Item label="Vai trò">
            {user?.roles.map((role) => (
              <Tag key={role} color="blue">
                {role}
              </Tag>
            ))}
          </Descriptions.Item>
        </Descriptions>
      </Card>

      <Card
        size="small"
        title="4Seller Cookie"
        style={{ maxWidth: 640, borderRadius: 6, marginTop: 16 }}
        extra={
          user?.hasFourSellerCookie ? (
            <Tag color="success">Đã cấu hình</Tag>
          ) : (
            <Tag>Chưa cấu hình</Tag>
          )
        }
      >
        <Text type="secondary" style={{ display: 'block', marginBottom: 12 }}>
          {COOKIE_HINT}
          {user?.hasFourSellerCookie
            ? ' Tài khoản này đã có cookie — để trống rồi Lưu nếu muốn xóa; dán cookie mới rồi Lưu để thay thế.'
            : null}
        </Text>
        <Form form={cookieForm} layout="vertical" onFinish={onSaveCookie} size="small">
          <Form.Item name="fourSellerCookie" label="Cookie" style={{ marginBottom: 12 }}>
            <Input.TextArea
              rows={4}
              placeholder={
                user?.hasFourSellerCookie
                  ? 'Để trống và Lưu để xóa, hoặc dán cookie mới để thay thế'
                  : 'Dán cookie 4Seller'
              }
              autoComplete="off"
            />
          </Form.Item>
          <Form.Item style={{ marginBottom: 0 }}>
            <Space wrap>
              <Button type="primary" htmlType="submit" loading={updateCookie.isPending}>
                Lưu cookie
              </Button>
              <Button
                onClick={onCheckLogin}
                loading={checkLogin.isPending}
                disabled={updateCookie.isPending}
              >
                Check login
              </Button>
            </Space>
          </Form.Item>
        </Form>
      </Card>
    </div>
  );
};
