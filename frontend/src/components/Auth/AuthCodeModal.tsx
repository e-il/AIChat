import { useState } from 'react';
import { Key, AlertCircle, ArrowRight } from 'lucide-react';
import { Dialog } from '../Common/Dialog';

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
    <Dialog open label="Sign in to AIChat" className="max-w-[420px]">
      <div className="bg-surface-container-lowest w-full overflow-hidden">
        {/* Header - Ethereal Gradient */}
        <div className="bg-[#272b27] px-8 py-8 flex flex-col items-start gap-6">
          <img src="/favicon.svg" alt="" className="w-10 h-10" />
          <div>
            <h2 className="text-3xl font-medium text-white font-headline">AIChat<span className="text-[#ef846e]">.</span></h2>
            <p className="text-sm text-white/60 mt-2 font-body">Your personal workspace</p>
          </div>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} className="p-8">
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
            <div role="alert" className="mb-5 flex items-center gap-2 text-error text-sm bg-error/10 px-4 py-3 rounded-md">
              <AlertCircle size={16} />
              <span className="font-medium">{error}</span>
            </div>
          )}

          <button
            type="submit"
            disabled={isLoading}
            className="w-full py-3 bg-primary hover:bg-primary-dim text-on-primary font-semibold
                       text-sm rounded-md flex items-center justify-center gap-3 transition-colors disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {isLoading ? 'Verifying...' : 'Continue'}
            {!isLoading && <ArrowRight size={16} />}
          </button>
        </form>
      </div>
    </Dialog>
  );
}
