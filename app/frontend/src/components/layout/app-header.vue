<script setup lang="ts">
import { storeToRefs } from 'pinia'
import { RouterLink } from 'vue-router'

import StarburstMark from '@/components/brand/starburst-mark.vue'
import { useAsyncAction } from '@/composables/useAsyncAction'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const { user } = storeToRefs(auth)

// logout() never throws (see the auth store); the action only provides the button's pending state. Navigation back to
// the login page is handled centrally by useSignedOutRedirect.
const { run: signOut, isPending: isSigningOut } = useAsyncAction(() => auth.logout())
</script>

<template>
  <header
    class="flex items-center justify-between gap-3 px-4 pt-[max(1.25rem,env(safe-area-inset-top))] sm:px-8 sm:pt-6"
  >
    <RouterLink
      :to="{ name: 'home' }"
      class="inline-flex items-center gap-3 rounded-full pr-3 font-display text-xl font-bold tracking-tight text-walnut-900 focus-visible:outline-3 focus-visible:outline-offset-4 focus-visible:outline-ember-600"
    >
      <StarburstMark class="size-10" />
      <span>Nibbles</span>
    </RouterLink>

    <div v-if="user" class="flex min-w-0 items-center gap-3">
      <p class="hidden truncate text-sm text-walnut-700 sm:block">
        Signed in as <span class="font-semibold text-walnut-900">{{ user.displayName }}</span>
      </p>
      <button
        type="button"
        class="shrink-0 rounded-full border-2 border-walnut-800 bg-cream-50 px-4 py-2 font-display text-sm font-semibold text-walnut-900 transition hover:bg-cream-200 focus-visible:outline-3 focus-visible:outline-offset-2 focus-visible:outline-ember-600 motion-reduce:transition-none"
        :aria-disabled="isSigningOut || undefined"
        @click="isSigningOut || signOut()"
      >
        {{ isSigningOut ? 'Signing out…' : 'Sign out' }}
      </button>
    </div>
  </header>
</template>
