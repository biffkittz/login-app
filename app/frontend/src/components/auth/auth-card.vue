<script setup lang="ts">
import { useId } from 'vue'

import StarburstMark from '@/components/brand/starburst-mark.vue'
import { vFocusOnMount } from '@/directives/focus-on-mount'

interface Props {
  /** A short kicker above the title, such as "Welcome back". */
  eyebrow?: string
  title: string
  subtitle?: string
  /**
   * Moves focus to the title when the card appears. Use on pages without a form field to focus, so screen
   * readers announce the new page.
   */
  focusTitle?: boolean
}

const { eyebrow, title, subtitle, focusTitle = false } = defineProps<Props>()

defineSlots<{
  /** The card body: a form or a message. */
  default: () => unknown
  /** Secondary content under a divider, such as a link to the other auth page. */
  footer?: () => unknown
}>()

const titleId = useId()
</script>

<template>
  <section
    :aria-labelledby="titleId"
    class="relative w-full max-w-md rounded-[2rem] border-2 border-walnut-800 bg-cream-100 px-6 py-8 shadow-[8px_8px_0_0_var(--color-walnut-800)] sm:px-10 sm:py-10"
  >
    <StarburstMark class="absolute -right-5 -top-5 size-12 rotate-12" />

    <header class="mb-7">
      <p
        v-if="eyebrow"
        class="mb-2 flex items-center gap-2 font-display text-xs font-semibold uppercase tracking-[0.2em] text-ember-700"
      >
        <span class="inline-block size-2 rounded-full bg-mustard-400" aria-hidden="true" />
        {{ eyebrow }}
      </p>
      <h1
        :id="titleId"
        v-focus-on-mount="focusTitle"
        :tabindex="focusTitle ? -1 : undefined"
        class="font-display text-3xl leading-tight font-bold text-balance text-walnut-900 focus:outline-none"
      >
        {{ title }}
      </h1>
      <p v-if="subtitle" class="mt-2 text-walnut-700">{{ subtitle }}</p>
    </header>

    <slot />

    <footer
      v-if="$slots.footer"
      class="mt-8 border-t-2 border-dashed border-cream-300 pt-6 text-center text-sm text-walnut-700"
    >
      <slot name="footer" />
    </footer>
  </section>
</template>
