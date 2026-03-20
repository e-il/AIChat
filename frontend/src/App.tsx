import { useState, useEffect, useCallback, useMemo, useRef } from 'react';
import { Menu, History } from 'lucide-react';
import { Sidebar } from './components/Sidebar/Sidebar';
import { ChatArea } from './components/Chat/ChatArea';
import { ChatInput } from './components/Input/ChatInput';
import { AuthCodeModal } from './components/Auth/AuthCodeModal';
import { Dropdown } from './components/Common/Dropdown';
import { useConversations } from './hooks/useConversations';
import { useChat } from './hooks/useChat';
import { chatApi } from './services/chatApi';
import { hasAuthCode, setAuthCode, clearAuthCode } from './services/auth';
import { getConversationSettings, saveConversationSettings, deleteConversationSettings } from './services/settings';
import type { ModelInfo, Message } from './types';
import './index.css';

function App() {
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [models, setModels] = useState<ModelInfo[]>([]);
  const [selectedModel, setSelectedModel] = useState<string>('');
  const [defaultContextSize, setDefaultContextSize] = useState(100000);
  const [currentContextSize, setCurrentContextSize] = useState(100000);
  const [maxMessagesOptions, setMaxMessagesOptions] = useState<number[]>([]);
  const [defaultMaxMessages, setDefaultMaxMessages] = useState(50);
  const [currentMaxMessages, setCurrentMaxMessages] = useState(50);
  const [showAuthModal, setShowAuthModal] = useState(!hasAuthCode());
  const [isAuthenticated, setIsAuthenticated] = useState(hasAuthCode());

  const {
    conversations,
    activeConversation,
    isLoading,
    loadConversations,
    loadConversation,
    createConversation,
    deleteConversation,
    addMessage,
    updateMessage,
  } = useConversations();

  // Track temp ID for optimistic user message
  const pendingUserMessageRef = useRef<{ conversationId: string; tempId: string } | null>(null);

  const {
    sendMessage,
    isStreaming,
    streamingContent,
    setOnMessageAdded,
    setOnMessageComplete,
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

    loadConversations();
  }, [isAuthenticated, handleAuthError, loadConversations]);

  // Load conversation settings when active conversation changes
  useEffect(() => {
    if (activeConversation) {
      const settings = getConversationSettings(activeConversation.id, defaultContextSize, defaultMaxMessages);
      setCurrentContextSize(settings.maxContextSize);
      setCurrentMaxMessages(settings.maxMessages);
    } else {
      setCurrentContextSize(defaultContextSize);
      setCurrentMaxMessages(defaultMaxMessages);
    }
  }, [activeConversation?.id, defaultContextSize, defaultMaxMessages]);

  // Update document title based on active conversation
  useEffect(() => {
    document.title = activeConversation?.title 
      ? `AIChat - ${activeConversation.title}`
      : 'AIChat';
  }, [activeConversation?.title]);

  // Wire up SignalR callbacks
  useEffect(() => {
    setOnMessageAdded((conversationId, message) => {
      // For user messages, update the temp ID with the real one
      if (message.role === 'user') {
        const pending = pendingUserMessageRef.current;
        if (pending && pending.conversationId === conversationId) {
          updateMessage(conversationId, pending.tempId, message);
          pendingUserMessageRef.current = null;
        }
        return;
      }
      addMessage(conversationId, message);
    });
    setOnMessageComplete((conversationId, message) => {
      addMessage(conversationId, message);
    });
    setOnAuthError(handleAuthError);
  }, [setOnMessageAdded, setOnMessageComplete, setOnAuthError, addMessage, updateMessage, handleAuthError]);

  const handleSelectConversation = (id: string) => {
    loadConversation(id);
    setSidebarOpen(false);
  };

  const handleNewChat = async () => {
    await createConversation();
    setSidebarOpen(false);
  };

  const handleDeleteConversation = async (id: string) => {
    await deleteConversation(id);
    deleteConversationSettings(id);
  };

  const handleContextSizeChange = (size: number) => {
    setCurrentContextSize(size);
    if (activeConversation) {
      saveConversationSettings(activeConversation.id, { maxContextSize: size, maxMessages: currentMaxMessages });
    }
  };

  const handleMaxMessagesChange = (count: number) => {
    setCurrentMaxMessages(count);
    if (activeConversation) {
      saveConversationSettings(activeConversation.id, { maxContextSize: currentContextSize, maxMessages: count });
    }
  };

  const handleSendMessage = async (message: string) => {
    // Create optimistic user message with temp ID
    const tempId = `temp-${Date.now()}`;
    const optimisticMessage: Message = {
      id: tempId,
      role: 'user',
      content: message,
      timestamp: new Date().toISOString(),
    };

    if (!activeConversation) {
      const newConv = await createConversation();
      if (newConv) {
        // Track temp ID for updating later
        pendingUserMessageRef.current = { conversationId: newConv.id, tempId };
        // Add user message optimistically
        addMessage(newConv.id, optimisticMessage);
        // Save settings for new conversation
        saveConversationSettings(newConv.id, { maxContextSize: currentContextSize, maxMessages: currentMaxMessages });
        sendMessage(newConv.id, message, selectedModel, currentContextSize, currentMaxMessages);
      }
    } else {
      // Track temp ID for updating later
      pendingUserMessageRef.current = { conversationId: activeConversation.id, tempId };
      // Add user message optimistically
      addMessage(activeConversation.id, optimisticMessage);
      sendMessage(activeConversation.id, message, selectedModel, currentContextSize, currentMaxMessages);
    }
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
        onNew={handleNewChat}
        onDelete={handleDeleteConversation}
        isOpen={sidebarOpen}
        onClose={() => setSidebarOpen(false)}
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
        />
      </main>
    </div>
  );
}

export default App;
