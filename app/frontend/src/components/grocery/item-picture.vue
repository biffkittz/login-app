<script setup lang="ts">
import { ref, watch } from 'vue'

interface Props {
  /** The picture's URL, or `null` when there is none (yet). */
  url: string | null
  /** The picture is being drawn. */
  pending?: boolean
}

const { url, pending = false } = defineProps<Props>()

// A picture that can't be loaded (the phone went offline before it arrived, say) shows the plain tile rather than
// the browser's broken-image icon.
const failed = ref(false)
watch(
  () => url,
  () => {
    failed.value = false
  },
)
</script>

<template>
  <!--
    Decorative: the item's name, right next to it, already says what it shows. The fixed size keeps rows from jumping
    when the picture arrives, and lazy loading leaves pictures below the fold for later.
  -->
  <span
    class="relative grid size-10 shrink-0 place-items-center overflow-hidden rounded-xl bg-cream-200 ring-1 ring-walnut-800/15"
    aria-hidden="true"
    data-item-picture
  >
    <img
      v-if="url && !failed"
      :src="url"
      alt=""
      width="40"
      height="40"
      loading="lazy"
      decoding="async"
      class="size-full object-cover"
      @error="failed = true"
    />
    <!-- Being drawn: a gently pulsing sparkle. -->
    <svg
      v-else-if="pending && !url"
      class="size-5 text-walnut-400 motion-safe:animate-pulse"
      viewBox="0 0 24 24"
      fill="currentColor"
      data-picture-pending
    >
      <path
        d="M12 2.5l1.9 5.6 5.6 1.9-5.6 1.9L12 17.5l-1.9-5.6L4.5 10l5.6-1.9L12 2.5zM18.5 15l.9 2.6 2.6.9-2.6.9-.9 2.6-.9-2.6-2.6-.9 2.6-.9.9-2.6z"
      />
    </svg>
    <!-- No picture: a plain grocery bag, which keeps the names in line with those that have one. -->
    <svg v-else class="size-5 text-walnut-400/70" viewBox="0 0 24 24" fill="none" data-picture-none>
      <path
        d="M5.5 8.5h13l-1 11.5h-11l-1-11.5zM9 8.5V7a3 3 0 0 1 6 0v1.5"
        stroke="currentColor"
        stroke-width="2"
        stroke-linejoin="round"
      />
    </svg>
  </span>
</template>
