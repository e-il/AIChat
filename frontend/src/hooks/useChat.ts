import { useState, useRef, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import type { Memory, Message, MessageAttachment, MessageToolCall } from '../types';
import { getAuthCode } from '../services/auth';
import { DEFAULT_PROMPT_PROFILE_ID } from '../services/promptProfiles';

export interface StreamCompletePayload {
  conversationId: string;
  content: string;
  usedMemories: Memory[];
  attachments: MessageAttachment[];
  toolCalls: MessageToolCall[];
}

export function useChat() {
  const [isStreaming, setIsStreaming] = useState(false);
  const [streamingContent, setStreamingContent] = useState('');
  const [streamingAttachments, setStreamingAttachments] = useState<MessageAttachment[]>([]);
  const [toolStatus, setToolStatus] = useState<string | null>(null);
  const onStreamCompleteRef = useRef<((payload: StreamCompletePayload) => void) | null>(null);
  const onAuthErrorRef = useRef<(() => void) | null>(null);

  const sendMessage = useCallback(async (
    conversationId: string,
    messages: Message[],
    modelId: string,
    maxContextSize: number,
    maxMessages: number,
    memoryMode: 'auto' | 'off' | 'explicit' = 'auto',
    explicitMemoryIds: string[] | null = null,
    promptProfileId: string = DEFAULT_PROMPT_PROFILE_ID,
    customSystemPrompt: string | null = null,
  ) => {
    const authCode = getAuthCode();
    if (!authCode) {
      onAuthErrorRef.current?.();
      return;
    }

    setIsStreaming(true);
    setStreamingContent('');
    setStreamingAttachments([]);
    setToolStatus(null);
    const accumulated: string[] = [];
    let usedMemories: Memory[] = [];
    const attachments: MessageAttachment[] = [];
    let toolCalls: MessageToolCall[] = [];

    const connection = new signalR.HubConnectionBuilder()
      .withUrl('/chathub', { accessTokenFactory: () => authCode })
      .build();

    connection.on('MemoryUsed', (_convId: string, memories: Memory[]) => {
      usedMemories = memories;
    });

    connection.on('ReceiveMessageChunk', (_convId: string, chunk: string) => {
      accumulated.push(chunk);
      setStreamingContent(prev => prev + chunk);
    });

    // The model invoked a tool — show "Generating image…" while we wait for the result.
    connection.on('ToolCallStart', (_convId: string, toolName: string) => {
      setToolStatus(toolName === 'edit_image' ? 'generate_image' : toolName);
    });

    // Tool produced an attachment (e.g. generated image). Surface it live so the
    // user sees the picture appear before the wrap-up text streams in.
    connection.on('AttachmentReady', (_convId: string, attachment: MessageAttachment) => {
      attachments.push(attachment);
      setStreamingAttachments(prev => [...prev, attachment]);
      setToolStatus(null);
    });

    connection.on('StreamComplete', (convId: string, completedToolCalls: MessageToolCall[] = []) => {
      setIsStreaming(false);
      setStreamingContent('');
      setStreamingAttachments([]);
      setToolStatus(null);
      toolCalls = completedToolCalls;
      onStreamCompleteRef.current?.({
        conversationId: convId,
        content: accumulated.join(''),
        usedMemories,
        attachments,
        toolCalls,
      });
      connection.stop().catch(err => console.error('Error stopping connection:', err));
    });

    connection.on('Error', (_convId: string, error: string) => {
      console.error('Chat error:', error);
      setIsStreaming(false);
      setStreamingContent('');
      setStreamingAttachments([]);
      setToolStatus(null);
      if (error.includes('authentication')) {
        onAuthErrorRef.current?.();
      }
      connection.stop().catch(err => console.error('Error stopping connection:', err));
    });

    try {
      await connection.start();
      connection
        .send('SendMessage', {
          conversationId,
          messages,
          modelId,
          maxContextSize,
          maxMessages,
          memoryMode,
          explicitMemoryIds,
          promptProfileId,
          customSystemPrompt,
        })
        .catch(err => console.error('SignalR send() rejected:', err));
    } catch (err) {
      console.error('Failed to send message:', err);
      setIsStreaming(false);
      setStreamingContent('');
      setStreamingAttachments([]);
      setToolStatus(null);
      connection.stop().catch(e => console.error('Error stopping connection:', e));
    }
  }, []);

  const setOnStreamComplete = useCallback((callback: (payload: StreamCompletePayload) => void) => {
    onStreamCompleteRef.current = callback;
  }, []);

  const setOnAuthError = useCallback((callback: () => void) => {
    onAuthErrorRef.current = callback;
  }, []);

  return {
    sendMessage,
    isStreaming,
    streamingContent,
    streamingAttachments,
    toolStatus,
    setOnStreamComplete,
    setOnAuthError,
  };
}
