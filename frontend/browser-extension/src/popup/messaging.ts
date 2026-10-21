import type { ExtMessage } from '../shared/messages';

/** Gửi message tới background service worker và chờ response. */
export function send<T = unknown>(message: ExtMessage): Promise<T> {
  return new Promise((resolve, reject) => {
    chrome.runtime.sendMessage(message, (response) => {
      const err = chrome.runtime.lastError;
      if (err) {
        reject(new Error(err.message));
        return;
      }
      if (response == null) {
        reject(new Error('Extension không trả lời. Tải lại extension trên chrome://extensions rồi thử lại.'));
        return;
      }
      resolve(response as T);
    });
  });
}
