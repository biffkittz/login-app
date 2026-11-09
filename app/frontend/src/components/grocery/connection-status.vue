<script setup lang="ts">
import { computed } from 'vue'

import type { ConnectionState } from '@/types/grocery'

interface Props {
  state: ConnectionState
}

const { state } = defineProps<Props>()

const STATUS: Record<
  ConnectionState,
  { label: string; detail: string; dot: string; tone: string }
> = {
  connected: {
    label: 'Live',
    detail: 'Changes from everyone appear as they happen.',
    dot: 'bg-sage-700',
    tone: 'border-sage-700 text-sage-700',
  },
  connecting: {
    label: 'Connecting…',
    detail: 'Connecting for live updates.',
    dot: 'bg-mustard-400 motion-safe:animate-pulse',
    tone: 'border-walnut-500 text-walnut-700',
  },
  reconnecting: {
    label: 'Reconnecting…',
    detail: 'The connection dropped. Reconnecting for live updates.',
    dot: 'bg-mustard-400 motion-safe:animate-pulse',
    tone: 'border-walnut-500 text-walnut-700',
  },
  disconnected: {
    label: 'Offline',
    detail: "Not receiving live updates. The list may be out of date until you're back online.",
    dot: 'bg-brick-700',
    tone: 'border-brick-700 text-brick-700',
  },
}

const status = computed(() => STATUS[state])
</script>

<template>
  <!-- role="status" announces changes politely, such as dropping to "Offline". -->
  <p
    role="status"
    :title="status.detail"
    class="inline-flex shrink-0 items-center gap-2 rounded-full border-2 bg-cream-50 px-3 py-1 font-display text-xs font-bold tracking-wide"
    :class="status.tone"
  >
    <span class="size-2 rounded-full" :class="status.dot" aria-hidden="true" />
    {{ status.label }}
    <span class="sr-only">. {{ status.detail }}</span>
  </p>
</template>
