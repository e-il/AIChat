import type { MessageAttachment } from '../types';
import { getAuthCode } from './auth';

const API_BASE = '/api';

export const imagesApi = {
  /**
   * Uploads an image for vision input. Returns the saved attachment whose `url`
   * points to the server-hosted file.
   */
  async upload(file: File): Promise<MessageAttachment> {
    const authCode = getAuthCode();
    if (!authCode) throw new Error('AUTH_REQUIRED');

    const form = new FormData();
    form.append('file', file);

    const response = await fetch(`${API_BASE}/images`, {
      method: 'POST',
      headers: { 'X-Auth-Code': authCode },
      body: form,
    });

    if (response.status === 401) throw new Error('AUTH_REQUIRED');
    if (!response.ok) throw new Error(`Upload failed: ${response.statusText}`);
    return response.json();
  },

  /**
   * Builds an authenticated image URL by appending ?access_token=. <img src> can't
   * send custom headers, so the backend accepts the token via query string for
   * GET /api/images/* (mirrors the SignalR convention).
   */
  buildAuthedUrl(url: string | undefined | null): string {
    if (!url) return '';
    const authCode = getAuthCode();
    if (!authCode) return url;
    const sep = url.includes('?') ? '&' : '?';
    return `${url}${sep}access_token=${encodeURIComponent(authCode)}`;
  },
};
