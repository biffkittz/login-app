<script setup lang="ts">
import { computed, useId, useTemplateRef } from 'vue'

import { useDragReorder } from '@/composables/useDragReorder'
import type { GroceryItem } from '@/types/grocery'
import { acceptsDrops, emptyStoreGroups, type StoreGroup } from '@/utils/store-groups'

import { GROCERY_CARD_CLASS } from './card'
import GroceryItemList from './grocery-item-list.vue'

interface Props {
  /** The list grouped by store, from `groupByStore`. */
  groups: readonly StoreGroup[]
  /** The offered stores, in display order: the ones an item can be dropped into. */
  stores: readonly string[]
  /** Items that have just been checked off and are about to leave. */
  checkedIds: ReadonlySet<string>
  /** Some items have pictures: line every name up as if all had one. */
  pictureColumn?: boolean
}

const { groups, stores, checkedIds, pictureColumn = false } = defineProps<Props>()

const emit = defineEmits<{
  check: [item: GroceryItem]
  edit: [item: GroceryItem]
  /**
   * Move an item. Within its own group (`to` is `from`), `toIndex` is where it should end up among that group's
   * items; into another group, where among that group's items it was dropped (0 above the first, their number below
   * the last), and the item takes that group's store.
   */
  move: [item: GroceryItem, from: StoreGroup, to: StoreGroup, toIndex: number]
  /** A drag began; realtime updates should wait until it ends. */
  dragStart: []
  dragEnd: []
}>()

const container = useTemplateRef<HTMLElement>('container')
const groupIdPrefix = useId()

// Stores with nothing on the list have no section to drop into, so while an item is being dragged they get a drop
// zone each, below the sections: there they can't move anything under the finger.
const emptyGroups = computed(() => emptyStoreGroups(groups, stores))

function findGroup(key: string): StoreGroup | undefined {
  return (
    groups.find((group) => group.key === key) ??
    emptyGroups.value.find((group) => group.key === key)
  )
}

const { dragging, dropTarget, start } = useDragReorder({
  container,
  canDrop: (_from, to) => {
    const group = findGroup(to)
    return group !== undefined && acceptsDrops(group, stores)
  },
  onDrop: ({ from, to }) => {
    const fromGroup = findGroup(from.group)
    const toGroup = findGroup(to.group)
    const item = fromGroup?.items[from.index]
    if (fromGroup !== undefined && toGroup !== undefined && item !== undefined) {
      emit('move', item, fromGroup, toGroup, to.index)
    }
  },
  onDragStart: () => emit('dragStart'),
  onDragEnd: () => emit('dragEnd'),
  // The sticky "Add an item" bar covers the top of the screen; auto-scroll upward starts below it.
  topInset: () =>
    document.querySelector('[data-sticky-header]')?.getBoundingClientRect().bottom ?? 0,
})

const isDropTarget = (group: StoreGroup): boolean => dropTarget.value?.group === group.key

// The heading of the section the dragged row would move to, if not its own.
const movingTo = computed(() =>
  dropTarget.value === null ? null : (findGroup(dropTarget.value.group)?.label ?? null),
)
</script>

<template>
  <div ref="container" class="flex flex-col gap-6">
    <section
      v-for="(group, groupIndex) in groups"
      :key="group.key"
      :data-reorder-group="group.key"
      :aria-labelledby="`${groupIdPrefix}-${groupIndex}`"
    >
      <h2
        :id="`${groupIdPrefix}-${groupIndex}`"
        class="mb-2 flex items-center gap-2 px-1 font-display text-lg font-bold text-walnut-900"
      >
        <svg
          v-if="group.store"
          class="size-5 shrink-0 text-ember-700"
          viewBox="0 0 24 24"
          fill="none"
          aria-hidden="true"
        >
          <path
            d="M4 9l1.5-5h13L20 9M4 9h16M4 9v11h16V9M9 20v-6h6v6"
            stroke="currentColor"
            stroke-width="2.25"
            stroke-linejoin="round"
          />
        </svg>
        <span class="min-w-0 flex-1 truncate">{{ group.label }}</span>
        <span class="sr-only">: </span>
        <span class="shrink-0 font-sans text-sm font-semibold text-walnut-700">
          {{ group.items.length }} {{ group.items.length === 1 ? 'item' : 'items' }}
        </span>
      </h2>
      <div
        :class="[GROCERY_CARD_CLASS, { 'ring-4 ring-ember-600/45': isDropTarget(group) }]"
        :data-drop-target="isDropTarget(group) || undefined"
      >
        <GroceryItemList
          :items="group.items"
          :checked-ids="checkedIds"
          :label="`${group.label} items`"
          :picture-column="pictureColumn"
          :dragging-index="dragging?.group === group.key ? dragging.index : null"
          :drop-index="dropTarget?.group === group.key ? dropTarget.index : null"
          :moving-to="dragging?.group === group.key ? movingTo : null"
          @check="emit('check', $event)"
          @edit="emit('edit', $event)"
          @move="(item, toIndex) => emit('move', item, group, group, toIndex)"
          @drag-start="(event, index) => start(event, group.key, index)"
        />
      </div>
    </section>

    <!--
      Only while dragging with a finger or mouse, so hidden from assistive technology: the item's details offer every
      store, whichever has items.
    -->
    <div
      v-if="dragging !== null && emptyGroups.length > 0"
      class="flex flex-col gap-2"
      aria-hidden="true"
    >
      <p class="px-1 text-sm font-semibold text-walnut-700">
        Or drop it on a store with nothing yet:
      </p>
      <div
        v-for="group in emptyGroups"
        :key="group.key"
        :data-reorder-group="group.key"
        :data-drop-target="isDropTarget(group) || undefined"
        class="flex items-center gap-2 rounded-2xl border-2 border-dashed px-4 py-3 font-display font-bold transition-colors motion-reduce:transition-none"
        :class="
          isDropTarget(group)
            ? 'border-ember-600 bg-ember-600/10 text-walnut-900'
            : 'border-walnut-500/70 text-walnut-700'
        "
      >
        <svg
          v-if="group.store"
          class="size-5 shrink-0 text-ember-700"
          viewBox="0 0 24 24"
          fill="none"
          aria-hidden="true"
        >
          <path
            d="M4 9l1.5-5h13L20 9M4 9h16M4 9v11h16V9M9 20v-6h6v6"
            stroke="currentColor"
            stroke-width="2.25"
            stroke-linejoin="round"
          />
        </svg>
        <span class="min-w-0 truncate">{{ group.label }}</span>
      </div>
    </div>
  </div>
</template>
