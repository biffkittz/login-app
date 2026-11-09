<script setup lang="ts">
import { computed } from 'vue'

import BottomSheet from '@/components/ui/bottom-sheet.vue'
import FormAlert from '@/components/ui/form-alert.vue'
import ToggleSwitch from '@/components/ui/toggle-switch.vue'
import type { GroceryListSettings } from '@/types/grocery'

interface Props {
  /** The list's settings; `null` until the list has loaded. */
  settings: GroceryListSettings | null
  /** Why the last change failed. */
  error?: string | null
}

const { settings, error = null } = defineProps<Props>()

/** Whether the sheet is open (two-way bound with `v-model:open`). */
const open = defineModel<boolean>('open', { required: true })

const emit = defineEmits<{
  /** Turn AI pictures on or off, for everyone. */
  setImageGeneration: [enabled: boolean]
}>()

const available = computed(() => settings?.imageGenerationAvailable === true)

// The switch shows the setting as the store holds it, which moves at once when changed; the store saves it.
const imageGeneration = computed({
  get: () => settings?.imageGenerationEnabled === true,
  set: (enabled: boolean) => emit('setImageGeneration', enabled),
})
</script>

<template>
  <BottomSheet v-model:open="open" title="List settings">
    <div
      class="flex flex-col gap-4 overflow-y-auto px-5 pt-4 pb-[max(1.25rem,env(safe-area-inset-bottom))]"
    >
      <FormAlert v-if="error">{{ error }}</FormAlert>

      <ToggleSwitch
        v-model="imageGeneration"
        label="AI pictures"
        description="Google's Gemini draws a small picture of each new item from its name; only the name is sent. Turning this off stops new pictures, and pictures already drawn stay."
        :disabled="!available"
      />

      <FormAlert v-if="settings !== null && !available" tone="info">
        AI pictures aren't set up on this server: it needs a Gemini API key.
      </FormAlert>

      <p class="text-sm text-walnut-700">
        Settings are shared by everyone using the list, like the list itself.
      </p>
    </div>
  </BottomSheet>
</template>
