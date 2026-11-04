<script setup lang="ts">
import { RouterLink } from 'vue-router'

import StarburstMark from '@/components/brand/starburst-mark.vue'
</script>

<template>
  <header class="px-4 pt-6 sm:px-8">
    <RouterLink
      :to="{ name: 'home' }"
      class="inline-flex items-center gap-3 rounded-full pr-3 font-display text-xl font-bold tracking-tight text-walnut-900 focus-visible:outline-3 focus-visible:outline-offset-4 focus-visible:outline-ember-600"
    >
      <StarburstMark class="size-10" />
      <span>Login App</span>
    </RouterLink>
  </header>
</template>
