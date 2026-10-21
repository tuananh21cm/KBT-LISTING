/** Setting keys for the 4Seller egress proxy pool (must match backend FourSellerProxySettingKeys). */
export const FOURSELLER_PROXY_SETTING_KEYS = {
  enabled: 'fourseller.proxy.enabled',
  pool: 'fourseller.proxy.pool',
  cooldownMinutes: 'fourseller.proxy.cooldown_minutes',
  /** Minutes a successful is-login is reused before probing again. */
  validateTtlMinutes: 'fourseller.session.validate_ttl_minutes',
} as const;
