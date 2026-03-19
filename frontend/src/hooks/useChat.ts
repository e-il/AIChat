import { useState, useRef, useCallback } from 'react';
import * as signalR from '@microsoft/signalr';
import type { Message } from '../types';
import { getAuthCode } from '../services/auth';

export function useChat() {
  const [isStreaming, setIsStreaming] = useState(false);
  const [streamingContent, setStreamingContent] = useState('');
  const [authError, setAuthError] = useState(false);
  const onMessageAddedRef = useRef<((conversationId: string, message: Message) => void) | null>(null);
  const onMessageCompleteRef = useRef<((conversationId: string, message: Message) => void) | null>(null);
  const onAuthErrorRef = useRef<(() => void) | null>(null);

  const sendMessage = useCallback(async (conversationId: string, message: string, modelId: string) => {
    const authCode = getAuthCode();
    if (!authCode) {
      setAuthError(true);
      onAuthErrorRef.current?.();
      return;
    }

    setIsStreaming(true);
    setStreamingContent('');
    setAuthError(false);

    // Create a new connection for this streaming session with auth code
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`/chathub?authCode=${encodeURIComponent(authCode)}`)
      .build();

    connection.on('ReceiveMessageChunk', (_conversationId: string, chunk: string) => {
      setStreamingContent(prev => prev + chunk);
    });

    connection.on('MessageAdded', (convId: string, msg: Message) => {
      onMessageAddedRef.current?.(convId, msg);
    });

    connection.on('MessageComplete', (convId: string, msg: Message) => {
      setIsStreaming(false);
      setStreamingContent('');
      onMessageCompleteRef.current?.(convId, msg);
      
      // Disconnect after message is complete to save resources
      connection.stop().catch(err => console.error('Error stopping connection:', err));
    });

    connection.on('Error', (_conversationId: string, error: string) => {
      console.error('Chat error:', error);
      setIsStreaming(false);
      setStreamingContent('');
      
      if (error.includes('authentication')) {
        setAuthError(true);
        onAuthErrorRef.current?.();
      }
      
      connection.stop().catch(err => console.error('Error stopping connection:', err));
    });

    try {
      await connection.start();
      // Use send() instead of invoke() - fire-and-forget since we get response via events
      // This avoids error when MessageComplete handler stops the connection
      connection.send('SendMessage', conversationId, message, modelId);
    } catch (err) {
      console.error('Failed to send message:', err);
      setIsStreaming(false);
      setStreamingContent('');
      connection.stop().catch(e => console.error('Error stopping connection:', e));
    }
  }, []);

  const setOnMessageAdded = useCallback((callback: (conversationId: string, message: Message) => void) => {
    onMessageAddedRef.current = callback;
  }, []);

  const setOnMessageComplete = useCallback((callback: (conversationId: string, message: Message) => void) => {
    onMessageCompleteRef.current = callback;
  }, []);

  const setOnAuthError = useCallback((callback: () => void) => {
    onAuthErrorRef.current = callback;
  }, []);

  return {
    sendMessage,
    isStreaming,
    streamingContent,
    authError,
    setOnMessageAdded,
    setOnMessageComplete,
    setOnAuthError,
  };
}
