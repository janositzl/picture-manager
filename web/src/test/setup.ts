import '@testing-library/jest-dom/vitest'
import { cleanup, configure } from '@testing-library/react'
import { afterAll, afterEach, beforeAll, beforeEach } from 'vitest'
import { resetAdminStore } from './adminHandlers'
import { resetAlbumStore } from './albumHandlers'
import { resetAuthStore } from './authHandlers'
import { resetHiddenStore } from './handlers'
import { server } from './server'
import { resetUserStore } from './userHandlers'

// Default findBy* timeout (1000ms) is tight when several test files run in parallel on a loaded
// machine; a query that would resolve in well under a second in isolation can be pushed past it
// purely by CPU contention. This doesn't loosen what's asserted, only how long an async query waits.
configure({ asyncUtilTimeout: 5000 })

beforeAll(() => server.listen({ onUnhandledRequest: 'error' }))
beforeEach(() => {
  resetAuthStore()
  resetAlbumStore()
  resetAdminStore()
  resetUserStore()
  resetHiddenStore()
})
afterEach(() => {
  server.resetHandlers()
  cleanup()
  localStorage.clear()
})
afterAll(() => server.close())

// jsdom gaps the app relies on.
if (!('ResizeObserver' in globalThis)) {
  class ResizeObserverStub {
    observe() {}
    unobserve() {}
    disconnect() {}
  }
  globalThis.ResizeObserver = ResizeObserverStub as unknown as typeof ResizeObserver
}

Object.defineProperty(window, 'matchMedia', {
  writable: true,
  value: (query: string) => ({
    matches: false,
    media: query,
    onchange: null,
    addEventListener() {},
    removeEventListener() {},
    addListener() {},
    removeListener() {},
    dispatchEvent: () => false,
  }),
})

Element.prototype.scrollIntoView = function scrollIntoView() {}

// jsdom lays nothing out, so give every element a desktop-sized box; the grid virtualizer
// measures its scroll container and would otherwise render no rows.
Object.defineProperties(HTMLElement.prototype, {
  offsetWidth: { configurable: true, get: () => 1200 },
  offsetHeight: { configurable: true, get: () => 800 },
  clientWidth: { configurable: true, get: () => 1200 },
  clientHeight: { configurable: true, get: () => 800 },
})
HTMLElement.prototype.getBoundingClientRect = function getBoundingClientRect() {
  return {
    x: 0,
    y: 0,
    top: 0,
    left: 0,
    right: 1200,
    bottom: 800,
    width: 1200,
    height: 800,
    toJSON: () => ({}),
  } as DOMRect
}
