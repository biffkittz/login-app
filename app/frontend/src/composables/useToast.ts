import { computed, getCurrentScope, onScopeDispose, shallowRef, type ComputedRef } from 'vue'

/** A short message at the bottom of the screen, optionally with one action such as "Undo". */
export interface Toast {
  /** Changes on every show, so the toast component can restart its countdown and announcement. */
  id: number
  message: string
  /** `error` interrupts screen readers immediately; `info` waits politely. */
  tone: 'info' | 'error'
  action?: { label: string; run: () => void }
}

/** Options for {@link ToastController.show}. */
export interface ToastOptions {
  tone?: Toast['tone']
  action?: Toast['action']
  /** How long the toast stays up, in milliseconds. */
  durationMs?: number
}

/** The state and controls returned by {@link useToast}. */
export interface ToastController {
  /** The toast on screen, or `null`. */
  toast: ComputedRef<Toast | null>
  /** Shows a toast, replacing any other. */
  show: (message: string, options?: ToastOptions) => void
  /** Hides the toast now. */
  dismiss: () => void
  /** Stops the countdown while the user is interacting with the toast (hover or keyboard focus). */
  pause: () => void
  /** Restarts the countdown. */
  resume: () => void
}

// Long enough to read a short sentence and reach the Undo button. The countdown pauses while the user hovers or focuses
// the toast, so nobody using a screen reader or a keyboard is rushed (WCAG 2.2.1).
const DEFAULT_DURATION_MS = 6_000

/**
 * One toast at a time, which hides itself after a while.
 */
export function useToast(): ToastController {
  const toast = shallowRef<Toast | null>(null)
  let nextId = 1
  let timer: ReturnType<typeof setTimeout> | undefined
  let durationMs = DEFAULT_DURATION_MS

  function dismiss(): void {
    clearTimeout(timer)
    toast.value = null
  }

  function resume(): void {
    clearTimeout(timer)
    if (toast.value !== null) {
      timer = setTimeout(dismiss, durationMs)
    }
  }

  function show(message: string, options: ToastOptions = {}): void {
    durationMs = options.durationMs ?? DEFAULT_DURATION_MS
    toast.value = { id: nextId++, message, tone: options.tone ?? 'info', action: options.action }
    resume()
  }

  if (getCurrentScope()) {
    onScopeDispose(() => clearTimeout(timer))
  }

  return {
    toast: computed(() => toast.value),
    show,
    dismiss,
    pause: () => clearTimeout(timer),
    resume,
  }
}
