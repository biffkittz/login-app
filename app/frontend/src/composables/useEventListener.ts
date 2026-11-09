import { onBeforeUnmount, onMounted } from 'vue'

/**
 * Listens to an event on `window` or `document` for as long as the calling component is mounted.
 *
 * @param target - What to listen on.
 * @param type - The event name.
 * @param listener - The handler.
 */
export function useEventListener<TTarget extends Window | Document>(
  target: TTarget,
  type: string,
  listener: (event: Event) => void,
): void {
  onMounted(() => target.addEventListener(type, listener))
  onBeforeUnmount(() => target.removeEventListener(type, listener))
}
