import { useCallback, useEffect, useState } from 'react';
import type { Memory, MemoryType } from '../types';
import { memoryApi } from '../services/memoryApi';

export function useMemory(enabled: boolean) {
  const [memories, setMemories] = useState<Memory[]>([]);
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!enabled) return;
    setIsLoading(true);
    setError(null);
    try {
      const data = await memoryApi.list();
      setMemories(data);
    } catch (err) {
      console.error('Failed to load memories:', err);
      setError(err instanceof Error ? err.message : 'Unknown error');
    } finally {
      setIsLoading(false);
    }
  }, [enabled]);

  useEffect(() => {
    load();
  }, [load]);

  const create = useCallback(async (type: MemoryType, content: string) => {
    const trimmed = content.trim();
    if (!trimmed) return null;
    try {
      const memory = await memoryApi.create(type, trimmed);
      setMemories(prev => [memory, ...prev]);
      return memory;
    } catch (err) {
      console.error('Failed to create memory:', err);
      setError(err instanceof Error ? err.message : 'Failed to create');
      return null;
    }
  }, []);

  const update = useCallback(async (id: string, patch: { type?: MemoryType; content?: string }) => {
    try {
      const memory = await memoryApi.update(id, patch);
      setMemories(prev => prev.map(m => (m.id === id ? memory : m)));
      return memory;
    } catch (err) {
      console.error('Failed to update memory:', err);
      setError(err instanceof Error ? err.message : 'Failed to update');
      return null;
    }
  }, []);

  const remove = useCallback(async (id: string) => {
    try {
      await memoryApi.remove(id);
      setMemories(prev => prev.filter(m => m.id !== id));
    } catch (err) {
      console.error('Failed to delete memory:', err);
      setError(err instanceof Error ? err.message : 'Failed to delete');
    }
  }, []);

  return { memories, isLoading, error, reload: load, create, update, remove };
}
