import { expect, test, type Page } from '@playwright/test';

const ANALYSIS_PROMPT = 'Analyze quarterly revenue trends';
const ACTION_PROMPT = 'Trigger alert for high CPU usage based on the analysis findings';

type ChatTurn = {
  actor: string;
  content: string;
};

async function sendPrompt(page: Page, prompt: string): Promise<ChatTurn> {
  await page.fill('#messageInput', prompt);
  await page.click('#sendBtn');
  await page.waitForFunction(() => !((document.getElementById('sendBtn') as HTMLButtonElement | null)?.disabled ?? true), {
    timeout: 120_000
  });

  const rows = page.locator('.chat-row');
  const rowCount = await rows.count();
  const lastRow = rows.nth(rowCount - 1);
  const actor = (await lastRow.locator('.chat-role').innerText()).trim();
  const content = (await lastRow.locator('.chat-content').innerText()).replace(/\s+/g, ' ').trim();
  return { actor, content };
}

async function sendPromptUntilAgent(
  page: Page,
  prompt: string,
  expectedActorContains: string,
  maxAttempts = 5
): Promise<ChatTurn> {
  for (let attempt = 1; attempt <= maxAttempts; attempt++) {
    const result = await sendPrompt(page, prompt);
    if (result.actor.toLowerCase().includes(expectedActorContains.toLowerCase())) {
      return result;
    }

    if (
      result.actor.toLowerCase().includes('system') &&
      result.content.toLowerCase().includes('warming up') &&
      attempt < maxAttempts
    ) {
      await page.waitForTimeout(1500);
      continue;
    }

    if (attempt < maxAttempts) {
      await page.waitForTimeout(1000);
      continue;
    }

    return result;
  }

  return sendPrompt(page, prompt);
}

test('two-prompt chain routes NeMo then MAF with analysis context', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('#messageInput')).toBeVisible();

  const analysis = await sendPromptUntilAgent(page, ANALYSIS_PROMPT, 'nemo');

  expect(analysis.actor.toLowerCase()).toContain('nemo');
  expect(analysis.content.length).toBeGreaterThan(20);

  const action = await sendPromptUntilAgent(page, ACTION_PROMPT, 'maf');
  expect(action.actor.toLowerCase()).toContain('maf');
  expect(action.content.toLowerCase()).toContain('used prior nemo analysis context');
});

test('predefined question labels include routing suffixes', async ({ page }) => {
  await page.goto('/');
  await expect(page.locator('#predefinedQuestionSelect')).toBeVisible();

  const comboText = (await page.locator('#predefinedQuestionSelect').innerText()).replace(/\s+/g, ' ');
  expect(comboText).toContain('Analyze quarterly revenue trends (NeMo)');
  expect(comboText).toContain('Trigger alert for high CPU usage (MAF)');
  expect(comboText).toContain('based on the analysis findings (NeMo + MAF)');
});
