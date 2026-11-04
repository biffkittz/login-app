<script setup lang="ts">
import { computed, useId } from 'vue'

import { vFocusOnMount } from '@/directives/focus-on-mount'

// Attributes such as `inputmode` or `name` belong on the <input>, not on the wrapper <div>.
defineOptions({ inheritAttrs: false })

interface Props {
  label: string
  type?: 'text' | 'email' | 'password'
  /** Lets browsers and password managers fill the field (`email`, `name`, `current-password`, ...). */
  autocomplete?: string
  /** Validation message. Marks the input invalid and is read out with it through `aria-describedby`. */
  error?: string
  /** Guidance shown under the input, such as password rules. */
  hint?: string
  required?: boolean
  /** Focuses the input when the page opens; use on the first field of a form. */
  autofocus?: boolean
}

const {
  label,
  type = 'text',
  autocomplete,
  error,
  hint,
  required = false,
  autofocus = false,
} = defineProps<Props>()

/** The input's value (two-way bound with `v-model`). */
const model = defineModel<string>({ required: true })

const emit = defineEmits<{
  /** The input lost focus: the moment to validate it for the first time. */
  blur: [event: FocusEvent]
}>()

defineSlots<{
  /** Content shown inside the input's right edge, e.g. a show-password toggle. */
  trailing?: (props: { inputId: string }) => unknown
}>()

// useId() gives ids that are unique per app instance, so labels and descriptions always point at the right input.
const inputId = useId()
const hintId = `${inputId}-hint`
const errorId = `${inputId}-error`
const describedBy = computed(
  () =>
    [hint ? hintId : undefined, error ? errorId : undefined].filter(Boolean).join(' ') || undefined,
)
</script>

<template>
  <div class="flex flex-col gap-1.5">
    <label :for="inputId" class="font-display text-sm font-semibold tracking-wide text-walnut-800">
      {{ label }}
    </label>
    <div class="relative">
      <input
        :id="inputId"
        v-bind="$attrs"
        v-model="model"
        v-focus-on-mount="autofocus"
        :type="type"
        :autocomplete="autocomplete"
        :aria-invalid="error ? 'true' : undefined"
        :aria-describedby="describedBy"
        :aria-required="required || undefined"
        :class="[
          'block w-full rounded-xl border-2 bg-cream-50 px-4 py-3 text-base text-walnut-900 transition placeholder:text-walnut-400 motion-reduce:transition-none',
          'focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-ember-600',
          error ? 'border-brick-700' : 'border-walnut-500 hover:border-walnut-700',
          { 'pr-20': $slots.trailing },
        ]"
        @blur="emit('blur', $event)"
      />
      <div v-if="$slots.trailing" class="absolute inset-y-0 right-2 flex items-center">
        <slot name="trailing" :input-id="inputId" />
      </div>
    </div>
    <p v-if="hint" :id="hintId" class="text-sm text-walnut-600">{{ hint }}</p>
    <p
      v-if="error"
      :id="errorId"
      class="flex items-start gap-1.5 text-sm font-medium text-brick-700"
    >
      <svg
        class="mt-0.5 size-4 shrink-0"
        viewBox="0 0 20 20"
        fill="currentColor"
        aria-hidden="true"
      >
        <path
          fill-rule="evenodd"
          d="M10 18a8 8 0 1 0 0-16 8 8 0 0 0 0 16Zm.75-11.75a.75.75 0 0 0-1.5 0v4.5a.75.75 0 0 0 1.5 0v-4.5ZM10 15a1 1 0 1 0 0-2 1 1 0 0 0 0 2Z"
          clip-rule="evenodd"
        />
      </svg>
      {{ error }}
    </p>
  </div>
</template>
