<script setup lang="ts">
import { computed, useId } from 'vue'

// Attributes such as `name` belong on the <select>, not on the wrapper <div>.
defineOptions({ inheritAttrs: false })

/** One choice in the dropdown. */
export interface SelectOption {
  value: string
  label: string
}

interface Props {
  label: string
  options: readonly SelectOption[]
  /** Validation message. Marks the field invalid and is read out with it through `aria-describedby`. */
  error?: string
}

const { label, options, error } = defineProps<Props>()

/** The selected value (two-way bound with `v-model`). */
const model = defineModel<string>({ required: true })

const selectId = useId()
const errorId = `${selectId}-error`
const describedBy = computed(() => (error ? errorId : undefined))
</script>

<template>
  <div class="flex flex-col gap-1.5">
    <label :for="selectId" class="font-display text-sm font-semibold tracking-wide text-walnut-800">
      {{ label }}
    </label>
    <!-- A native <select>: phones show their own picker, which is the easiest choice to make with a thumb. -->
    <div class="relative">
      <select
        :id="selectId"
        v-bind="$attrs"
        v-model="model"
        :aria-invalid="error ? 'true' : undefined"
        :aria-describedby="describedBy"
        :class="[
          'block w-full appearance-none rounded-xl border-2 bg-cream-50 py-3 pr-11 pl-4 text-base text-walnut-900 transition motion-reduce:transition-none',
          'focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-ember-600',
          error ? 'border-brick-700' : 'border-walnut-500 hover:border-walnut-700',
        ]"
      >
        <option v-for="option in options" :key="option.value" :value="option.value">
          {{ option.label }}
        </option>
      </select>
      <svg
        class="pointer-events-none absolute top-1/2 right-4 size-5 -translate-y-1/2 text-walnut-700"
        viewBox="0 0 24 24"
        fill="none"
        aria-hidden="true"
      >
        <path
          d="M6 9l6 6 6-6"
          stroke="currentColor"
          stroke-width="2.5"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
      </svg>
    </div>
    <p v-if="error" :id="errorId" class="text-sm font-medium text-brick-700">{{ error }}</p>
  </div>
</template>
