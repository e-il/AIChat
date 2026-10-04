import type { ModelsResponse, PromptProfilesResponse } from '../types';
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
  async getModels(): Promise<ModelsResponse> {
    const response = await fetch(`${API_BASE}/models`, {
      headers: getHeaders(),
    });
    return handleResponse(response);
  },

  async saveModel(model: { id: string; name: string; deploymentName: string; kind?: string }): Promise<void> {
    const response = await fetch(`${API_BASE}/models`, {
      method: 'PUT', headers: { ...getHeaders(), 'Content-Type': 'application/json' }, body: JSON.stringify(model),
    });
    return handleResponse(response).then(() => undefined);
  },

  async deleteModel(id: string): Promise<void> {
    const response = await fetch(`${API_BASE}/models/${encodeURIComponent(id)}`, { method: 'DELETE', headers: getHeaders() });
    if (response.status === 401) throw new Error('AUTH_REQUIRED');
    if (!response.ok) throw new Error(`Request failed: ${response.statusText}`);
  },

  async getPromptProfiles(): Promise<PromptProfilesResponse> {
    const response = await fetch(`${API_BASE}/promptprofiles`, {
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
