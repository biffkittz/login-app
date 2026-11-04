import { nextTick, type Ref } from 'vue'

/**
 * Returns a function that moves keyboard focus to the first invalid field of a form.
 *
 * Call it after a failed submit: screen readers then announce the field's label together with its error
 * (linked through `aria-describedby`), and keyboard users land exactly where they need to type.
 *
 * @param form - Template ref to the `<form>` element.
 */
export function useFocusFirstInvalidField(
  form: Readonly<Ref<HTMLElement | null>>,
): () => Promise<void> {
  return async () => {
    // Wait for Vue to render the new error state (aria-invalid) before looking for it.
    await nextTick()
    form.value?.querySelector<HTMLElement>('[aria-invalid="true"]')?.focus()
  }
}
