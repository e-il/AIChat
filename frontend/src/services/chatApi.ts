import type { Conversation, ConversationSummary, ModelsResponse } from '../types';
import { getAuthCode } from './auth';

const API_BASE = '/api';

function getHeaders(): HeadersInit {
  const headers: HeadersInit = {};
  const authCode = getAuthCode();
  if (authCode) {
    headers['X-Auth-Code'] = authCode;
  }
  return headers;
}

async function handleResponse<T>(response: Response): Promise<T> {
  if (response.status === 401) {
    throw new Error('AUTH_REQUIRED');
  }
  if (!response.ok) {
    throw new Error(`Request failed: ${response.statusText}`);
  }
  return response.json();
}

export const chatApi = {
  async getConversations(): Promise<ConversationSummary[]> {
    const response = await fetch(`${API_BASE}/conversations`, {
      headers: getHeaders(),
    });
    return handleResponse(response);
  },

  async getConversation(id: string): Promise<Conversation> {
    const response = await fetch(`${API_BASE}/conversations/${id}`, {
      headers: getHeaders(),
    });
    return handleResponse(response);
  },

  async createConversation(): Promise<Conversation> {
    const response = await fetch(`${API_BASE}/conversations`, {
      method: 'POST',
      headers: getHeaders(),
    });
    return handleResponse(response);
  },

  async deleteConversation(id: string): Promise<void> {
    const response = await fetch(`${API_BASE}/conversations/${id}`, {
      method: 'DELETE',
      headers: getHeaders(),
    });
    if (response.status === 401) throw new Error('AUTH_REQUIRED');
    if (!response.ok) throw new Error('Failed to delete conversation');
  },

  async updateTitle(id: string, title: string): Promise<void> {
    const response = await fetch(`${API_BASE}/conversations/${id}/title`, {
      method: 'PATCH',
      headers: { ...getHeaders(), 'Content-Type': 'application/json' },
      body: JSON.stringify({ title }),
    });
    if (response.status === 401) throw new Error('AUTH_REQUIRED');
    if (!response.ok) throw new Error('Failed to update title');
  },

  async getModels(): Promise<ModelsResponse> {
    const response = await fetch(`${API_BASE}/models`, {
      headers: getHeaders(),
    });
    return handleResponse(response);
  },

  async validateAuthCode(code: string): Promise<boolean> {
    const response = await fetch(`${API_BASE}/models`, {
      headers: { 'X-Auth-Code': code },
    });
    return response.ok;
  },
};
