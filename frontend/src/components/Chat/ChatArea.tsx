import { useEffect, useRef } from 'react';
import type { Message, MessageAttachment } from '../../types';
import { MessageBubble, StreamingBubble } from './MessageBubble';
import { TypingIndicator } from './TypingIndicator';
import { ArrowUpRight, Code2, PencilLine, Compass } from 'lucide-react';

interface ChatAreaProps {
  messages: Message[];
  streamingContent: string;
  streamingAttachments: MessageAttachment[];
  toolStatus: string | null;
  isStreaming: boolean;
  isLoading: boolean;
  onSuggestion: (message: string) => void;
}

export function ChatArea({ messages, streamingContent, streamingAttachments, toolStatus, isStreaming, isLoading, onSuggestion }: ChatAreaProps) {
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages, streamingContent, streamingAttachments, toolStatus]);

  if (isLoading) {
    return (
      <div className="flex-1 flex items-center justify-center">
        <div className="flex items-center gap-3 text-on-surface-variant">
          <div className="w-5 h-5 border-2 border-primary border-t-transparent rounded-full animate-spin" />
          <span className="font-body">Loading conversation...</span>
        </div>
      </div>
    );
  }

  if (messages.length === 0 && !isStreaming) {
    return (
      <div className="welcome-area flex-1 min-h-0 overflow-y-auto flex px-6 md:px-12">
        <div className="w-full max-w-[760px] m-auto py-8 welcome-content">
          <div className="flex items-center gap-3 mb-7 text-xs text-on-surface-variant"><span className="w-8 h-px bg-primary" />A FRESH CONVERSATION</div>
          <h1 className="font-headline text-[36px] md:text-[48px] font-medium leading-[1.12] text-on-surface">A little curiosity.<br /><span className="text-primary">A new perspective.</span></h1>
          <div className="mt-10 md:mt-12 border-t border-outline-variant/30">
            {[
              { icon: Compass, label: 'Explore an idea', prompt: 'Help me explore a new idea. Start by asking what I have in mind.', number: '01' },
              { icon: Code2, label: 'Work through a coding problem', prompt: 'Help me work through a coding problem. Ask me about the code and what I want to achieve.', number: '02' },
              { icon: PencilLine, label: 'Find the right words', prompt: 'Help me draft a clear, thoughtful message. Ask who it is for and what I want to say.', number: '03' },
            ].map(({ icon: Icon, label, prompt, number }) => (
              <button key={number} onClick={() => onSuggestion(prompt)} className="suggestion-row group flex items-center gap-4 w-full text-left py-4 border-b border-outline-variant/30 hover:text-primary">
                <span className="font-mono text-[10px] text-on-surface-variant/60">{number}</span><Icon size={18} className="text-secondary shrink-0" /><span className="text-sm flex-1">{label}</span><ArrowUpRight size={17} className="text-on-surface-variant group-hover:text-primary" />
              </button>
            ))}
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex-1 min-h-0 overflow-y-auto bg-surface">
      <div className="max-w-[880px] mx-auto px-5 md:px-10 py-8 space-y-8">
        {messages.map(message => (
          <MessageBubble key={message.id} message={message} />
        ))}
        
        {isStreaming && (streamingContent || streamingAttachments.length > 0 || toolStatus) && (
          <StreamingBubble
            content={streamingContent}
            attachments={streamingAttachments}
            toolStatus={toolStatus}
          />
        )}

        {isStreaming && !streamingContent && streamingAttachments.length === 0 && !toolStatus && (
          <TypingIndicator />
        )}
        
        <div ref={bottomRef} />
      </div>
    </div>
  );
}
