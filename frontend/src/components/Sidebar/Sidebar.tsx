import { useState } from 'react';
import { MessageSquarePlus, Trash2, Search, MessageCircle, Brain, X, PencilLine, Languages } from 'lucide-react';
import type { ConversationSummary } from '../../types';
import { REWRITE_PROMPT_PROFILE_ID, TRANSLATE_PROMPT_PROFILE_ID } from '../../services/promptProfiles';

interface SidebarProps {
  conversations: ConversationSummary[];
  activeId: string | null;
  onSelect: (id: string) => void;
  onNew: () => void;
  onNewWithProfile: (profileId: string) => void;
  onDelete: (id: string) => void;
  onOpenMemory: () => void;
  isOpen: boolean;
  onClose: () => void;
}

export function Sidebar({
  conversations,
  activeId,
  onSelect,
  onNew,
  onNewWithProfile,
  onDelete,
  onOpenMemory,
  isOpen,
  onClose,
}: SidebarProps) {
  const [search, setSearch] = useState('');
  const filteredConversations = conversations.filter(conversation => conversation.title.toLocaleLowerCase().includes(search.toLocaleLowerCase()));

  return (
    <>
      {/* Mobile overlay */}
      {isOpen && (
        <div
          className="fixed inset-0 bg-black/40 z-40 lg:hidden backdrop-blur-sm"
          onClick={onClose}
        />
      )}

      {/* Sidebar - Ethereal Design */}
      <aside
        className={`
          workspace-sidebar fixed lg:static inset-y-0 left-0 z-50
          w-[264px] shrink-0 border-r-0
          transform transition-transform duration-200 ease-in-out
          lg:transform-none flex flex-col p-5
          ${isOpen ? 'translate-x-0 visible' : '-translate-x-full invisible lg:visible lg:translate-x-0'}
        `}
      >
        {/* Logo & Branding */}
        <div className="flex items-center gap-3 px-2 mb-8 pt-1">
          <img src="/favicon.svg" alt="" className="w-9 h-9" />
          <div>
            <h1 className="text-white text-2xl font-semibold font-headline">
              AIChat<span className="text-[#ef846e]">.</span>
            </h1>
          </div>
          <button title="Close navigation" aria-label="Close navigation" onClick={onClose} className="ml-auto lg:hidden p-2 text-white/70"><X size={18} /></button>
        </div>

        {/* New Chat CTA */}
        <button
          onClick={onNew}
          className="flex items-center gap-3 w-full py-3 px-4 mb-4 bg-[#eb705b] text-[#201f1c] rounded-md font-semibold hover:bg-[#f38a77]"
        >
          <MessageSquarePlus size={18} />
          <span className="font-body text-sm">New conversation</span>
        </button>

        <div className="space-y-1 mb-6">
          <button
            onClick={() => onNewWithProfile(REWRITE_PROMPT_PROFILE_ID)}
            className="sidebar-link"
          >
            <PencilLine size={17} />
            <span className="text-xs font-semibold">Rewrite</span>
          </button>
          <button
            onClick={() => onNewWithProfile(TRANSLATE_PROMPT_PROFILE_ID)}
            className="sidebar-link"
          >
            <Languages size={17} />
            <span className="text-xs font-semibold">Translate</span>
          </button>
        </div>

        {/* Navigation Scrollable Area */}
        <div className="flex items-center justify-between text-[11px] text-white/45 mb-3 px-2"><span>CONVERSATIONS</span><span className="font-mono">{conversations.length.toString().padStart(2, '0')}</span></div>
        <label className="flex items-center gap-2 border border-white/15 rounded-md px-3 py-2 mb-3 text-white/45 focus-within:border-white/40">
          <Search size={14} className="shrink-0" />
          <input aria-label="Search conversations" value={search} onChange={event => setSearch(event.target.value)} placeholder="Search conversations" className="min-w-0 w-full bg-transparent text-xs text-white outline-none! placeholder:text-white/35" />
        </label>
        <nav aria-label="Conversations" className="flex-1 min-h-0 overflow-y-auto sidebar-scroll">
          {/* History Section */}
          <div>
            <div className="space-y-1">
              {filteredConversations.length === 0 ? (
                <p className="text-white/40 text-xs px-2 py-4">
                  {search ? 'No matching conversations' : 'No conversations yet'}
                </p>
              ) : (
                filteredConversations.map(conv => (
                  <div
                    key={conv.id}
                    className={`
                      group flex items-center gap-2 px-2 py-1
                      transition-colors rounded-md
                      ${activeId === conv.id
                        ? 'bg-white/10 text-white font-semibold'
                        : 'text-white/60 hover:bg-white/5'}
                    `}
                  >
                    <button onClick={() => onSelect(conv.id)} aria-current={activeId === conv.id ? 'page' : undefined} className="flex items-center gap-2.5 text-left flex-1 min-w-0 py-2"><MessageCircle size={14} className="shrink-0 opacity-60" /><span className="text-[13px] truncate">{conv.title}</span></button>
                    <button
                      onClick={(e) => {
                        e.stopPropagation();
                        if (window.confirm(`Delete "${conv.title}"?`)) onDelete(conv.id);
                      }}
                      title="Delete conversation"
                      aria-label={`Delete ${conv.title}`}
                      className="lg:opacity-0 group-hover:opacity-100 focus-visible:opacity-100 p-1.5 hover:bg-white/10 rounded transition-all"
                    >
                      <Trash2 size={12} className="text-red-500" />
                    </button>
                  </div>
                ))
              )}
            </div>
          </div>
        </nav>

        {/* Footer Navigation */}
        <div className="mt-4 pt-4 border-t border-white/10 space-y-1">
          <button
            onClick={() => { onOpenMemory(); onClose(); }}
            className="sidebar-link"
          >
            <Brain size={18} />
            <span className="text-sm">Memory library</span>
          </button>

          <div className="flex items-center gap-2 px-3 pt-5 pb-1 text-[10px] text-white/35"><span className="w-1.5 h-1.5 rounded-full bg-[#98b69b]" />PERSONAL WORKSPACE</div>
        </div>
      </aside>
    </>
  );
}
