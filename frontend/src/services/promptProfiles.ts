import type { PromptProfile } from '../types';

export const DEFAULT_PROMPT_PROFILE_ID = 'general';
export const REWRITE_PROMPT_PROFILE_ID = 'rewrite';
export const TRANSLATE_PROMPT_PROFILE_ID = 'translate-zh-en';
export const FALLBACK_MAX_CUSTOM_SYSTEM_PROMPT_LENGTH = 8000;

const CUSTOM_PROMPT_PROFILES_KEY = 'aichat_custom_prompt_profiles';

export const FALLBACK_BUILT_IN_PROMPT_PROFILES: PromptProfile[] = [
  {
    id: DEFAULT_PROMPT_PROFILE_ID,
    name: 'General',
    description: 'General-purpose assistant for coding, writing, analysis, and everyday questions.',
    systemPrompt: 'You are a helpful AI assistant. Be concise and helpful in your responses.',
    inputPlaceholder: 'Message AIChat...',
    isBuiltIn: true,
  },
  {
    id: REWRITE_PROMPT_PROFILE_ID,
    name: 'Rewrite',
    description: 'Polish wording while preserving the original meaning.',
    systemPrompt: [
      "Rewrite the user's text to be clear, natural, and concise.",
      'Preserve the original meaning and tone unless the user asks for a different tone.',
      'Do not translate unless the user explicitly asks.',
      'Return only the rewritten text unless the user asks for explanation or alternatives.',
    ].join('\n'),
    inputPlaceholder: 'Paste text to rewrite...',
    isBuiltIn: true,
  },
  {
    id: TRANSLATE_PROMPT_PROFILE_ID,
    name: 'Chinese-English',
    description: 'Translate naturally between Chinese and English.',
    systemPrompt: [
      'Translate between Chinese and English.',
      'Auto-detect the source language: if the input is Chinese, translate to English; if the input is English, translate to Simplified Chinese.',
      'Preserve meaning, formatting, names, numbers, and domain-specific terms.',
      'Return only the translation unless the user asks for explanation, alternatives, or a specific style.',
    ].join('\n'),
    inputPlaceholder: 'Paste Chinese or English text...',
    isBuiltIn: true,
  },
];

function isPromptProfile(value: unknown): value is PromptProfile {
  if (!value || typeof value !== 'object') return false;
  const candidate = value as Partial<PromptProfile>;
  return typeof candidate.id === 'string'
    && typeof candidate.name === 'string'
    && typeof candidate.systemPrompt === 'string';
}

export function loadCustomPromptProfiles(): PromptProfile[] {
  const stored = localStorage.getItem(CUSTOM_PROMPT_PROFILES_KEY);
  if (!stored) return [];

  try {
    const parsed = JSON.parse(stored);
    if (!Array.isArray(parsed)) return [];
    return parsed
      .filter(isPromptProfile)
      .map(profile => ({
        id: profile.id,
        name: profile.name,
        description: profile.description ?? '',
        systemPrompt: profile.systemPrompt,
        inputPlaceholder: profile.inputPlaceholder || 'Message with this prompt...',
        isBuiltIn: false,
      }));
  } catch {
    return [];
  }
}

export function saveCustomPromptProfiles(profiles: PromptProfile[]): void {
  localStorage.setItem(
    CUSTOM_PROMPT_PROFILES_KEY,
    JSON.stringify(profiles.filter(profile => !profile.isBuiltIn))
  );
}

export function getPromptProfileById(profiles: PromptProfile[], id?: string): PromptProfile {
  return profiles.find(profile => profile.id === id)
    ?? profiles.find(profile => profile.id === DEFAULT_PROMPT_PROFILE_ID)
    ?? FALLBACK_BUILT_IN_PROMPT_PROFILES[0];
}

export function mergePromptProfiles(
  builtInProfiles: PromptProfile[],
  customProfiles: PromptProfile[]
): PromptProfile[] {
  const builtInIds = new Set(builtInProfiles.map(profile => profile.id));
  return [
    ...builtInProfiles,
    ...customProfiles.filter(profile => !builtInIds.has(profile.id)),
  ];
}
