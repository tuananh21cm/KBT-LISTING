import { Secret, TOTP } from 'otpauth';

/**
 * Generates a 6-digit Google Authenticator TOTP code from a base32 secret.
 */
export function generateGoogleAuthCode(secretValue: string): string {
  const normalized = secretValue.replace(/\s/g, '').toUpperCase();
  const secret = Secret.fromBase32(normalized);
  const totp = new TOTP({
    secret,
    digits: 6,
    period: 30,
    algorithm: 'SHA1',
  });
  return totp.generate();
}
