export function TypingIndicator() {
  return (
    <div className="flex justify-start mb-4">
      <div className="px-4 py-3 bg-white border border-black/10 rounded-[4px] rounded-bl-none depth-4">
        <div className="flex gap-1">
          <span className="w-2 h-2 bg-neutral-400 rounded-full animate-bounce [animation-delay:-0.3s]" />
          <span className="w-2 h-2 bg-neutral-400 rounded-full animate-bounce [animation-delay:-0.15s]" />
          <span className="w-2 h-2 bg-neutral-400 rounded-full animate-bounce" />
        </div>
      </div>
    </div>
  );
}
