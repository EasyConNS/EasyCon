import { test, expect, type Page } from '@playwright/test'

const blank = () => ({ phase: 'disconnected', id: null as string | null, detail: '', error: null as string | null })

async function backend(page: Page) {
  const state = { gamepad: blank(), video: blank(), audio: blank(), rms: 0, dbfs: null as number | null }
  const selected: string[] = []
  const videoRequests: string[] = []
  let failGamepad = false
  // Test-only transport fixtures. The shipped application always calls the Rust service.
  await page.route('**/api/**', async route => {
    const url = new URL(route.request().url())
    const kind = url.pathname.split('/').pop() as 'gamepad' | 'video' | 'audio'
    const json = (value: unknown, status = 200) => route.fulfill({ status, json: value })
    if (url.pathname === '/api/state') return json(state)
    if (url.pathname.startsWith('/api/devices/')) {
      return json({ gamepad: [{ id: 'COM3', name: 'COM3' }], video: [{ id: 'index:0', name: 'USB Capture HDMI' }], audio: [{ id: 'audio:0', name: 'USB Capture Audio' }] }[kind])
    }
    if (url.pathname.startsWith('/api/connections/')) {
      if (route.request().method() === 'DELETE') {
        state[kind] = blank()
        if (kind === 'audio') { state.rms = 0; state.dbfs = null }
      } else {
        if (kind === 'gamepad' && failGamepad) return json({ error: '串口被占用，请关闭其他程序' }, 409)
        state[kind] = { phase: 'connected', id: route.request().postDataJSON().id, detail: { gamepad: 'EasyCon · 115200 baud', video: '1920 × 1080 · 30 fps', audio: '48000 Hz · 2 声道' }[kind], error: null }
        if (kind === 'audio') { state.rms = 0.1; state.dbfs = -20 }
      }
      return route.fulfill({ status: 204 })
    }
    if (url.pathname === '/api/files') {
      return json({ directory: '/projects/宝可梦', parent: '/projects', entries: [{ name: 'workflow.json', path: '/projects/宝可梦/workflow.json', directory: false }] })
    }
    if (url.pathname === '/api/workflow') {
      selected.push(route.request().postDataJSON().path)
      return json({ path: '/projects/宝可梦/workflow.json', preferences: { gamepad: 'COM3', video: 'index:0' } })
    }
    if (url.pathname === '/api/video/stream') {
      videoRequests.push(route.request().url())
      const image = Buffer.from('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aH1sAAAAASUVORK5CYII=', 'base64')
      const part = Buffer.concat([Buffer.from(`--easycon-frame\r\nContent-Type: image/png\r\nContent-Length: ${image.length}\r\n\r\n`), image, Buffer.from('\r\n')])
      return route.fulfill({ status: 200, contentType: 'multipart/x-mixed-replace; boundary=easycon-frame', body: Buffer.concat([part, part, Buffer.from('--easycon-frame--\r\n')]) })
    }
    return route.fulfill({ status: 404 })
  })
  return { state, selected, videoRequests, failGamepad: () => { failGamepad = true } }
}

test('selects a validated workflow and displays its full real path', async ({ page }) => {
  const fixture = await backend(page)
  await page.goto('/')
  await page.getByRole('button', { name: '选择文件' }).click()
  await expect(page.getByRole('dialog')).toBeVisible()
  await page.getByRole('button', { name: 'workflow.json JSON' }).click()
  await expect(page.getByRole('dialog')).not.toBeVisible()
  await expect(page.locator('.workflow-path')).toHaveText('/projects/宝可梦/workflow.json')
  await expect(page.getByText('已校验', { exact: true })).toBeVisible()
  expect(fixture.selected).toEqual(['/projects/宝可梦/workflow.json'])
})

test('all three cards connect, preview and meter update, then disconnect', async ({ page }) => {
  await backend(page)
  await page.goto('/')
  for (const kind of ['gamepad', 'video', 'audio']) {
    const card = page.locator(`.card-${kind}`)
    await card.getByRole('button', { name: '连接', exact: true }).click()
    await expect(card.getByText('已连接', { exact: true })).toBeVisible()
    await expect(card.getByRole('combobox')).toBeDisabled()
  }
  await expect(page.getByAltText('视频源实时画面')).toBeVisible()
  await expect(page.getByRole('meter')).toHaveAttribute('aria-valuenow', '-20')
  await expect(page.locator('.meter-reading strong')).toHaveText('-20.0')
  await expect(page.locator('.section-heading strong')).toHaveText('03')
  for (const kind of ['gamepad', 'video', 'audio']) {
    const card = page.locator(`.card-${kind}`)
    await card.getByRole('button', { name: '断开', exact: true }).click()
    await expect(card.getByText('未连接', { exact: true })).toBeVisible()
  }
  await expect(page.getByAltText('视频源实时画面')).toHaveCount(0)
  await expect(page.getByRole('meter')).toHaveAttribute('aria-valuenow', '-60')
})

test('connection errors remain local and allow a retry', async ({ page }) => {
  const fixture = await backend(page)
  fixture.failGamepad()
  await page.goto('/')
  const pad = page.locator('.card-gamepad')
  await pad.getByRole('button', { name: '连接', exact: true }).click()
  await expect(pad.getByRole('alert')).toHaveText('串口被占用，请关闭其他程序')
  await expect(pad.getByRole('button', { name: '连接', exact: true })).toBeEnabled()
  await page.locator('.card-video').getByRole('button', { name: '连接', exact: true }).click()
  await expect(page.locator('.card-video .status')).toHaveText('已连接')
})

test('monitor enlarges the same stream, Escape returns it, hidden pages pause it', async ({ page }) => {
  const fixture = await backend(page)
  await page.goto('/')
  await page.locator('.card-video').getByRole('button', { name: '连接', exact: true }).click()
  const image = page.getByAltText('视频源实时画面')
  await expect(image).toBeVisible()
  await expect.poll(() => fixture.videoRequests.length).toBe(1)
  const source = await image.getAttribute('src')
  await page.getByRole('button', { name: '打开视频监视窗口' }).click()
  await expect(page.getByRole('dialog', { name: '视频监视' })).toBeVisible()
  await expect(page.locator('.video-dialog img')).toHaveCount(1)
  expect(await image.getAttribute('src')).toBe(source)
  expect(fixture.videoRequests).toHaveLength(1)
  await page.keyboard.press('Escape')
  await expect(page.getByRole('dialog', { name: '视频监视' })).not.toBeVisible()
  await expect(page.locator('.card-video img')).toHaveCount(1)
  expect(fixture.videoRequests).toHaveLength(1)
  await page.evaluate(() => {
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'hidden' })
    document.dispatchEvent(new Event('visibilitychange'))
  })
  await expect(image).toHaveCount(0)
  await page.evaluate(() => {
    Object.defineProperty(document, 'visibilityState', { configurable: true, value: 'visible' })
    document.dispatchEvent(new Event('visibilitychange'))
  })
  await expect(image).toBeVisible()
  await expect.poll(() => fixture.videoRequests.length).toBe(2)
  await page.getByRole('button', { name: '打开视频监视窗口' }).click()
  fixture.state.video = blank()
  await expect(page.getByRole('dialog', { name: '视频监视' })).not.toBeVisible()
  await expect(image).toHaveCount(0)
})

test('offline state clears live indicators and disables connection controls', async ({ page }) => {
  await backend(page)
  await page.goto('/')
  await page.locator('.card-audio').getByRole('button', { name: '连接', exact: true }).click()
  await expect(page.getByRole('meter')).toHaveAttribute('aria-valuenow', '-20')
  await page.route('**/api/state', route => route.abort())
  await expect(page.locator('.host-status')).toContainText('本地服务离线')
  await expect(page.getByRole('meter')).toHaveAttribute('aria-valuenow', '-60')
  await expect(page.locator('.card-video').getByRole('button', { name: '连接', exact: true })).toBeDisabled()
})

for (const width of [1440, 375]) {
  test(`layout remains usable at ${width}px`, async ({ page }) => {
    await page.setViewportSize({ width, height: 1050 })
    await backend(page)
    await page.goto('/')
    await expect(page.locator('.host-status')).toContainText('本地服务已就绪')
    await expect(page.locator('.device-card')).toHaveCount(3)
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth)).toBe(true)
    await page.screenshot({ path: `test-results/console-${width}.png`, fullPage: true })
    await page.locator('.card-video').getByRole('button', { name: '连接', exact: true }).click()
    await page.getByRole('button', { name: '打开视频监视窗口' }).click()
    const dialog = page.getByRole('dialog', { name: '视频监视' })
    await expect(dialog).toBeVisible()
    const bounds = await dialog.boundingBox()
    expect(bounds).not.toBeNull()
    expect(bounds!.x).toBeGreaterThanOrEqual(0)
    expect(bounds!.x + bounds!.width).toBeLessThanOrEqual(width)
    expect(await dialog.evaluate(element => element.scrollWidth <= element.clientWidth)).toBe(true)
    await page.screenshot({ path: `test-results/monitor-${width}.png`, fullPage: true })
  })
}
