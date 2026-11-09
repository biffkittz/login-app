<script setup lang="ts">
import type { Toast } from '@/composables/useToast'

interface Props {
  /** The toast to show, or `null` for none. */
  toast: Toast | null
}

const { toast } = defineProps<Props>()

const emit = defineEmits<{
  dismiss: []
  /** The user is pointing at or tabbing through the toast: stop its countdown. */
  pause: []
  resume: []
}>()

function runAction(): void {
  toast?.action?.run()
  emit('dismiss')
}
</script>

<template>
  <!-- The live region always exists, so screen readers notice when a message appears in it. -->
  <div
    class="pointer-events-none fixed inset-x-0 bottom-0 z-30 flex justify-center px-4 pb-[max(1rem,env(safe-area-inset-bottom))]"
    :aria-live="toast?.tone === 'error' ? 'assertive' : 'polite'"
    aria-atomic="true"
  >
    <div
      v-if="toast"
      :key="toast.id"
      class="pointer-events-auto flex w-full max-w-md items-center gap-2 rounded-2xl border-2 py-2 pr-2 pl-4 shadow-[4px_4px_0_0_var(--color-walnut-900)] motion-safe:animate-toast-in"
      :class="
        toast.tone === 'error'
          ? 'border-brick-700 bg-cream-50 text-brick-700'
          : 'border-walnut-900 bg-walnut-900 text-cream-50'
      "
      @pointerenter="emit('pause')"
      @pointerleave="emit('resume')"
      @focusin="emit('pause')"
      @focusout="emit('resume')"
    >
      <p class="flex-1 py-1.5 text-sm font-medium">{{ toast.message }}</p>
      <button
        v-if="toast.action"
        type="button"
        class="rounded-full px-3 py-2 font-display text-sm font-bold tracking-wide underline decoration-2 underline-offset-4 focus-visible:outline-3 focus-visible:outline-offset-1 focus-visible:outline-ember-400"
        :class="toast.tone === 'error' ? 'text-brick-700' : 'text-ember-300'"
        @click="runAction"
      >
        {{ toast.action.label }}
      </button>
      <button
        type="button"
        class="grid size-9 shrink-0 place-items-center rounded-full focus-visible:outline-3 focus-visible:outline-offset-1 focus-visible:outline-ember-400"
        @click="emit('dismiss')"
      >
        <svg class="size-4" viewBox="0 0 24 24" fill="none" aria-hidden="true">
          <path
            d="M6 6l12 12M18 6L6 18"
            stroke="currentColor"
            stroke-width="2.5"
            stroke-linecap="round"
          />
        </svg>
        <span class="sr-only">Dismiss</span>
      </button>
    </div>
  </div>
</template>
