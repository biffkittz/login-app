<script setup lang="ts">
import { computed, reactive, ref, useTemplateRef, watch } from 'vue'

import BaseButton from '@/components/ui/base-button.vue'
import BottomSheet from '@/components/ui/bottom-sheet.vue'
import FormAlert from '@/components/ui/form-alert.vue'
import SelectField, { type SelectOption } from '@/components/ui/select-field.vue'
import TextAreaField from '@/components/ui/text-area-field.vue'
import TextField from '@/components/ui/text-field.vue'
import { useFocusFirstInvalidField } from '@/composables/useFocusFirstInvalidField'
import { useFormValidation } from '@/composables/useFormValidation'
import { useNow } from '@/composables/useNow'
import { formLevelMessage, type ApiError } from '@/services/api-error'
import type { GroceryItem, GroceryItemChanges } from '@/types/grocery'
import { describeEditor, formatRelativeTime } from '@/utils/grocery-format'
import { changedDetails, toItemDetails } from '@/utils/grocery-item-form'
import { NO_STORE_LABEL } from '@/utils/store-groups'
import {
  ITEM_BRAND_MAX_LENGTH,
  ITEM_NAME_MAX_LENGTH,
  ITEM_NOTES_MAX_LENGTH,
  ITEM_QUANTITY_MAX_LENGTH,
} from '@/validation/grocery-item-policy'
import { maxLength, required } from '@/validation/rules'

interface Props {
  /** The item being edited; becomes `null` if someone else removes it while the editor is open. */
  item: GroceryItem | null
  /** The item's index in its store's group, for the move buttons. */
  index: number
  /** How many items the item's store group holds. */
  count: number
  /** The heading of the item's store group, e.g. "Costco". */
  groupLabel: string
  /** The stores the server offers, in order. */
  stores: readonly string[]
  currentUserId?: string
  /** A save is in progress. */
  saving?: boolean
  /** Why the last save failed. */
  error?: ApiError | null
}

const {
  item,
  index,
  count,
  groupLabel,
  stores,
  currentUserId,
  saving = false,
  error = null,
} = defineProps<Props>()

/** Whether the editor is open (two-way bound with `v-model:open`). */
const open = defineModel<boolean>('open', { required: true })

const emit = defineEmits<{
  /** Save the fields that changed. */
  save: [changes: GroceryItemChanges]
  remove: []
  /** Move the item one place: -1 up, 1 down. */
  moveBy: [delta: -1 | 1]
}>()

// A type alias rather than an interface: form models must be assignable to Record<string, string>.
type ItemForm = { name: string; quantity: string; brand: string; store: string; notes: string }

const form = useTemplateRef<HTMLFormElement>('form')
const focusFirstInvalidField = useFocusFirstInvalidField(form)
const now = useNow()

const draft = reactive<ItemForm>({ name: '', quantity: '', brand: '', store: '', notes: '' })
let original: ItemForm = { ...draft }
// When the editor opened, so a change made by someone else afterwards can be pointed out.
const openedAt = ref<string | null>(null)

const { errors, touch, validateAll, setServerErrors } = useFormValidation(draft, {
  name: [
    required('Enter what to buy.'),
    maxLength(ITEM_NAME_MAX_LENGTH, `Use ${ITEM_NAME_MAX_LENGTH} characters or fewer.`),
  ],
  quantity: [
    maxLength(ITEM_QUANTITY_MAX_LENGTH, `Use ${ITEM_QUANTITY_MAX_LENGTH} characters or fewer.`),
  ],
  brand: [maxLength(ITEM_BRAND_MAX_LENGTH, `Use ${ITEM_BRAND_MAX_LENGTH} characters or fewer.`)],
  notes: [maxLength(ITEM_NOTES_MAX_LENGTH, `Use ${ITEM_NOTES_MAX_LENGTH} characters or fewer.`)],
})

// A store saved before the store choices existed (or by an older version of the app). It stays selectable, so the
// form shows the item as it is and saving other changes doesn't silently drop it.
const unlistedStore = ref<string | null>(null)

const storeOptions = computed<SelectOption[]>(() => [
  { value: '', label: NO_STORE_LABEL },
  ...stores.map((store) => ({ value: store, label: store })),
  ...(unlistedStore.value
    ? [{ value: unlistedStore.value, label: `${unlistedStore.value} (no longer offered)` }]
    : []),
])

// Fill the form each time the editor opens, from the item as it is at that moment.
watch(
  open,
  (isOpen) => {
    if (isOpen && item !== null) {
      Object.assign(draft, toItemDetails(item))
      // Match the offered store regardless of case, as the server does, so the dropdown shows it selected.
      const offered = stores.find((store) => store.toLowerCase() === draft.store.toLowerCase())
      draft.store = offered ?? draft.store
      unlistedStore.value = draft.store !== '' && offered === undefined ? draft.store : null
      original = { ...draft }
      openedAt.value = item.lastModifiedAt
      setServerErrors({})
    }
  },
  { immediate: true },
)

watch(
  () => error,
  (latest) => {
    if (latest !== null) {
      setServerErrors(latest.fieldErrors)
      void focusFirstInvalidField()
    }
  },
)

const formError = computed(() => (error ? formLevelMessage(error) : null))

// Someone else changed the item while it was open. Saving still only sends the fields edited here, so their changes
// to other fields survive; this just explains why the list underneath may look different.
const changedByOthers = computed(
  () =>
    item !== null &&
    openedAt.value !== null &&
    item.lastModifiedAt !== openedAt.value &&
    item.lastModifiedById !== currentUserId,
)

const history = computed(() => {
  if (item === null) {
    return null
  }
  const added = formatRelativeTime(item.createdAt, now.value)
  const changed = formatRelativeTime(item.lastModifiedAt, now.value)
  const editor = describeEditor(item, currentUserId)
  return item.lastModifiedAt === item.createdAt
    ? { added: `Added ${added} by ${editor}`, changed: null }
    : { added: `Added ${added}`, changed: `Last changed ${changed} by ${editor}` }
})

async function onSubmit(): Promise<void> {
  if (item === null || saving) {
    return
  }
  if (!validateAll()) {
    await focusFirstInvalidField()
    return
  }

  const changes = changedDetails(original, draft)
  if (Object.keys(changes).length === 0) {
    open.value = false
    return
  }
  emit('save', changes)
}
</script>

<template>
  <BottomSheet v-model:open="open" title="Item details">
    <form ref="form" class="flex min-h-0 flex-col" novalidate @submit.prevent="onSubmit">
      <div class="flex min-h-0 flex-col gap-4 overflow-y-auto px-5 py-4">
        <FormAlert v-if="item === null" tone="info">
          Someone has just removed this item from the list.
        </FormAlert>
        <template v-else>
          <FormAlert v-if="formError">{{ formError }}</FormAlert>
          <FormAlert v-else-if="changedByOthers" tone="info">
            {{ item.lastModifiedByName ?? 'Someone' }} changed this item while you had it open.
            Saving keeps their changes to anything you haven't edited.
          </FormAlert>

          <TextField
            v-model="draft.name"
            label="Item"
            required
            :maxlength="ITEM_NAME_MAX_LENGTH"
            autocapitalize="sentences"
            enterkeyhint="done"
            :error="errors.name"
            @blur="touch('name')"
          />

          <div class="grid grid-cols-[minmax(0,2fr)_minmax(0,3fr)] gap-3">
            <TextField
              v-model="draft.quantity"
              label="Quantity"
              placeholder="2, 1 dozen…"
              :maxlength="ITEM_QUANTITY_MAX_LENGTH"
              enterkeyhint="done"
              :error="errors.quantity"
              @blur="touch('quantity')"
            />
            <TextField
              v-model="draft.brand"
              label="Brand"
              :maxlength="ITEM_BRAND_MAX_LENGTH"
              autocapitalize="words"
              enterkeyhint="done"
              :error="errors.brand"
              @blur="touch('brand')"
            />
          </div>

          <SelectField
            v-model="draft.store"
            label="Store"
            :options="storeOptions"
            :error="errors.store"
          />

          <TextAreaField
            v-model="draft.notes"
            label="Notes"
            rows="3"
            :maxlength="ITEM_NOTES_MAX_LENGTH"
            autocapitalize="sentences"
            :error="errors.notes"
            @blur="touch('notes')"
          />

          <div
            class="flex flex-wrap items-center justify-between gap-3 rounded-2xl border-2 border-cream-300 bg-cream-50 px-4 py-3"
          >
            <p class="text-sm text-walnut-700">
              Position <strong class="text-walnut-900">{{ index + 1 }}</strong> of {{ count }} in
              {{ groupLabel }}
            </p>
            <div class="flex gap-2">
              <button
                type="button"
                class="rounded-full border-2 border-walnut-800 px-4 py-2 font-display text-sm font-semibold text-walnut-900 transition hover:bg-cream-200 focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-ember-600 disabled:cursor-not-allowed disabled:opacity-40 motion-reduce:transition-none"
                :disabled="index <= 0"
                @click="emit('moveBy', -1)"
              >
                <span aria-hidden="true">↑</span> Move up
              </button>
              <button
                type="button"
                class="rounded-full border-2 border-walnut-800 px-4 py-2 font-display text-sm font-semibold text-walnut-900 transition hover:bg-cream-200 focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-ember-600 disabled:cursor-not-allowed disabled:opacity-40 motion-reduce:transition-none"
                :disabled="index >= count - 1"
                @click="emit('moveBy', 1)"
              >
                <span aria-hidden="true">↓</span> Move down
              </button>
            </div>
          </div>

          <p v-if="history" class="text-sm text-walnut-700">
            <time :datetime="item.createdAt">{{ history.added }}</time>
            <template v-if="history.changed">
              <span aria-hidden="true"> · </span><br class="sm:hidden" />
              <time :datetime="item.lastModifiedAt">{{ history.changed }}</time>
            </template>
          </p>
        </template>
      </div>

      <div
        class="flex items-center gap-3 border-t-2 border-dashed border-cream-300 px-5 pt-3 pb-[max(0.75rem,env(safe-area-inset-bottom))]"
      >
        <button
          v-if="item !== null"
          type="button"
          class="mr-auto rounded-full px-3 py-2 font-display text-sm font-semibold text-brick-700 underline decoration-2 underline-offset-4 focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-ember-600"
          @click="emit('remove')"
        >
          Delete
        </button>
        <BaseButton variant="secondary" class="ml-auto" @click="open = false">
          {{ item === null ? 'Close' : 'Cancel' }}
        </BaseButton>
        <BaseButton v-if="item !== null" type="submit" :loading="saving">
          {{ saving ? 'Saving…' : 'Save' }}
        </BaseButton>
      </div>
    </form>
  </BottomSheet>
</template>
