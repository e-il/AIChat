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
   * Returns the image URL unchanged. Image GETs are public (filenames are unguessable
   * GUIDs), so no token is appended. Kept as a single indirection point in case image
   * URL handling needs to change again.
   */
  buildAuthedUrl(url: string | undefined | null): string {
    return url ?? '';
  },
};
