import { useState, useRef, useEffect } from 'react';
import type { KeyboardEvent } from 'react';
import { ArrowUp, Paperclip, Layers, X, Loader2 } from 'lucide-react';
import type { MessageAttachment } from '../../types';
import { imagesApi } from '../../services/imagesApi';
import { Dropdown } from '../Common/Dropdown';

interface DropdownOption<T> {
  value: T;
  label: string;
}

interface ChatInputProps {
  message: string;
  onMessageChange: (message: string) => void;
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
  placeholder?: string;
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

function useImeEnterGuard() {
  const isComposingRef = useRef(false);
  const justEndedComposingRef = useRef(false);
  const compositionEndTimeoutRef = useRef<number | null>(null);

  const clearCompositionEndProtection = () => {
    if (compositionEndTimeoutRef.current !== null) {
      window.clearTimeout(compositionEndTimeoutRef.current);
      compositionEndTimeoutRef.current = null;
    }
  };

  useEffect(() => {
    return () => clearCompositionEndProtection();
  }, []);

  return {
    compositionHandlers: {
      onCompositionStart: () => {
        clearCompositionEndProtection();
        isComposingRef.current = true;
        justEndedComposingRef.current = false;
      },
      onCompositionEnd: () => {
        isComposingRef.current = false;
        justEndedComposingRef.current = true;
        clearCompositionEndProtection();
        compositionEndTimeoutRef.current = window.setTimeout(() => {
          justEndedComposingRef.current = false;
          compositionEndTimeoutRef.current = null;
        }, 0);
      },
    },
    shouldSkipEnterForIme: (e: KeyboardEvent<HTMLTextAreaElement>) => {
      if (isComposingRef.current || e.nativeEvent.isComposing) return true;

      if (justEndedComposingRef.current) {
        e.preventDefault();
        return true;
      }

      return false;
    },
  };
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
        aria-label="Context size"
        aria-expanded={isOpen}
        className="p-1.5 text-on-surface-variant hover:text-primary hover:bg-surface-container
                   rounded-lg transition-colors cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed"
      >
        {icon}
      </button>

      {isOpen && (
        <div
          className="absolute bottom-full right-0 mb-2 w-48
                     bg-surface-container-lowest rounded-lg px-3 py-2 z-50"
          style={{
            boxShadow: '0 4px 24px rgba(0,0,0,0.12), 0 1px 4px rgba(0,0,0,0.08)',
            animation: 'dropdownFadeIn 150ms ease-out'
          }}
        >
          <div className="flex items-center gap-3">
            <input
              type="range"
              aria-label="Context size"
              min="0"
              max="1000000"
              step="25000"
              value={value}
              onChange={handleSliderChange}
              className="min-w-0 flex-1 h-1 rounded-full appearance-none cursor-pointer"
              style={{
                background: `linear-gradient(to right, var(--color-primary) ${percentage}%, var(--color-surface-container-high) ${percentage}%)`
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
  message,
  onMessageChange: setMessage,
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
  placeholder = 'Message AIChat...',
}: ChatInputProps) {
  const [uploading, setUploading] = useState<UploadingItem[]>([]);
  const [uploadError, setUploadError] = useState('');
  const activeUploads = useRef(new Map<string, string>());
  const textareaRef = useRef<HTMLTextAreaElement>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const { compositionHandlers, shouldSkipEnterForIme } = useImeEnterGuard();

  const isUploading = uploading.length > 0;
  const canSend =
    !disabled && !!selectedModel && !isUploading && (message.trim().length > 0 || pendingAttachments.length > 0);

  const handleSubmit = () => {
    if (!canSend) return;
    onSend(message.trim());
    setMessage('');
  };

  const handleKeyDown = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key !== 'Enter' || e.shiftKey) return;

    if (shouldSkipEnterForIme(e)) return;

    e.preventDefault();
    handleSubmit();
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
    const uploads = activeUploads.current;
    return () => {
      uploads.forEach(url => URL.revokeObjectURL(url));
      uploads.clear();
    };
  }, []);

  const handleFilesPicked = async (files: FileList | null) => {
    if (!files || files.length === 0) return;
    setUploadError('');

    const items: UploadingItem[] = [];
    for (const file of Array.from(files)) {
      if (!ACCEPTED_IMAGE_TYPES.split(',').includes(file.type)) {
        setUploadError('Choose a PNG, JPEG, WebP or GIF image.');
        continue;
      }
      if (file.size > MAX_FILE_BYTES) {
        setUploadError('Each image must be 10 MB or smaller.');
        continue;
      }
      items.push({
        id: crypto.randomUUID(),
        file,
        previewUrl: URL.createObjectURL(file),
      });
    }

    if (items.length === 0) return;
    items.forEach(item => activeUploads.current.set(item.id, item.previewUrl));
    setUploading(prev => [...prev, ...items]);

    // Upload each file independently; replace the chip with the real attachment as it lands.
    // Functional setState avoids the stale-closure bug when multiple uploads complete out of order.
    await Promise.all(items.map(async item => {
      try {
        const attachment = await imagesApi.upload(item.file);
        if (activeUploads.current.has(item.id)) onAttachmentsChange(prev => [...prev, attachment]);
      } catch (err) {
        console.error('Upload failed:', err);
        if (activeUploads.current.has(item.id)) setUploadError('Image upload failed. Please try again.');
        if (err instanceof Error && err.message === 'AUTH_REQUIRED') {
          onAuthError?.();
        }
      } finally {
        setUploading(prev => prev.filter(u => u.id !== item.id));
        URL.revokeObjectURL(item.previewUrl);
        activeUploads.current.delete(item.id);
      }
    }));
  };

  const handleRemoveAttachment = (id: string) => {
    onAttachmentsChange(prev => prev.filter(a => a.id !== id));
  };

  const handleCancelUpload = (id: string) => {
    const url = activeUploads.current.get(id);
    if (url) URL.revokeObjectURL(url);
    activeUploads.current.delete(id);
    setUploading(prev => prev.filter(item => item.id !== id));
  };

  const showAttachmentRow = pendingAttachments.length > 0 || uploading.length > 0;

  return (
    <div className="composer shrink-0 px-4 md:px-10 pb-4 pt-3 w-full max-w-[880px] mx-auto z-20">
      {uploadError && <p role="alert" className="text-xs text-error mb-2">{uploadError}</p>}
      <div className="bg-white rounded-lg p-3 border border-outline-variant/40 shadow-[0_4px_24px_#25282306] focus-within:border-primary/50">
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
        <div className="relative flex flex-wrap min-w-0 items-end gap-2">
          {/* Left Controls - Model & Context */}
          <div className="order-2 flex min-w-0 flex-1 items-center gap-1">
            <Dropdown
              placement="top"
              className="min-w-0 max-w-[calc(100%-36px)]"
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
            id="chat-message"
            aria-label="Message"
            ref={textareaRef}
            value={message}
            onChange={(e) => setMessage(e.target.value)}
            {...compositionHandlers}
            onKeyDown={handleKeyDown}
            placeholder={placeholder}
            disabled={disabled}
            rows={1}
            className="order-1 w-full min-w-0 overflow-y-auto break-words bg-transparent border-none focus:ring-0 focus:outline-none
                       text-sm font-body px-2 py-2 min-h-[64px] max-h-[200px] resize-none
                       text-on-surface placeholder:text-on-surface-variant/50
                       disabled:opacity-50 disabled:cursor-not-allowed"
          />

          {/* Right Controls */}
          <div className="order-3 flex shrink-0 items-center gap-2">
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
              onClick={handleSubmit}
              title="Send message"
              aria-label="Send message"
              disabled={!canSend}
              className="w-9 h-9 bg-primary text-on-primary rounded-md
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
