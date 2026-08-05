// Headless-Chromium end-to-end check of the browser bundle.
// Spawns the mock server + vite preview, drives the page like a user,
// and asserts the @apeira/core agent loop completes a multi-tool turn.
//
// Run: node e2e.mjs  (requires `pnpm build` first; playwright installed)

import { spawn } from 'node:child_process'
import { chromium } from 'playwright'

const MOCK_PORT = 8787
const WEB_PORT = 4173
const BASE_URL = `http://127.0.0.1:${MOCK_PORT}/v1`

const start = (cmd, args, cwd, readyPattern) => new Promise((resolve, reject) => {
  const child = spawn(cmd, args, { cwd, stdio: ['ignore', 'pipe', 'pipe'] })
  let out = ''
  const onData = (chunk) => {
    out += chunk.toString()
    if (readyPattern.test(out)) {
      cleanup()
      resolve(child)
    }
  }
  const onError = (error) => {
    cleanup()
    reject(error)
  }
  const cleanup = () => {
    child.stdout.off('data', onData)
    child.stderr.off('data', onData)
    child.off('error', onError)
  }
  child.stdout.on('data', onData)
  child.stderr.on('data', onData)
  child.on('error', onError)
  child.on('exit', (code) => {
    if (!out || !readyPattern.test(out))
      reject(new Error(`process exited early (${code}): ${out}`))
  })
})

const kill = (child) => {
  if (!child || child.killed)
    return
  child.kill('SIGTERM')
}

const mock = await start('node', ['server.mjs'], '../mock', /mock OpenAI-compatible server/)
const preview = await start('pnpm', ['exec', 'vite', 'preview', '--port', String(WEB_PORT), '--strictPort'], '.', /Local:/)

const browser = await chromium.launch()
try {
  const page = await browser.newPage()
  // vite preview binds to `localhost` (which may resolve to ::1 only).
  await page.goto(`http://localhost:${WEB_PORT}`, { waitUntil: 'networkidle' })

  // Point the UI at the mock server directly (preview has no dev proxy; mock has CORS enabled).
  await page.getByPlaceholder('baseURL').fill(BASE_URL)
  await page.getByPlaceholder('API Key（留空则用本地 mock）').fill('')
  await page.getByPlaceholder('model').fill('mock-gpt')
  await page.getByRole('button', { name: '应用配置' }).click()

  await page.getByPlaceholder('对 AI 市长说话…').fill('建一条路，然后跑 4 小时模拟')
  await page.getByRole('button', { name: '发送' }).click()

  await page.getByText('三步操作已完成').waitFor({ timeout: 20_000 })
  const body = await page.locator('body').innerText()
  const calls = (body.match(/工具调用:/g) ?? []).length

  console.log(`\n[browser e2e] PASS — ${calls} tool calls rendered, final answer streamed.`)
  await page.screenshot({ path: 'dist/e2e.png', fullPage: true })
}
finally {
  await browser.close()
  kill(preview)
  kill(mock)
}
