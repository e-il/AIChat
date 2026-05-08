import type { Conversation, ConversationSummary, Message } from '../types';

const DB_NAME = 'aichat';
const DB_VERSION = 1;
const CONVERSATIONS_STORE = 'conversations';
const MESSAGES_STORE = 'messages';

interface StoredConversation {
  id: string;
  title: string;
  createdAt: string;
  updatedAt: string;
}

interface StoredMessage extends Message {
  conversationId: string;
}

let dbPromise: Promise<IDBDatabase> | null = null;

function getDb(): Promise<IDBDatabase> {
  if (!dbPromise) {
    dbPromise = new Promise((resolve, reject) => {
      const request = indexedDB.open(DB_NAME, DB_VERSION);
      request.onerror = () => reject(request.error);
      request.onsuccess = () => resolve(request.result);
      request.onupgradeneeded = () => {
        const db = request.result;
        if (!db.objectStoreNames.contains(CONVERSATIONS_STORE)) {
          db.createObjectStore(CONVERSATIONS_STORE, { keyPath: 'id' });
        }
        if (!db.objectStoreNames.contains(MESSAGES_STORE)) {
          const store = db.createObjectStore(MESSAGES_STORE, { keyPath: 'id' });
          store.createIndex('conversationId', 'conversationId', { unique: false });
        }
      };
    });
  }
  return dbPromise;
}

function promisify<T>(request: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result);
    request.onerror = () => reject(request.error);
  });
}

function awaitTx(tx: IDBTransaction): Promise<void> {
  return new Promise((resolve, reject) => {
    tx.oncomplete = () => resolve();
    tx.onerror = () => reject(tx.error);
    tx.onabort = () => reject(tx.error);
  });
}

function truncateTitle(content: string): string {
  return content.length > 50 ? content.slice(0, 47) + '...' : content;
}

export const conversationStore = {
  async getAllConversations(): Promise<ConversationSummary[]> {
    const db = await getDb();
    const tx = db.transaction([CONVERSATIONS_STORE, MESSAGES_STORE], 'readonly');
    const conversations = await promisify(
      tx.objectStore(CONVERSATIONS_STORE).getAll() as IDBRequest<StoredConversation[]>
    );
    const allMessages = await promisify(
      tx.objectStore(MESSAGES_STORE).getAll() as IDBRequest<StoredMessage[]>
    );

    const countMap = new Map<string, number>();
    for (const msg of allMessages) {
      countMap.set(msg.conversationId, (countMap.get(msg.conversationId) ?? 0) + 1);
    }

    return conversations
      .map(c => ({
        id: c.id,
        title: c.title,
        createdAt: c.createdAt,
        updatedAt: c.updatedAt,
        messageCount: countMap.get(c.id) ?? 0,
      }))
      .sort((a, b) => b.updatedAt.localeCompare(a.updatedAt));
  },

  async getConversation(id: string): Promise<Conversation | null> {
    const db = await getDb();
    const tx = db.transaction([CONVERSATIONS_STORE, MESSAGES_STORE], 'readonly');
    const conversation = await promisify(
      tx.objectStore(CONVERSATIONS_STORE).get(id) as IDBRequest<StoredConversation | undefined>
    );
    if (!conversation) return null;

    const messages = await promisify(
      tx.objectStore(MESSAGES_STORE)
        .index('conversationId')
        .getAll(IDBKeyRange.only(id)) as IDBRequest<StoredMessage[]>
    );
    messages.sort((a, b) => a.timestamp.localeCompare(b.timestamp));

    return {
      id: conversation.id,
      title: conversation.title,
      createdAt: conversation.createdAt,
      updatedAt: conversation.updatedAt,
      messages: messages.map(msg => ({
        id: msg.id,
        role: msg.role,
        content: msg.content,
        timestamp: msg.timestamp,
        usedMemories: msg.usedMemories,
        attachments: msg.attachments,
        toolCalls: msg.toolCalls,
      })),
    };
  },

  async createConversation(): Promise<Conversation> {
    const db = await getDb();
    const now = new Date().toISOString();
    const stored: StoredConversation = {
      id: crypto.randomUUID(),
      title: 'New Chat',
      createdAt: now,
      updatedAt: now,
    };
    const tx = db.transaction(CONVERSATIONS_STORE, 'readwrite');
    tx.objectStore(CONVERSATIONS_STORE).add(stored);
    await awaitTx(tx);
    return { ...stored, messages: [] };
  },

  async deleteConversation(id: string): Promise<void> {
    const db = await getDb();
    const tx = db.transaction([CONVERSATIONS_STORE, MESSAGES_STORE], 'readwrite');
    const messagesStore = tx.objectStore(MESSAGES_STORE);
    const keys = await promisify(
      messagesStore.index('conversationId').getAllKeys(IDBKeyRange.only(id))
    );
    for (const key of keys) {
      messagesStore.delete(key);
    }
    tx.objectStore(CONVERSATIONS_STORE).delete(id);
    await awaitTx(tx);
  },

  async addMessage(conversationId: string, message: Message): Promise<void> {
    const db = await getDb();
    const tx = db.transaction([CONVERSATIONS_STORE, MESSAGES_STORE], 'readwrite');
    const conversationsStore = tx.objectStore(CONVERSATIONS_STORE);
    const messagesStore = tx.objectStore(MESSAGES_STORE);

    const conv = await promisify(
      conversationsStore.get(conversationId) as IDBRequest<StoredConversation | undefined>
    );
    if (!conv) {
      throw new Error(`Conversation ${conversationId} not found`);
    }

    const existingCount = await promisify(
      messagesStore.index('conversationId').count(IDBKeyRange.only(conversationId))
    );

    messagesStore.put({ ...message, conversationId });

    conv.updatedAt = new Date().toISOString();
    if (conv.title === 'New Chat' && message.role === 'user' && existingCount === 0) {
      conv.title = truncateTitle(message.content);
    }
    conversationsStore.put(conv);

    await awaitTx(tx);
  },

  async updateTitle(conversationId: string, title: string): Promise<void> {
    const db = await getDb();
    const tx = db.transaction(CONVERSATIONS_STORE, 'readwrite');
    const store = tx.objectStore(CONVERSATIONS_STORE);
    const conv = await promisify(
      store.get(conversationId) as IDBRequest<StoredConversation | undefined>
    );
    if (conv) {
      conv.title = title;
      conv.updatedAt = new Date().toISOString();
      store.put(conv);
    }
    await awaitTx(tx);
  },
};
