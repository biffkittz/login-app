import type { Directive } from 'vue'

/**
 * `v-focus-on-mount`: focuses the element once it is mounted, unless bound to `false`.
 *
 * A client-side route change, unlike a full page load, leaves focus on whatever was clicked. Moving it to the
 * start of the new page (its heading or first field) tells screen-reader users that the page changed and saves
 * keyboard users from tabbing through the header again.
 */
export const vFocusOnMount: Directive<HTMLElement, boolean | undefined> = {
  mounted(element, binding) {
    if (binding.value !== false) {
      element.focus()
    }
  },
}
