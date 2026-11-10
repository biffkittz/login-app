<script setup lang="ts">
import { computed } from 'vue'

import type { GroceryItem } from '@/types/grocery'

import ItemPicture from './item-picture.vue'

interface Props {
  item: GroceryItem
  /** The row is being dragged: lift it above its neighbors. */
  dragging?: boolean
  /** The item has just been checked off and is about to leave the list. */
  checked?: boolean
  /** The id of the element that explains how to use the move handle. */
  moveHintId: string
  /**
   * Other items have pictures: show a plain tile when this one has none, so every name starts in the same place. A
   * list without any pictures (they were never on) shows no tiles at all.
   */
  pictureColumn?: boolean
  /** A row dragged from another store's section would land just above or below this one: mark the spot. */
  dropMarker?: 'before' | 'after' | null
  /**
   * The row is being dragged over another store's section, named here: the row says where it is going, because it
   * covers the spot it would land on, and a finger covers the rest.
   */
  movingTo?: string | null
}

const {
  item,
  dragging = false,
  checked = false,
  moveHintId,
  pictureColumn = false,
  dropMarker = null,
  movingTo = null,
} = defineProps<Props>()

const showsTile = computed(() => pictureColumn || item.imageUrl !== null || item.imagePending)

const emit = defineEmits<{
  /** Check the item off (remove it from the list). */
  check: []
  /** Open the item's details. */
  edit: []
  /** A finger or mouse pressed the move handle. */
  dragStart: [event: PointerEvent]
  /** Arrow key on the move handle: -1 moves the item up, 1 down. */
  moveBy: [delta: -1 | 1]
}>()
</script>

<template>
  <li
    data-reorder-item
    :data-item-id="item.id"
    class="relative flex items-center gap-0.5 px-1 py-1"
    :class="{
      'z-20 rounded-2xl bg-cream-50 shadow-[4px_4px_0_0_var(--color-walnut-800)] ring-2 ring-walnut-800':
        dragging,
    }"
  >
    <!-- Drawn over the divider between rows, so marking the spot doesn't move anything. -->
    <span
      v-if="dropMarker"
      class="pointer-events-none absolute inset-x-2 z-10 h-1 rounded-full bg-ember-600"
      :class="dropMarker === 'before' ? '-top-[3px]' : '-bottom-[3px]'"
      aria-hidden="true"
      data-drop-marker
    />
    <button
      type="button"
      class="group grid size-11 shrink-0 place-items-center rounded-full focus-visible:outline-3 focus-visible:outline-offset-1 focus-visible:outline-ember-600"
      :aria-label="`Check off ${item.name}`"
      :aria-pressed="checked"
      @click="emit('check')"
    >
      <span
        class="grid size-7 place-items-center rounded-full border-2 transition motion-reduce:transition-none"
        :class="
          checked
            ? 'border-ember-700 bg-ember-700 text-cream-50'
            : 'border-walnut-500 text-transparent group-hover:border-ember-700 group-hover:text-ember-700'
        "
      >
        <svg class="size-4" viewBox="0 0 24 24" fill="none" aria-hidden="true">
          <path
            d="M5 12.5l4.5 4.5L19 7.5"
            stroke="currentColor"
            stroke-width="3"
            stroke-linecap="round"
            stroke-linejoin="round"
          />
        </svg>
      </span>
    </button>

    <!-- The picture is part of the button, so tapping it opens the item too. -->
    <button
      type="button"
      class="flex min-w-0 flex-1 items-center gap-3 rounded-xl pr-1 pl-1.5 text-left focus-visible:outline-3 focus-visible:outline-offset-1 focus-visible:outline-ember-600"
      :class="showsTile ? 'py-1' : 'py-2'"
      @click="emit('edit')"
    >
      <ItemPicture v-if="showsTile" :url="item.imageUrl" :pending="item.imagePending" />
      <span class="block min-w-0 flex-1">
        <span class="flex flex-wrap items-baseline gap-x-2 gap-y-0.5">
          <span
            class="text-[1.0625rem] leading-snug font-semibold [overflow-wrap:anywhere]"
            :class="checked ? 'text-walnut-500 line-through' : 'text-walnut-900'"
          >
            {{ item.name }}
          </span>
          <span
            v-if="item.quantity"
            class="rounded-full bg-mustard-400/40 px-2 py-0.5 text-xs font-bold text-walnut-900"
          >
            <span class="sr-only">, quantity </span>{{ item.quantity }}
          </span>
          <!-- Shown only during a pointer drag, so screen readers don't need it. -->
          <span
            v-if="movingTo"
            class="inline-flex max-w-full items-center gap-1 rounded-full bg-ember-700 px-2 py-0.5 text-xs font-bold text-cream-50"
            aria-hidden="true"
            data-moving-to
          >
            <svg class="size-3.5 shrink-0" viewBox="0 0 24 24" fill="none">
              <path
                d="M5 12h14M13 6l6 6-6 6"
                stroke="currentColor"
                stroke-width="2.75"
                stroke-linecap="round"
                stroke-linejoin="round"
              />
            </svg>
            <span class="truncate">{{ movingTo }}</span>
          </span>
        </span>
        <!-- No store here: the list is grouped by store, and the group's heading already names it. -->
        <span v-if="item.brand" class="mt-0.5 flex flex-wrap gap-x-3 text-sm text-walnut-700">
          <span class="inline-flex min-w-0 items-center gap-1">
            <svg class="size-3.5 shrink-0" viewBox="0 0 24 24" fill="none" aria-hidden="true">
              <path
                d="M3 12V4h8l10 10-8 8L3 12z"
                stroke="currentColor"
                stroke-width="2.25"
                stroke-linejoin="round"
              />
              <circle cx="7.5" cy="8.5" r="1.5" fill="currentColor" />
            </svg>
            <span class="sr-only">, brand </span><span class="truncate">{{ item.brand }}</span>
          </span>
        </span>
        <span v-if="item.notes" class="mt-0.5 line-clamp-1 text-sm text-walnut-700 italic">
          <span class="sr-only">, notes: </span>{{ item.notes }}
        </span>
      </span>
    </button>

    <!-- touch-action: none lets a finger drag the handle without scrolling the page. -->
    <button
      type="button"
      data-reorder-handle
      class="grid h-11 w-10 shrink-0 cursor-grab touch-none place-items-center rounded-xl text-walnut-500 select-none hover:text-walnut-800 focus-visible:outline-3 focus-visible:outline-offset-1 focus-visible:outline-ember-600 active:cursor-grabbing [-webkit-touch-callout:none]"
      :aria-label="`Move ${item.name}`"
      :aria-describedby="moveHintId"
      @pointerdown="emit('dragStart', $event)"
      @keydown.up.prevent="emit('moveBy', -1)"
      @keydown.down.prevent="emit('moveBy', 1)"
    >
      <svg class="size-5" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
        <circle cx="9" cy="6" r="1.75" />
        <circle cx="15" cy="6" r="1.75" />
        <circle cx="9" cy="12" r="1.75" />
        <circle cx="15" cy="12" r="1.75" />
        <circle cx="9" cy="18" r="1.75" />
        <circle cx="15" cy="18" r="1.75" />
      </svg>
    </button>
  </li>
</template>
