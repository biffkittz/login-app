<script setup lang="ts">
import { computed } from 'vue'

import BaseButton from '@/components/ui/base-button.vue'
import BottomSheet from '@/components/ui/bottom-sheet.vue'
import FormAlert from '@/components/ui/form-alert.vue'
import type { CostEstimate } from '@/types/grocery'
import { formatUsd } from '@/utils/grocery-format'

interface Props {
  /** The latest estimate, or `null` while there is none. */
  estimate: CostEstimate | null
  /** An estimate is on its way. */
  loading?: boolean
  /** Why the latest estimate failed. */
  error?: string | null
  /** The server can estimate at all (it has a Gemini API key). */
  available: boolean
  /**
   * Item IDs in the order the list shows them (grouped by store). The estimate comes in the list's own order;
   * following the screen makes it easy to compare. Items not named here (added since) go last.
   */
  order?: readonly string[]
}

const { estimate, loading = false, error = null, available, order = [] } = defineProps<Props>()

/** Whether the sheet is open (two-way bound with `v-model:open`). */
const open = defineModel<boolean>('open', { required: true })

const emit = defineEmits<{
  /** Ask for a new estimate: after a failure, or once the list has changed. */
  estimate: []
}>()

const lines = computed(() => {
  if (estimate === null) {
    return []
  }
  const place = new Map(order.map((id, index) => [id, index]))
  return estimate.items
    .map((line, index) => ({ line, rank: place.get(line.itemId) ?? order.length + index }))
    .sort((a, b) => a.rank - b.rank)
    .map(({ line }) => line)
})

const unpriced = computed(() =>
  estimate === null ? 0 : estimate.items.length - estimate.estimatedItemCount,
)

const coverage = computed(() => {
  if (estimate === null) {
    return ''
  }
  const count = estimate.items.length
  const items = count === 1 ? '1 item' : `${count} items`
  return unpriced.value === 0
    ? `for ${items}`
    : `for ${estimate.estimatedItemCount} of ${items}; ${unpriced.value} couldn't be priced`
})
</script>

<template>
  <BottomSheet v-model:open="open" title="Estimated cost">
    <div
      class="flex flex-col gap-4 overflow-y-auto px-5 pt-4 pb-[max(1.25rem,env(safe-area-inset-bottom))]"
    >
      <FormAlert v-if="!available" tone="info">
        Cost estimates aren't set up on this server: it needs a Gemini API key.
      </FormAlert>

      <div
        v-else-if="loading"
        class="flex items-center justify-center gap-3 py-10 text-walnut-700"
        role="status"
      >
        <svg
          class="size-5 motion-safe:animate-spin"
          viewBox="0 0 24 24"
          fill="none"
          aria-hidden="true"
        >
          <circle
            cx="12"
            cy="12"
            r="9"
            stroke="currentColor"
            stroke-opacity="0.3"
            stroke-width="3"
          />
          <path
            d="M21 12a9 9 0 0 0-9-9"
            stroke="currentColor"
            stroke-width="3"
            stroke-linecap="round"
          />
        </svg>
        Pricing the list…
      </div>

      <template v-else-if="error !== null">
        <FormAlert>{{ error }}</FormAlert>
        <BaseButton variant="secondary" block @click="emit('estimate')">Try again</BaseButton>
      </template>

      <template v-else-if="estimate !== null">
        <div role="status" class="text-center">
          <p class="font-display text-4xl font-bold text-walnut-900" data-total>
            {{ formatUsd(estimate.totalUsd) }}
          </p>
          <p class="mt-1 text-sm text-walnut-700">{{ coverage }}</p>
        </div>

        <ul
          class="divide-y-2 divide-dashed divide-cream-300 rounded-2xl border-2 border-walnut-800 bg-cream-50"
          aria-label="Estimate by item"
        >
          <li
            v-for="line in lines"
            :key="line.itemId"
            class="flex items-start justify-between gap-3 px-4 py-2.5"
          >
            <div class="min-w-0">
              <p class="font-medium break-words text-walnut-900">
                {{ line.name
                }}<span v-if="line.quantity" class="font-normal text-walnut-700">
                  · {{ line.quantity }}</span
                >
              </p>
              <p v-if="line.basis" class="text-sm break-words text-walnut-700">{{ line.basis }}</p>
            </div>
            <p
              class="shrink-0 font-semibold tabular-nums"
              :class="line.estimatedUsd === null ? 'text-walnut-500' : 'text-walnut-900'"
            >
              {{ line.estimatedUsd === null ? 'No estimate' : formatUsd(line.estimatedUsd) }}
            </p>
          </li>
        </ul>

        <p v-if="estimate.note" class="text-sm text-walnut-800 italic">{{ estimate.note }}</p>

        <p class="text-sm text-walnut-700">
          Average prices across the U.S., estimated by Google's Gemini from each item's name,
          quantity and brand. What you pay depends on the store and where you live.
        </p>

        <BaseButton variant="secondary" block @click="emit('estimate')">Estimate again</BaseButton>
      </template>
    </div>
  </BottomSheet>
</template>
