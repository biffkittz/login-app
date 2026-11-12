<script setup lang="ts">
import { computed, ref, useId, watch } from 'vue'

import BaseButton from '@/components/ui/base-button.vue'
import BottomSheet from '@/components/ui/bottom-sheet.vue'
import FormAlert from '@/components/ui/form-alert.vue'
import { formatLastCallLength } from '@/utils/grocery-format'

interface Props {
  /** The lengths of last call the server offers, in minutes. */
  lengths: readonly number[]
  /** The server can hold grocery runs at all. */
  available: boolean
  /** The request to start is on its way, or the run hasn't appeared yet. */
  starting?: boolean
  /** Why starting failed. */
  error?: string | null
}

const { lengths, available, starting = false, error = null } = defineProps<Props>()

/** Whether the sheet is open (two-way bound with `v-model:open`). */
const open = defineModel<boolean>('open', { required: true })

const emit = defineEmits<{
  /** Start a run with last call of this many minutes. */
  start: [minutes: number]
}>()

const groupName = useId()

// Half an hour is long enough for everyone to look, short enough to still leave soon; otherwise the first length.
const preferred = computed(() => (lengths.includes(30) ? 30 : (lengths[0] ?? null)))
const chosen = ref<number | null>(null)

watch(open, (isOpen) => {
  if (isOpen) {
    chosen.value = preferred.value
  }
})

function onSubmit(): void {
  if (chosen.value !== null && !starting) {
    emit('start', chosen.value)
  }
}
</script>

<template>
  <BottomSheet v-model:open="open" title="Start a grocery run">
    <form
      class="flex min-h-0 flex-col gap-4 overflow-y-auto px-5 pt-4 pb-[max(1.25rem,env(safe-area-inset-bottom))]"
      novalidate
      @submit.prevent="onSubmit"
    >
      <FormAlert v-if="error">{{ error }}</FormAlert>
      <FormAlert v-if="!available" tone="info">
        Grocery runs aren't set up on this server.
      </FormAlert>

      <p class="text-sm text-walnut-800">
        Everyone who has the list open gets a last call to add what they need. Until it ends, items
        can be added and changed but not removed or moved. It ends early once they've all said
        they're good, or whenever you start shopping.
      </p>

      <fieldset v-if="available && lengths.length > 0" class="flex flex-col gap-2">
        <legend class="mb-2 font-display font-semibold text-walnut-900">Last call lasts</legend>
        <label
          v-for="minutes in lengths"
          :key="minutes"
          class="flex cursor-pointer items-center gap-3 rounded-2xl border-2 px-4 py-3 font-semibold transition-colors motion-reduce:transition-none has-[:focus-visible]:outline-3 has-[:focus-visible]:outline-offset-2 has-[:focus-visible]:outline-ember-600"
          :class="
            chosen === minutes
              ? 'border-ember-700 bg-ember-600/10 text-walnut-900'
              : 'border-cream-300 bg-cream-50 text-walnut-800 hover:border-walnut-500'
          "
        >
          <input
            v-model="chosen"
            type="radio"
            class="size-5 accent-ember-700"
            :name="groupName"
            :value="minutes"
          />
          {{ formatLastCallLength(minutes) }}
        </label>
      </fieldset>

      <div class="flex justify-end gap-3 pt-1">
        <BaseButton variant="secondary" @click="open = false">Cancel</BaseButton>
        <BaseButton v-if="available" type="submit" :loading="starting">
          {{ starting ? 'Starting…' : 'Start' }}
        </BaseButton>
      </div>
    </form>
  </BottomSheet>
</template>
