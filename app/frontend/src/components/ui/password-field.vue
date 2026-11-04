<script setup lang="ts">
import { ref } from 'vue'

import TextField from './text-field.vue'

interface Props {
  label: string
  /** `current-password` when signing in, `new-password` when choosing one, so password managers do the right thing. */
  autocomplete: 'current-password' | 'new-password'
  error?: string
  hint?: string
  required?: boolean
  autofocus?: boolean
}

const props = defineProps<Props>()

/** The password (two-way bound with `v-model`). */
const model = defineModel<string>({ required: true })

const emit = defineEmits<{
  blur: [event: FocusEvent]
}>()

const isVisible = ref(false)
</script>

<template>
  <!-- spellcheck is off because some browsers send spell-checked text to a cloud service once it is visible. -->
  <TextField
    v-model="model"
    v-bind="props"
    :type="isVisible ? 'text' : 'password'"
    spellcheck="false"
    autocapitalize="none"
    @blur="emit('blur', $event)"
  >
    <template #trailing="{ inputId }">
      <!-- The visible label changes ("Show"/"Hide") instead of using aria-pressed, which would announce it twice. -->
      <button
        type="button"
        :aria-controls="inputId"
        class="rounded-full px-3 py-1.5 font-display text-sm font-semibold text-ember-700 transition hover:bg-cream-200 focus-visible:outline-3 focus-visible:outline-offset-1 focus-visible:outline-ember-600 motion-reduce:transition-none"
        @click="isVisible = !isVisible"
      >
        {{ isVisible ? 'Hide' : 'Show' }}<span class="sr-only"> password</span>
      </button>
    </template>
  </TextField>
</template>
