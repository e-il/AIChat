import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { Menu, History, Brain, BrainCircuit, Settings, SlidersHorizontal } from 'lucide-react';
import { Sidebar } from './components/Sidebar/Sidebar';
import { ChatArea } from './components/Chat/ChatArea';
import { ChatInput } from './components/Input/ChatInput';
import { AuthCodeModal } from './components/Auth/AuthCodeModal';
import { MemoryPanel } from './components/Memory/MemoryPanel';
import { PromptProfilesPanel } from './components/PromptProfiles/PromptProfilesPanel';
import { Dropdown } from './components/Common/Dropdown';
import { ModelsPanel } from './components/Models/ModelsPanel';
import { useConversations } from './hooks/useConversations';
import { useChat } from './hooks/useChat';
import { chatApi } from './services/chatApi';
import { memoryApi } from './services/memoryApi';
import { hasAuthCode, setAuthCode, clearAuthCode } from './services/auth';
import { getConversationSettings, saveConversationSettings, deleteConversationSettings } from './services/settings';
import {
  DEFAULT_PROMPT_PROFILE_ID,
  FALLBACK_BUILT_IN_PROMPT_PROFILES,
  FALLBACK_MAX_CUSTOM_SYSTEM_PROMPT_LENGTH,
  getPromptProfileById,
  loadCustomPromptProfiles,
  mergePromptProfiles,
  saveCustomPromptProfiles,
} from './services/promptProfiles';
import type { ModelInfo, Message, MemoryMode, MessageAttachment, PromptProfile } from './types';
import './index.css';

function App() {
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [draft, setDraft] = useState('');
  const [models, setModels] = useState<ModelInfo[]>([]);
  const [allModels, setAllModels] = useState<ModelInfo[]>([]);
  const [isAdmin, setIsAdmin] = useState(false);
  const [modelsOpen, setModelsOpen] = useState(false);
  const [selectedModel, setSelectedModel] = useState<string>('');
  const [defaultModel, setDefaultModel] = useState<string>('');
  const [defaultContextSize, setDefaultContextSize] = useState(100000);
  const [currentContextSize, setCurrentContextSize] = useState(100000);
  const [maxMessagesOptions, setMaxMessagesOptions] = useState<number[]>([]);
  const [defaultMaxMessages, setDefaultMaxMessages] = useState(50);
  const [currentMaxMessages, setCurrentMaxMessages] = useState(50);
  const [memoryMode, setMemoryMode] = useState<MemoryMode>('auto');
  const [memoryModeSaving, setMemoryModeSaving] = useState(false);
  const [memoryModeError, setMemoryModeError] = useState<{ conversationId: string; message: string } | null>(null);
  const [showAuthModal, setShowAuthModal] = useState(!hasAuthCode());
  const [isAuthenticated, setIsAuthenticated] = useState(hasAuthCode());
  const [memoryOpen, setMemoryOpen] = useState(false);
  const [promptProfilesOpen, setPromptProfilesOpen] = useState(false);
  const [pendingAttachments, setPendingAttachments] = useState<MessageAttachment[]>([]);
  const [builtInPromptProfiles, setBuiltInPromptProfiles] = useState<PromptProfile[]>(FALLBACK_BUILT_IN_PROMPT_PROFILES);
  const [customPromptProfiles, setCustomPromptProfiles] = useState<PromptProfile[]>(() => loadCustomPromptProfiles());
  const [selectedPromptProfileId, setSelectedPromptProfileId] = useState(DEFAULT_PROMPT_PROFILE_ID);
  const [maxCustomSystemPromptLength, setMaxCustomSystemPromptLength] = useState(FALLBACK_MAX_CUSTOM_SYSTEM_PROMPT_LENGTH);

  const {
    conversations,
    activeConversation,
    isLoading,
    loadConversations,
    loadConversation,
    createConversation,
    deleteConversation,
    addMessage,
  } = useConversations();

  const activeConversationIdRef = useRef(activeConversation?.id);
  useEffect(() => { activeConversationIdRef.current = activeConversation?.id; }, [activeConversation?.id]);

  const {
    sendMessage,
    isStreaming,
    streamingContent,
    streamingAttachments,
    toolStatus,
    streamingConversationId,
    error: chatError,
    setOnStreamComplete,
    setOnAuthError,
  } = useChat();

  // Handle auth code submission
  const handleAuthSubmit = useCallback(async (code: string): Promise<boolean> => {
    const isValid = await chatApi.validateAuthCode(code);
    if (isValid) {
      setAuthCode(code);
      setIsAuthenticated(true);
      setShowAuthModal(false);
    }
    return isValid;
  }, []);

  // Handle auth errors
  const handleAuthError = useCallback(() => {
    clearAuthCode();
    setIsAuthenticated(false);
    setShowAuthModal(true);
  }, []);

  // Load available models and conversations when authenticated
  useEffect(() => {
    if (!isAuthenticated) return;

    chatApi.getModels().then(response => {
      setModels(response.models);
      setAllModels(response.allModels ?? response.models);
      setDefaultModel(response.defaultModel);
      setSelectedModel(response.defaultModel);
      setDefaultContextSize(response.defaultContextSize);
      setCurrentContextSize(response.defaultContextSize);
      setMaxMessagesOptions(response.maxMessagesOptions);
      setDefaultMaxMessages(response.defaultMaxMessages);
      setCurrentMaxMessages(response.defaultMaxMessages);
      setIsAdmin(response.isAdmin);
    }).catch(err => {
      console.error('Failed to load models:', err);
      if (err.message === 'AUTH_REQUIRED') {
        handleAuthError();
      }
    });

    chatApi.getPromptProfiles().then(response => {
      setBuiltInPromptProfiles(response.profiles);
      setMaxCustomSystemPromptLength(response.maxCustomSystemPromptLength);
    }).catch(err => {
      console.error('Failed to load prompt profiles:', err);
      if (err.message === 'AUTH_REQUIRED') {
        handleAuthError();
      }
    });

    loadConversations();
  }, [isAuthenticated, handleAuthError, loadConversations]);

  const reloadModels = useCallback(async () => {
    const response = await chatApi.getModels();
      setModels(response.models);
      setAllModels(response.allModels ?? response.models);
      setDefaultModel(response.defaultModel);
      setSelectedModel(current => response.models.some(model => model.id === current) ? current : response.defaultModel);
      setIsAdmin(response.isAdmin);
  }, []);

  const promptProfiles = useMemo(
    () => mergePromptProfiles(builtInPromptProfiles, customPromptProfiles),
    [builtInPromptProfiles, customPromptProfiles]
  );

  const selectedPromptProfile = useMemo(
    () => getPromptProfileById(promptProfiles, selectedPromptProfileId),
    [promptProfiles, selectedPromptProfileId]
  );

  // Load conversation settings when active conversation changes
  useEffect(() => {
    if (activeConversation) {
      const settings = getConversationSettings(activeConversation.id, defaultContextSize, defaultMaxMessages);
      setCurrentContextSize(settings.maxContextSize);
      setCurrentMaxMessages(settings.maxMessages);
      setMemoryMode(settings.memoryMode ?? 'auto');
      setSelectedPromptProfileId(getPromptProfileById(promptProfiles, settings.promptProfileId).id);
    } else {
      setCurrentContextSize(defaultContextSize);
      setCurrentMaxMessages(defaultMaxMessages);
      setMemoryMode('auto');
      setSelectedPromptProfileId(DEFAULT_PROMPT_PROFILE_ID);
      if (defaultModel) {
        setSelectedModel(defaultModel);
      }
    }
    // Track conversation identity only; message updates should not reset chat controls.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeConversation?.id, defaultContextSize, defaultMaxMessages, defaultModel]);

  // Update document title based on active conversation
  useEffect(() => {
    document.title = activeConversation?.title
      ? `AIChat - ${activeConversation.title}`
      : 'AIChat';
  }, [activeConversation?.title]);

  // Wire up SignalR callbacks
  useEffect(() => {
    setOnStreamComplete(({ conversationId, content, usedMemories, attachments, toolCalls }) => {
      const assistantMessage: Message = {
        id: crypto.randomUUID(),
        role: 'assistant',
        content,
        timestamp: new Date().toISOString(),
        usedMemories: usedMemories.length > 0 ? usedMemories : undefined,
        attachments: attachments.length > 0 ? attachments : undefined,
        toolCalls: toolCalls.length > 0 ? toolCalls : undefined,
      };
      addMessage(conversationId, assistantMessage);
    });
    setOnAuthError(handleAuthError);
  }, [setOnStreamComplete, setOnAuthError, addMessage, handleAuthError]);

  const handleSelectConversation = (id: string) => {
    loadConversation(id);
    setSidebarOpen(false);
  };

  const saveActiveConversationSettings = useCallback((settings: Partial<{
    maxContextSize: number;
    maxMessages: number;
    memoryMode: MemoryMode;
    promptProfileId: string;
  }>) => {
    if (!activeConversation) return;
    saveConversationSettings(activeConversation.id, {
      maxContextSize: settings.maxContextSize ?? currentContextSize,
      maxMessages: settings.maxMessages ?? currentMaxMessages,
      memoryMode: settings.memoryMode ?? memoryMode,
      promptProfileId: settings.promptProfileId ?? selectedPromptProfileId,
    });
  }, [activeConversation, currentContextSize, currentMaxMessages, memoryMode, selectedPromptProfileId]);

  const handleNewChat = async (profileId = DEFAULT_PROMPT_PROFILE_ID) => {
    const profile = getPromptProfileById(promptProfiles, profileId);
    const model = defaultModel || selectedModel;
    if (model) {
      setSelectedModel(model);
    }
    setSelectedPromptProfileId(profile.id);
    setCurrentContextSize(defaultContextSize);
    setCurrentMaxMessages(defaultMaxMessages);
    setMemoryMode('auto');
    const newConversation = await createConversation();
    if (newConversation) {
      saveConversationSettings(newConversation.id, {
        maxContextSize: defaultContextSize,
        maxMessages: defaultMaxMessages,
        memoryMode: 'auto',
        promptProfileId: profile.id,
      });
    }
    setSidebarOpen(false);
  };

  const handleDeleteConversation = async (id: string) => {
    await deleteConversation(id);
    deleteConversationSettings(id);
  };

  const handleContextSizeChange = (size: number) => {
    setCurrentContextSize(size);
    saveActiveConversationSettings({ maxContextSize: size });
  };

  const handleMaxMessagesChange = (count: number) => {
    setCurrentMaxMessages(count);
    saveActiveConversationSettings({ maxMessages: count });
  };

  const handleMemoryModeToggle = async () => {
    const next: MemoryMode = memoryMode === 'off' ? 'auto' : 'off';
    if (!activeConversation) { setMemoryMode(next); return; }
    const conversationId = activeConversation.id;
    setMemoryModeSaving(true);
    setMemoryModeError(null);
    try {
      await memoryApi.setConversationMode(conversationId, next !== 'off', activeConversation.messages.at(-1)?.id);
      saveConversationSettings(conversationId, {
        ...getConversationSettings(conversationId, defaultContextSize, defaultMaxMessages),
        memoryMode: next,
      });
      if (activeConversationIdRef.current === conversationId) setMemoryMode(next);
    } catch (cause) {
      if (cause instanceof Error && cause.message === 'AUTH_REQUIRED') handleAuthError();
      setMemoryModeError({ conversationId, message: 'Unable to update conversation memory. Please try again.' });
    } finally { setMemoryModeSaving(false); }
  };

  const handlePromptProfileChange = (profileId: string) => {
    const profile = getPromptProfileById(promptProfiles, profileId);
    setSelectedPromptProfileId(profile.id);
    saveActiveConversationSettings({ promptProfileId: profile.id });
  };

  const handleSaveCustomPromptProfile = (profile: PromptProfile) => {
    const nextProfiles = [
      ...customPromptProfiles.filter(p => p.id !== profile.id),
      { ...profile, isBuiltIn: false },
    ];
    setCustomPromptProfiles(nextProfiles);
    saveCustomPromptProfiles(nextProfiles);
    setSelectedPromptProfileId(profile.id);
    saveActiveConversationSettings({ promptProfileId: profile.id });
  };

  const handleDeleteCustomPromptProfile = (profileId: string) => {
    const nextProfiles = customPromptProfiles.filter(p => p.id !== profileId);
    setCustomPromptProfiles(nextProfiles);
    saveCustomPromptProfiles(nextProfiles);
    if (selectedPromptProfileId === profileId) {
      setSelectedPromptProfileId(DEFAULT_PROMPT_PROFILE_ID);
      saveActiveConversationSettings({ promptProfileId: DEFAULT_PROMPT_PROFILE_ID });
    }
  };

  const handleSendMessage = async (message: string) => {
    const userMessage: Message = {
      id: crypto.randomUUID(),
      role: 'user',
      content: message,
      timestamp: new Date().toISOString(),
      attachments: pendingAttachments.length > 0 ? pendingAttachments : undefined,
    };

    let conv = activeConversation;
    if (!conv) {
      const newConv = await createConversation();
      if (!newConv) return;
      conv = newConv;
      saveConversationSettings(newConv.id, {
        maxContextSize: currentContextSize,
        maxMessages: currentMaxMessages,
        memoryMode,
        promptProfileId: selectedPromptProfile.id,
      });
    }

    const messagesForServer = [...conv.messages, userMessage];
    addMessage(conv.id, userMessage);
    setPendingAttachments([]);
    sendMessage(
      conv.id,
      messagesForServer,
      selectedModel,
      currentContextSize,
      currentMaxMessages,
      memoryMode,
      null,
      selectedPromptProfile.id,
      selectedPromptProfile.isBuiltIn ? null : selectedPromptProfile.systemPrompt
    );
  };

  // Memoize dropdown options
  const modelOptions = useMemo(() =>
    models.map(m => ({ value: m.id, label: m.name })),
    [models]
  );

  const maxMessagesDropdownOptions = useMemo(() =>
    maxMessagesOptions.map(c => ({ value: c, label: `${c} msgs` })),
    [maxMessagesOptions]
  );

  const promptProfileOptions = useMemo(() =>
    promptProfiles.map(profile => ({ value: profile.id, label: profile.name })),
    [promptProfiles]
  );

  // Get selected model name for header badge
  const selectedModelName = models.find(m => m.id === selectedModel)?.name || '';

  return (
    <div className="workspace-shell flex w-full bg-surface">
      {/* Auth Modal */}
      {showAuthModal && (
        <AuthCodeModal onSubmit={handleAuthSubmit} />
      )}

      <Sidebar
        conversations={conversations}
        activeId={activeConversation?.id || null}
        onSelect={handleSelectConversation}
        onNew={() => handleNewChat()}
        onNewWithProfile={handleNewChat}
        onDelete={handleDeleteConversation}
        onOpenMemory={() => setMemoryOpen(true)}
        isOpen={sidebarOpen}
        onClose={() => setSidebarOpen(false)}
      />

      <MemoryPanel open={memoryOpen} onClose={() => setMemoryOpen(false)} />
      <PromptProfilesPanel
        open={promptProfilesOpen}
        profiles={promptProfiles}
        selectedProfileId={selectedPromptProfile.id}
        maxCustomSystemPromptLength={maxCustomSystemPromptLength}
        onClose={() => setPromptProfilesOpen(false)}
        onSelectProfile={handlePromptProfileChange}
        onSaveCustomProfile={handleSaveCustomPromptProfile}
        onDeleteCustomProfile={handleDeleteCustomPromptProfile}
      />
      <ModelsPanel open={modelsOpen} models={allModels} onClose={() => setModelsOpen(false)} onChanged={reloadModels} />

      {/* Main Content Area */}
      <main className="flex-1 flex flex-col min-w-0 relative">
        {/* TopAppBar - Glassmorphic Header */}
        <header className="workspace-header flex flex-wrap justify-between items-center gap-3 px-5 md:px-8 py-4 z-30 border-b border-outline-variant/25">
          <div className="flex items-center gap-3 min-w-0 flex-1">
            {/* Mobile menu button */}
            <button
              onClick={() => setSidebarOpen(true)}
              aria-label="Open navigation"
              title="Open navigation"
              className="lg:hidden p-2 hover:bg-surface-container rounded-lg transition-colors cursor-pointer"
            >
              <Menu size={20} className="text-on-surface-variant" />
            </button>

            {isAdmin && <button onClick={() => setModelsOpen(true)} title="Manage models" className="flex h-9 w-9 items-center justify-center rounded-lg text-on-surface-variant hover:bg-surface-container hover:text-primary"><SlidersHorizontal size={17}/></button>}

            {/* Title */}
            <h2 className="font-headline text-sm font-medium text-on-surface truncate">
              {activeConversation?.title || 'New conversation'}
            </h2>

            {/* Model name badge */}
            {selectedModelName && (
              <span className="hidden xl:inline-flex px-2 py-1 text-[10px] text-on-surface-variant border border-outline-variant/30 rounded whitespace-nowrap max-w-40 truncate">
                {selectedModelName}
              </span>
            )}
          </div>

          <div className="flex items-center gap-2">
            <div className="hidden md:block">
              <Dropdown
                options={promptProfileOptions}
                value={selectedPromptProfile.id}
                onChange={handlePromptProfileChange}
                disabled={isStreaming}
                title="System Prompt Profile"
              />
            </div>

            <button
              onClick={() => setPromptProfilesOpen(true)}
              disabled={isStreaming}
              title="Prompt profiles"
              className="w-9 h-9 rounded-lg flex items-center justify-center text-on-surface-variant
                         hover:bg-surface-container hover:text-primary transition-colors cursor-pointer
                         disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <Settings size={18} />
            </button>

            {/* Memory mode toggle */}
            <button
              onClick={handleMemoryModeToggle}
              role="switch"
              aria-checked={memoryMode !== 'off'}
              aria-label="Conversation memory"
              disabled={isStreaming || memoryModeSaving}
              aria-busy={memoryModeSaving}
              title={memoryMode === 'off' ? 'Memory use and collection are off' : 'Memory use and collection are on'}
              className={`flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-semibold
                          transition-colors cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed
                          ${memoryMode === 'off'
                            ? 'bg-surface-container text-on-surface-variant hover:bg-surface-container-high'
                            : 'bg-primary/10 text-primary hover:bg-primary/15'}`}
            >
              {memoryMode === 'off' ? <Brain size={14} /> : <BrainCircuit size={14} />}
              <span className="hidden sm:inline">Memory</span><span className={`relative w-6 h-3.5 rounded-full ${memoryMode === 'off' ? 'bg-outline-variant' : 'bg-primary'}`}><span className={`absolute top-0.5 h-2.5 w-2.5 rounded-full bg-white transition-all ${memoryMode === 'off' ? 'left-0.5' : 'left-3'}`} /></span>
            </button>

            {/* Max Messages / History Button */}
            <Dropdown
              options={maxMessagesDropdownOptions}
              value={currentMaxMessages}
              onChange={handleMaxMessagesChange}
              disabled={isStreaming}
              title="Max Messages in History"
              icon={<History size={18} />}
            />
          </div>
        </header>

        {/* Chat Area */}
        <ChatArea
          messages={activeConversation?.messages || []}
          streamingContent={streamingConversationId === activeConversation?.id ? streamingContent : ''}
          streamingAttachments={streamingConversationId === activeConversation?.id ? streamingAttachments : []}
          toolStatus={streamingConversationId === activeConversation?.id ? toolStatus : null}
          isStreaming={isStreaming && streamingConversationId === activeConversation?.id}
          isLoading={isLoading}
          onSuggestion={message => { setDraft(message); document.getElementById('chat-message')?.focus(); }}
        />

        {chatError && streamingConversationId === activeConversation?.id && <p role="alert" className="mx-auto w-full max-w-[880px] px-5 md:px-10 pt-3 text-xs text-error">{chatError}</p>}
        {memoryModeError?.conversationId === activeConversation?.id && memoryModeError && <p role="alert" className="mx-auto w-full max-w-[880px] px-5 md:px-10 pt-3 text-xs text-error">{memoryModeError.message}</p>}

        {/* Floating Input */}
        <ChatInput
          message={draft}
          onMessageChange={setDraft}
          onSend={handleSendMessage}
          disabled={isStreaming || memoryModeSaving}
          models={modelOptions}
          selectedModel={selectedModel}
          onModelChange={setSelectedModel}
          currentContextSize={currentContextSize}
          onContextSizeChange={handleContextSizeChange}
          pendingAttachments={pendingAttachments}
          onAttachmentsChange={setPendingAttachments}
          onAuthError={handleAuthError}
          placeholder={selectedPromptProfile.inputPlaceholder}
        />
      </main>
    </div>
  );
}

export default App;
