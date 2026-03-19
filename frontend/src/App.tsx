import { useState, useEffect, useCallback } from 'react';
import { Menu, ChevronDown } from 'lucide-react';
import { Sidebar } from './components/Sidebar/Sidebar';
import { ChatArea } from './components/Chat/ChatArea';
import { ChatInput } from './components/Input/ChatInput';
import { AuthCodeModal } from './components/Auth/AuthCodeModal';
import { useConversations } from './hooks/useConversations';
import { useChat } from './hooks/useChat';
import { chatApi } from './services/chatApi';
import { hasAuthCode, setAuthCode, clearAuthCode } from './services/auth';
import type { ModelInfo } from './types';
import './index.css';

function App() {
  const [sidebarOpen, setSidebarOpen] = useState(false);
  const [models, setModels] = useState<ModelInfo[]>([]);
  const [selectedModel, setSelectedModel] = useState<string>('');
  const [modelDropdownOpen, setModelDropdownOpen] = useState(false);
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
    }).catch(err => {
      console.error('Failed to load models:', err);
      if (err.message === 'AUTH_REQUIRED') {
        handleAuthError();
      }
    });

    loadConversations();
  }, [isAuthenticated, handleAuthError, loadConversations]);

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

  const handleSendMessage = async (message: string) => {
    if (!activeConversation) {
      const newConv = await createConversation();
      if (newConv) {
        sendMessage(newConv.id, message, selectedModel);
      }
    } else {
      sendMessage(activeConversation.id, message, selectedModel);
    }
  };

  const selectedModelName = models.find(m => m.id === selectedModel)?.name || selectedModel;

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
        onDelete={deleteConversation}
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
          <div className="relative">
            <button
              onClick={() => setModelDropdownOpen(!modelDropdownOpen)}
              disabled={isStreaming}
              className="flex items-center gap-1.5 px-3 py-1 text-sm 
                         bg-neutral-100 hover:bg-neutral-200 border border-neutral-300
                         rounded transition-all cursor-pointer
                         disabled:opacity-50 disabled:cursor-not-allowed"
            >
              <span className="text-neutral-700 font-medium">{selectedModelName}</span>
              <ChevronDown size={14} className="text-neutral-500" />
            </button>
            
            {modelDropdownOpen && (
              <>
                <div 
                  className="fixed inset-0 z-10" 
                  onClick={() => setModelDropdownOpen(false)} 
                />
                <div className="absolute right-0 mt-1 w-48 bg-white rounded-sm shadow-lg
                                border border-neutral-200 py-1 z-20">
                  {models.map(model => (
                    <button
                      key={model.id}
                      onClick={() => {
                        setSelectedModel(model.id);
                        setModelDropdownOpen(false);
                      }}
                      className={`w-full text-left px-3 py-2 text-sm 
                                  cursor-pointer transition-colors
                                  ${model.id === selectedModel 
                                    ? 'bg-neutral-100 text-neutral-900 font-medium' 
                                    : 'text-neutral-700 hover:bg-neutral-50'}`}
                    >
                      {model.name}
                    </button>
                  ))}
                </div>
              </>
            )}
          </div>
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
