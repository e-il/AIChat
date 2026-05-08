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
