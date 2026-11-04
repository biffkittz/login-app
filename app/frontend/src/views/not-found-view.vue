<script setup lang="ts">
import { RouterLink } from 'vue-router'

import AuthCard from '@/components/auth/auth-card.vue'
</script>

<template>
  <AuthCard
    eyebrow="Page not found"
    title="This page wandered off."
    subtitle="We looked under every cushion but couldn't find it."
    focus-title
  >
    <RouterLink
      :to="{ name: 'home' }"
      class="inline-flex w-full items-center justify-center rounded-full border-2 border-walnut-800 bg-cream-50 px-6 py-3 font-display font-semibold text-walnut-900 transition hover:bg-cream-200 focus-visible:outline-3 focus-visible:outline-offset-3 focus-visible:outline-ember-600 motion-reduce:transition-none"
    >
      Take me back
    </RouterLink>
  </AuthCard>
</template>
