import { Modal as AntModal } from 'antd';
import type { ModalProps } from 'antd';

// Mặc định không cho đóng modal khi click ra ngoài (mask) hoặc nhấn ESC.
// Chỉ đóng qua nút Close (X) hoặc Hủy/Cancel. Có thể ghi đè bằng cách
// truyền maskClosable/mask.closable/keyboard tường minh (vd: modal xem ảnh/video).
//
// antd 6 deprecate `maskClosable` (→ `mask.closable`) và `destroyOnClose`
// (→ `destroyOnHidden`). AppModal nhận các prop cũ và tự map sang API mới nên
// mọi màn hình dùng chung wrapper này không cần sửa lẻ tẻ.
function AppModal({
  destroyOnClose,
  destroyOnHidden,
  maskClosable,
  mask,
  keyboard,
  ...props
}: ModalProps) {
  const closable = maskClosable ?? false;
  let resolvedMask: ModalProps['mask'];
  if (mask === false) {
    resolvedMask = false;
  } else if (mask && typeof mask === 'object') {
    resolvedMask = { closable, ...mask };
  } else {
    resolvedMask = { closable };
  }

  return (
    <AntModal
      {...props}
      mask={resolvedMask}
      keyboard={keyboard ?? false}
      destroyOnHidden={destroyOnHidden ?? destroyOnClose}
    />
  );
}

export const Modal = Object.assign(AppModal, {
  useModal: AntModal.useModal,
  confirm: AntModal.confirm,
  info: AntModal.info,
  success: AntModal.success,
  error: AntModal.error,
  warning: AntModal.warning,
  destroyAll: AntModal.destroyAll,
  config: AntModal.config,
});
