import { useEffect, useRef } from 'react';
import type { ReactNode } from 'react';

export function Dialog({ open, onClose, label, className = '', children }: {
  open: boolean;
  onClose?: () => void;
  label: string;
  className?: string;
  children: ReactNode;
}) {
  const dialogRef = useRef<HTMLDialogElement>(null);

  useEffect(() => {
    const dialog = dialogRef.current;
    if (open) dialog?.showModal();
    else dialog?.close();
    return () => dialog?.close();
  }, [open]);

  return (
    <dialog ref={dialogRef} aria-label={label} className={`app-dialog ${className}`}
      onCancel={event => { event.preventDefault(); onClose?.(); }}
      onClick={event => { if (event.target === event.currentTarget) onClose?.(); }}>
      {children}
    </dialog>
  );
}