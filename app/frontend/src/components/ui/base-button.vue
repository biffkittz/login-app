<script setup lang="ts">
interface Props {
  /** `primary` for the single main action on a page, `secondary` for everything else. */
  variant?: 'primary' | 'secondary'
  type?: 'button' | 'submit'
  /**
   * Shows a spinner and ignores clicks while work is in progress. The button deliberately stays focusable
   * (`aria-disabled` instead of `disabled`), so keyboard and screen-reader users don't lose their place.
   */
  loading?: boolean
  /** Stretches the button to the full width of its container. */
  block?: boolean
}

const {
  variant = 'primary',
  type = 'button',
  loading = false,
  block = false,
} = defineProps<Props>()

defineSlots<{
  /** The button label. */
  default: () => unknown
}>()

// Runs in the capture phase so it can stop the parent's click handler, and, for submit buttons, the form
// submission, while a previous action is still in flight.
function blockWhileLoading(event: MouseEvent): void {
  if (loading) {
    event.preventDefault()
    event.stopImmediatePropagation()
  }
}
</script>

<template>
  <button
    :type="type"
    :aria-disabled="loading || undefined"
    :class="[
      'inline-flex items-center justify-center gap-2 rounded-full px-6 py-3 font-display text-base font-semibold tracking-wide transition motion-reduce:transition-none',
      'focus-visible:outline-3 focus-visible:outline-offset-3 focus-visible:outline-ember-600',
      variant === 'primary'
        ? 'bg-ember-700 text-cream-50 shadow-[4px_4px_0_0_var(--color-walnut-900)] hover:bg-ember-800 active:translate-x-0.5 active:translate-y-0.5 active:shadow-[2px_2px_0_0_var(--color-walnut-900)]'
        : 'border-2 border-walnut-800 bg-cream-50 text-walnut-900 hover:bg-cream-200',
      { 'w-full': block, 'cursor-wait opacity-85': loading },
    ]"
    @click.capture="blockWhileLoading"
  >
    <svg
      v-if="loading"
      class="size-5 motion-safe:animate-spin"
      viewBox="0 0 24 24"
      fill="none"
      aria-hidden="true"
    >
      <circle cx="12" cy="12" r="9" stroke="currentColor" stroke-opacity="0.3" stroke-width="3" />
      <path
        d="M21 12a9 9 0 0 0-9-9"
        stroke="currentColor"
        stroke-width="3"
        stroke-linecap="round"
      />
    </svg>
    <slot />
  </button>
</template>
