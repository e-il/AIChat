import type { Memory, MemoryType } from '../types';
import { getAuthCode } from './auth';

const API_BASE = '/api/memory';

function getHeaders(withBody = false): HeadersInit {
  const headers: Record<string, string> = {};
  const authCode = getAuthCode();
  if (authCode) headers['X-Auth-Code'] = authCode;
  if (withBody) headers['Content-Type'] = 'application/json';
  return headers;
}

async function handle<T>(response: Response): Promise<T> {
  if (response.status === 401) throw new Error('AUTH_REQUIRED');
  if (!response.ok) throw new Error(`Request failed: ${response.statusText}`);
  return response.json();
}

export const memoryApi = {
  async list(): Promise<Memory[]> {
    const response = await fetch(API_BASE, { headers: getHeaders() });
    return handle(response);
  },

  async create(type: MemoryType, content: string, sourceConversationId?: string): Promise<Memory> {
    const response = await fetch(API_BASE, {
      method: 'POST',
      headers: getHeaders(true),
      body: JSON.stringify({ type, content, sourceConversationId }),
    });
    return handle(response);
  },

  async update(id: string, patch: { type?: MemoryType; content?: string }): Promise<Memory> {
    const response = await fetch(`${API_BASE}/${id}`, {
      method: 'PATCH',
      headers: getHeaders(true),
      body: JSON.stringify(patch),
    });
    return handle(response);
  },

  async remove(id: string): Promise<void> {
    const response = await fetch(`${API_BASE}/${id}`, {
      method: 'DELETE',
      headers: getHeaders(),
    });
    if (response.status === 401) throw new Error('AUTH_REQUIRED');
    if (!response.ok && response.status !== 204) throw new Error('Failed to delete memory');
  },

  async retrieve(query: string, limit?: number): Promise<Memory[]> {
    const response = await fetch(`${API_BASE}/retrieve`, {
      method: 'POST',
      headers: getHeaders(true),
      body: JSON.stringify({ query, limit }),
    });
    return handle(response);
  },
};
