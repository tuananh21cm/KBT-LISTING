import { describe, expect, it } from 'vitest';
import type { ShopDailyTaskPlan } from './types';
import {
  formatDailyTaskSyncBlockMessage,
  formatDailyTaskSyncUnlockHint,
  getDailyTaskProgress,
} from './dailyTaskProgress';

function plan(doneFlags: boolean[]): ShopDailyTaskPlan {
  return {
    date: '2026-08-24',
    sellerId: 'seller-1',
    fetchedAt: Date.now(),
    items: doneFlags.map((done, i) => ({
      id: `task-${i}`,
      name: `Task ${i}`,
      url: `https://seller-us.tiktok.com/p/${i}`,
      urlMatchPattern: `https://seller-us.tiktok.com/p/${i}`,
      viewDurationSeconds: 0,
      done,
    })),
  };
}

describe('getDailyTaskProgress', () => {
  it('null hoặc rỗng → sync mở', () => {
    expect(getDailyTaskProgress(null)).toEqual({
      total: 0,
      doneCount: 0,
      requiredCount: 0,
      syncUnlocked: true,
    });
    expect(getDailyTaskProgress(plan([]))).toEqual({
      total: 0,
      doneCount: 0,
      requiredCount: 0,
      syncUnlocked: true,
    });
  });

  it('1 nhiệm vụ: cần 1 xong', () => {
    expect(getDailyTaskProgress(plan([false]))).toMatchObject({
      total: 1,
      doneCount: 0,
      requiredCount: 1,
      syncUnlocked: false,
    });
    expect(getDailyTaskProgress(plan([true]))).toMatchObject({
      total: 1,
      doneCount: 1,
      requiredCount: 1,
      syncUnlocked: true,
    });
  });

  it('2 nhiệm vụ: cần 1 xong (50%)', () => {
    expect(getDailyTaskProgress(plan([false, false]))).toMatchObject({
      requiredCount: 1,
      syncUnlocked: false,
    });
    expect(getDailyTaskProgress(plan([true, false]))).toMatchObject({
      doneCount: 1,
      syncUnlocked: true,
    });
  });

  it('3 nhiệm vụ: cần 2 xong (ceil 50%)', () => {
    expect(getDailyTaskProgress(plan([true, false, false]))).toMatchObject({
      total: 3,
      doneCount: 1,
      requiredCount: 2,
      syncUnlocked: false,
    });
    expect(getDailyTaskProgress(plan([true, true, false]))).toMatchObject({
      doneCount: 2,
      syncUnlocked: true,
    });
  });

  it('4 nhiệm vụ: cần 2 xong (50%)', () => {
    expect(getDailyTaskProgress(plan([true, false, false, false]))).toMatchObject({
      requiredCount: 2,
      syncUnlocked: false,
    });
    expect(getDailyTaskProgress(plan([true, true, false, false]))).toMatchObject({
      syncUnlocked: true,
    });
  });
});

describe('formatDailyTaskSyncBlockMessage', () => {
  it('trả rỗng khi không có nhiệm vụ', () => {
    expect(formatDailyTaskSyncBlockMessage(null)).toBe('');
  });

  it('mô tả ngưỡng 50%', () => {
    expect(formatDailyTaskSyncBlockMessage(plan([false, false, false, false]))).toBe(
      'Hoàn thành ít nhất 2/4 nhiệm vụ (50%) trước khi Sync.',
    );
  });
});

describe('formatDailyTaskSyncUnlockHint', () => {
  it('trả rỗng khi đã mở khóa hoặc không có nhiệm vụ', () => {
    expect(formatDailyTaskSyncUnlockHint(null)).toBe('');
    expect(formatDailyTaskSyncUnlockHint(plan([true, true]))).toBe('');
  });

  it('gợi ý mở khóa Sync', () => {
    expect(formatDailyTaskSyncUnlockHint(plan([false, false, false]))).toBe(
      'Hoàn thành ít nhất 2/3 nhiệm vụ để mở khóa Sync.',
    );
  });
});
