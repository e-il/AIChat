import { Sparkles } from 'lucide-react';

export function TypingIndicator() {
  return (
    <div className="flex gap-4" role="status" aria-label="Generating response">
      <div className="flex-shrink-0 w-8 h-8 rounded-md bg-[#272b27] flex items-center justify-center text-[#ef846e] self-start mt-1">
        <Sparkles size={16} className="animate-pulse" />
      </div>
      <div className="flex flex-col gap-2 max-w-[85%]">
        <div className="text-on-surface py-4">
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
