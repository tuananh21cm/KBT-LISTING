/** Setting keys for Auto Specifics (must match backend ListingSpecificsSettingKeys). */
export const LISTING_SPECIFICS_SETTING_KEYS = {
  allowRepeat: 'listing.specifics.allow_repeat',
} as const;

export function parseListingSpecificsAllowRepeat(
  raw: string | undefined,
): boolean {
  return (raw ?? '').trim().toLowerCase() === 'true';
}
