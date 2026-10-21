import type { ExtMessage } from '../shared/messages';

/** Gửi message tới background service worker và chờ response. */
export function send<T = unknown>(message: ExtMessage): Promise<T> {
  return chrome.runtime.sendMessage(message) as Promise<T>;
}
