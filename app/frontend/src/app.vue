<script setup lang="ts">
import { RouterView } from 'vue-router'

import ErrorBoundary from '@/components/error-boundary.vue'
import AppHeader from '@/components/layout/app-header.vue'
import MidCenturyBackdrop from '@/components/layout/mid-century-backdrop.vue'
import { useSignedOutRedirect } from '@/composables/useSignedOutRedirect'

useSignedOutRedirect()
</script>

<template>
  <div class="relative isolate flex min-h-dvh flex-col overflow-x-clip">
    <a
      href="#main-content"
      class="sr-only rounded-full bg-walnut-900 px-4 py-2 font-semibold text-cream-50 focus:not-sr-only focus:absolute focus:top-4 focus:left-4 focus:z-20"
    >
      Skip to main content
    </a>

    <MidCenturyBackdrop />
    <AppHeader />

    <main
      id="main-content"
      tabindex="-1"
      class="flex flex-1 items-start justify-center px-4 py-8 focus:outline-none sm:items-center sm:px-6 sm:py-12"
    >
      <ErrorBoundary>
        <RouterView v-slot="{ Component }">
          <Transition
            mode="out-in"
            enter-active-class="transition duration-300 ease-out motion-reduce:transition-none"
            enter-from-class="translate-y-2 opacity-0"
            leave-active-class="transition duration-150 ease-in motion-reduce:transition-none"
            leave-to-class="opacity-0"
          >
            <component :is="Component" />
          </Transition>
        </RouterView>
      </ErrorBoundary>
    </main>
  </div>
</template>
