export interface Message {
  id: string;
  role: 'user' | 'assistant' | 'system';
  content: string;
  timestamp: string;
  // Memories injected into the system prompt for this assistant turn.
  // Only populated on assistant messages that used memory. Persisted with the message.
  usedMemories?: Memory[];
  // Media attachments. Set on user messages for vision input,
  // and on assistant messages for generated media.
  attachments?: MessageAttachment[];
  // Tool calls the assistant invoked (e.g. generate_image). Persisted so the next
  // turn can replay the asst_tool_call → tool_result shape back to the model.
  toolCalls?: MessageToolCall[];
}

export interface MessageAttachment {
  id: string;
  type: 'image' | 'video';
  mimeType: string;
  url: string;          // server-relative, e.g. /api/images/{filename}
  prompt?: string;
  revisedPrompt?: string;
  width?: number;
  height?: number;
  durationSeconds?: number;
}

export interface MessageToolCall {
  id: string;
  name: string;
  argumentsJson: string;
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

export interface PromptProfile {
  id: string;
  name: string;
  description: string;
  systemPrompt: string;
  inputPlaceholder: string;
  isBuiltIn: boolean;
}

export interface PromptProfilesResponse {
  profiles: PromptProfile[];
  maxCustomSystemPromptLength: number;
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
  promptProfileId?: string;
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
