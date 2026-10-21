import { describe, expect, it } from 'vitest';
import {
  canonicalSizeToken,
  pickSizeOptionIndex,
  sizeOptionKeys,
  sizesMatch,
  sizeWantKeys,
} from './sheinVariantSelect';

/** Options từ DOM mẫu SHEIN: 4 (S) … 14 (XXL). */
const SAMPLE_OPTIONS = [
  { attrValueName: 'S', ariaLabel: '4 (S)', text: '4 (S)' },
  { attrValueName: 'M', ariaLabel: '6 (M)', text: '6 (M)' },
  { attrValueName: 'L', ariaLabel: '8/10 (L)', text: '8/10 (L)' },
  { attrValueName: 'XL', ariaLabel: '12 (XL)', text: '12 (XL)' },
  { attrValueName: 'XXL', ariaLabel: '14 (XXL)', text: '14 (XXL)' },
];

describe('canonicalSizeToken', () => {
  it('lấy token trong ngoặc', () => {
    expect(canonicalSizeToken('12 (XL)')).toBe('xl');
    expect(canonicalSizeToken('8/10 (L)')).toBe('l');
    expect(canonicalSizeToken('4 (S)')).toBe('s');
  });

  it('XL ≠ XXL', () => {
    expect(canonicalSizeToken('XL')).toBe('xl');
    expect(canonicalSizeToken('XXL')).toBe('xxl');
    expect(sizesMatch('XL', 'XXL')).toBe(false);
    expect(sizesMatch('12 (XL)', 'XXL')).toBe(false);
    expect(sizesMatch('12 (XL)', 'XL')).toBe(true);
  });

  it('S ≠ XS / XXS', () => {
    expect(canonicalSizeToken('S')).toBe('s');
    expect(canonicalSizeToken('XS')).toBe('xs');
    expect(sizesMatch('S', 'XS')).toBe(false);
  });
});

describe('sizeOptionKeys', () => {
  it('12 (XL) → xl + 12', () => {
    const keys = sizeOptionKeys({
      attrValueName: 'XL',
      ariaLabel: '12 (XL)',
      text: '12 (XL)',
    });
    expect(keys).toContain('xl');
    expect(keys).toContain('12');
  });

  it('8/10 (L) → l + 8 + 10', () => {
    const keys = sizeOptionKeys({
      attrValueName: 'L',
      ariaLabel: '8/10 (L)',
      text: '8/10 (L)',
    });
    expect(keys).toContain('l');
    expect(keys).toContain('8');
    expect(keys).toContain('10');
  });
});

describe('sizeWantKeys', () => {
  it('parse Size:XL / Black, XXL', () => {
    expect(sizeWantKeys('Size:XL')).toContain('xl');
    expect(sizeWantKeys('Black, XXL')).toContain('xxl');
    expect(sizeWantKeys('Color:Black / Size:M')).toContain('m');
  });
});

describe('pickSizeOptionIndex', () => {
  it('want XL → index 12 (XL)', () => {
    expect(pickSizeOptionIndex(SAMPLE_OPTIONS, 'XL')).toBe(3);
    expect(pickSizeOptionIndex(SAMPLE_OPTIONS, 'Size:XL')).toBe(3);
  });

  it('want Black, XXL → 14 (XXL)', () => {
    expect(pickSizeOptionIndex(SAMPLE_OPTIONS, 'Black, XXL')).toBe(4);
  });

  it('want số 12 → XL; 10 → L', () => {
    expect(pickSizeOptionIndex(SAMPLE_OPTIONS, '12')).toBe(3);
    expect(pickSizeOptionIndex(SAMPLE_OPTIONS, '10')).toBe(2);
    expect(pickSizeOptionIndex(SAMPLE_OPTIONS, '8')).toBe(2);
  });

  it('want S không khớp XS (không có trong mẫu)', () => {
    expect(pickSizeOptionIndex(SAMPLE_OPTIONS, 'S')).toBe(0);
    const withXs = [
      { attrValueName: 'XS', ariaLabel: '2 (XS)', text: '2 (XS)' },
      ...SAMPLE_OPTIONS,
    ];
    expect(pickSizeOptionIndex(withXs, 'S')).toBe(1);
    expect(pickSizeOptionIndex(withXs, 'XS')).toBe(0);
  });

  it('bỏ disabled / soldOut', () => {
    const opts = SAMPLE_OPTIONS.map((o, i) =>
      i === 3 ? { ...o, soldOut: true } : o,
    );
    expect(pickSizeOptionIndex(opts, 'XL')).toBe(-1);
    expect(pickSizeOptionIndex(opts, 'L')).toBe(2);
  });

  it('không khớp → -1', () => {
    expect(pickSizeOptionIndex(SAMPLE_OPTIONS, '3XL')).toBe(-1);
    expect(pickSizeOptionIndex(SAMPLE_OPTIONS, '')).toBe(-1);
  });
});
