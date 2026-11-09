<script setup lang="ts">
import { onErrorCaptured, ref } from 'vue'

import BaseButton from '@/components/ui/base-button.vue'
import { vFocusOnMount } from '@/directives/focus-on-mount'

defineSlots<{
  /** The content to protect. */
  default: () => unknown
}>()

const hasFailed = ref(false)

// A local error boundary: if a page throws while rendering, show a friendly fallback in its place instead of a
// blank screen. Returning false marks the error as handled so it doesn't propagate further.
onErrorCaptured((error) => {
  console.error('[nibbles] A page failed to render:', error)
  hasFailed.value = true
  return false
})
</script>

<template>
  <section
    v-if="hasFailed"
    class="w-full max-w-md rounded-[2rem] border-2 border-walnut-800 bg-cream-100 px-6 py-8 text-center shadow-[8px_8px_0_0_var(--color-walnut-800)]"
    aria-labelledby="error-boundary-title"
  >
    <h1
      id="error-boundary-title"
      v-focus-on-mount
      tabindex="-1"
      class="font-display text-2xl font-bold text-walnut-900 focus:outline-none"
    >
      Something went sideways.
    </h1>
    <p class="mt-2 text-walnut-700">This page hit a snag. Trying again usually sorts it out.</p>
    <BaseButton class="mt-6" @click="hasFailed = false">Try again</BaseButton>
  </section>
  <slot v-else />
</template>
