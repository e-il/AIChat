import { useState, useRef, useEffect } from 'react';
import type { KeyboardEvent } from 'react';
import { Send } from 'lucide-react';

interface ChatInputProps {
  onSend: (message: string) => void;
  disabled: boolean;
}

export function ChatInput({ onSend, disabled }: ChatInputProps) {
  const [message, setMessage] = useState('');
  const textareaRef = useRef<HTMLTextAreaElement>(null);

  const handleSubmit = () => {
    const trimmed = message.trim();
    if (trimmed && !disabled) {
      onSend(trimmed);
      setMessage('');
    }
  };

  const handleKeyDown = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      handleSubmit();
    }
  };

  // Auto-resize textarea
  useEffect(() => {
    const textarea = textareaRef.current;
    if (textarea) {
      textarea.style.height = 'auto';
      textarea.style.height = `${Math.min(textarea.scrollHeight, 200)}px`;
    }
  }, [message]);

  return (
    <div className="bg-white border-t border-neutral-200 px-4 py-3">
      <div className="max-w-4xl mx-auto">
        <div className="flex items-end gap-2 border border-neutral-300 
                        focus-within:border-primary-500 transition-colors rounded-sm">
          <textarea
            ref={textareaRef}
            value={message}
            onChange={(e) => setMessage(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Type your message..."
            disabled={disabled}
            rows={1}
            className="flex-1 resize-none px-3 py-2 bg-white
                       focus:outline-none text-sm text-neutral-800 placeholder-neutral-400
                       disabled:opacity-50 disabled:cursor-not-allowed
                       min-h-[36px] max-h-[200px] rounded-sm"
          />
          <button
            onClick={handleSubmit}
            disabled={disabled || !message.trim()}
            className="flex-shrink-0 w-8 h-8 m-1 flex items-center justify-center
                       bg-primary-500 hover:bg-primary-600 text-white rounded-sm
                       disabled:bg-neutral-300 disabled:text-neutral-500
                       disabled:cursor-not-allowed cursor-pointer transition-colors"
          >
            <Send size={16} />
          </button>
        </div>
      </div>
    </div>
  );
}
