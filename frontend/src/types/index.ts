export interface Message {
  id: string;
  role: 'user' | 'assistant' | 'system';
  content: string;
  timestamp: string;
  // Memories injected into the system prompt for this assistant turn.
  // Only populated on assistant messages that used memory. Persisted with the message.
  usedMemories?: Memory[];
}

export interface Conversation {
  id: string;
  title: string;
  messages: Message[];
  createdAt: string;
  updatedAt: string;
}

export interface ConversationSummary {
  id: string;
  title: string;
  createdAt: string;
  updatedAt: string;
  messageCount: number;
}

export interface ModelInfo {
  id: string;
  name: string;
  deploymentName: string;
}

export interface ModelsResponse {
  models: ModelInfo[];
  defaultModel: string;
  defaultContextSize: number;
  contextSizeOptions: number[];
  defaultMaxMessages: number;
  maxMessagesOptions: number[];
}

// Memory modes that the server accepts on SendMessage.
// 'auto' = server retrieves relevant memory (default)
// 'off'  = don't inject any memory for this turn
// 'explicit' = use only the IDs in explicitMemoryIds (not yet exposed in UI)
export type MemoryMode = 'auto' | 'off' | 'explicit';

// Client-side conversation settings (stored in localStorage)
export interface ConversationSettings {
  maxContextSize: number;
  maxMessages: number;
  memoryMode?: MemoryMode;
}

export type MemoryType = 'fact' | 'preference' | 'summary';

export interface Memory {
  id: string;
  userId: string;
  type: MemoryType;
  content: string;
  sourceConversationId?: string | null;
  createdAt: string;
  lastUsedAt: string;
  useCount: number;
}
