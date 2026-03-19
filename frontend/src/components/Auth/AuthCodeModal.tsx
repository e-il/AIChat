import { useState } from 'react';
import { Key, AlertCircle } from 'lucide-react';

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
    <div className="fixed inset-0 bg-black/50 flex items-center justify-center z-50 p-4">
      <div className="bg-white depth-64 max-w-md w-full">
        {/* Header - ADO Blue */}
        <div className="bg-primary-500 px-6 py-4 flex items-center gap-3">
          <Key size={24} className="text-white" />
          <div>
            <h2 className="text-lg font-semibold text-white">Authentication Required</h2>
            <p className="text-sm text-white/80">Enter your access code to continue</p>
          </div>
        </div>

        {/* Form */}
        <form onSubmit={handleSubmit} className="p-6">
          <div className="mb-4">
            <label htmlFor="authCode" className="block text-sm font-semibold text-neutral-700 mb-1">
              Authentication Code
            </label>
            <input
              type="password"
              id="authCode"
              value={code}
              onChange={(e) => setCode(e.target.value)}
              placeholder="Enter your code"
              className="w-full px-3 py-2 border border-neutral-300 
                         focus:outline-none focus:border-primary-500
                         text-neutral-800 placeholder-neutral-500 text-sm"
              autoFocus
              disabled={isLoading}
            />
          </div>

          {error && (
            <div className="mb-4 flex items-center gap-2 text-red-600 text-sm">
              <AlertCircle size={16} />
              <span>{error}</span>
            </div>
          )}

          <button
            type="submit"
            disabled={isLoading}
            className="w-full py-2 bg-primary-500 hover:bg-primary-600 text-white font-semibold
                       text-sm transition-colors disabled:opacity-50 disabled:cursor-not-allowed
                       cursor-pointer"
          >
            {isLoading ? 'Verifying...' : 'Continue'}
          </button>
        </form>
      </div>
    </div>
  );
}
