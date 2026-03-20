import { useEffect, useRef } from 'react';
import type { Message } from '../../types';
import { MessageBubble, StreamingBubble } from './MessageBubble';
import { TypingIndicator } from './TypingIndicator';
import { MessageSquare, Sparkles, Code, FileText } from 'lucide-react';

interface ChatAreaProps {
  messages: Message[];
  streamingContent: string;
  isStreaming: boolean;
  isLoading: boolean;
}

export function ChatArea({ messages, streamingContent, isStreaming, isLoading }: ChatAreaProps) {
  const bottomRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages, streamingContent]);

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
      <div className="flex-1 flex flex-col items-center justify-center px-8 bg-surface">
        {/* Hero Section */}
        <div className="w-16 h-16 bg-gradient-to-br from-primary to-primary-container rounded-2xl 
                        flex items-center justify-center mb-6 shadow-lg shadow-primary/20">
          <Sparkles size={32} className="text-white" />
        </div>
        <h1 className="text-2xl font-bold text-on-surface mb-2 font-headline">
          Welcome to AIChat
        </h1>
        <p className="text-on-surface-variant text-center max-w-md mb-10 text-sm font-body">
          Start a conversation with AI. Ask questions, get help with coding, writing, analysis, and more.
        </p>
        
        {/* Capability Cards - Bento Style */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4 max-w-3xl w-full">
          <div className="p-5 bg-surface-container-high rounded-2xl 
                          hover:bg-surface-container-highest transition-colors cursor-pointer group">
            <div className="w-10 h-10 bg-primary rounded-xl 
                            flex items-center justify-center mb-4 group-hover:scale-105 transition-transform">
              <MessageSquare size={20} className="text-white" />
            </div>
            <h3 className="font-semibold text-on-surface text-sm mb-1 font-headline">Natural Conversations</h3>
            <p className="text-xs text-on-surface-variant font-body">Chat naturally and get helpful, contextual responses.</p>
          </div>
          
          <div className="p-5 bg-surface-container-high rounded-2xl 
                          hover:bg-surface-container-highest transition-colors cursor-pointer group">
            <div className="w-10 h-10 bg-tertiary rounded-xl 
                            flex items-center justify-center mb-4 group-hover:scale-105 transition-transform">
              <Code size={20} className="text-white" />
            </div>
            <h3 className="font-semibold text-on-surface text-sm mb-1 font-headline">Code Assistance</h3>
            <p className="text-xs text-on-surface-variant font-body">Get help writing, debugging, and explaining code.</p>
          </div>
          
          <div className="p-5 bg-surface-container-high rounded-2xl 
                          hover:bg-surface-container-highest transition-colors cursor-pointer group">
            <div className="w-10 h-10 bg-secondary rounded-xl 
                            flex items-center justify-center mb-4 group-hover:scale-105 transition-transform">
              <FileText size={20} className="text-white" />
            </div>
            <h3 className="font-semibold text-on-surface text-sm mb-1 font-headline">Content Creation</h3>
            <p className="text-xs text-on-surface-variant font-body">Draft documents, emails, and creative content.</p>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex-1 overflow-y-auto bg-surface">
      <div className="max-w-5xl mx-auto px-6 md:px-12 py-8 space-y-10">
        {messages.map(message => (
          <MessageBubble key={message.id} message={message} />
        ))}
        
        {isStreaming && streamingContent && (
          <StreamingBubble content={streamingContent} />
        )}
        
        {isStreaming && !streamingContent && (
          <TypingIndicator />
        )}
        
        <div ref={bottomRef} />
      </div>
    </div>
  );
}
