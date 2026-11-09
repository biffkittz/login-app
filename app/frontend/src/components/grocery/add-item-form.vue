<script setup lang="ts">
import { ref, useId, useTemplateRef } from 'vue'

import BaseButton from '@/components/ui/base-button.vue'
import { ITEM_NAME_MAX_LENGTH } from '@/validation/grocery-item-policy'

const emit = defineEmits<{
  /** A non-blank, trimmed item name to add. */
  add: [name: string]
}>()

const name = ref('')
const input = useTemplateRef<HTMLInputElement>('input')
const inputId = useId()

function onSubmit(): void {
  const trimmed = name.value.trim()
  if (trimmed !== '') {
    emit('add', trimmed)
    name.value = ''
  }
  // Keep focus (and the phone's keyboard) in the field, ready for the next item.
  input.value?.focus()
}
</script>

<template>
  <form class="flex items-center gap-2" @submit.prevent="onSubmit">
    <label :for="inputId" class="sr-only">New item</label>
    <!-- 16px text (text-base) keeps iOS from zooming the page when the field gets focus. -->
    <input
      :id="inputId"
      ref="input"
      v-model="name"
      type="text"
      placeholder="Add an item…"
      :maxlength="ITEM_NAME_MAX_LENGTH"
      autocomplete="off"
      autocapitalize="sentences"
      enterkeyhint="enter"
      class="min-w-0 flex-1 rounded-full border-2 border-walnut-800 bg-cream-50 px-5 py-3 text-base text-walnut-900 transition placeholder:text-walnut-500 focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-ember-600 motion-reduce:transition-none"
    />
    <BaseButton type="submit" class="shrink-0 px-5">
      <svg class="size-5" viewBox="0 0 24 24" fill="none" aria-hidden="true">
        <path
          d="M12 5v14M5 12h14"
          stroke="currentColor"
          stroke-width="2.75"
          stroke-linecap="round"
        />
      </svg>
      Add
    </BaseButton>
  </form>
</template>
