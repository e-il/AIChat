import { useEffect, useState } from 'react';
import { Lock, Plus, Save, Trash2, X } from 'lucide-react';
import type { PromptProfile } from '../../types';
import { DEFAULT_PROMPT_PROFILE_ID } from '../../services/promptProfiles';

interface PromptProfilesPanelProps {
  open: boolean;
  profiles: PromptProfile[];
  selectedProfileId: string;
  maxCustomSystemPromptLength: number;
  onClose: () => void;
  onSelectProfile: (profileId: string) => void;
  onSaveCustomProfile: (profile: PromptProfile) => void;
  onDeleteCustomProfile: (profileId: string) => void;
}

function createCustomProfile(): PromptProfile {
  return {
    id: crypto.randomUUID(),
    name: 'New profile',
    description: '',
    systemPrompt: '',
    inputPlaceholder: 'Message with this prompt...',
    isBuiltIn: false,
  };
}

export function PromptProfilesPanel({
  open,
  profiles,
  selectedProfileId,
  maxCustomSystemPromptLength,
  onClose,
  onSelectProfile,
  onSaveCustomProfile,
  onDeleteCustomProfile,
}: PromptProfilesPanelProps) {
  const [activeProfileId, setActiveProfileId] = useState(selectedProfileId);
  const [draft, setDraft] = useState<PromptProfile | null>(null);

  /* eslint-disable react-hooks/set-state-in-effect */
  useEffect(() => {
    if (!open) return;
    const profile = profiles.find(p => p.id === selectedProfileId)
      ?? profiles.find(p => p.id === DEFAULT_PROMPT_PROFILE_ID)
      ?? profiles[0];
    if (!profile) return;
    setActiveProfileId(profile.id);
    setDraft({ ...profile });
  }, [open, profiles, selectedProfileId]);
  /* eslint-enable react-hooks/set-state-in-effect */

  if (!open || !draft) return null;

  const isBuiltIn = draft.isBuiltIn;
  const trimmedName = draft.name.trim();
  const trimmedPrompt = draft.systemPrompt.trim();
  const promptTooLong = trimmedPrompt.length > maxCustomSystemPromptLength;
  const isExistingProfile = profiles.some(profile => profile.id === draft.id);
  const canSave = !isBuiltIn && trimmedName.length > 0 && trimmedPrompt.length > 0 && !promptTooLong;

  const handlePickProfile = (profile: PromptProfile) => {
    setActiveProfileId(profile.id);
    setDraft({ ...profile });
  };

  const handleCreateProfile = () => {
    const profile = createCustomProfile();
    setActiveProfileId(profile.id);
    setDraft(profile);
  };

  const handleSave = () => {
    if (!canSave) return;
    const profile: PromptProfile = {
      ...draft,
      name: trimmedName,
      description: draft.description.trim(),
      systemPrompt: trimmedPrompt,
      inputPlaceholder: draft.inputPlaceholder.trim() || 'Message with this prompt...',
      isBuiltIn: false,
    };
    onSaveCustomProfile(profile);
    setActiveProfileId(profile.id);
    setDraft(profile);
  };

  const handleDelete = () => {
    if (isBuiltIn) return;
    if (!window.confirm(`Delete "${draft.name}"? Conversations using it will fall back to General.`)) return;
    onDeleteCustomProfile(draft.id);
    const general = profiles.find(p => p.id === DEFAULT_PROMPT_PROFILE_ID) ?? profiles[0];
    if (general) {
      setActiveProfileId(general.id);
      setDraft({ ...general });
    }
  };

  return (
    <div className="fixed inset-0 z-[80]">
      <div className="absolute inset-0 bg-black/40 backdrop-blur-sm" onClick={onClose} />
      <section
        className="absolute right-0 top-0 h-full w-full max-w-4xl bg-surface-container-lowest
                   shadow-2xl flex flex-col"
        role="dialog"
        aria-modal="true"
        aria-label="Prompt profiles"
      >
        <header className="flex items-center justify-between px-6 py-4 border-b border-outline-variant/20">
          <div>
            <h2 className="font-headline text-lg font-bold text-on-surface">Prompt profiles</h2>
            <p className="text-xs text-on-surface-variant">Choose a system prompt for each chat or create your own.</p>
          </div>
          <button
            onClick={onClose}
            className="w-10 h-10 rounded-full flex items-center justify-center text-on-surface-variant
                       hover:bg-surface-container transition-colors cursor-pointer"
            aria-label="Close prompt profiles"
          >
            <X size={20} />
          </button>
        </header>

        <div className="flex-1 min-h-0 grid grid-cols-1 md:grid-cols-[16rem_1fr]">
          <aside className="border-b md:border-b-0 md:border-r border-outline-variant/20 p-4 overflow-y-auto">
            <button
              onClick={handleCreateProfile}
              className="w-full mb-4 flex items-center justify-center gap-2 rounded-full bg-primary
                         px-4 py-2.5 text-sm font-semibold text-on-primary transition-colors
                         hover:bg-primary-dim cursor-pointer"
            >
              <Plus size={16} />
              New profile
            </button>

            <div className="space-y-1">
              {profiles.map(profile => {
                const isActive = activeProfileId === profile.id;
                return (
                  <button
                    key={profile.id}
                    onClick={() => handlePickProfile(profile)}
                    className={`w-full rounded-2xl px-3 py-3 text-left transition-colors cursor-pointer
                                ${isActive ? 'bg-primary/10 text-primary' : 'text-on-surface hover:bg-surface-container'}`}
                  >
                    <span className="flex items-center gap-2 text-sm font-semibold">
                      {profile.name}
                      {profile.isBuiltIn && <Lock size={12} className="text-on-surface-variant" />}
                    </span>
                    {profile.description && (
                      <span className="mt-1 block text-xs text-on-surface-variant line-clamp-2">
                        {profile.description}
                      </span>
                    )}
                  </button>
                );
              })}
            </div>
          </aside>

          <div className="min-h-0 overflow-y-auto p-6 space-y-5">
            {isBuiltIn && (
              <div className="rounded-2xl bg-primary/10 px-4 py-3 text-sm text-primary">
                Built-in profiles are managed by AIChat. Create a new profile to write a custom system prompt.
              </div>
            )}

            <label className="block">
              <span className="text-xs font-bold uppercase tracking-wider text-on-surface-variant">Name</span>
              <input
                value={draft.name}
                onChange={e => setDraft({ ...draft, name: e.target.value })}
                readOnly={isBuiltIn}
                className="mt-2 w-full rounded-xl border border-outline-variant/30 bg-surface-container-lowest
                           px-4 py-3 text-sm text-on-surface outline-none transition-colors
                           focus:border-primary focus:ring-2 focus:ring-primary/20 read-only:bg-surface-container"
              />
            </label>

            <label className="block">
              <span className="text-xs font-bold uppercase tracking-wider text-on-surface-variant">Description</span>
              <input
                value={draft.description}
                onChange={e => setDraft({ ...draft, description: e.target.value })}
                readOnly={isBuiltIn}
                className="mt-2 w-full rounded-xl border border-outline-variant/30 bg-surface-container-lowest
                           px-4 py-3 text-sm text-on-surface outline-none transition-colors
                           focus:border-primary focus:ring-2 focus:ring-primary/20 read-only:bg-surface-container"
              />
            </label>

            <label className="block">
              <span className="text-xs font-bold uppercase tracking-wider text-on-surface-variant">Input placeholder</span>
              <input
                value={draft.inputPlaceholder}
                onChange={e => setDraft({ ...draft, inputPlaceholder: e.target.value })}
                readOnly={isBuiltIn}
                className="mt-2 w-full rounded-xl border border-outline-variant/30 bg-surface-container-lowest
                           px-4 py-3 text-sm text-on-surface outline-none transition-colors
                           focus:border-primary focus:ring-2 focus:ring-primary/20 read-only:bg-surface-container"
              />
            </label>

            <label className="block">
              <span className="flex items-center justify-between gap-3">
                <span className="text-xs font-bold uppercase tracking-wider text-on-surface-variant">System prompt</span>
                <span className={`text-xs ${promptTooLong ? 'text-error' : 'text-on-surface-variant'}`}>
                  {trimmedPrompt.length}/{maxCustomSystemPromptLength}
                </span>
              </span>
              <textarea
                value={draft.systemPrompt}
                onChange={e => setDraft({ ...draft, systemPrompt: e.target.value })}
                readOnly={isBuiltIn}
                rows={12}
                className="mt-2 w-full rounded-xl border border-outline-variant/30 bg-surface-container-lowest
                           px-4 py-3 text-sm leading-relaxed text-on-surface outline-none transition-colors
                           focus:border-primary focus:ring-2 focus:ring-primary/20 read-only:bg-surface-container"
              />
            </label>

            {isExistingProfile && (
              <button
                onClick={() => onSelectProfile(draft.id)}
                className="rounded-full bg-primary/10 px-4 py-2.5 text-sm font-semibold text-primary
                           transition-colors hover:bg-primary/15 cursor-pointer"
              >
                Use for current chat
              </button>
            )}

            {!isBuiltIn && (
              <div className="flex flex-col-reverse sm:flex-row sm:justify-between gap-3 pt-2">
                <button
                  onClick={handleDelete}
                  className="flex items-center justify-center gap-2 rounded-full px-4 py-2.5 text-sm font-semibold
                             text-error hover:bg-error/10 transition-colors cursor-pointer"
                >
                  <Trash2 size={16} />
                  Delete
                </button>
                <button
                  onClick={handleSave}
                  disabled={!canSave}
                  className="flex items-center justify-center gap-2 rounded-full bg-primary px-5 py-2.5
                             text-sm font-semibold text-on-primary transition-colors hover:bg-primary-dim
                             disabled:bg-surface-container-high disabled:text-on-surface-variant
                             disabled:cursor-not-allowed cursor-pointer"
                >
                  <Save size={16} />
                  Save profile
                </button>
              </div>
            )}
          </div>
        </div>
      </section>
    </div>
  );
}
