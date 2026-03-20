import ReactMarkdown from 'react-markdown';
import { Prism as SyntaxHighlighter } from 'react-syntax-highlighter';
import { vs } from 'react-syntax-highlighter/dist/esm/styles/prism';
import { Copy, Check } from 'lucide-react';
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
    <div className="relative group my-3 border border-neutral-200 overflow-hidden">
      {/* Header */}
      <div className="flex items-center justify-between px-3 py-1 bg-neutral-100 border-b border-neutral-200">
        <span className="text-xs text-neutral-500 font-medium">
          {language || 'code'}
        </span>
        <button
          onClick={handleCopy}
          className="flex items-center gap-1 px-1.5 py-0.5 text-xs text-neutral-500 
                     hover:text-neutral-700 hover:bg-neutral-200 rounded transition-colors cursor-pointer"
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
          lineHeight: '1.5',
          background: '#FAFAFA',
        }}
        showLineNumbers={children.split('\n').length > 3}
        lineNumberStyle={{ color: '#999', paddingRight: '1rem' }}
      >
        {children.trim()}
      </SyntaxHighlighter>
    </div>
  );
}

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

  if (isUser) {
    return (
      <div className="flex justify-end mb-3">
        <div className="max-w-[70%] px-3 py-2 bg-neutral-200/70 text-neutral-800 rounded">
          <p className="text-sm whitespace-pre-wrap leading-relaxed">{message.content}</p>
        </div>
      </div>
    );
  }

  // AI message - full width, white card style
  return (
    <div className="mb-3 group">
      <div className="bg-white border border-neutral-200 px-3 py-2 rounded">
        <div className="markdown-content text-sm leading-relaxed text-neutral-800">
          <ReactMarkdown
            components={{
            code({ className, children, ...props }) {
              const match = /language-(\w+)/.exec(className || '');
              const isInline = !match && !String(children).includes('\n');
              
              if (isInline) {
                return (
                  <code className="px-1.5 py-0.5 bg-neutral-100 text-primary-600 rounded text-[13px] font-mono" {...props}>
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
              return <p className="my-2 first:mt-0 last:mb-0">{children}</p>;
            },
            h1({ children }) {
              return <h1 className="text-xl font-semibold text-neutral-900 mt-4 mb-2 first:mt-0">{children}</h1>;
            },
            h2({ children }) {
              return <h2 className="text-lg font-semibold text-neutral-900 mt-4 mb-2 first:mt-0">{children}</h2>;
            },
            h3({ children }) {
              return <h3 className="text-base font-semibold text-neutral-900 mt-3 mb-2 first:mt-0">{children}</h3>;
            },
            ul({ children }) {
              return <ul className="list-disc pl-5 my-2 space-y-1">{children}</ul>;
            },
            ol({ children }) {
              return <ol className="list-decimal pl-5 my-2 space-y-1">{children}</ol>;
            },
            li({ children }) {
              return <li className="text-neutral-700">{children}</li>;
            },
            blockquote({ children }) {
              return (
                <blockquote className="border-l-4 border-primary-500 pl-4 py-1 my-2 bg-primary-50/50 text-neutral-600 italic">
                  {children}
                </blockquote>
              );
            },
            a({ href, children }) {
              return (
                <a href={href} target="_blank" rel="noopener noreferrer" 
                   className="text-primary-600 hover:underline">
                  {children}
                </a>
              );
            },
            table({ children }) {
              return (
                <div className="overflow-x-auto my-3">
                  <table className="min-w-full border border-neutral-200 rounded">
                    {children}
                  </table>
                </div>
              );
            },
            th({ children }) {
              return (
                <th className="px-3 py-2 bg-neutral-100 text-left text-sm font-semibold text-neutral-800 border-b border-neutral-200">
                  {children}
                </th>
              );
            },
            td({ children }) {
              return (
                <td className="px-3 py-2 text-sm text-neutral-700 border-b border-neutral-100">
                  {children}
                </td>
              );
            },
            hr() {
              return <hr className="my-4 border-neutral-200" />;
            },
            strong({ children }) {
              return <strong className="font-semibold text-neutral-900">{children}</strong>;
            },
            em({ children }) {
              return <em className="italic">{children}</em>;
            },
          }}
        >
          {message.content}
        </ReactMarkdown>
        </div>
      </div>
      {/* Copy button - bottom left, outside card */}
      <button
        onClick={handleCopyMessage}
        className="mt-1 p-1 rounded text-neutral-400 hover:text-neutral-600 hover:bg-neutral-100
                   opacity-0 group-hover:opacity-100 transition-opacity cursor-pointer"
      >
        {copied ? <Check size={14} /> : <Copy size={14} />}
      </button>
    </div>
  );
}

interface StreamingBubbleProps {
  content: string;
}

export function StreamingBubble({ content }: StreamingBubbleProps) {
  return (
    <div className="mb-3">
      <div className="bg-white border border-neutral-200 px-3 py-2 rounded">
        <div className="markdown-content text-sm leading-relaxed text-neutral-800">
          <ReactMarkdown
          components={{
            code({ className, children, ...props }) {
              const match = /language-(\w+)/.exec(className || '');
              const isInline = !match && !String(children).includes('\n');
              
              if (isInline) {
                return (
                  <code className="px-1.5 py-0.5 bg-neutral-100 text-primary-600 rounded text-[13px] font-mono" {...props}>
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
              return <p className="my-2 first:mt-0 last:mb-0">{children}</p>;
            },
          }}
        >
          {content}
        </ReactMarkdown>
        <span className="inline-block w-0.5 h-4 bg-primary-500 animate-pulse ml-0.5" />
        </div>
      </div>
    </div>
  );
}
