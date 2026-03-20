import { useState, useEffect, useCallback, useMemo } from 'react';
import { Menu } from 'lucide-react';
import { Sidebar } from './components/Sidebar/Sidebar';
import { ChatArea } from './components/Chat/ChatArea';
import { ChatInput } from './components/Input/ChatInput';
import { AuthCodeModal } from './components/Auth/AuthCodeModal';
import { FluentDropdown } from './components/Common/FluentDropdown';
import { useConversations } from './hooks/useConversations';
import { useChat } from './hooks/useChat';
import { chatApi } from './services/chatApi';
import { hasAuthCode, setAuthCode, clearAuthCode } from './services/auth';
import { getConversationSettings, saveConversationSettings, deleteConversationSettings } from './services/settings';
import type { ModelInfo } from './types';
import './index.css';

// Format context size for display (e.g., 100000 -> "100k")
function formatContextSize(size: number): string {
  if (size >= 1000) {
    return `${size / 1000}k`;
  }
  return size.toString();
}

function App() {
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [models, setModels] = useState<ModelInfo[]>([]);
  const [selectedModel, setSelectedModel] = useState<string>('');
  const [contextSizeOptions, setContextSizeOptions] = useState<number[]>([]);
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
  } = useConversations();

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
      setContextSizeOptions(response.contextSizeOptions);
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
      addMessage(conversationId, message);
    });
    setOnMessageComplete((conversationId, message) => {
      addMessage(conversationId, message);
    });
    setOnAuthError(handleAuthError);
  }, [setOnMessageAdded, setOnMessageComplete, setOnAuthError, addMessage, handleAuthError]);

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
    if (!activeConversation) {
      const newConv = await createConversation();
      if (newConv) {
        // Save settings for new conversation
        saveConversationSettings(newConv.id, { maxContextSize: currentContextSize, maxMessages: currentMaxMessages });
        sendMessage(newConv.id, message, selectedModel, currentContextSize, currentMaxMessages);
      }
    } else {
      sendMessage(activeConversation.id, message, selectedModel, currentContextSize, currentMaxMessages);
    }
  };

  // Memoize dropdown options
  const modelOptions = useMemo(() => 
    models.map(m => ({ value: m.id, label: m.name })), 
    [models]
  );

  const contextSizeDropdownOptions = useMemo(() => 
    contextSizeOptions.map(s => ({ value: s, label: formatContextSize(s) })), 
    [contextSizeOptions]
  );

  const maxMessagesDropdownOptions = useMemo(() => 
    maxMessagesOptions.map(c => ({ value: c, label: `${c} msgs` })), 
    [maxMessagesOptions]
  );

  return (
    <div className="flex h-screen w-full">
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

      <main className="flex-1 flex flex-col min-w-0 bg-white">
        {/* Header - Light Style */}
        <header className="flex items-center gap-3 px-4 py-2 bg-white border-b border-neutral-200">
          <button
            onClick={() => setSidebarOpen(true)}
            className="lg:hidden p-2 hover:bg-neutral-100 rounded transition-colors cursor-pointer"
          >
            <Menu size={20} className="text-neutral-600" />
          </button>
          <h1 className="text-sm font-semibold truncate flex-1 min-w-0 text-neutral-800">
            {activeConversation?.title || 'AIChat'}
          </h1>
          
          {/* Model Selector */}
          <FluentDropdown
            options={modelOptions}
            value={selectedModel}
            onChange={setSelectedModel}
            disabled={isStreaming}
          />

          {/* Context Size Selector */}
          <FluentDropdown
            options={contextSizeDropdownOptions}
            value={currentContextSize}
            onChange={handleContextSizeChange}
            disabled={isStreaming}
            title="Context Size Limit"
          />

          {/* Max Messages Selector */}
          <FluentDropdown
            options={maxMessagesDropdownOptions}
            value={currentMaxMessages}
            onChange={handleMaxMessagesChange}
            disabled={isStreaming}
            title="Max Messages"
          />
        </header>

        {/* Chat Area */}
        <ChatArea
          messages={activeConversation?.messages || []}
          streamingContent={streamingContent}
          isStreaming={isStreaming}
          isLoading={isLoading}
        />

        {/* Input */}
        <ChatInput
          onSend={handleSendMessage}
          disabled={isStreaming}
        />
      </main>
    </div>
  );
}

export default App;
