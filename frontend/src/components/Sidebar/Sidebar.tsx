import { MessageSquarePlus, Trash2 } from 'lucide-react';
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
  const formatDate = (dateStr: string) => {
    const date = new Date(dateStr);
    const now = new Date();
    const diffDays = Math.floor((now.getTime() - date.getTime()) / (1000 * 60 * 60 * 24));
    
    if (diffDays === 0) return 'Today';
    if (diffDays === 1) return 'Yesterday';
    if (diffDays < 7) return `${diffDays} days ago`;
    return date.toLocaleDateString();
  };

  return (
    <>
      {/* Mobile overlay */}
      {isOpen && (
        <div
          className="fixed inset-0 bg-black/50 z-40 lg:hidden"
          onClick={onClose}
        />
      )}
      
      {/* Sidebar - Gray background */}
      <aside
        className={`
          fixed lg:static inset-y-0 left-0 z-50
          w-64 bg-neutral-100 border-r border-neutral-200
          transform transition-transform duration-200 ease-in-out
          lg:transform-none flex flex-col
          ${isOpen ? 'translate-x-0' : '-translate-x-full lg:translate-x-0'}
        `}
      >
        {/* Sidebar Header */}
        <div className="p-3 border-b border-neutral-200">
          <button
            onClick={onNew}
            className="flex items-center justify-center gap-2 w-full py-2 px-4 
                       bg-neutral-100 hover:bg-neutral-200 text-neutral-700 text-sm font-medium
                       border border-neutral-300 rounded transition-colors cursor-pointer"
          >
            <MessageSquarePlus size={16} />
            New Chat
          </button>
        </div>

        {/* Conversations List */}
        <div className="flex-1 overflow-y-auto py-2">
          {conversations.length === 0 ? (
            <p className="text-center text-neutral-500 text-sm py-8">
              No conversations yet
            </p>
          ) : (
            conversations.map(conv => (
              <div
                key={conv.id}
                className={`
                  group flex items-center gap-2 px-3 py-2 mx-1 cursor-pointer
                  transition-colors duration-75
                  ${activeId === conv.id
                    ? 'bg-primary-50 border-l-2 border-primary-500'
                    : 'hover:bg-neutral-100 border-l-2 border-transparent'}
                `}
                onClick={() => onSelect(conv.id)}
              >
                <div className="flex-1 min-w-0">
                  <p className={`text-sm truncate ${activeId === conv.id ? 'text-primary-700 font-medium' : 'text-neutral-700'}`}>
                    {conv.title}
                  </p>
                  <p className="text-xs text-neutral-500 mt-0.5">
                    {formatDate(conv.updatedAt)}
                  </p>
                </div>
                <button
                  onClick={(e) => {
                    e.stopPropagation();
                    onDelete(conv.id);
                  }}
                  className="opacity-0 group-hover:opacity-100 p-1.5 
                             hover:bg-red-100 rounded transition-all cursor-pointer"
                >
                  <Trash2 size={14} className="text-red-600" />
                </button>
              </div>
            ))
          )}
        </div>
      </aside>
    </>
  );
}
