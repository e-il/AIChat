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
        <div className="flex items-center gap-3 text-neutral-500">
          <div className="w-5 h-5 border-2 border-primary-500 border-t-transparent rounded-full animate-spin" />
          Loading conversation...
        </div>
      </div>
    );
  }

  if (messages.length === 0 && !isStreaming) {
    return (
      <div className="flex-1 flex flex-col items-center justify-center px-8 bg-neutral-50">
        {/* Hero Section */}
        <div className="w-16 h-16 bg-primary-500 rounded-sm 
                        flex items-center justify-center mb-6 depth-8">
          <Sparkles size={32} className="text-white" />
        </div>
        <h1 className="text-xl font-semibold text-neutral-800 mb-2">
          Welcome to AIChat
        </h1>
        <p className="text-neutral-600 text-center max-w-md mb-8 text-sm">
          Start a conversation with AI. Ask questions, get help with coding, writing, analysis, and more.
        </p>
        
        {/* Capability Cards */}
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4 max-w-3xl w-full">
          <div className="p-4 bg-white border border-neutral-200 
                          hover:border-primary-500 transition-colors">
            <div className="w-8 h-8 bg-primary-500 rounded-sm 
                            flex items-center justify-center mb-3">
              <MessageSquare size={16} className="text-white" />
            </div>
            <h3 className="font-semibold text-neutral-800 text-sm mb-1">Natural Conversations</h3>
            <p className="text-xs text-neutral-600">Chat naturally and get helpful, contextual responses.</p>
          </div>
          
          <div className="p-4 bg-white border border-neutral-200 
                          hover:border-green-500 transition-colors">
            <div className="w-8 h-8 bg-green-600 rounded-sm 
                            flex items-center justify-center mb-3">
              <Code size={16} className="text-white" />
            </div>
            <h3 className="font-semibold text-neutral-800 text-sm mb-1">Code Assistance</h3>
            <p className="text-xs text-neutral-600">Get help writing, debugging, and explaining code.</p>
          </div>
          
          <div className="p-4 bg-white border border-neutral-200 
                          hover:border-orange-500 transition-colors">
            <div className="w-8 h-8 bg-orange-500 rounded-sm 
                            flex items-center justify-center mb-3">
              <FileText size={16} className="text-white" />
            </div>
            <h3 className="font-semibold text-neutral-800 text-sm mb-1">Content Creation</h3>
            <p className="text-xs text-neutral-600">Draft documents, emails, and creative content.</p>
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className="flex-1 overflow-y-auto bg-neutral-50">
      <div className="max-w-4xl mx-auto px-4 py-4">
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
