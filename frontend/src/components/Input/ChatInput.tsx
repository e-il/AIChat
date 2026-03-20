import { useState, useRef, useEffect } from 'react';
import type { KeyboardEvent } from 'react';
import { ArrowUp, Paperclip, Mic, Brain, Layers } from 'lucide-react';

interface DropdownOption<T> {
  value: T;
  label: string;
}

interface ChatInputProps {
  onSend: (message: string) => void;
  disabled: boolean;
  models: DropdownOption<string>[];
  selectedModel: string;
  onModelChange: (model: string) => void;
  currentContextSize: number;
  onContextSizeChange: (size: number) => void;
}

// Format context size for display
function formatSize(size: number): string {
  if (size >= 1000000) return `${size / 1000000}M`;
  if (size >= 1000) return `${size / 1000}k`;
  return size.toString();
}

// Small icon button with hover dropdown
function IconDropdown<T>({ 
  icon, 
  options, 
  value, 
  onChange, 
  title,
  disabled 
}: { 
  icon: React.ReactNode;
  options: DropdownOption<T>[];
  value: T;
  onChange: (value: T) => void;
  title: string;
  disabled?: boolean;
}) {
  const [isOpen, setIsOpen] = useState(false);
  const dropdownRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (dropdownRef.current && !dropdownRef.current.contains(event.target as Node)) {
        setIsOpen(false);
      }
    }
    if (isOpen) {
      document.addEventListener('mousedown', handleClickOutside);
      return () => document.removeEventListener('mousedown', handleClickOutside);
    }
  }, [isOpen]);

  useEffect(() => {
    function handleEscape(event: KeyboardEvent) {
      if (event.key === 'Escape') setIsOpen(false);
    }
    if (isOpen) {
      document.addEventListener('keydown', handleEscape as unknown as EventListener);
      return () => document.removeEventListener('keydown', handleEscape as unknown as EventListener);
    }
  }, [isOpen]);

  return (
    <div ref={dropdownRef} className="relative">
      <button
        onClick={() => !disabled && setIsOpen(!isOpen)}
        disabled={disabled}
        title={title}
        className="p-1.5 text-on-surface-variant hover:text-primary hover:bg-surface-container 
                   rounded-lg transition-colors cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
      >
        {icon}
      </button>
      
      {isOpen && (
        <div 
          className="absolute bottom-full left-0 mb-2 min-w-[140px] w-max
                     bg-surface-container-lowest rounded-xl py-1 z-50"
          style={{
            boxShadow: '0 4px 24px rgba(0,0,0,0.12), 0 1px 4px rgba(0,0,0,0.08)',
            animation: 'dropdownFadeIn 150ms ease-out'
          }}
        >
          <div className="px-3 py-1.5 text-[0.625rem] font-bold text-on-surface-variant uppercase tracking-wider">
            {title}
          </div>
          {options.map((option, index) => {
            const isSelected = option.value === value;
            return (
              <button
                key={index}
                onClick={() => {
                  onChange(option.value);
                  setIsOpen(false);
                }}
                className={`
                  w-full text-left px-3 py-1.5 text-sm
                  flex items-center gap-2
                  transition-colors duration-75
                  cursor-pointer
                  ${isSelected 
                    ? 'bg-primary/10 text-primary' 
                    : 'text-on-surface hover:bg-surface-container-high'
                  }
                `}
              >
                <span 
                  className={`w-0.5 h-3.5 rounded-full transition-colors
                    ${isSelected ? 'bg-primary' : 'bg-transparent'}`} 
                />
                <span className={isSelected ? 'font-semibold' : 'font-medium'}>
                  {option.label}
                </span>
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}

// Context size slider popup - compact version
function ContextSlider({ 
  icon, 
  value, 
  onChange, 
  disabled 
}: { 
  icon: React.ReactNode;
  value: number;
  onChange: (value: number) => void;
  disabled?: boolean;
}) {
  const [isOpen, setIsOpen] = useState(false);
  const popupRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (popupRef.current && !popupRef.current.contains(event.target as Node)) {
        setIsOpen(false);
      }
    }
    if (isOpen) {
      document.addEventListener('mousedown', handleClickOutside);
      return () => document.removeEventListener('mousedown', handleClickOutside);
    }
  }, [isOpen]);

  const handleSliderChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    onChange(parseInt(e.target.value, 10));
  };

  // Calculate percentage for gradient
  const percentage = (value / 1000000) * 100;

  return (
    <div ref={popupRef} className="relative">
      <button
        onClick={() => !disabled && setIsOpen(!isOpen)}
        disabled={disabled}
        title="Context Size"
        className="p-1.5 text-on-surface-variant hover:text-primary hover:bg-surface-container 
                   rounded-lg transition-colors cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
      >
        {icon}
      </button>
      
      {isOpen && (
        <div 
          className="absolute bottom-full left-0 mb-2 w-48
                     bg-surface-container-lowest rounded-lg px-3 py-2 z-50"
          style={{
            boxShadow: '0 4px 24px rgba(0,0,0,0.12), 0 1px 4px rgba(0,0,0,0.08)',
            animation: 'dropdownFadeIn 150ms ease-out'
          }}
        >
          <div className="flex items-center gap-3">
            <input
              type="range"
              min="0"
              max="1000000"
              step="25000"
              value={value}
              onChange={handleSliderChange}
              className="flex-1 h-1 rounded-full appearance-none cursor-pointer"
              style={{
                background: `linear-gradient(to right, #0053dc ${percentage}%, #e3e9ed ${percentage}%)`
              }}
            />
            <span className="text-xs font-semibold text-primary min-w-[32px] text-right">
              {formatSize(value)}
            </span>
          </div>
        </div>
      )}
    </div>
  );
}

export function ChatInput({ 
  onSend, 
  disabled, 
  models, 
  selectedModel, 
  onModelChange,
  currentContextSize,
  onContextSizeChange
}: ChatInputProps) {
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
    <div className="p-4 md:px-8 pb-6 w-full max-w-4xl mx-auto z-20">
      <div className="bg-surface-container-lowest glass-effect rounded-2xl p-2 
                      shadow-xl border border-outline-variant/10">
        {/* Text Area with Controls */}
        <div className="relative flex items-end gap-2">
          {/* Left Controls - Model & Context */}
          <div className="flex items-center gap-1 pb-1.5 pl-1">
            <IconDropdown
              icon={<Brain size={16} />}
              options={models}
              value={selectedModel}
              onChange={onModelChange}
              title="Model"
              disabled={disabled}
            />
            <ContextSlider
              icon={<Layers size={16} />}
              value={currentContextSize}
              onChange={onContextSizeChange}
              disabled={disabled}
            />
          </div>
          
          <textarea
            ref={textareaRef}
            value={message}
            onChange={(e) => setMessage(e.target.value)}
            onKeyDown={handleKeyDown}
            placeholder="Message AIChat..."
            disabled={disabled}
            rows={1}
            className="flex-1 bg-transparent border-none focus:ring-0 focus:outline-none
                       text-sm font-body px-2 py-2 min-h-[40px] max-h-[200px] resize-none 
                       text-on-surface placeholder:text-on-surface-variant/50
                       disabled:opacity-50 disabled:cursor-not-allowed"
          />
          
          {/* Right Controls */}
          <div className="flex items-center gap-1 pb-1.5 pr-1">
            <button className="p-1.5 text-on-surface-variant hover:text-primary transition-colors 
                               rounded-lg hover:bg-surface-container cursor-pointer">
              <Paperclip size={16} />
            </button>
            <button className="p-1.5 text-on-surface-variant hover:text-primary transition-colors 
                               rounded-lg hover:bg-surface-container cursor-pointer">
              <Mic size={16} />
            </button>
            <button
              onClick={handleSubmit}
              disabled={disabled || !message.trim()}
              className="w-8 h-8 bg-primary text-on-primary rounded-lg 
                         flex items-center justify-center transition-all 
                         hover:bg-primary-dim hover:scale-105 active:scale-95 
                         shadow-md shadow-primary/20
                         disabled:bg-surface-container-high disabled:text-on-surface-variant
                         disabled:shadow-none disabled:scale-100 disabled:cursor-not-allowed cursor-pointer"
            >
              <ArrowUp size={16} strokeWidth={2.5} />
            </button>
          </div>
        </div>
      </div>
      
      <p className="text-center mt-3 text-[0.625rem] text-on-surface-variant font-medium">
        AIChat can make mistakes. Verify important information.
      </p>
    </div>
  );
}
