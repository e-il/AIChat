import { Sparkles } from 'lucide-react';

export function TypingIndicator() {
  return (
    <div className="flex gap-4">
      <div className="flex-shrink-0 w-10 h-10 rounded-lg bg-surface-container-highest 
                      flex items-center justify-center text-primary self-start mt-1">
        <Sparkles size={18} className="fill-primary animate-pulse" />
      </div>
      <div className="flex flex-col gap-2 max-w-[85%]">
        <div className="bg-surface-container-high text-on-surface p-4 rounded-xl rounded-bl-sm">
          <div className="flex gap-1.5">
            <span className="w-2 h-2 bg-primary/60 rounded-full animate-bounce [animation-delay:-0.3s]" />
            <span className="w-2 h-2 bg-primary/60 rounded-full animate-bounce [animation-delay:-0.15s]" />
            <span className="w-2 h-2 bg-primary/60 rounded-full animate-bounce" />
          </div>
        </div>
      </div>
    </div>
  );
}
