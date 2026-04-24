import { useState, useRef, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import type { Memory, Message } from '../types';
import { getAuthCode } from '../services/auth';

export function useChat() {
  const [isStreaming, setIsStreaming] = useState(false);
  const [streamingContent, setStreamingContent] = useState('');
  const onStreamCompleteRef = useRef<((conversationId: string, content: string, usedMemories: Memory[]) => void) | null>(null);
  const onAuthErrorRef = useRef<(() => void) | null>(null);

  const sendMessage = useCallback(async (
    conversationId: string,
    messages: Message[],
    modelId: string,
    maxContextSize: number,
    maxMessages: number,
    memoryMode: 'auto' | 'off' | 'explicit' = 'auto',
    explicitMemoryIds: string[] | null = null,
  ) => {
    const authCode = getAuthCode();
    if (!authCode) {
      onAuthErrorRef.current?.();
      return;
    }

    setIsStreaming(true);
    setStreamingContent('');
    const accumulated: string[] = [];
    let usedMemories: Memory[] = [];

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

    connection.on('StreamComplete', (convId: string) => {
      setIsStreaming(false);
      setStreamingContent('');
      onStreamCompleteRef.current?.(convId, accumulated.join(''), usedMemories);
      connection.stop().catch(err => console.error('Error stopping connection:', err));
    });

    connection.on('Error', (_convId: string, error: string) => {
      console.error('Chat error:', error);
      setIsStreaming(false);
      setStreamingContent('');
      if (error.includes('authentication')) {
        onAuthErrorRef.current?.();
      }
      connection.stop().catch(err => console.error('Error stopping connection:', err));
    });

    try {
      await connection.start();
      // SignalR requires exact arg count -- server method has 7 params even though
      // the last two have C# defaults (SignalR doesn't honor default values).
      connection
        .send('SendMessage', conversationId, messages, modelId, maxContextSize, maxMessages, memoryMode, explicitMemoryIds)
        .catch(err => console.error('SignalR send() rejected:', err));
    } catch (err) {
      console.error('Failed to send message:', err);
      setIsStreaming(false);
      setStreamingContent('');
      connection.stop().catch(e => console.error('Error stopping connection:', e));
    }
  }, []);

  const setOnStreamComplete = useCallback((callback: (conversationId: string, content: string, usedMemories: Memory[]) => void) => {
    onStreamCompleteRef.current = callback;
  }, []);

  const setOnAuthError = useCallback((callback: () => void) => {
    onAuthErrorRef.current = callback;
  }, []);

  return {
    sendMessage,
    isStreaming,
    streamingContent,
    setOnStreamComplete,
    setOnAuthError,
  };
}
