import { useState, useRef, useEffect } from 'react';
import type { ReactNode } from 'react';
import { ChevronDown } from 'lucide-react';

export interface DropdownOption<T> {
  value: T;
  label: string;
}

interface DropdownProps<T> {
  options: DropdownOption<T>[];
  value: T;
  onChange: (value: T) => void;
  disabled?: boolean;
  title?: string;
  className?: string;
  icon?: ReactNode;
  placement?: 'top' | 'bottom';
}

export function Dropdown<T>({
  options,
  value,
  onChange,
  disabled = false,
  title,
  className = '',
  icon,
  placement = 'bottom',
}: DropdownProps<T>) {
  const [isOpen, setIsOpen] = useState(false);
  const dropdownRef = useRef<HTMLDivElement>(null);

  const selectedOption = options.find(o => o.value === value);

  // Close on click outside
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

  // Close on escape
  useEffect(() => {
    function handleEscape(event: KeyboardEvent) {
      if (event.key === 'Escape') setIsOpen(false);
    }
    if (isOpen) {
      document.addEventListener('keydown', handleEscape);
      return () => document.removeEventListener('keydown', handleEscape);
    }
  }, [isOpen]);

  // Icon-only mode when icon is provided
  const isIconOnly = !!icon;

  return (
    <div ref={dropdownRef} className={`relative ${className}`}>
      {/* Trigger Button */}
      <button
        onClick={() => !disabled && setIsOpen(!isOpen)}
        disabled={disabled}
        title={title}
        aria-label={title}
        aria-expanded={isOpen}
        className={`
          flex items-center justify-center gap-2 text-[0.75rem] font-semibold
          ${isIconOnly 
            ? 'p-2 hover:bg-surface-container rounded-lg' 
            : 'px-3 py-2 border border-outline-variant/30 hover:bg-surface-container rounded-md max-w-full'
          }
          transition-all duration-150 ease-out
          cursor-pointer select-none group
          disabled:opacity-50 disabled:cursor-not-allowed disabled:hover:bg-surface-container-high
          focus:outline-none focus:ring-2 focus:ring-primary/30
          ${isIconOnly ? 'text-on-surface-variant hover:text-primary' : ''}
        `}
      >
        {isIconOnly ? (
          icon
        ) : (
          <>
            <span className="text-on-surface truncate">{selectedOption?.label || title}</span>
            <ChevronDown 
              size={14} 
              className={`text-on-surface-variant transition-transform duration-200 group-hover:translate-y-0.5 ${isOpen ? 'rotate-180' : ''}`} 
            />
          </>
        )}
      </button>
      
      {/* Dropdown Menu */}
      {isOpen && (
        <div 
          className={`absolute ${placement === 'top' ? 'bottom-full left-0 mb-2' : 'right-0 mt-2'} min-w-[160px] w-max max-w-[min(320px,calc(100vw-48px))] max-h-72 overflow-y-auto border border-outline-variant/25 bg-surface-container-lowest rounded-md py-1 z-50`}
          style={{
            boxShadow: '0 4px 24px rgba(0,0,0,0.12), 0 1px 4px rgba(0,0,0,0.08)',
            animation: 'dropdownFadeIn 150ms ease-out'
          }}
        >
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
                  w-full text-left px-4 py-2 text-sm
                  flex items-center gap-2
                  transition-colors duration-75
                  cursor-pointer
                  ${isSelected 
                    ? 'bg-primary/10 text-primary' 
                    : 'text-on-surface hover:bg-surface-container-high'
                  }
                `}
              >
                {/* Selection indicator */}
                <span 
                  className={`
                    w-0.5 h-4 rounded-full transition-colors
                    ${isSelected ? 'bg-primary' : 'bg-transparent'}
                  `} 
                />
                <span className={`min-w-0 break-words ${isSelected ? 'font-semibold' : 'font-medium'}`}>
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
