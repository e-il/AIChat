import { useState, useEffect, useCallback, useMemo } from 'react';
import { Menu, History, Brain, BrainCircuit, Settings } from 'lucide-react';
import { Sidebar } from './components/Sidebar/Sidebar';
import { ChatArea } from './components/Chat/ChatArea';
import { ChatInput } from './components/Input/ChatInput';
import { AuthCodeModal } from './components/Auth/AuthCodeModal';
import { MemoryPanel } from './components/Memory/MemoryPanel';
import { PromptProfilesPanel } from './components/PromptProfiles/PromptProfilesPanel';
import { Dropdown } from './components/Common/Dropdown';
import { useConversations } from './hooks/useConversations';
import { useChat } from './hooks/useChat';
import { chatApi } from './services/chatApi';
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
  const [models, setModels] = useState<ModelInfo[]>([]);
  const [selectedModel, setSelectedModel] = useState<string>('');
  const [defaultModel, setDefaultModel] = useState<string>('');
  const [defaultContextSize, setDefaultContextSize] = useState(100000);
  const [currentContextSize, setCurrentContextSize] = useState(100000);
  const [maxMessagesOptions, setMaxMessagesOptions] = useState<number[]>([]);
  const [defaultMaxMessages, setDefaultMaxMessages] = useState(50);
  const [currentMaxMessages, setCurrentMaxMessages] = useState(50);
  const [memoryMode, setMemoryMode] = useState<MemoryMode>('auto');
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

  const {
    sendMessage,
    isStreaming,
    streamingContent,
    streamingAttachments,
    toolStatus,
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
      setDefaultModel(response.defaultModel);
      setSelectedModel(response.defaultModel);
      setDefaultContextSize(response.defaultContextSize);
      setCurrentContextSize(response.defaultContextSize);
      setMaxMessagesOptions(response.maxMessagesOptions);
      setDefaultMaxMessages(response.defaultMaxMessages);
      setCurrentMaxMessages(response.defaultMaxMessages);
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
    setOnStreamComplete(({ conversationId, content, usedMemories, attachments }) => {
      const assistantMessage: Message = {
        id: crypto.randomUUID(),
        role: 'assistant',
        content,
        timestamp: new Date().toISOString(),
        usedMemories: usedMemories.length > 0 ? usedMemories : undefined,
        attachments: attachments.length > 0 ? attachments : undefined,
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

  const handleMemoryModeToggle = () => {
    const next: MemoryMode = memoryMode === 'off' ? 'auto' : 'off';
    setMemoryMode(next);
    saveActiveConversationSettings({ memoryMode: next });
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
    <div className="flex h-screen w-full bg-surface">
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

      {/* Main Content Area */}
      <main className="flex-1 flex flex-col min-w-0 relative">
        {/* TopAppBar - Glassmorphic Header */}
        <header className="sticky top-0 flex justify-between items-center px-6 py-3
                           bg-white/80 backdrop-blur-xl shadow-sm z-30
                           border-b border-slate-200/50">
          <div className="flex items-center gap-4">
            {/* Mobile menu button */}
            <button
              onClick={() => setSidebarOpen(true)}
              className="lg:hidden p-2 hover:bg-surface-container rounded-lg transition-colors cursor-pointer"
            >
              <Menu size={20} className="text-on-surface-variant" />
            </button>

            {/* Accent bar */}
            <div className="hidden sm:block h-8 w-[2px] bg-primary/20 rounded-full" />

            {/* Title */}
            <h2 className="font-headline text-lg font-bold text-slate-900 truncate">
              {activeConversation?.title || 'New Chat'}
            </h2>

            {/* Model name badge */}
            {selectedModelName && (
              <span className="px-2.5 py-1 text-[0.65rem] font-semibold text-primary bg-primary/10
                               rounded-full whitespace-nowrap">
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
              disabled={isStreaming}
              title={memoryMode === 'off' ? 'Memory off — click to enable' : 'Memory on — click to disable for this chat'}
              className={`flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-semibold
                          transition-colors cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed
                          ${memoryMode === 'off'
                            ? 'bg-surface-container text-on-surface-variant hover:bg-surface-container-high'
                            : 'bg-primary/10 text-primary hover:bg-primary/15'}`}
            >
              {memoryMode === 'off' ? <Brain size={14} /> : <BrainCircuit size={14} />}
              <span>{memoryMode === 'off' ? 'Memory off' : 'Memory'}</span>
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
          streamingContent={streamingContent}
          streamingAttachments={streamingAttachments}
          toolStatus={toolStatus}
          isStreaming={isStreaming}
          isLoading={isLoading}
        />

        {/* Floating Input */}
        <ChatInput
          onSend={handleSendMessage}
          disabled={isStreaming}
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
