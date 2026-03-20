import { MessageSquarePlus, Trash2, Bot, MessageCircle, Settings, MoreVertical } from 'lucide-react';
import type { ConversationSummary } from '../../types';

interface SidebarProps {
  conversations: ConversationSummary[];
  activeId: string | null;
  onSelect: (id: string) => void;
  onNew: () => void;
  onDelete: (id: string) => void;
  isOpen: boolean;
  onClose: () => void;
}

export function Sidebar({
  conversations,
  activeId,
  onSelect,
  onNew,
  onDelete,
  isOpen,
  onClose,
}: SidebarProps) {

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
          fixed lg:static inset-y-0 left-0 z-50
          w-72 bg-slate-50 border-r-0
          transform transition-transform duration-200 ease-in-out
          lg:transform-none flex flex-col h-screen p-4
          ${isOpen ? 'translate-x-0' : '-translate-x-full lg:translate-x-0'}
        `}
      >
        {/* Logo & Branding */}
        <div className="flex items-center gap-3 px-2 mb-8">
          <div className="w-10 h-10 bg-primary-container rounded-lg flex items-center justify-center">
            <Bot size={20} className="text-white" />
          </div>
          <div>
            <h1 className="text-slate-900 text-xl font-bold tracking-tight font-headline">
              AIChat
            </h1>
            <p className="text-[0.6875rem] text-on-surface-variant uppercase tracking-wider font-semibold">
              AI Assistant
            </p>
          </div>
        </div>

        {/* New Chat CTA */}
        <button
          onClick={onNew}
          className="flex items-center justify-center gap-2 w-full py-3 px-4 mb-6 
                     bg-primary text-on-primary rounded-full font-semibold 
                     transition-all hover:bg-primary-dim active:scale-[0.98] cursor-pointer"
        >
          <MessageSquarePlus size={18} />
          <span className="font-body text-sm">New Chat</span>
        </button>

        {/* Navigation Scrollable Area */}
        <nav className="flex-1 overflow-y-auto sidebar-scroll space-y-6 pr-2">
          {/* History Section */}
          <div>
            <p className="px-4 text-[0.6875rem] font-bold text-on-surface-variant uppercase tracking-widest mb-3">
              History
            </p>
            <div className="space-y-1">
              {conversations.length === 0 ? (
                <p className="text-center text-on-surface-variant text-sm py-4">
                  No conversations yet
                </p>
              ) : (
                conversations.map(conv => (
                  <div
                    key={conv.id}
                    className={`
                      group flex items-center gap-3 px-4 py-2 cursor-pointer
                      transition-colors rounded-full
                      ${activeId === conv.id
                        ? 'bg-blue-100 text-blue-700 font-semibold'
                        : 'text-slate-600 hover:bg-slate-200/50'}
                    `}
                    onClick={() => onSelect(conv.id)}
                  >
                    <MessageCircle size={14} className={activeId === conv.id ? 'text-blue-600' : 'text-slate-400'} />
                    <div className="flex-1 min-w-0">
                      <p className="text-sm truncate">{conv.title}</p>
                    </div>
                    <button
                      onClick={(e) => {
                        e.stopPropagation();
                        onDelete(conv.id);
                      }}
                      className="opacity-0 group-hover:opacity-100 p-1.5 
                                 hover:bg-red-100 rounded-full transition-all cursor-pointer"
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
        <div className="mt-auto pt-4 border-t border-slate-200/50 space-y-1">
          <a 
            href="#" 
            className="flex items-center gap-3 px-4 py-2 text-slate-600 
                       hover:bg-slate-200/50 transition-colors rounded-full"
          >
            <Settings size={18} />
            <span className="text-sm">Settings</span>
          </a>
          
          {/* User Profile */}
          <div className="flex items-center gap-3 px-4 py-4 mt-2">
            <div className="w-8 h-8 rounded-lg bg-gradient-to-br from-primary to-primary-container 
                            flex items-center justify-center text-white text-sm font-semibold">
              U
            </div>
            <div className="flex-1 overflow-hidden">
              <p className="text-sm font-semibold truncate text-slate-900">User</p>
              <p className="text-[0.6875rem] text-on-surface-variant">Free Plan</p>
            </div>
            <MoreVertical size={16} className="text-slate-400" />
          </div>
        </div>
      </aside>
    </>
  );
}
