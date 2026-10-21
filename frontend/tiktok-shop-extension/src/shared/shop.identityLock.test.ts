import { describe, expect, it } from 'vitest';
import {
  IDENTITY_VIDEO_VERIFICATION_REASON_KEY,
  detectIdentityVideoVerificationLock,
} from './shop';

function productsList(products: unknown[]) {
  return { code: 0, data: { products } };
}

function productWithKey(key: string | undefined) {
  return {
    suspend_reason: {
      traffic_control_reasons: [
        {
          sub_reasons: key
            ? [{ reason: { key } }]
            : [],
        },
      ],
    },
  };
}

describe('detectIdentityVideoVerificationLock', () => {
  it('true khi bất kỳ product nào có key identity video', () => {
    expect(
      detectIdentityVideoVerificationLock(
        productsList([productWithKey(IDENTITY_VIDEO_VERIFICATION_REASON_KEY)]),
      ),
    ).toBe(true);
  });

  it('false khi products có suspend_reason nhưng không có key', () => {
    expect(
      detectIdentityVideoVerificationLock(
        productsList([productWithKey('other_reason')]),
      ),
    ).toBe(false);
  });

  it('false khi suspend_reason rỗng', () => {
    expect(
      detectIdentityVideoVerificationLock(
        productsList([{ suspend_reason: { traffic_control_reasons: [] } }]),
      ),
    ).toBe(false);
  });

  it('false khi shop 0 listing', () => {
    expect(detectIdentityVideoVerificationLock(productsList([]))).toBe(false);
  });

  it('false khi payload thiếu / không phải JSON products/list', () => {
    expect(detectIdentityVideoVerificationLock(null)).toBe(false);
    expect(detectIdentityVideoVerificationLock({})).toBe(false);
  });
});
