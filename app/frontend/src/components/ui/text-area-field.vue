<script setup lang="ts">
import { computed, useId } from 'vue'

// Attributes such as `maxlength` or `rows` belong on the <textarea>, not on the wrapper <div>.
defineOptions({ inheritAttrs: false })

interface Props {
  label: string
  /** Validation message. Marks the field invalid and is read out with it through `aria-describedby`. */
  error?: string
  /** Guidance shown under the field. */
  hint?: string
}

const { label, error, hint } = defineProps<Props>()

/** The text (two-way bound with `v-model`). */
const model = defineModel<string>({ required: true })

const emit = defineEmits<{
  /** The field lost focus: the moment to validate it for the first time. */
  blur: [event: FocusEvent]
}>()

const fieldId = useId()
const hintId = `${fieldId}-hint`
const errorId = `${fieldId}-error`
const describedBy = computed(
  () =>
    [hint ? hintId : undefined, error ? errorId : undefined].filter(Boolean).join(' ') || undefined,
)
</script>

<template>
  <div class="flex flex-col gap-1.5">
    <label :for="fieldId" class="font-display text-sm font-semibold tracking-wide text-walnut-800">
      {{ label }}
    </label>
    <textarea
      :id="fieldId"
      v-bind="$attrs"
      v-model="model"
      :aria-invalid="error ? 'true' : undefined"
      :aria-describedby="describedBy"
      :class="[
        'block w-full resize-y rounded-xl border-2 bg-cream-50 px-4 py-3 text-base text-walnut-900 transition placeholder:text-walnut-500 motion-reduce:transition-none',
        'focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-ember-600',
        error ? 'border-brick-700' : 'border-walnut-500 hover:border-walnut-700',
      ]"
      @blur="emit('blur', $event)"
    />
    <p v-if="hint" :id="hintId" class="text-sm text-walnut-600">{{ hint }}</p>
    <p v-if="error" :id="errorId" class="text-sm font-medium text-brick-700">{{ error }}</p>
  </div>
</template>
