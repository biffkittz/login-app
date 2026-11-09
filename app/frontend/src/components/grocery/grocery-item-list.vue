<script setup lang="ts">
import { nextTick, useId, useTemplateRef } from 'vue'

import { useDragReorder } from '@/composables/useDragReorder'
import type { GroceryItem } from '@/types/grocery'

import GroceryItemRow from './grocery-item-row.vue'

interface Props {
  /** The items, in list order. */
  items: readonly GroceryItem[]
  /** Items that have just been checked off and are about to leave. */
  checkedIds: ReadonlySet<string>
  /** Names the list for assistive technology, e.g. "Costco items". */
  label: string
  /** Some items on the page have pictures: line every name up as if all had one. */
  pictureColumn?: boolean
}

const { items, checkedIds, label, pictureColumn = false } = defineProps<Props>()

const emit = defineEmits<{
  check: [item: GroceryItem]
  edit: [item: GroceryItem]
  /** Move an item so it ends up at `toIndex`. */
  move: [item: GroceryItem, toIndex: number]
  /** A drag began; realtime updates should wait until it ends. */
  dragStart: []
  dragEnd: []
}>()

const list = useTemplateRef<HTMLUListElement>('list')
const moveHintId = useId()

const { draggingIndex, start } = useDragReorder({
  list,
  onDrop: (fromIndex, toIndex) => {
    const item = items[fromIndex]
    if (item !== undefined) {
      emit('move', item, toIndex)
    }
  },
  onDragStart: () => emit('dragStart'),
  onDragEnd: () => emit('dragEnd'),
  // The sticky "Add an item" bar covers the top of the screen; auto-scroll upward starts below it.
  topInset: () =>
    document.querySelector('[data-sticky-header]')?.getBoundingClientRect().bottom ?? 0,
})

async function moveBy(index: number, delta: -1 | 1): Promise<void> {
  const item = items[index]
  const toIndex = index + delta
  if (item === undefined || toIndex < 0 || toIndex >= items.length) {
    return
  }

  emit('move', item, toIndex)
  // Re-rendering in the new order can take focus away from the handle; put it back so the arrow keys keep working.
  await nextTick()
  list.value
    ?.querySelector<HTMLElement>(`[data-item-id="${item.id}"] [data-reorder-handle]`)
    ?.focus()
}
</script>

<template>
  <div>
    <p :id="moveHintId" class="sr-only">
      Drag to reorder, or use the up and down arrow keys. The item's details also have move buttons.
    </p>
    <ul ref="list" class="divide-y-2 divide-dashed divide-cream-300" :aria-label="label">
      <GroceryItemRow
        v-for="(item, index) in items"
        :key="item.id"
        :item="item"
        :dragging="draggingIndex === index"
        :checked="checkedIds.has(item.id)"
        :move-hint-id="moveHintId"
        :picture-column="pictureColumn"
        @check="emit('check', item)"
        @edit="emit('edit', item)"
        @drag-start="start($event, index)"
        @move-by="moveBy(index, $event)"
      />
    </ul>
  </div>
</template>
