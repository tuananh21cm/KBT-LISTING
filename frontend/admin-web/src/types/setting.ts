export type SettingType = 'text' | 'number' | 'boolean' | 'password';

export interface Setting {
  id: string;
  group: string;
  key: string;
  label: string;
  value: string;
  type: SettingType;
  description?: string;
  createdAt: string;
  updatedAt?: string;
}

export interface CreateSettingRequest {
  group: string;
  key: string;
  label: string;
  value: string;
  type: SettingType;
  description?: string;
}

export interface UpdateSettingRequest {
  group: string;
  key: string;
  label: string;
  value: string;
  type: SettingType;
  description?: string;
}

export const SETTING_TYPE_OPTIONS: { value: SettingType; label: string }[] = [
  { value: 'text', label: 'Text' },
  { value: 'number', label: 'Number' },
  { value: 'boolean', label: 'Boolean' },
  { value: 'password', label: 'Password' },
];
