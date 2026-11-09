import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import type { ConnectionState, GroceryListChange, GroceryListSettings } from '@/types/grocery'

import { createGroceryListHub, retryDelay, type GroceryListHubHandlers } from '../grocery-list-hub'

// A stand-in for SignalR's HubConnection that the tests drive by hand.
const fake = vi.hoisted(() => {
  const connection = {
    state: 'Disconnected' as 'Disconnected' | 'Connected',
    messageHandlers: new Map<string, (payload: unknown) => void>(),
    closeHandler: undefined as (() => void) | undefined,
    reconnectedHandler: undefined as (() => void) | undefined,
    failNextStarts: 0,
    start: vi.fn(),
    stop: vi.fn(),
  }
  connection.start.mockImplementation(async () => {
    if (connection.failNextStarts > 0) {
      connection.failNextStarts--
      throw new Error('WebSocket failed to connect.')
    }
    connection.state = 'Connected'
  })
  connection.stop.mockImplementation(async () => {
    connection.state = 'Disconnected'
    connection.closeHandler?.()
  })
  return connection
})

vi.mock('@microsoft/signalr', () => {
  class HubConnectionBuilder {
    withUrl(): this {
      return this
    }
    withAutomaticReconnect(): this {
      return this
    }
    configureLogging(): this {
      return this
    }
    build() {
      return {
        get state() {
          return fake.state
        },
        on: (name: string, handler: (payload: unknown) => void) =>
          fake.messageHandlers.set(name, handler),
        onreconnecting: () => {},
        onreconnected: (handler: () => void) => (fake.reconnectedHandler = handler),
        onclose: (handler: () => void) => (fake.closeHandler = handler),
        start: fake.start,
        stop: fake.stop,
      }
    }
  }
  return {
    HubConnectionBuilder,
    HubConnectionState: { Disconnected: 'Disconnected', Connected: 'Connected' },
    HttpTransportType: { WebSockets: 1 },
    LogLevel: { Information: 2, Warning: 3 },
  }
})

function recordingHandlers(): GroceryListHubHandlers & { states: ConnectionState[] } {
  const states: ConnectionState[] = []
  return {
    states,
    onChange: vi.fn(),
    onSettingsChange: vi.fn(),
    onStateChange: (state) => states.push(state),
    onConnected: vi.fn(),
  }
}

describe('retryDelay', () => {
  it('backs off from a second to every 30 seconds', () => {
    const noJitter = () => 0.5
    expect([0, 1, 2, 3, 4, 5, 50].map((attempt) => retryDelay(attempt, noJitter))).toEqual([
      1_000, 2_000, 5_000, 10_000, 20_000, 30_000, 30_000,
    ])
  })

  it('spreads attempts by up to 20% either way', () => {
    expect(retryDelay(5, () => 0)).toBe(24_000)
    expect(retryDelay(5, () => 1)).toBe(36_000)
  })
})

describe('createGroceryListHub', () => {
  beforeEach(() => {
    fake.state = 'Disconnected'
    fake.failNextStarts = 0
    fake.messageHandlers.clear()
    fake.start.mockClear()
    fake.stop.mockClear()
  })

  afterEach(() => {
    vi.useRealTimers()
  })

  it('connects, reports it, and asks for a reload', async () => {
    const handlers = recordingHandlers()

    await createGroceryListHub(handlers).start()

    expect(handlers.states).toEqual(['connecting', 'connected'])
    expect(handlers.onConnected).toHaveBeenCalledOnce()
  })

  it('passes pushed changes on', async () => {
    const handlers = recordingHandlers()
    await createGroceryListHub(handlers).start()
    const change: GroceryListChange = { savedItems: [], deletedItemIds: ['gone'] }

    fake.messageHandlers.get('ListChanged')?.(change)

    expect(handlers.onChange).toHaveBeenCalledWith(change)
  })

  it('passes pushed settings on', async () => {
    const handlers = recordingHandlers()
    await createGroceryListHub(handlers).start()
    const settings: GroceryListSettings = {
      version: 2,
      imageGenerationEnabled: false,
      imageGenerationAvailable: true,
    }

    fake.messageHandlers.get('SettingsChanged')?.(settings)

    expect(handlers.onSettingsChange).toHaveBeenCalledWith(settings)
  })

  it('keeps trying until the server can be reached', async () => {
    vi.useFakeTimers()
    fake.failNextStarts = 2
    const handlers = recordingHandlers()

    const starting = createGroceryListHub(handlers).start()
    await vi.runAllTimersAsync()
    await starting

    expect(fake.start).toHaveBeenCalledTimes(3)
    expect(handlers.states.at(-1)).toBe('connected')
    expect(handlers.onConnected).toHaveBeenCalledOnce()
  })

  it('stops trying once stopped', async () => {
    vi.useFakeTimers()
    fake.failNextStarts = 100
    const handlers = recordingHandlers()
    const hub = createGroceryListHub(handlers)

    const starting = hub.start()
    await vi.advanceTimersByTimeAsync(1_500)
    await hub.stop()
    await vi.runAllTimersAsync()
    await starting

    const attempts = fake.start.mock.calls.length
    await vi.advanceTimersByTimeAsync(120_000)
    expect(fake.start).toHaveBeenCalledTimes(attempts)
    expect(handlers.states.at(-1)).toBe('disconnected')
  })

  it('reconnects afresh when restarted, and reloads again', async () => {
    const handlers = recordingHandlers()
    const hub = createGroceryListHub(handlers)
    await hub.start()

    await hub.restart()
    await vi.waitFor(() => expect(handlers.onConnected).toHaveBeenCalledTimes(2))

    expect(fake.stop).toHaveBeenCalledOnce()
    expect(fake.state).toBe('Connected')
  })

  it('reloads after SignalR re-establishes a dropped connection by itself', async () => {
    const handlers = recordingHandlers()
    await createGroceryListHub(handlers).start()

    fake.reconnectedHandler?.()

    expect(handlers.onConnected).toHaveBeenCalledTimes(2)
    expect(handlers.states.at(-1)).toBe('connected')
  })
})
