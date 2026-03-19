const AUTH_CODE_KEY = 'aichat_auth_code';

export function getAuthCode(): string | null {
  return localStorage.getItem(AUTH_CODE_KEY);
}

export function setAuthCode(code: string): void {
  localStorage.setItem(AUTH_CODE_KEY, code);
}

export function clearAuthCode(): void {
  localStorage.removeItem(AUTH_CODE_KEY);
}

export function hasAuthCode(): boolean {
  return !!getAuthCode();
}
