import { useState, useCallback } from 'react';
import type { Conversation, ConversationSummary, Message } from '../types';
import { chatApi } from '../services/chatApi';

export function useConversations() {
  const [conversations, setConversations] = useState<ConversationSummary[]>([]);
  const [activeConversation, setActiveConversation] = useState<Conversation | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const loadConversations = useCallback(async () => {
    try {
      const data = await chatApi.getConversations();
      setConversations(data);
    } catch (err) {
      console.error('Failed to load conversations:', err);
    }
  }, []);

  const loadConversation = useCallback(async (id: string) => {
    setIsLoading(true);
    try {
      const data = await chatApi.getConversation(id);
      setActiveConversation(data);
    } catch (err) {
      console.error('Failed to load conversation:', err);
    } finally {
      setIsLoading(false);
    }
  }, []);

  const createConversation = useCallback(async () => {
    try {
      const newConversation = await chatApi.createConversation();
      setConversations(prev => [
        {
          id: newConversation.id,
          title: newConversation.title,
          createdAt: newConversation.createdAt,
          updatedAt: newConversation.updatedAt,
          messageCount: 0,
        },
        ...prev,
      ]);
      setActiveConversation(newConversation);
      return newConversation;
    } catch (err) {
      console.error('Failed to create conversation:', err);
      return null;
    }
  }, []);

  const deleteConversation = useCallback(async (id: string) => {
    try {
      await chatApi.deleteConversation(id);
      setConversations(prev => prev.filter(c => c.id !== id));
      if (activeConversation?.id === id) {
        setActiveConversation(null);
      }
    } catch (err) {
      console.error('Failed to delete conversation:', err);
    }
  }, [activeConversation]);

  const addMessage = useCallback((conversationId: string, message: Message) => {
    if (activeConversation?.id === conversationId) {
      setActiveConversation(prev => {
        if (!prev) return prev;
        return {
          ...prev,
          messages: [...prev.messages, message],
          updatedAt: new Date().toISOString(),
        };
      });
    }
    // Update title in sidebar if it was the first message
    setConversations(prev => prev.map(c => {
      if (c.id === conversationId && c.messageCount === 0 && message.role === 'user') {
        const title = message.content.length > 50 
          ? message.content.slice(0, 47) + '...' 
          : message.content;
        return { ...c, title, messageCount: 1 };
      }
      return c;
    }));
  }, [activeConversation]);

  // Update a temporary message with the real one from server
  const updateMessage = useCallback((conversationId: string, tempId: string, realMessage: Message) => {
    if (activeConversation?.id === conversationId) {
      setActiveConversation(prev => {
        if (!prev) return prev;
        return {
          ...prev,
          messages: prev.messages.map(m => 
            m.id === tempId ? realMessage : m
          ),
        };
      });
    }
  }, [activeConversation]);

  return {
    conversations,
    activeConversation,
    isLoading,
    loadConversations,
    loadConversation,
    createConversation,
    deleteConversation,
    addMessage,
    updateMessage,
    setActiveConversation,
  };
}
