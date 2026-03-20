import { useState, useRef, useEffect } from 'react';
import { ChevronDown } from 'lucide-react';

interface DropdownOption<T> {
  value: T;
  label: string;
}

interface FluentDropdownProps<T> {
  options: DropdownOption<T>[];
  value: T;
  onChange: (value: T) => void;
  disabled?: boolean;
  title?: string;
  className?: string;
}

export function FluentDropdown<T>({
  options,
  value,
  onChange,
  disabled = false,
  title,
  className = '',
}: FluentDropdownProps<T>) {
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

  return (
    <div ref={dropdownRef} className={`relative ${className}`}>
      {/* Trigger Button - Fluent Design Style */}
      <button
        onClick={() => !disabled && setIsOpen(!isOpen)}
        disabled={disabled}
        title={title}
        className={`
          flex items-center gap-1.5 h-8 px-3 text-sm font-normal
          bg-white hover:bg-neutral-50 active:bg-neutral-100
          border border-neutral-300 hover:border-neutral-400
          rounded shadow-sm
          transition-all duration-150 ease-out
          cursor-pointer select-none
          disabled:opacity-50 disabled:cursor-not-allowed disabled:hover:bg-white disabled:hover:border-neutral-300
          focus:outline-none focus:ring-2 focus:ring-blue-500/40 focus:border-blue-500
        `}
      >
        <span className="text-neutral-800">{selectedOption?.label}</span>
        <ChevronDown 
          size={14} 
          className={`text-neutral-500 transition-transform duration-200 ${isOpen ? 'rotate-180' : ''}`} 
        />
      </button>
      
      {/* Dropdown Menu - Fluent Design Style */}
      {isOpen && (
        <div 
          className="
            absolute right-0 mt-1 min-w-full w-max
            bg-white rounded-md
            border border-neutral-200
            py-1 z-50
            origin-top-right
          "
          style={{
            boxShadow: '0 3px 12px rgba(0,0,0,0.15), 0 1px 2px rgba(0,0,0,0.08)',
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
                  w-full text-left px-3 py-1.5 text-sm
                  flex items-center gap-2
                  transition-colors duration-75
                  cursor-pointer
                  ${isSelected 
                    ? 'bg-blue-50 text-blue-700' 
                    : 'text-neutral-700 hover:bg-neutral-100'
                  }
                `}
              >
                {/* Selection indicator - Fluent accent bar */}
                <span 
                  className={`
                    w-0.5 h-4 rounded-full transition-colors
                    ${isSelected ? 'bg-blue-600' : 'bg-transparent'}
                  `} 
                />
                <span className={isSelected ? 'font-medium' : 'font-normal'}>
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
