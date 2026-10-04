import { useState } from 'react';
import { Plus, Trash2, X, Pencil, Loader2 } from 'lucide-react';
import type { ModelInfo } from '../../types';
import { chatApi } from '../../services/chatApi';
import { Dialog } from '../Common/Dialog';

export function ModelsPanel({ open, models, onClose, onChanged }: { open: boolean; models: ModelInfo[]; onClose: () => void; onChanged: () => void | Promise<void> }) {
  const [draft, setDraft] = useState({ id: '', name: '', deploymentName: '', kind: 'chat' });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  if (!open) return null;
  const mutate = async (operation: () => Promise<unknown>) => {
    setBusy(true);
    setError('');
    try { await operation(); await onChanged(); }
    catch (cause) { setError(cause instanceof Error ? cause.message : 'Unable to save changes.'); }
    finally { setBusy(false); }
  };
  const save = () => mutate(async () => {
    await chatApi.saveModel({ ...draft, id: draft.id.trim(), name: draft.name.trim(), deploymentName: draft.deploymentName.trim() });
    setDraft({ id: '', name: '', deploymentName: '', kind: 'chat' });
  });
  const remove = (id: string) => { if (window.confirm(`Delete ${id}?`)) void mutate(() => chatApi.deleteModel(id)); };
  return <Dialog open={open} onClose={busy ? undefined : onClose} label="Model catalog" className="max-w-2xl">
    <section className="w-full bg-white flex flex-col max-h-[85dvh]">
      <header className="p-6 flex items-center justify-between border-b border-outline-variant/25"><div><p className="text-[10px] text-primary mb-1">ADMINISTRATION</p><h2 className="font-headline text-xl font-medium">Model catalog</h2></div><button title="Close model catalog" onClick={onClose} disabled={busy} className="rounded-md p-2 hover:bg-surface-container"><X size={18}/></button></header>
      <div className="px-6 overflow-y-auto min-h-0 flex-1">{models.length === 0 && <p className="py-6 text-sm text-on-surface-variant">No models configured</p>}{models.map(model => <div key={model.id} className="flex gap-3 items-center justify-between border-b border-outline-variant/20 py-4"><div className="min-w-0"><p className="font-medium text-sm break-words">{model.name}</p><p className="text-xs text-on-surface-variant mt-1 break-all">{model.id} / {model.deploymentName}</p></div><div className="flex items-center shrink-0 gap-1"><span className="text-[10px] bg-surface-container px-2 py-1 rounded mr-1">{model.kind}</span><button title={`Edit ${model.name}`} disabled={busy} onClick={() => setDraft({ id: model.id, name: model.name, deploymentName: model.deploymentName, kind: model.kind ?? 'chat' })} className="p-2 text-on-surface-variant hover:text-primary"><Pencil size={15}/></button><button title={`Delete ${model.name}`} disabled={busy} onClick={() => remove(model.id)} className="p-2 text-error hover:bg-error/10 rounded"><Trash2 size={15}/></button></div></div>)}</div>
      <form className="p-6 border-t border-outline-variant/25 bg-surface-container-low shrink-0" onSubmit={event => { event.preventDefault(); if (!busy) void save(); }}>
        <h3 className="font-headline text-sm mb-4">{models.some(model => model.id === draft.id) ? 'Edit model' : 'Add a model'}</h3>
        <fieldset disabled={busy} className="grid grid-cols-2 gap-3 min-w-0">
          <label className="field-label">Model ID<input required value={draft.id} onChange={event => setDraft({...draft, id: event.target.value})}/></label>
          <label className="field-label">Display name<input required value={draft.name} onChange={event => setDraft({...draft, name: event.target.value})}/></label>
          <label className="field-label col-span-2">Azure deployment<input required value={draft.deploymentName} onChange={event => setDraft({...draft, deploymentName: event.target.value})}/></label>
          <label className="field-label">Type<select aria-label="Model type" value={draft.kind} onChange={event => setDraft({...draft, kind: event.target.value})}><option value="chat">Chat</option><option value="image">Image</option><option value="video">Video</option></select></label>
        </fieldset>
        {error && <p role="alert" className="text-error text-xs mt-3">{error}</p>}
        <button disabled={busy || !draft.id.trim() || !draft.name.trim() || !draft.deploymentName.trim()} className="mt-4 flex w-full items-center justify-center gap-2 rounded-md bg-primary py-2.5 text-sm font-semibold text-white hover:bg-primary-dim disabled:opacity-50">{busy ? <Loader2 size={16} className="animate-spin"/> : <Plus size={16}/>} Save model</button>
      </form>
    </section>
  </Dialog>;
}
