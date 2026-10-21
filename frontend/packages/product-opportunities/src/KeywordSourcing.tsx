import { Tooltip, message } from 'antd';
import { ExportOutlined } from '@ant-design/icons';
import { isSameSearchQuery } from './audienceSearchQuery';

export const copySourcingQuery = async (searchQuery?: string | null, keyword?: string | null) => {
  const text = (searchQuery?.trim() || keyword?.trim() || '');
  if (!text) return;
  try {
    await navigator.clipboard.writeText(text);
    message.success(
      isSameSearchQuery(keyword, text) ? 'Đã copy keyword' : 'Đã copy câu tìm Shein',
    );
  } catch {
    message.error('Không copy được');
  }
};

export const SheinSearchLink = ({ url }: { url?: string | null }) => {
  const href = url?.trim() ?? '';
  if (!href) return null;
  return (
    <Tooltip title="Mở Shein">
      <a
        className="product-opportunities-shein-link"
        href={href}
        target="_blank"
        rel="noopener noreferrer"
        aria-label="Mở Shein"
        onClick={(e) => e.stopPropagation()}
      >
        <ExportOutlined />
      </a>
    </Tooltip>
  );
};
