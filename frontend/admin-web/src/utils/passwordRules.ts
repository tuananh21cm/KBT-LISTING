import type { Rule } from 'antd/es/form';

export const PASSWORD_HINT =
  'Tối thiểu 8 ký tự, gồm chữ hoa, chữ thường và số.';

export const passwordFormRules = (label = 'Mật khẩu'): Rule[] => [
  { required: true, message: `Nhập ${label.toLowerCase()}` },
  { min: 8, message: `${label} phải có ít nhất 8 ký tự` },
  { pattern: /[A-Z]/, message: `${label} phải có ít nhất một chữ hoa` },
  { pattern: /[a-z]/, message: `${label} phải có ít nhất một chữ thường` },
  { pattern: /[0-9]/, message: `${label} phải có ít nhất một chữ số` },
];
