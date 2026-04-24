import { useState } from 'react';
import { Brain, X, Plus, Trash2, Pencil, Check } from 'lucide-react';
import type { Memory, MemoryType } from '../../types';
import { useMemory } from '../../hooks/useMemory';

interface MemoryPanelProps {
  open: boolean;
  onClose: () => void;
}

const TYPE_STYLES: Record<MemoryType, { label: string; className: string }> = {
  preference: { label: 'Preference', className: 'bg-primary/10 text-primary' },
  fact: { label: 'Fact', className: 'bg-blue-100 text-blue-700' },
  summary: { label: 'Summary', className: 'bg-amber-100 text-amber-700' },
};

const TYPE_ORDER: MemoryType[] = ['preference', 'fact', 'summary'];

function formatDate(iso: string): string {
  const d = new Date(iso);
  const now = new Date();
  const diffMs = now.getTime() - d.getTime();
  const day = 1000 * 60 * 60 * 24;
  if (diffMs < day) return 'Today';
  if (diffMs < 2 * day) return 'Yesterday';
  if (diffMs < 7 * day) return `${Math.floor(diffMs / day)} days ago`;
  return d.toLocaleDateString();
}

export function MemoryPanel({ open, onClose }: MemoryPanelProps) {
  const { memories, isLoading, error, create, update, remove } = useMemory(open);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editingContent, setEditingContent] = useState('');
  const [newType, setNewType] = useState<MemoryType>('fact');
  const [newContent, setNewContent] = useState('');
  const [isAdding, setIsAdding] = useState(false);

  if (!open) return null;

  const handleAdd = async () => {
    const trimmed = newContent.trim();
    if (!trimmed) return;
    setIsAdding(true);
    await create(newType, trimmed);
    setNewContent('');
    setIsAdding(false);
  };

  const startEdit = (memory: Memory) => {
    setEditingId(memory.id);
    setEditingContent(memory.content);
  };

  const cancelEdit = () => {
    setEditingId(null);
    setEditingContent('');
  };

  const saveEdit = async () => {
    if (!editingId) return;
    const trimmed = editingContent.trim();
    if (!trimmed) return;
    await update(editingId, { content: trimmed });
    cancelEdit();
  };

  const grouped = TYPE_ORDER.map(type => ({
    type,
    items: memories.filter(m => m.type === type),
  }));

  return (
    <div className="fixed inset-0 bg-black/40 backdrop-blur-sm flex items-center justify-center z-50 p-4">
      <div className="bg-surface-container-lowest rounded-3xl shadow-2xl max-w-2xl w-full max-h-[80vh] flex flex-col overflow-hidden">
        {/* Header */}
        <div className="bg-gradient-to-br from-primary to-primary-container px-6 py-5 flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="w-10 h-10 bg-white/20 rounded-xl flex items-center justify-center backdrop-blur-sm">
              <Brain size={20} className="text-white" />
            </div>
            <div>
              <h2 className="text-lg font-bold text-white font-headline">Memory</h2>
              <p className="text-xs text-white/80 font-body">{memories.length} item{memories.length === 1 ? '' : 's'} remembered</p>
            </div>
          </div>
          <button
            onClick={onClose}
            className="w-9 h-9 flex items-center justify-center text-white/80 hover:bg-white/10 rounded-full transition-colors cursor-pointer"
          >
            <X size={20} />
          </button>
        </div>

        {/* Add memory form */}
        <div className="px-6 py-4 border-b border-slate-200/60 bg-surface-container-low">
          <div className="flex gap-2 items-start">
            <select
              value={newType}
              onChange={(e) => setNewType(e.target.value as MemoryType)}
              className="px-3 py-2 bg-surface-container-high border-2 border-transparent focus:border-primary
                         rounded-lg text-sm font-medium text-on-surface cursor-pointer focus:outline-none"
              disabled={isAdding}
            >
              <option value="fact">Fact</option>
              <option value="preference">Preference</option>
              <option value="summary">Summary</option>
            </select>
            <input
              type="text"
              value={newContent}
              onChange={(e) => setNewContent(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') handleAdd(); }}
              placeholder="Teach me something to remember..."
              className="flex-1 px-3 py-2 bg-surface-container-high border-2 border-transparent
                         focus:border-primary rounded-lg text-sm text-on-surface
                         placeholder-on-surface-variant/60 focus:outline-none"
              disabled={isAdding}
            />
            <button
              onClick={handleAdd}
              disabled={!newContent.trim() || isAdding}
              className="flex items-center gap-1.5 px-4 py-2 bg-primary text-on-primary rounded-lg
                         text-sm font-semibold disabled:opacity-50 disabled:cursor-not-allowed
                         cursor-pointer hover:bg-primary-dim transition-colors"
            >
              <Plus size={16} />
              Add
            </button>
          </div>
        </div>

        {/* Memory list */}
        <div className="flex-1 overflow-y-auto px-6 py-4">
          {isLoading && (
            <div className="flex items-center justify-center py-12 text-on-surface-variant">
              <div className="w-5 h-5 border-2 border-primary border-t-transparent rounded-full animate-spin mr-3" />
              Loading memories...
            </div>
          )}

          {error && !isLoading && (
            <p className="text-center text-error text-sm py-6">{error}</p>
          )}

          {!isLoading && !error && memories.length === 0 && (
            <div className="text-center py-12">
              <Brain size={40} className="mx-auto text-on-surface-variant/40 mb-3" />
              <p className="text-on-surface-variant text-sm">No memories yet.</p>
              <p className="text-on-surface-variant/70 text-xs mt-1">
                Add one above, or they'll appear as the AI learns about you.
              </p>
            </div>
          )}

          {!isLoading && !error && memories.length > 0 && (
            <div className="space-y-5">
              {grouped.map(({ type, items }) => items.length === 0 ? null : (
                <div key={type}>
                  <div className="flex items-center gap-2 mb-2">
                    <span className={`px-2 py-0.5 rounded-full text-[0.65rem] font-bold uppercase tracking-wider ${TYPE_STYLES[type].className}`}>
                      {TYPE_STYLES[type].label}
                    </span>
                    <span className="text-xs text-on-surface-variant">{items.length}</span>
                  </div>
                  <div className="space-y-2">
                    {items.map(memory => (
                      <div key={memory.id} className="group bg-surface-container-low rounded-xl px-4 py-3 hover:bg-surface-container transition-colors">
                        {editingId === memory.id ? (
                          <div className="flex gap-2 items-start">
                            <textarea
                              value={editingContent}
                              onChange={(e) => setEditingContent(e.target.value)}
                              className="flex-1 px-3 py-2 bg-surface-container-high border-2 border-primary
                                         rounded-lg text-sm text-on-surface resize-none focus:outline-none"
                              rows={2}
                              autoFocus
                            />
                            <div className="flex flex-col gap-1">
                              <button
                                onClick={saveEdit}
                                className="w-8 h-8 flex items-center justify-center bg-primary text-on-primary
                                           rounded-lg hover:bg-primary-dim cursor-pointer"
                              >
                                <Check size={14} />
                              </button>
                              <button
                                onClick={cancelEdit}
                                className="w-8 h-8 flex items-center justify-center text-on-surface-variant
                                           hover:bg-slate-200/50 rounded-lg cursor-pointer"
                              >
                                <X size={14} />
                              </button>
                            </div>
                          </div>
                        ) : (
                          <div className="flex items-start gap-3">
                            <p className="flex-1 text-sm text-on-surface leading-relaxed">{memory.content}</p>
                            <div className="flex items-center gap-1 opacity-0 group-hover:opacity-100 transition-opacity">
                              <button
                                onClick={() => startEdit(memory)}
                                className="w-8 h-8 flex items-center justify-center text-on-surface-variant
                                           hover:bg-slate-200/50 rounded-lg cursor-pointer"
                                title="Edit"
                              >
                                <Pencil size={13} />
                              </button>
                              <button
                                onClick={() => remove(memory.id)}
                                className="w-8 h-8 flex items-center justify-center text-red-500
                                           hover:bg-red-100 rounded-lg cursor-pointer"
                                title="Delete"
                              >
                                <Trash2 size={13} />
                              </button>
                            </div>
                          </div>
                        )}
                        {editingId !== memory.id && (
                          <div className="flex items-center gap-3 mt-1.5 text-[0.7rem] text-on-surface-variant">
                            <span>{formatDate(memory.createdAt)}</span>
                            {memory.useCount > 0 && <span>· used {memory.useCount}×</span>}
                          </div>
                        )}
                      </div>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
