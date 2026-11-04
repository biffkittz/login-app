<script setup lang="ts">
import { computed } from 'vue'
import { storeToRefs } from 'pinia'

import AuthCard from '@/components/auth/auth-card.vue'
import BaseButton from '@/components/ui/base-button.vue'
import { useAsyncAction } from '@/composables/useAsyncAction'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const { user } = storeToRefs(auth)

// logout() never throws (see the auth store); the action only provides the button's pending state. Navigation
// back to the login page is handled centrally by useSignedOutRedirect.
const { run: signOut, isPending: isSigningOut } = useAsyncAction(() => auth.logout())

const timeFormat = new Intl.DateTimeFormat(undefined, { hour: 'numeric', minute: '2-digit' })
const sessionEndsAt = computed(() =>
  user.value ? timeFormat.format(new Date(user.value.sessionExpiresAt)) : '',
)

// Long addresses must wrap on narrow cards; offering a break right after the "@" keeps both halves readable
// instead of splitting a word at an arbitrary letter.
const emailParts = computed(() => {
  const email = user.value?.email ?? ''
  const at = email.lastIndexOf('@')
  return at < 0
    ? { mailbox: email, domain: '' }
    : { mailbox: email.slice(0, at + 1), domain: email.slice(at + 1) }
})
</script>

<template>
  <AuthCard
    v-if="user"
    eyebrow="You're signed in"
    :title="`Welcome home, ${user.displayName}.`"
    subtitle="Kick off your shoes and stay a while."
    focus-title
  >
    <dl class="grid gap-4 rounded-2xl border-2 border-cream-300 bg-cream-50 p-5 text-sm">
      <div>
        <dt class="font-display text-xs font-semibold tracking-[0.15em] text-walnut-600 uppercase">
          Signed in as
        </dt>
        <dd class="mt-1 text-base font-semibold [overflow-wrap:anywhere] text-walnut-900">
          {{ emailParts.mailbox }}<wbr />{{ emailParts.domain }}
        </dd>
      </div>
      <div>
        <dt class="font-display text-xs font-semibold tracking-[0.15em] text-walnut-600 uppercase">
          Session ends
        </dt>
        <dd class="mt-1 text-base text-walnut-900">
          <time :datetime="user.sessionExpiresAt">{{ sessionEndsAt }}</time>
        </dd>
      </div>
    </dl>

    <BaseButton block class="mt-8" :loading="isSigningOut" @click="signOut()">
      {{ isSigningOut ? 'Signing out…' : 'Sign out' }}
    </BaseButton>
  </AuthCard>
</template>
