import { useState, useRef, useEffect } from 'react';
import type { KeyboardEvent } from 'react';
import { ArrowUp, Paperclip, Mic, Bot, Layers, X, Loader2 } from 'lucide-react';
import type { MessageAttachment } from '../../types';
import { imagesApi } from '../../services/imagesApi';

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
  pendingAttachments: MessageAttachment[];
  onAttachmentsChange: React.Dispatch<React.SetStateAction<MessageAttachment[]>>;
  onAuthError?: () => void;
}

interface UploadingItem {
  id: string;          // temp id used for chip key
  file: File;
  previewUrl: string;  // object URL for instant thumbnail
}

const ACCEPTED_IMAGE_TYPES = 'image/png,image/jpeg,image/jpg,image/webp,image/gif';
const MAX_FILE_BYTES = 10 * 1024 * 1024;

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

interface AttachedChipProps {
  src: string;
  uploading?: boolean;
  onRemove: () => void;
}

function AttachedChip({ src, uploading, onRemove }: AttachedChipProps) {
  return (
    <div className="relative w-14 h-14 rounded-lg overflow-hidden border border-outline-variant/30 bg-surface-container-high group">
      <img src={src} alt="" className="w-full h-full object-cover" />
      {uploading && (
        <div className="absolute inset-0 flex items-center justify-center bg-black/40">
          <Loader2 size={16} className="text-white animate-spin" />
        </div>
      )}
      <button
        onClick={onRemove}
        title="Remove"
        className="absolute top-0.5 right-0.5 w-4 h-4 rounded-full bg-black/60 text-white
                   flex items-center justify-center hover:bg-black/80 transition-colors cursor-pointer"
      >
        <X size={10} strokeWidth={3} />
      </button>
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
  onContextSizeChange,
  pendingAttachments,
  onAttachmentsChange,
  onAuthError,
}: ChatInputProps) {
  const [message, setMessage] = useState('');
  const [uploading, setUploading] = useState<UploadingItem[]>([]);
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  const isUploading = uploading.length > 0;
  const canSend =
    !disabled && !isUploading && (message.trim().length > 0 || pendingAttachments.length > 0);

  const handleSubmit = () => {
    if (!canSend) return;
    onSend(message.trim());
    setMessage('');
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

  // Revoke object URLs when uploads finish to avoid memory leaks.
  useEffect(() => {
    return () => {
      uploading.forEach(u => URL.revokeObjectURL(u.previewUrl));
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleFilesPicked = async (files: FileList | null) => {
    if (!files || files.length === 0) return;

    const items: UploadingItem[] = [];
    for (const file of Array.from(files)) {
      if (!file.type.startsWith('image/')) {
        console.warn(`Skipping non-image file: ${file.name}`);
        continue;
      }
      if (file.size > MAX_FILE_BYTES) {
        console.warn(`Skipping oversized file: ${file.name} (${file.size} bytes)`);
        continue;
      }
      items.push({
        id: crypto.randomUUID(),
        file,
        previewUrl: URL.createObjectURL(file),
      });
    }

    if (items.length === 0) return;
    setUploading(prev => [...prev, ...items]);

    // Upload each file independently; replace the chip with the real attachment as it lands.
    // Functional setState avoids the stale-closure bug when multiple uploads complete out of order.
    await Promise.all(items.map(async item => {
      try {
        const attachment = await imagesApi.upload(item.file);
        onAttachmentsChange(prev => [...prev, attachment]);
      } catch (err) {
        console.error('Upload failed:', err);
        if (err instanceof Error && err.message === 'AUTH_REQUIRED') {
          onAuthError?.();
        }
      } finally {
        setUploading(prev => prev.filter(u => u.id !== item.id));
        URL.revokeObjectURL(item.previewUrl);
      }
    }));
  };

  const handleRemoveAttachment = (id: string) => {
    onAttachmentsChange(prev => prev.filter(a => a.id !== id));
  };

  const handleCancelUpload = (id: string) => {
    setUploading(prev => {
      const found = prev.find(u => u.id === id);
      if (found) URL.revokeObjectURL(found.previewUrl);
      return prev.filter(u => u.id !== id);
    });
  };

  const showAttachmentRow = pendingAttachments.length > 0 || uploading.length > 0;

  return (
    <div className="p-4 md:px-8 pb-6 w-full max-w-4xl mx-auto z-20">
      <div className="bg-surface-container-lowest glass-effect rounded-2xl p-2
                      shadow-xl border border-outline-variant/10">
        {/* Pending attachments / uploads */}
        {showAttachmentRow && (
          <div className="flex flex-wrap gap-2 px-2 pt-1 pb-2">
            {pendingAttachments.map(a => (
              <AttachedChip
                key={a.id}
                src={imagesApi.buildAuthedUrl(a.url)}
                onRemove={() => handleRemoveAttachment(a.id)}
              />
            ))}
            {uploading.map(u => (
              <AttachedChip
                key={u.id}
                src={u.previewUrl}
                uploading
                onRemove={() => handleCancelUpload(u.id)}
              />
            ))}
          </div>
        )}

        {/* Text Area with Controls */}
        <div className="relative flex items-end gap-2">
          {/* Left Controls - Model & Context */}
          <div className="flex items-center gap-1 pb-1.5 pl-1">
            <IconDropdown
              icon={<Bot size={16} />}
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
            <input
              ref={fileInputRef}
              type="file"
              accept={ACCEPTED_IMAGE_TYPES}
              multiple
              className="hidden"
              onChange={(e) => {
                handleFilesPicked(e.target.files);
                e.target.value = ''; // allow re-selecting the same file
              }}
            />
            <button
              type="button"
              onClick={() => fileInputRef.current?.click()}
              disabled={disabled}
              title="Attach image"
              className="p-1.5 text-on-surface-variant hover:text-primary transition-colors
                         rounded-lg hover:bg-surface-container cursor-pointer
                         disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <Paperclip size={16} />
            </button>
            <button
              type="button"
              disabled
              className="p-1.5 text-on-surface-variant hover:text-primary transition-colors
                         rounded-lg hover:bg-surface-container cursor-pointer
                         disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <Mic size={16} />
            </button>
            <button
              onClick={handleSubmit}
              disabled={!canSend}
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
