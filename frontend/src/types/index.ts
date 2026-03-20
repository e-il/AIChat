export interface Message {
  id: string;
  role: 'user' | 'assistant' | 'system';
  content: string;
  timestamp: string;
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

// Client-side conversation settings (stored in localStorage)
export interface ConversationSettings {
  maxContextSize: number;
  maxMessages: number;
}
