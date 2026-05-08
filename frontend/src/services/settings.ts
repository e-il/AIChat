import type { ConversationSettings } from '../types';
import { DEFAULT_PROMPT_PROFILE_ID } from './promptProfiles';

const SETTINGS_KEY_PREFIX = 'aichat_settings_';
const GLOBAL_SETTINGS_KEY = 'aichat_global_settings';

export interface GlobalSettings {
  defaultContextSize: number;
  defaultMaxMessages: number;
}

// Get settings for a specific conversation
export function getConversationSettings(conversationId: string, defaultContextSize: number, defaultMaxMessages: number): ConversationSettings {
  const key = `${SETTINGS_KEY_PREFIX}${conversationId}`;
  const stored = localStorage.getItem(key);
  if (stored) {
    try {
      const parsed = JSON.parse(stored) as ConversationSettings;
      return {
        maxContextSize: parsed.maxContextSize ?? defaultContextSize,
        maxMessages: parsed.maxMessages ?? defaultMaxMessages,
        memoryMode: parsed.memoryMode ?? 'auto',
        promptProfileId: parsed.promptProfileId ?? DEFAULT_PROMPT_PROFILE_ID,
      };
    } catch {
      // Invalid JSON, fall through to defaults
    }
  }
  return {
    maxContextSize: defaultContextSize,
    maxMessages: defaultMaxMessages,
    memoryMode: 'auto',
    promptProfileId: DEFAULT_PROMPT_PROFILE_ID,
  };
}

// Save settings for a specific conversation
export function saveConversationSettings(conversationId: string, settings: ConversationSettings): void {
  const key = `${SETTINGS_KEY_PREFIX}${conversationId}`;
  localStorage.setItem(key, JSON.stringify(settings));
}

// Delete settings when conversation is deleted
export function deleteConversationSettings(conversationId: string): void {
  const key = `${SETTINGS_KEY_PREFIX}${conversationId}`;
  localStorage.removeItem(key);
}

// Get global settings (default context size to use for new conversations)
export function getGlobalSettings(defaultContextSize: number, defaultMaxMessages: number): GlobalSettings {
  const stored = localStorage.getItem(GLOBAL_SETTINGS_KEY);
  if (stored) {
    try {
      return JSON.parse(stored);
    } catch {
      // Invalid JSON, return defaults
    }
  }
  return { defaultContextSize, defaultMaxMessages };
}

// Save global settings
export function saveGlobalSettings(settings: GlobalSettings): void {
  localStorage.setItem(GLOBAL_SETTINGS_KEY, JSON.stringify(settings));
}
