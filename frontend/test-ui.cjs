const { chromium } = require('playwright');
const assert = require('node:assert/strict');
const { mkdirSync } = require('node:fs');
const { join } = require('node:path');

const baseUrl = process.env.UI_BASE_URL || 'http://127.0.0.1:5175';
const output = join(__dirname, 'artifacts', 'ui');
mkdirSync(output, { recursive: true });

const models = [
  { id: 'chat-test', name: 'GPT Test', deploymentName: 'chat-deployment', kind: 'chat' },
  { id: 'image-test', name: 'Image Test', deploymentName: 'image-deployment', kind: 'image' },
];
const profiles = [
  { id: 'general', name: 'General', description: '', systemPrompt: 'Be helpful.', inputPlaceholder: 'Message AIChat...', isBuiltIn: true },
  { id: 'rewrite', name: 'Rewrite', description: '', systemPrompt: 'Rewrite clearly.', inputPlaceholder: 'Paste text to rewrite...', isBuiltIn: true },
  { id: 'translate-zh-en', name: 'Chinese-English', description: '', systemPrompt: 'Translate.', inputPlaceholder: 'Paste Chinese or English text...', isBuiltIn: true },
];

(async () => {
  const browser = await chromium.launch({ channel: process.env.PLAYWRIGHT_CHANNEL || undefined });
  try {
    const context = await browser.newContext({ viewport: { width: 1440, height: 1000 } });
    const page = await context.newPage();
    const errors = [];
    page.on('pageerror', error => errors.push(error.message));
    let savedModel;
    let rejectMemory = true;
    let finishUpload;
    let markUploadStarted;
    const uploadStarted = new Promise(resolve => { markUploadStarted = resolve; });
    let socket;
    let sentRequest;
    let memoryModeRequest;
    let rejectMemoryMode = true;
    await page.route('**/api/**', async route => {
      const request = route.request();
      const path = new URL(request.url()).pathname;
      const method = request.method();
      const json = body => route.fulfill({ json: body });
      if (request.headers()['x-auth-code'] !== 'ui-test') return route.fulfill({ status: 401, json: { error: 'Unauthorized' } });
      if (path === '/api/models' && method === 'GET') return json({ models: models.filter(model => model.kind === 'chat'), allModels: models, defaultModel: 'chat-test', defaultContextSize: 100000, contextSizeOptions: [25000, 100000], defaultMaxMessages: 50, maxMessagesOptions: [10, 50, 100], isAdmin: true });
      if (path === '/api/models' && method === 'PUT') { savedModel = request.postDataJSON(); return json(models); }
      if (path === '/api/promptprofiles') return json({ profiles, maxCustomSystemPromptLength: 8000 });
      if (path.endsWith('/mode') && method === 'PUT') {
        memoryModeRequest = request.postDataJSON();
        return route.fulfill({ status: rejectMemoryMode ? 500 : 204 });
      }
      if (path === '/api/memory' && method === 'GET') return json([{ id: 'memory-1', type: 'fact', content: 'A sample memory for interface verification.', createdAt: new Date().toISOString(), useCount: 2 }]);
      if (path === '/api/memory' && method === 'POST') {
        if (rejectMemory) return route.fulfill({ status: 500, json: { error: 'Test failure' } });
        return json({ ...request.postDataJSON(), id: 'memory-2', createdAt: new Date().toISOString(), useCount: 0 });
      }
      if (path === '/api/images' && method === 'POST') {
        await new Promise(resolve => { finishUpload = resolve; markUploadStarted(); });
        return json({ id: 'uploaded-test', type: 'image', mimeType: 'image/png', url: '/favicon.svg' });
      }
      return route.fulfill({ status: 404, json: {} });
    });
    await page.route('**/chathub/negotiate?*', route => route.fulfill({ json: { negotiateVersion: 1, connectionId: 'test', connectionToken: 'test', availableTransports: [{ transport: 'WebSockets', transferFormats: ['Text', 'Binary'] }] } }));
    await page.routeWebSocket('**/chathub?*', webSocket => {
      socket = webSocket;
      webSocket.onMessage(payload => {
        for (const frame of String(payload).split('\x1e').filter(Boolean)) {
          const message = JSON.parse(frame);
          if (message.protocol) webSocket.send('{}\x1e');
          if (message.target === 'SendMessage') {
            sentRequest = message.arguments[0];
            webSocket.send(JSON.stringify({ type: 1, target: 'ReceiveMessageChunk', arguments: [sentRequest.conversationId, 'A streamed answer for this conversation.'] }) + '\x1e');
          }
        }
      });
    });

    await page.goto(baseUrl);
    await page.getByRole('dialog', { name: 'Sign in to AIChat' }).waitFor();
    await page.screenshot({ path: join(output, 'desktop-login.png') });
    await page.getByLabel('Authentication Code').fill('invalid-test');
    await page.getByRole('button', { name: 'Continue', exact: true }).click();
    await page.getByRole('alert').waitFor();
    await page.getByLabel('Authentication Code').fill('ui-test');
    await page.getByRole('button', { name: 'Continue', exact: true }).click();
    await page.getByRole('dialog').waitFor({ state: 'hidden' });
    await page.getByTitle('Model', { exact: true }).waitFor();
    await page.screenshot({ path: join(output, 'desktop-empty.png') });
    await page.getByRole('button', { name: /Explore an idea/ }).click();
    assert.match(await page.getByRole('textbox', { name: 'Message', exact: true }).inputValue(), /explore a new idea/);
    await page.getByRole('textbox', { name: 'Message', exact: true }).fill('IME draft');
    await page.getByRole('textbox', { name: 'Message', exact: true }).dispatchEvent('compositionstart');
    await page.getByRole('textbox', { name: 'Message', exact: true }).press('Enter');
    assert.equal(sentRequest, undefined);
    await page.getByRole('textbox', { name: 'Message', exact: true }).dispatchEvent('compositionend');

    await page.locator('input[type=file]').setInputFiles({ name: 'sample.png', mimeType: 'image/png', buffer: Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aZusAAAAASUVORK5CYII=', 'base64') });
    await uploadStarted;
    await page.getByTitle('Remove', { exact: true }).click();
    const uploadFinished = page.waitForResponse(response => response.url().endsWith('/api/images'));
    finishUpload();
    await uploadFinished;
    await page.getByTitle('Remove', { exact: true }).waitFor({ state: 'hidden' });

    await page.getByRole('button', { name: 'Memory library', exact: true }).click();
    await page.getByText('A sample memory for interface verification.').waitFor();
    await page.getByLabel('New memory', { exact: true }).fill('Preserve this draft on failure');
    await page.getByRole('button', { name: 'Add', exact: true }).click();
    await page.getByRole('alert').waitFor();
    assert.equal(await page.getByLabel('New memory', { exact: true }).inputValue(), 'Preserve this draft on failure');
    rejectMemory = false;
    await page.screenshot({ path: join(output, 'desktop-memory.png') });
    await page.keyboard.press('Escape');
    await page.getByRole('dialog').waitFor({ state: 'hidden' });

    await page.getByTitle('Manage models').click();
    await page.getByTitle('Edit Image Test').click();
    assert.equal(await page.getByLabel('Model type', { exact: true }).inputValue(), 'image');
    await page.getByRole('button', { name: 'Save model' }).click();
    await page.getByRole('button', { name: 'Save model' }).waitFor();
    await page.getByLabel('Model ID', { exact: true }).waitFor();
    await page.waitForFunction(() => document.querySelector('dialog input')?.value === '');
    assert.equal(savedModel.kind, 'image');
    await page.screenshot({ path: join(output, 'desktop-models.png') });
    await page.keyboard.press('Escape');

    await page.getByTitle('Prompt profiles', { exact: true }).click();
    await page.getByRole('button', { name: 'New profile', exact: true }).click();
    await page.getByLabel('Name', { exact: true }).fill('Review assistant');
    await page.getByRole('textbox', { name: /^System prompt/ }).fill('Review code carefully.');
    await page.getByRole('button', { name: 'Save profile' }).click();
    await page.getByRole('button', { name: 'Review assistant', exact: true }).waitFor();
    await page.screenshot({ path: join(output, 'desktop-profiles.png') });
    await page.getByRole('button', { name: 'Use for current chat' }).click();
    await page.getByRole('dialog').waitFor({ state: 'hidden' });

    await page.evaluate(async () => {
      const database = await new Promise((resolve, reject) => { const request = indexedDB.open('aichat'); request.onsuccess = () => resolve(request.result); request.onerror = () => reject(request.error); });
      const transaction = database.transaction(['conversations', 'messages'], 'readwrite');
      const timestamp = new Date().toISOString();
      transaction.objectStore('conversations').put({ id: 'history-test', title: 'A study in clear thinking', createdAt: timestamp, updatedAt: timestamp });
      transaction.objectStore('messages').put({ id: 'question-test', conversationId: 'history-test', role: 'user', content: 'How can I make this code easier to understand?', timestamp });
      transaction.objectStore('messages').put({ id: 'answer-test', conversationId: 'history-test', role: 'assistant', content: '## Start with intent\nGive each operation a clear name, and keep the data flow visible.\n\n```typescript\nfunction describeWorkspace(name: string) {\n  const message = `Welcome to ${name}`;\n  return message;\n}\n```\n\nA long identifier: ' + 'workspace'.repeat(40), timestamp: new Date(Date.now() + 1).toISOString() });
      await new Promise((resolve, reject) => { transaction.oncomplete = resolve; transaction.onerror = reject; });
      database.close();
    });
    await page.reload();
    await page.getByRole('button', { name: 'A study in clear thinking', exact: true }).click();
    await page.getByText('Start with intent', { exact: true }).waitFor();
    const memorySwitch = page.getByRole('switch', { name: 'Conversation memory' });
    await memorySwitch.click();
    await page.getByRole('alert').waitFor();
    assert.equal(await memorySwitch.getAttribute('aria-checked'), 'true');
    rejectMemoryMode = false;
    await memorySwitch.click();
    await page.waitForFunction(() => document.querySelector('[role=switch]')?.getAttribute('aria-checked') === 'false');
    assert.equal(memoryModeRequest.enabled, false);
    assert.equal(memoryModeRequest.lastMessageId, 'answer-test');
    await memorySwitch.click();
    await page.waitForFunction(() => document.querySelector('[role=switch]')?.getAttribute('aria-checked') === 'true');
    await page.getByLabel('Search conversations').fill('nothing-matches');
    await page.getByText('No matching conversations').waitFor();
    await page.getByLabel('Search conversations').fill('');
    await page.screenshot({ path: join(output, 'desktop-conversation.png') });
    await page.getByRole('textbox', { name: 'Message', exact: true }).fill('Stream a response');
    await page.getByRole('button', { name: 'Send message', exact: true }).click();
    await page.getByText('A streamed answer for this conversation.', { exact: true }).waitFor();
    assert.equal(sentRequest.attachments, undefined);
    assert.equal(sentRequest.messages.at(-1).attachments, undefined);
    await page.getByRole('button', { name: 'New conversation', exact: true }).click();
    await page.getByText('A streamed answer for this conversation.', { exact: true }).waitFor({ state: 'hidden' });
    socket.close({ code: 1011, reason: 'Test disconnect' });
    await page.getByRole('button', { name: 'A study in clear thinking', exact: true }).click();
    await page.getByRole('alert').waitFor();
    assert.equal(await page.getByRole('textbox', { name: 'Message', exact: true }).isEnabled(), true);

    for (const width of [1440, 768, 390, 320]) {
      await page.setViewportSize({ width, height: 844 });
      const overflow = await page.evaluate(() => [...document.querySelectorAll('main, .composer, .markdown-content')].some(element => element.scrollWidth > element.clientWidth + 1));
      assert.equal(overflow, false, `Content overflow at ${width}px`);
      await page.screenshot({ path: join(output, `conversation-${width}.png`) });
      if (width < 1024) {
        await page.getByRole('button', { name: 'Open navigation', exact: true }).click();
        await page.getByRole('button', { name: 'Memory library', exact: true }).click();
        await page.getByRole('dialog', { name: 'Memory library' }).waitFor();
        assert.equal(await page.evaluate(() => { const dialog = document.querySelector('dialog[open]'); return dialog.scrollWidth <= dialog.clientWidth + 1; }), true);
        await page.screenshot({ path: join(output, `memory-${width}.png`) });
        await page.keyboard.press('Escape');
        await page.getByTitle('Prompt profiles', { exact: true }).click();
        await page.getByRole('button', { name: 'New profile', exact: true }).click();
        await page.getByRole('textbox', { name: /^System prompt/ }).fill('A mobile profile');
        await page.getByRole('button', { name: 'Save profile' }).scrollIntoViewIfNeeded();
        await page.screenshot({ path: join(output, `profiles-${width}.png`) });
        await page.keyboard.press('Escape');
      }
    }
    assert.deepEqual(errors, []);
    console.log('PASS: login, suggestions, IME, canceled upload, memory failure, model editing, profiles, history search, streaming isolation, disconnect recovery, and responsive layouts.');
    console.log(`Screenshots: ${output}`);
  } finally {
    await browser.close();
  }
})().catch(error => { console.error(error); process.exitCode = 1; });