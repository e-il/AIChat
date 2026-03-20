import ReactMarkdown from 'react-markdown';
import type { Components } from 'react-markdown';
import { Prism as SyntaxHighlighter } from 'react-syntax-highlighter';
import { vs } from 'react-syntax-highlighter/dist/esm/styles/prism';
import { Copy, Check, Sparkles } from 'lucide-react';
import { useState } from 'react';
import type { Message } from '../../types';

interface CodeBlockProps {
  language: string;
  children: string;
}

function CodeBlock({ language, children }: CodeBlockProps) {
  const [copied, setCopied] = useState(false);

  const handleCopy = async () => {
    await navigator.clipboard.writeText(children);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  return (
    <div className="relative group my-3 rounded-xl overflow-hidden">
      {/* Header */}
      <div className="flex items-center justify-between px-4 py-2 bg-surface-container-highest">
        <span className="text-xs text-on-surface-variant font-semibold uppercase tracking-wider">
          {language || 'code'}
        </span>
        <button
          onClick={handleCopy}
          className="flex items-center gap-1.5 px-2 py-1 text-xs text-on-surface-variant 
                     hover:text-primary hover:bg-surface-container rounded-lg transition-colors cursor-pointer"
        >
          {copied ? <Check size={12} /> : <Copy size={12} />}
          {copied ? 'Copied' : 'Copy'}
        </button>
      </div>
      {/* Code */}
      <SyntaxHighlighter
        language={language || 'text'}
        style={vs}
        customStyle={{
          margin: 0,
          padding: '1rem',
          fontSize: '13px',
          lineHeight: '1.6',
          background: '#f0f4f7',
          borderRadius: '0 0 0.75rem 0.75rem',
        }}
        showLineNumbers={children.split('\n').length > 3}
        lineNumberStyle={{ color: '#9ca3af', paddingRight: '1rem', minWidth: '2.5rem' }}
      >
        {children.trim()}
      </SyntaxHighlighter>
    </div>
  );
}

// Shared markdown components config - Ethereal style
const markdownComponents: Components = {
  code({ className, children, ...props }) {
    const match = /language-(\w+)/.exec(className || '');
    const isInline = !match && !String(children).includes('\n');
    
    if (isInline) {
      return (
        <code className="px-1.5 py-0.5 bg-surface-container-high text-primary rounded-md text-[13px] font-mono" {...props}>
          {children}
        </code>
      );
    }
    
    return (
      <CodeBlock language={match?.[1] || ''}>
        {String(children).replace(/\n$/, '')}
      </CodeBlock>
    );
  },
  p({ children }) {
    return <p className="my-2 first:mt-0 last:mb-0 leading-relaxed">{children}</p>;
  },
  h1({ children }) {
    return <h1 className="text-xl font-bold text-on-surface mt-6 mb-3 first:mt-0 font-headline">{children}</h1>;
  },
  h2({ children }) {
    return <h2 className="text-lg font-bold text-on-surface mt-5 mb-2 first:mt-0 font-headline">{children}</h2>;
  },
  h3({ children }) {
    return <h3 className="text-base font-semibold text-on-surface mt-4 mb-2 first:mt-0 font-headline">{children}</h3>;
  },
  ul({ children }) {
    return <ul className="list-disc pl-5 my-2 space-y-1">{children}</ul>;
  },
  ol({ children }) {
    return <ol className="list-decimal pl-5 my-2 space-y-1">{children}</ol>;
  },
  li({ children }) {
    return <li className="text-on-surface">{children}</li>;
  },
  blockquote({ children }) {
    return (
      <blockquote className="border-l-4 border-primary pl-4 py-1 my-3 bg-surface-container rounded-r-lg text-on-surface-variant italic">
        {children}
      </blockquote>
    );
  },
  a({ href, children }) {
    return (
      <a href={href} target="_blank" rel="noopener noreferrer" 
         className="text-primary hover:text-primary-dim underline underline-offset-2">
        {children}
      </a>
    );
  },
  table({ children }) {
    return (
      <div className="overflow-x-auto my-3">
        <table className="min-w-full rounded-xl overflow-hidden">
          {children}
        </table>
      </div>
    );
  },
  th({ children }) {
    return (
      <th className="px-4 py-2.5 bg-surface-container-highest text-left text-sm font-semibold text-on-surface">
        {children}
      </th>
    );
  },
  td({ children }) {
    return (
      <td className="px-4 py-2.5 text-sm text-on-surface border-t border-surface-container-high">
        {children}
      </td>
    );
  },
  hr() {
    return <hr className="my-6 border-surface-container-high" />;
  },
  strong({ children }) {
    return <strong className="font-semibold text-on-surface">{children}</strong>;
  },
  em({ children }) {
    return <em className="italic">{children}</em>;
  },
};

interface MessageBubbleProps {
  message: Message;
}

export function MessageBubble({ message }: MessageBubbleProps) {
  const [copied, setCopied] = useState(false);
  const isUser = message.role === 'user';

  const handleCopyMessage = async () => {
    await navigator.clipboard.writeText(message.content);
    setCopied(true);
    setTimeout(() => setCopied(false), 2000);
  };

  const formatTime = (timestamp?: string) => {
    if (!timestamp) return '';
    return new Date(timestamp).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
  };

  if (isUser) {
    // User message - right aligned with gradient
    return (
      <div className="flex flex-row-reverse gap-4 group">
        <div className="flex-shrink-0 w-10 h-10 rounded-lg overflow-hidden self-start mt-1 
                        bg-gradient-to-br from-primary to-primary-container flex items-center justify-center">
          <span className="text-white text-sm font-semibold">U</span>
        </div>
        <div className="flex flex-col gap-2 max-w-[85%] items-end">
          <div className="bg-gradient-to-br from-primary to-primary-container text-on-primary 
                          p-4 rounded-xl rounded-br-sm shadow-md">
            <p className="text-sm leading-relaxed font-body whitespace-pre-wrap">{message.content}</p>
          </div>
          <span className="text-[0.6875rem] text-on-surface-variant font-medium mr-1">
            {formatTime(message.timestamp)}
          </span>
        </div>
      </div>
    );
  }

  // AI message - left aligned with surface background
  return (
    <div className="flex gap-4 group">
      <div className="flex-shrink-0 w-10 h-10 rounded-lg bg-surface-container-highest 
                      flex items-center justify-center text-primary self-start mt-1">
        <Sparkles size={18} className="fill-primary" />
      </div>
      <div className="flex flex-col gap-2 max-w-[85%]">
        <div className="bg-surface-container-high text-on-surface p-4 rounded-xl rounded-bl-sm relative">
          <div className="markdown-content text-sm leading-relaxed font-body">
            <ReactMarkdown components={markdownComponents}>
              {message.content}
            </ReactMarkdown>
          </div>
        </div>
        <div className="flex items-center gap-2 ml-1">
          <span className="text-[0.6875rem] text-on-surface-variant font-medium">
            {formatTime(message.timestamp)}
          </span>
          <button
            onClick={handleCopyMessage}
            className="p-1 rounded-lg text-on-surface-variant hover:text-primary hover:bg-surface-container
                       opacity-0 group-hover:opacity-100 transition-all cursor-pointer"
          >
            {copied ? <Check size={12} /> : <Copy size={12} />}
          </button>
        </div>
      </div>
    </div>
  );
}

interface StreamingBubbleProps {
  content: string;
}

export function StreamingBubble({ content }: StreamingBubbleProps) {
  return (
    <div className="flex gap-4">
      <div className="flex-shrink-0 w-10 h-10 rounded-lg bg-surface-container-highest 
                      flex items-center justify-center text-primary self-start mt-1">
        <Sparkles size={18} className="fill-primary animate-pulse" />
      </div>
      <div className="flex flex-col gap-2 max-w-[85%]">
        <div className="bg-surface-container-high text-on-surface p-4 rounded-xl rounded-bl-sm">
          <div className="markdown-content text-sm leading-relaxed font-body">
            <ReactMarkdown components={markdownComponents}>
              {content}
            </ReactMarkdown>
            <span className="inline-block w-0.5 h-4 bg-primary animate-pulse ml-0.5 rounded-full" />
          </div>
        </div>
      </div>
    </div>
  );
}
