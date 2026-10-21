import { describe, expect, it } from 'vitest';
import {
  buildDwellEntry,
  dwellRemainingSeconds,
  formatDwellCountdown,
  parseTaskDwellAlarmName,
  taskDwellAlarmName,
} from './taskDwell';

describe('taskDwell', () => {
  it('formats countdown as seconds', () => {
    expect(formatDwellCountdown(0)).toBe('0s');
    expect(formatDwellCountdown(9)).toBe('9s');
    expect(formatDwellCountdown(16)).toBe('16s');
    expect(formatDwellCountdown(65)).toBe('65s');
  });

  it('computes remaining seconds from endsAt', () => {
    const now = 1_000_000;
    expect(dwellRemainingSeconds(now + 2500, now)).toBe(3);
    expect(dwellRemainingSeconds(now - 1, now)).toBe(0);
  });

  it('builds dwell entry with duration', () => {
    const entry = buildDwellEntry('task-1', 'https://seller-us.tiktok.com/home', 30, 1000);
    expect(entry).toEqual({
      taskId: 'task-1',
      url: 'https://seller-us.tiktok.com/home',
      startedAt: 1000,
      endsAt: 31_000,
    });
  });

  it('round-trips alarm names', () => {
    const name = taskDwellAlarmName('abc-123');
    expect(parseTaskDwellAlarmName(name)).toBe('abc-123');
    expect(parseTaskDwellAlarmName('other')).toBeNull();
  });
});
