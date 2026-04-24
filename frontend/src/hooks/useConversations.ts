import { useState, useCallback } from 'react';
import type { Conversation, ConversationSummary, Message } from '../types';
import { conversationStore } from '../services/conversationStore';

export function useConversations() {
  const [conversations, setConversations] = useState<ConversationSummary[]>([]);
  const [activeConversation, setActiveConversation] = useState<Conversation | null>(null);
  const [isLoading, setIsLoading] = useState(false);

  const loadConversations = useCallback(async () => {
    try {
      const data = await conversationStore.getAllConversations();
      setConversations(data);
    } catch (err) {
      console.error('Failed to load conversations:', err);
    }
  }, []);

  const loadConversation = useCallback(async (id: string) => {
    setIsLoading(true);
    try {
      const data = await conversationStore.getConversation(id);
      setActiveConversation(data);
    } catch (err) {
      console.error('Failed to load conversation:', err);
    } finally {
      setIsLoading(false);
    }
  }, []);

  const createConversation = useCallback(async () => {
    try {
      const newConversation = await conversationStore.createConversation();
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
      await conversationStore.deleteConversation(id);
      setConversations(prev => prev.filter(c => c.id !== id));
      setActiveConversation(prev => (prev?.id === id ? null : prev));
    } catch (err) {
      console.error('Failed to delete conversation:', err);
    }
  }, []);

  const addMessage = useCallback(async (conversationId: string, message: Message) => {
    try {
      await conversationStore.addMessage(conversationId, message);
    } catch (err) {
      console.error('Failed to persist message:', err);
    }

    const now = new Date().toISOString();

    setActiveConversation(prev => {
      if (!prev || prev.id !== conversationId) return prev;
      return {
        ...prev,
        messages: [...prev.messages, message],
        updatedAt: now,
      };
    });

    setConversations(prev => prev.map(c => {
      if (c.id !== conversationId) return c;
      const isFirstUserMsg = c.title === 'New Chat' && c.messageCount === 0 && message.role === 'user';
      return {
        ...c,
        title: isFirstUserMsg
          ? (message.content.length > 50 ? message.content.slice(0, 47) + '...' : message.content)
          : c.title,
        messageCount: c.messageCount + 1,
        updatedAt: now,
      };
    }));
  }, []);

  return {
    conversations,
    activeConversation,
    isLoading,
    loadConversations,
    loadConversation,
    createConversation,
    deleteConversation,
    addMessage,
    setActiveConversation,
  };
}
