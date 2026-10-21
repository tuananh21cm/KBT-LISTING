import { useState } from 'react';
import { Button } from 'antd';
import { CaretDownOutlined, CaretUpOutlined, QuestionCircleOutlined } from '@ant-design/icons';

type GuideRow = {
  key: string;
  label: string;
  formula: string;
  tips: string[];
};

const GUIDE_ROWS: GuideRow[] = [
  {
    key: 'impressions',
    label: 'Impressions',
    formula: 'Lượt hiển thị sản phẩm',
    tips: [
      'Đăng thêm video / live có gắn sản phẩm (affiliate, shop tab).',
      'Tối ưu tiêu đề, ảnh bìa video và hashtag để tăng reach.',
      'Chạy Spark Ads / Shop Ads cho sản phẩm có dấu hiệu bán tốt.',
      'Mở rộng danh mục listing — nhiều SKU hợp lệ = nhiều điểm vào funnel.',
    ],
  },
  {
    key: 'ctr',
    label: 'CTR',
    formula: 'Clicks ÷ Impressions',
    tips: [
      'Ảnh thumbnail chính rõ nét, khác biệt, có ngữ cảnh sử dụng.',
      'Tiêu đề nhấn lợi ích / giá / điểm nổi bật trong 2–3 từ đầu.',
      'Hiển thị rating & review trên card (duy trì chất lượng phản hồi).',
      'Thử A/B ảnh chính và giá hiển thị trên các SKU tương tự.',
    ],
  },
  {
    key: 'atc',
    label: 'ATC rate',
    formula: 'Add-to-cart ÷ Clicks',
    tips: [
      'Giá cạnh tranh so với đối thủ; dùng coupon / flash sale khi hợp lý.',
      'Mô tả rõ size, chất liệu, benefit; có size chart / ảnh chi tiết.',
      'Đủ biến thể (màu, size) — tránh “hết hàng” trên variant hot.',
      'Video ngắn demo sản phẩm trên listing và trong content.',
    ],
  },
  {
    key: 'order',
    label: 'Order rate',
    formula: 'Orders ÷ Add-to-cart',
    tips: [
      'Phí ship / thời gian giao rõ ràng; free-ship threshold nếu có thể.',
      'Giá cuối (sau discount) nhất quán giữa video và shop.',
      'Tồn kho thực tế — tránh add-to-cart nhưng không checkout được.',
      'Giảm friction checkout: ít bước, không đổi giá bất ngờ khi thanh toán.',
    ],
  },
  {
    key: 'cvr',
    label: 'CVR',
    formula: 'Orders ÷ Impressions',
    tips: [
      'CVR = CTR × ATC rate × Order rate — cải thiện bước nào cũng kéo đơn.',
      'Dùng mô phỏng trên funnel: chỉnh % mũi tên để ước tính đơn / GMV.',
      'Ưu tiên bước có volume lớn (vd. CTR khi impressions cao).',
    ],
  },
];

export function ListingAnalyticsFunnelGuide() {
  const [open, setOpen] = useState(false);
  const [activeKey, setActiveKey] = useState<string | null>('impressions');

  const toggleRow = (key: string) => {
    setActiveKey((prev) => (prev === key ? null : key));
    if (!open) setOpen(true);
  };

  return (
    <div className={`listing-analytics-funnel-guide${open ? ' is-open' : ''}`}>
      <Button
        type="link"
        size="small"
        className="listing-analytics-funnel-guide-toggle"
        icon={<QuestionCircleOutlined />}
        onClick={() => setOpen((value) => !value)}
      >
        Hướng dẫn tăng chỉ số chuyển đổi
        {open ? <CaretUpOutlined /> : <CaretDownOutlined />}
      </Button>

      {open ? (
        <div className="listing-analytics-funnel-guide-panel">
          <p className="listing-analytics-funnel-guide-intro">
            Bấm tên chỉ số để xem gợi ý. Bấm % trên funnel để mô phỏng khi cải thiện tỷ lệ.
          </p>
          <div className="listing-analytics-funnel-guide-table-wrap">
            <table className="listing-analytics-funnel-guide-table">
              <thead>
                <tr>
                  <th scope="col">Chỉ số</th>
                  <th scope="col">Công thức</th>
                  <th scope="col">Hướng dẫn</th>
                </tr>
              </thead>
              <tbody>
                {GUIDE_ROWS.map((row) => {
                  const isActive = activeKey === row.key;
                  return (
                    <tr
                      key={row.key}
                      className={isActive ? 'is-active' : undefined}
                      onClick={() => toggleRow(row.key)}
                    >
                      <th scope="row">
                        <button
                          type="button"
                          className="listing-analytics-funnel-guide-row-btn"
                          onClick={(event) => {
                            event.stopPropagation();
                            toggleRow(row.key);
                          }}
                        >
                          {row.label}
                        </button>
                      </th>
                      <td className="listing-analytics-funnel-guide-formula">{row.formula}</td>
                      <td className="listing-analytics-funnel-guide-tips-cell">
                        {isActive ? (
                          <ul className="listing-analytics-funnel-guide-tips">
                            {row.tips.map((tip) => (
                              <li key={tip}>{tip}</li>
                            ))}
                          </ul>
                        ) : (
                          <span className="listing-analytics-funnel-guide-hint">
                            Bấm để xem gợi ý
                          </span>
                        )}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>
      ) : null}
    </div>
  );
}
