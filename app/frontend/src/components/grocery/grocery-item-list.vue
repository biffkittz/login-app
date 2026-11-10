<script setup lang="ts">
import { nextTick, useId, useTemplateRef } from 'vue'

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
  /** The index of the row being dragged, or `null`. */
  draggingIndex?: number | null
  /**
   * A row from another list is being dragged over this one and would land here: 0 above the first row, the number of
   * rows below the last. `null` otherwise.
   */
  dropIndex?: number | null
  /** The dragged row is over another store's section, with this heading; `null` otherwise. */
  movingTo?: string | null
}

const {
  items,
  checkedIds,
  label,
  pictureColumn = false,
  draggingIndex = null,
  dropIndex = null,
  movingTo = null,
} = defineProps<Props>()

const emit = defineEmits<{
  check: [item: GroceryItem]
  edit: [item: GroceryItem]
  /** Move an item one place with the arrow keys, so it ends up at `toIndex`. */
  move: [item: GroceryItem, toIndex: number]
  /** A finger or mouse pressed the move handle of row `index`. */
  dragStart: [event: PointerEvent, index: number]
}>()

const list = useTemplateRef<HTMLUListElement>('list')
const moveHintId = useId()

function dropMarker(index: number): 'before' | 'after' | null {
  if (dropIndex === null) {
    return null
  }
  if (dropIndex === index) {
    return 'before'
  }
  return dropIndex === items.length && index === items.length - 1 ? 'after' : null
}

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
      Drag to reorder, or into another store's section to change its store. The up and down arrow
      keys also reorder, and the item's details have move buttons and its store.
    </p>
    <ul ref="list" class="divide-y-2 divide-dashed divide-cream-300" :aria-label="label">
      <GroceryItemRow
        v-for="(item, index) in items"
        :key="item.id"
        :item="item"
        :dragging="draggingIndex === index"
        :drop-marker="dropMarker(index)"
        :moving-to="draggingIndex === index ? movingTo : null"
        :checked="checkedIds.has(item.id)"
        :move-hint-id="moveHintId"
        :picture-column="pictureColumn"
        @check="emit('check', item)"
        @edit="emit('edit', item)"
        @drag-start="emit('dragStart', $event, index)"
        @move-by="moveBy(index, $event)"
      />
    </ul>
  </div>
</template>
