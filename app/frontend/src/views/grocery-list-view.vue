<script setup lang="ts">
import { computed, ref, shallowRef } from 'vue'
import { storeToRefs } from 'pinia'

import StarburstMark from '@/components/brand/starburst-mark.vue'
import AddItemForm from '@/components/grocery/add-item-form.vue'
import ConnectionStatus from '@/components/grocery/connection-status.vue'
import { GROCERY_CARD_CLASS } from '@/components/grocery/card'
import GroceryStoreGroups from '@/components/grocery/grocery-store-groups.vue'
import ItemEditor from '@/components/grocery/item-editor.vue'
import ListSettings from '@/components/grocery/list-settings.vue'
import BaseButton from '@/components/ui/base-button.vue'
import FormAlert from '@/components/ui/form-alert.vue'
import ToastNotice from '@/components/ui/toast-notice.vue'
import { useEventListener } from '@/composables/useEventListener'
import { useGroceryListLiveUpdates } from '@/composables/useGroceryListLiveUpdates'
import { useToast } from '@/composables/useToast'
import { vFocusOnMount } from '@/directives/focus-on-mount'
import { toApiError, type ApiError } from '@/services/api-error'
import { useAuthStore } from '@/stores/auth'
import { useGroceryListStore } from '@/stores/grocery-list'
import type { GroceryItem, GroceryItemChanges, RemovedItem } from '@/types/grocery'
import { listIndexForGroupMove, listIndexForGroupTransfer } from '@/utils/list-order'
import type { StoreGroup } from '@/utils/store-groups'

// A checked-off item shows its check mark this long before it leaves the list: enough to see that the tap landed.
const CHECK_OFF_DELAY_MS = 350

const auth = useAuthStore()
const groceryList = useGroceryListStore()
const { items, stores, groups, settings, loadState, loadError, connectionState } =
  storeToRefs(groceryList)
const toast = useToast()
useGroceryListLiveUpdates()

const announcement = ref('')
const checkedIds = shallowRef<ReadonlySet<string>>(new Set())

const editorOpen = ref(false)
const editingId = ref<string | null>(null)
const saving = ref(false)
const saveError = shallowRef<ApiError | null>(null)
// The editor works within the item's store group: its position and its move buttons are relative to that group.
const editingGroup = computed(
  () =>
    groups.value.find((group) => group.items.some((item) => item.id === editingId.value)) ?? null,
)
const editingIndex = computed(
  () => editingGroup.value?.items.findIndex((item) => item.id === editingId.value) ?? -1,
)
const editingItem = computed(() => editingGroup.value?.items[editingIndex.value] ?? null)

// Once any item has a picture, items without one get a plain tile, so names line up in every section.
const pictureColumn = computed(() =>
  items.value.some((item) => item.imageUrl !== null || item.imagePending),
)

const settingsOpen = ref(false)
const settingsError = ref<string | null>(null)

function announce(message: string): void {
  announcement.value = message
}

function showError(error: unknown, fallback: string): void {
  const apiError = toApiError(error)
  if (!apiError.isUnauthorized) {
    toast.show(apiError.status === null ? apiError.message : `${fallback} ${apiError.message}`, {
      tone: 'error',
    })
  }
}

async function addItem(name: string): Promise<void> {
  announce(`Added ${name}.`)
  try {
    await groceryList.addItem({ name })
  } catch (error: unknown) {
    showError(error, `Couldn't add ${name}.`)
  }
}

// Removing is instant and undoable rather than confirmed first: in a store, nobody wants a dialog for every item
// that goes into the cart.
function removeWithUndo(item: GroceryItem, message: string): void {
  const index = items.value.findIndex((existing) => existing.id === item.id)
  const removed: RemovedItem = { item, afterItemId: items.value[index - 1]?.id ?? null }
  toast.show(message, { action: { label: 'Undo', run: () => void undo(removed) } })
  groceryList
    .removeItem(item.id)
    .catch((error: unknown) => showError(error, `Couldn't remove ${item.name}.`))
}

async function undo({ item, afterItemId }: RemovedItem): Promise<void> {
  toast.dismiss()
  announce(`${item.name} is back on the list.`)
  try {
    await groceryList.addItem(
      {
        name: item.name,
        quantity: item.quantity ?? '',
        brand: item.brand ?? '',
        store: item.store ?? '',
        notes: item.notes ?? '',
      },
      afterItemId,
    )
  } catch (error: unknown) {
    showError(error, `Couldn't put ${item.name} back.`)
  }
}

function checkOff(item: GroceryItem): void {
  if (checkedIds.value.has(item.id)) {
    return
  }
  checkedIds.value = new Set(checkedIds.value).add(item.id)
  setTimeout(() => {
    const next = new Set(checkedIds.value)
    next.delete(item.id)
    checkedIds.value = next
    removeWithUndo(item, `Checked off ${item.name}.`)
  }, CHECK_OFF_DELAY_MS)
}

// Items are reordered within their store's group. The group is part of the whole list, so the move is translated
// into a place in the whole list; how groups interleave there doesn't matter, because each is shown on its own.
async function moveWithinGroup(
  group: StoreGroup,
  item: GroceryItem,
  toIndex: number,
): Promise<void> {
  const fromIndex = group.items.findIndex((existing) => existing.id === item.id)
  const listIndex = listIndexForGroupMove(items.value, group.items, fromIndex, toIndex)
  if (listIndex === null) {
    return
  }

  announce(
    `${item.name} moved to position ${toIndex + 1} of ${group.items.length} in ${group.label}.`,
  )
  try {
    await groceryList.moveItem(item.id, listIndex)
  } catch (error: unknown) {
    showError(error, `Couldn't move ${item.name}.`)
  }
}

// Dropped into another store's section: the item takes that store, and lands where it was dropped among the
// section's items. The store and the place are saved together.
async function moveToGroup(item: GroceryItem, group: StoreGroup, toIndex: number): Promise<void> {
  const listIndex = listIndexForGroupTransfer(items.value, item.id, group.items, toIndex)
  if (listIndex === null) {
    return
  }

  announce(`${item.name} moved to ${group.label}.`)
  try {
    await groceryList.moveItem(item.id, listIndex, group.store ?? '')
  } catch (error: unknown) {
    showError(error, `Couldn't move ${item.name} to ${group.label}.`)
  }
}

function moveItem(item: GroceryItem, from: StoreGroup, to: StoreGroup, toIndex: number): void {
  void (from.key === to.key ? moveWithinGroup(from, item, toIndex) : moveToGroup(item, to, toIndex))
}

function openEditor(item: GroceryItem): void {
  editingId.value = item.id
  saveError.value = null
  editorOpen.value = true
}

async function saveChanges(changes: GroceryItemChanges): Promise<void> {
  const itemId = editingId.value
  if (itemId === null) {
    return
  }
  saving.value = true
  saveError.value = null
  try {
    await groceryList.updateItem(itemId, changes)
    editorOpen.value = false
  } catch (error: unknown) {
    saveError.value = toApiError(error)
  } finally {
    saving.value = false
  }
}

function removeFromEditor(): void {
  const item = editingItem.value
  editorOpen.value = false
  if (item !== null) {
    removeWithUndo(item, `Deleted ${item.name}.`)
  }
}

function moveFromEditor(delta: -1 | 1): void {
  const item = editingItem.value
  const group = editingGroup.value
  if (item !== null && group !== null) {
    void moveWithinGroup(group, item, editingIndex.value + delta)
  }
}

function openSettings(): void {
  settingsError.value = null
  settingsOpen.value = true
}

// The error shows in the sheet: a toast would be hidden behind it.
async function setImageGeneration(enabled: boolean): Promise<void> {
  settingsError.value = null
  try {
    await groceryList.setImageGeneration(enabled)
  } catch (error: unknown) {
    const apiError = toApiError(error)
    if (!apiError.isUnauthorized) {
      settingsError.value = `Couldn't change the setting. ${apiError.message}`
    }
  }
}

// Ctrl+Z (Cmd+Z on a Mac) undoes the last removal while its toast is up, except while typing in a field, where it
// keeps undoing typing as usual.
useEventListener(document, 'keydown', (event) => {
  const action = toast.toast.value?.action
  const target = event.target
  const isTyping =
    target instanceof HTMLElement &&
    (target.isContentEditable || ['INPUT', 'TEXTAREA'].includes(target.tagName))
  if (
    action &&
    !isTyping &&
    event instanceof KeyboardEvent &&
    (event.ctrlKey || event.metaKey) &&
    event.key.toLowerCase() === 'z'
  ) {
    event.preventDefault()
    action.run()
  }
})
</script>

<template>
  <section aria-labelledby="grocery-list-title" class="w-full max-w-xl self-start pb-24">
    <div class="mb-3 flex items-center justify-between gap-3">
      <h1
        id="grocery-list-title"
        v-focus-on-mount
        tabindex="-1"
        class="font-display text-3xl leading-tight font-bold text-walnut-900 focus:outline-none"
      >
        Grocery list
      </h1>
      <div class="flex shrink-0 items-center gap-1">
        <ConnectionStatus :state="connectionState" />
        <button
          type="button"
          class="grid size-11 place-items-center rounded-full text-walnut-700 transition hover:bg-cream-200 hover:text-walnut-900 focus-visible:outline-3 focus-visible:outline-offset-1 focus-visible:outline-ember-600 motion-reduce:transition-none"
          aria-label="List settings"
          aria-haspopup="dialog"
          @click="openSettings"
        >
          <svg class="size-6" viewBox="0 0 24 24" fill="none" aria-hidden="true">
            <path
              d="M4 7h9M17 7h3M4 17h3M11 17h9"
              stroke="currentColor"
              stroke-width="2.25"
              stroke-linecap="round"
            />
            <circle cx="15" cy="7" r="2.25" stroke="currentColor" stroke-width="2.25" />
            <circle cx="9" cy="17" r="2.25" stroke="currentColor" stroke-width="2.25" />
          </svg>
        </button>
      </div>
    </div>

    <!-- Sticky, so adding is always one tap away however far down the list the user has scrolled. -->
    <div
      data-sticky-header
      class="sticky top-0 z-10 -mx-4 bg-cream-50 px-4 pt-2 pb-3 sm:-mx-6 sm:px-6"
    >
      <AddItemForm @add="addItem" />
    </div>

    <div v-if="items.length === 0" :class="GROCERY_CARD_CLASS">
      <div
        v-if="loadState === 'loading'"
        class="flex items-center justify-center gap-3 px-6 py-12 text-walnut-700"
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
        Loading the list…
      </div>

      <div v-else-if="loadState === 'error'" class="flex flex-col gap-4 p-5">
        <FormAlert>{{ loadError?.message ?? "We couldn't load the list." }}</FormAlert>
        <BaseButton variant="secondary" @click="groceryList.load()">Try again</BaseButton>
      </div>

      <div v-else class="flex flex-col items-center px-6 py-12 text-center">
        <StarburstMark class="size-14" />
        <p class="mt-4 font-display text-xl font-bold text-walnut-900">The list is empty.</p>
        <p class="mt-1 text-walnut-700">
          Add what you need above; everyone signed in sees it straight away.
        </p>
      </div>
    </div>

    <!--
      The list, grouped by store. Items move within their store's section, or into another one, which changes their
      store.
    -->
    <GroceryStoreGroups
      v-else
      :groups="groups"
      :stores="stores"
      :checked-ids="checkedIds"
      :picture-column="pictureColumn"
      @check="checkOff"
      @edit="openEditor"
      @move="moveItem"
      @drag-start="groceryList.holdChanges()"
      @drag-end="groceryList.releaseChanges()"
    />

    <p class="sr-only" aria-live="polite" aria-atomic="true">{{ announcement }}</p>

    <ItemEditor
      v-model:open="editorOpen"
      :item="editingItem"
      :index="editingIndex"
      :count="editingGroup?.items.length ?? 0"
      :group-label="editingGroup?.label ?? ''"
      :stores="stores"
      :current-user-id="auth.user?.id"
      :saving="saving"
      :error="saveError"
      @save="saveChanges"
      @remove="removeFromEditor"
      @move-by="moveFromEditor"
    />

    <ListSettings
      v-model:open="settingsOpen"
      :settings="settings"
      :error="settingsError"
      @set-image-generation="setImageGeneration"
    />

    <ToastNotice
      :toast="toast.toast.value"
      @dismiss="toast.dismiss()"
      @pause="toast.pause()"
      @resume="toast.resume()"
    />
  </section>
</template>
