/** Setting keys for order handling fees (must match backend OrderHandlingSettingKeys). */
export const ORDER_HANDLING_SETTING_KEYS = {
  buyTrack: 'order.handling.buy_track',
  thankCard: 'order.handling.thank_card',
} as const;

export const ORDER_HANDLING_DEFAULTS = {
  /** USD — BuyTrack scan track fee. */
  buyTrack: 0.5,
  /** USD — ThankCard fee. */
  thankCard: 1,
} as const;
