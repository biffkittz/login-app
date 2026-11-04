<script setup lang="ts">
interface Props {
  /**
   * `error` interrupts screen readers immediately (role="alert"); `success` and `info` wait politely for a pause
   * (role="status").
   */
  tone?: 'error' | 'success' | 'info'
}

const { tone = 'error' } = defineProps<Props>()

defineSlots<{
  /** The message. */
  default: () => unknown
}>()
</script>

<template>
  <div
    :role="tone === 'error' ? 'alert' : 'status'"
    :class="[
      'flex items-start gap-3 rounded-2xl border-2 bg-cream-50 px-4 py-3 text-sm font-medium',
      {
        'border-brick-700 text-brick-700': tone === 'error',
        'border-sage-700 text-sage-700': tone === 'success',
        'border-walnut-500 text-walnut-800': tone === 'info',
      },
    ]"
  >
    <svg class="mt-px size-5 shrink-0" viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
      <path
        v-if="tone === 'success'"
        fill-rule="evenodd"
        d="M10 18a8 8 0 1 0 0-16 8 8 0 0 0 0 16Zm3.857-9.809a.75.75 0 0 0-1.214-.882l-3.483 4.79-1.88-1.88a.75.75 0 1 0-1.06 1.061l2.5 2.5a.75.75 0 0 0 1.137-.089l4-5.5Z"
        clip-rule="evenodd"
      />
      <path
        v-else
        fill-rule="evenodd"
        d="M10 18a8 8 0 1 0 0-16 8 8 0 0 0 0 16Zm.75-11.75a.75.75 0 0 0-1.5 0v4.5a.75.75 0 0 0 1.5 0v-4.5ZM10 15a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z"
        clip-rule="evenodd"
      />
    </svg>
    <p><slot /></p>
  </div>
</template>
