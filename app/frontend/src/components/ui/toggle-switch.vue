<script setup lang="ts">
import { useId } from 'vue'

interface Props {
  /** What the switch turns on and off. */
  label: string
  /** What the switch does, read out by screen readers together with the label. */
  description?: string
  disabled?: boolean
}

const { label, description, disabled = false } = defineProps<Props>()

/** Whether the switch is on (two-way bound with `v-model`). */
const checked = defineModel<boolean>({ required: true })

const inputId = useId()
const descriptionId = useId()
</script>

<template>
  <!--
    A native checkbox with role="switch": the browser provides the keyboard handling (Space), the focus and the label
    association, and screen readers announce "switch, on". The track and knob only draw it. The label, the text and
    the switch are all one tap target.
  -->
  <div class="flex items-start justify-between gap-4">
    <div class="min-w-0">
      <label
        :for="inputId"
        class="font-display text-base font-bold text-walnut-900"
        :class="disabled ? 'cursor-not-allowed' : 'cursor-pointer'"
      >
        {{ label }}
      </label>
      <p v-if="description" :id="descriptionId" class="mt-1 text-sm text-walnut-700">
        {{ description }}
      </p>
    </div>
    <span class="relative mt-0.5 inline-flex shrink-0">
      <input
        :id="inputId"
        v-model="checked"
        type="checkbox"
        role="switch"
        :disabled="disabled"
        :aria-describedby="description ? descriptionId : undefined"
        class="peer absolute -inset-1.5 z-10 cursor-pointer opacity-0 disabled:cursor-not-allowed"
      />
      <span
        class="h-8 w-14 rounded-full border-2 border-walnut-800 bg-cream-200 transition peer-checked:bg-sage-700 peer-focus-visible:outline-3 peer-focus-visible:outline-offset-2 peer-focus-visible:outline-ember-600 peer-disabled:opacity-50 motion-reduce:transition-none"
        aria-hidden="true"
      />
      <span
        class="pointer-events-none absolute top-1 left-1 size-6 rounded-full border-2 border-walnut-800 bg-cream-50 transition peer-checked:translate-x-6 peer-disabled:opacity-50 motion-reduce:transition-none"
        aria-hidden="true"
      />
    </span>
  </div>
</template>
