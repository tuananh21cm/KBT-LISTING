import { useState } from 'react';
import {
  Button,
  Popconfirm,
  Space,
  Table,
  Tag,
  Typography,
  Upload,
} from 'antd';
import { InboxOutlined, DeleteOutlined, ReloadOutlined } from '@ant-design/icons';
import type { ColumnsType } from 'antd/es/table';
import type { UploadProps } from 'antd';
import { useDeleteFile, useFiles, useUploadFile } from '../../hooks/useFiles';
import type { UserFile } from '../../types/file';

const { Title, Text } = Typography;
const { Dragger } = Upload;

const formatFileSize = (bytes: number) => {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
};

export const FilesPage = () => {
  const { data: files = [], isLoading, isFetching, refetch } = useFiles();
  const uploadFile = useUploadFile();
  const deleteFile = useDeleteFile();
  const [uploading, setUploading] = useState(false);

  const uploadProps: UploadProps = {
    name: 'file',
    multiple: true,
    showUploadList: false,
    beforeUpload: async (file) => {
      setUploading(true);
      try {
        await uploadFile.mutateAsync({ file });
      } finally {
        setUploading(false);
      }
      return false;
    },
  };

  const columns: ColumnsType<UserFile> = [
    { title: 'Tên file', dataIndex: 'originalFileName', key: 'originalFileName', ellipsis: true },
    { title: 'Loại', dataIndex: 'contentType', key: 'contentType', width: 140 },
    {
      title: 'Kích thước',
      dataIndex: 'fileSizeBytes',
      key: 'fileSizeBytes',
      width: 100,
      render: (size: number) => formatFileSize(size),
    },
    {
      title: 'Hash',
      dataIndex: 'fileHash',
      key: 'fileHash',
      width: 120,
      render: (hash: string) => <Text code copyable={{ text: hash }}>{hash.slice(0, 8)}…</Text>,
    },
    {
      title: 'Thao tác',
      key: 'actions',
      width: 100,
      render: (_, record) => (
        <Space>
          {record.publicUrl && (
            <Button type="link" size="small" href={record.publicUrl} target="_blank" rel="noreferrer">
              Mở
            </Button>
          )}
          <Popconfirm title="Xóa file này?" onConfirm={() => deleteFile.mutate(record.id)}>
            <Button type="text" danger icon={<DeleteOutlined />} />
          </Popconfirm>
        </Space>
      ),
    },
  ];

  return (
    <div>
      <div style={{ display: 'flex', justifyContent: 'space-between', marginBottom: 16, alignItems: 'center' }}>
        <Title level={5} style={{ margin: 0 }}>Quản lý File</Title>
        <Button
          icon={<ReloadOutlined />}
          onClick={() => void refetch()}
          loading={isFetching}
          size="small"
        >
          Tải lại
        </Button>
      </div>

      <Dragger {...uploadProps} disabled={uploading} style={{ marginBottom: 24 }}>
        <p className="ant-upload-drag-icon">
          <InboxOutlined />
        </p>
        <p className="ant-upload-text">Kéo thả hoặc click để upload file</p>
        <p className="ant-upload-hint">
          Hỗ trợ image, video, PDF. Tối đa 20MB/file. File trùng hash sẽ được phát hiện tự động.
        </p>
        {uploading && <Tag color="processing">Đang upload...</Tag>}
      </Dragger>

      <Table
        rowKey="id"
        columns={columns}
        dataSource={files}
        loading={isLoading || isFetching}
        size="small"
        pagination={{ pageSize: 10 }}
      />
    </div>
  );
};
