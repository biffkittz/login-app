import {
  HttpTransportType,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
  type HubConnection,
} from '@microsoft/signalr'

import type { ConnectionState, GroceryListChange, GroceryListSettings } from '@/types/grocery'

/** The hub's path on this origin (`GroceryListHub.Path` in the backend). */
export const GROCERY_LIST_HUB_PATH = '/api/grocery-list/hub'

// Waits between connection attempts, backing off to every 30 seconds, forever: a phone that has been offline for an
// hour should still reconnect by itself. Even the first retry waits a second. A server that is shutting down closes
// every connection but goes on listening for a moment, and a client that came straight back would land on that same
// dying server, keeping it from exiting until the shutdown timeout (30 seconds) and believing itself live meanwhile.
const RETRY_DELAYS_MS = [1_000, 2_000, 5_000, 10_000, 20_000, 30_000] as const
const JITTER = 0.2

// After this many failed reconnection attempts (about 8 seconds), say "Offline" rather than "Reconnecting…": a blip
// is over by then, and the user should know the list may be out of date.
const ATTEMPTS_BEFORE_OFFLINE = 3

/** Callbacks through which the hub reports to its owner (the grocery list store). */
export interface GroceryListHubHandlers {
  /** The list changed: someone saved, moved or removed an item. */
  onChange: (change: GroceryListChange) => void
  /** Someone changed the list's settings, such as turning AI pictures off. */
  onSettingsChange: (settings: GroceryListSettings) => void
  /** The connection's state changed, e.g. to show "Reconnecting…". */
  onStateChange: (state: ConnectionState) => void
  /**
   * The connection was established or re-established. Changes made while it was down were never received, so this is
   * the moment to reload the list.
   */
  onConnected: () => void
}

/** A realtime connection that keeps the grocery list up to date, reconnecting by itself until stopped. */
export interface GroceryListHub {
  /** Connects, retrying until connected or stopped. Calling it again while running does nothing. */
  start: () => Promise<void>
  /** Disconnects and stops reconnecting. */
  stop: () => Promise<void>
  /**
   * Drops the connection and connects afresh. For when the page returns from the background: a phone may have
   * suspended the page long enough for the server to give up on the connection, without the page noticing yet.
   */
  restart: () => Promise<void>
}

/**
 * The wait before connection attempt number `attempt` (0-based), with ±20% random jitter so that many phones
 * reconnecting after a server restart don't all knock at the same moment.
 *
 * @param attempt - How many attempts have failed so far.
 * @param random - The random source; injectable for tests.
 */
export function retryDelay(attempt: number, random: () => number = Math.random): number {
  const base = RETRY_DELAYS_MS[Math.min(attempt, RETRY_DELAYS_MS.length - 1)] ?? 0
  return Math.round(base * (1 - JITTER + random() * 2 * JITTER))
}

/**
 * Creates the SignalR connection to `GroceryListHub`.
 *
 * - **WebSockets only, without negotiation.** That saves a round trip when connecting, which is noticeable on a phone
 *   network. Cloudflare and the Container Apps ingress both pass WebSockets through.
 * - **Authentication is the HttpOnly auth cookie.** The browser sends it with the WebSocket request because the hub
 *   is on this origin; no token ever passes through JavaScript.
 * - **Clients never send anything.** Changes go through the REST API (see `grocery-api.ts`); the hub only pushes.
 *
 * @param handlers - Receive changes and state updates.
 */
export function createGroceryListHub(handlers: GroceryListHubHandlers): GroceryListHub {
  const connection: HubConnection = new HubConnectionBuilder()
    .withUrl(GROCERY_LIST_HUB_PATH, {
      transport: HttpTransportType.WebSockets,
      skipNegotiation: true,
    })
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: ({ previousRetryCount }) => {
        if (previousRetryCount === ATTEMPTS_BEFORE_OFFLINE) {
          handlers.onStateChange('disconnected')
        }
        return retryDelay(previousRetryCount)
      },
    })
    .configureLogging(import.meta.env.DEV ? LogLevel.Information : LogLevel.Warning)
    .build()

  let running = false
  let connecting: Promise<void> | null = null
  let wakeFromRetryWait: (() => void) | null = null

  connection.on('ListChanged', (change: GroceryListChange) => handlers.onChange(change))
  connection.on('SettingsChanged', (settings: GroceryListSettings) =>
    handlers.onSettingsChange(settings),
  )
  connection.onreconnecting(() => handlers.onStateChange('reconnecting'))
  connection.onreconnected(() => {
    handlers.onStateChange('connected')
    handlers.onConnected()
  })
  // Automatic reconnection covers dropped connections; it doesn't cover a connection the server closed for good (an
  // expired session) or one restart() dropped deliberately. Keep trying while running: if the session really has
  // ended, the auth store signs the user out and the list's view stops the hub.
  connection.onclose(() => {
    if (running) {
      void connect()
    }
  })

  function waitBeforeRetry(milliseconds: number): Promise<void> {
    return new Promise((resolve) => {
      const timer = setTimeout(resolve, milliseconds)
      wakeFromRetryWait = () => {
        clearTimeout(timer)
        resolve()
      }
    })
  }

  // One attempt loop at a time; start() and the onclose handler share it.
  function connect(): Promise<void> {
    connecting ??= (async () => {
      for (
        let attempt = 0;
        running && connection.state === HubConnectionState.Disconnected;
        attempt++
      ) {
        handlers.onStateChange(attempt === 0 ? 'connecting' : 'reconnecting')
        try {
          await connection.start()
          handlers.onStateChange('connected')
          handlers.onConnected()
          return
        } catch {
          // Offline, server restarting, or stopped meanwhile: wait and try again (the loop condition handles "stopped").
          if (running) {
            handlers.onStateChange('disconnected')
            await waitBeforeRetry(retryDelay(attempt))
          }
        }
      }
    })().finally(() => {
      connecting = null
      wakeFromRetryWait = null
    })
    return connecting
  }

  return {
    start() {
      running = true
      return connect()
    },

    async stop() {
      running = false
      wakeFromRetryWait?.()
      await connection.stop()
      handlers.onStateChange('disconnected')
    },

    async restart() {
      if (!running) {
        return
      }
      if (connection.state === HubConnectionState.Disconnected) {
        // Already between attempts: try again now instead of waiting out the back-off.
        wakeFromRetryWait?.()
        await connect()
        return
      }
      // Stopping fires onclose, which starts connecting again.
      await connection.stop()
    },
  }
}
