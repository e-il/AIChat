import { useState } from 'react';
import { Key, AlertCircle, Bot } from 'lucide-react';

interface AuthCodeModalProps {
  onSubmit: (code: string) => Promise<boolean>;
}

export function AuthCodeModal({ onSubmit }: AuthCodeModalProps) {
  const [code, setCode] = useState('');
  const [error, setError] = useState('');
  const [isLoading, setIsLoading] = useState(false);

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!code.trim()) {
      setError('Please enter an authentication code');
      return;
    }

    setIsLoading(true);
    setError('');

    try {
      const isValid = await onSubmit(code.trim());
      if (!isValid) {
        setError('Invalid authentication code');
      }
    } catch {
      setError('Failed to validate code. Please try again.');
    } finally {
      setIsLoading(false);
    }
  };

  return (
    <div className="fixed inset-0 bg-black/40 backdrop-blur-sm flex items-center justify-center z-50 p-4">
      <div className="bg-surface-container-lowest rounded-3xl shadow-2xl max-w-md w-full overflow-hidden">
        {/* Header - Ethereal Gradient */}
        <div className="bg-gradient-to-br from-primary to-primary-container px-6 py-8 flex flex-col items-center gap-4 text-center">
          <div className="w-16 h-16 bg-white/20 rounded-2xl flex items-center justify-center backdrop-blur-sm">
            <Bot size={32} className="text-white" />
          </div>
          <div>
            <h2 className="text-xl font-bold text-white font-headline">Welcome to AIChat</h2>
            <p className="text-sm text-white/80 mt-1 font-body">Enter your access code to continue</p>
          </div>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} className="p-6">
          <div className="mb-5">
            <label htmlFor="authCode" className="block text-sm font-semibold text-on-surface mb-2 font-body">
              Authentication Code
            </label>
            <div className="relative">
              <Key size={18} className="absolute left-4 top-1/2 -translate-y-1/2 text-on-surface-variant" />
              <input
                type="password"
                id="authCode"
                value={code}
                onChange={(e) => setCode(e.target.value)}
                placeholder="Enter your code"
                className="w-full pl-11 pr-4 py-3 bg-surface-container-high rounded-xl
                           border-2 border-transparent
                           focus:outline-none focus:border-primary focus:bg-surface-container-low
                           text-on-surface placeholder-on-surface-variant/50 text-sm font-body
                           transition-all"
                autoFocus
                disabled={isLoading}
              />
            </div>
          </div>

          {error && (
            <div className="mb-5 flex items-center gap-2 text-error text-sm bg-error/10 px-4 py-3 rounded-xl">
              <AlertCircle size={16} />
              <span className="font-medium">{error}</span>
            </div>
          )}

          <button
            type="submit"
            disabled={isLoading}
            className="w-full py-3 bg-primary hover:bg-primary-dim text-on-primary font-semibold
                       text-sm rounded-full transition-all disabled:opacity-50 disabled:cursor-not-allowed
                       cursor-pointer shadow-lg shadow-primary/20 hover:shadow-xl hover:shadow-primary/30
                       active:scale-[0.98]"
          >
            {isLoading ? 'Verifying...' : 'Continue'}
          </button>
        </form>
      </div>
    </div>
  );
}
