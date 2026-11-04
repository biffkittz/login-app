<script setup lang="ts">
import { computed, reactive, useTemplateRef } from 'vue'
import { RouterLink, useRoute, useRouter } from 'vue-router'

import AuthCard from '@/components/auth/auth-card.vue'
import BaseButton from '@/components/ui/base-button.vue'
import FormAlert from '@/components/ui/form-alert.vue'
import PasswordField from '@/components/ui/password-field.vue'
import TextField from '@/components/ui/text-field.vue'
import { useAsyncAction } from '@/composables/useAsyncAction'
import { useFocusFirstInvalidField } from '@/composables/useFocusFirstInvalidField'
import { useFormValidation } from '@/composables/useFormValidation'
import { useRedirectTarget } from '@/composables/useRedirectTarget'
import { formLevelMessage } from '@/services/api-error'
import { useAuthStore } from '@/stores/auth'
import type { SignOutReason } from '@/types/auth'
import { email, required } from '@/validation/rules'

const SIGN_OUT_NOTICES: Record<SignOutReason, { tone: 'success' | 'info'; message: string }> = {
  'signed-out': { tone: 'success', message: "You've signed out. Come back anytime." },
  'session-expired': {
    tone: 'info',
    message: 'Your session timed out, so we signed you out. Please sign in again.',
  },
}

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const redirectTarget = useRedirectTarget()
const form = useTemplateRef<HTMLFormElement>('form')
const focusFirstInvalidField = useFocusFirstInvalidField(form)

// Explain once why the user landed here, then forget it so the notice doesn't reappear on the next visit.
const notice = auth.signOutReason ? SIGN_OUT_NOTICES[auth.signOutReason] : null
auth.clearSignOutReason()

const credentials = reactive({ email: '', password: '' })

const { errors, touch, validateAll, setServerErrors } = useFormValidation(credentials, {
  email: [
    required('Enter your email address.'),
    email('Enter an email address like name@example.com.'),
  ],
  password: [required('Enter your password.')],
})

const {
  run: signIn,
  isPending,
  error,
} = useAsyncAction((signal) =>
  auth.login({ email: credentials.email.trim(), password: credentials.password }, { signal }),
)

const formError = computed(() => (error.value ? formLevelMessage(error.value) : null))

async function onSubmit(): Promise<void> {
  if (!validateAll()) {
    await focusFirstInvalidField()
    return
  }

  const result = await signIn()
  if (result.ok) {
    await router.replace(redirectTarget.value)
  } else if (result.error) {
    setServerErrors(result.error.fieldErrors)
    await focusFirstInvalidField()
  }
}
</script>

<template>
  <AuthCard
    eyebrow="Welcome back"
    title="Sign in"
    subtitle="Pull up a chair. It's good to see you again."
  >
    <form ref="form" class="flex flex-col gap-5" novalidate @submit.prevent="onSubmit">
      <FormAlert v-if="notice && !formError" :tone="notice.tone">{{ notice.message }}</FormAlert>
      <FormAlert v-if="formError">{{ formError }}</FormAlert>

      <TextField
        v-model="credentials.email"
        label="Email address"
        type="email"
        autocomplete="username"
        inputmode="email"
        required
        autofocus
        :error="errors.email"
        @blur="touch('email')"
      />

      <PasswordField
        v-model="credentials.password"
        label="Password"
        autocomplete="current-password"
        required
        :error="errors.password"
        @blur="touch('password')"
      />

      <BaseButton type="submit" block :loading="isPending" class="mt-2">
        {{ isPending ? 'Signing in…' : 'Sign in' }}
      </BaseButton>
    </form>

    <template #footer>
      New here?
      <RouterLink :to="{ name: 'register', query: route.query }" class="text-link">
        Create an account
      </RouterLink>
    </template>
  </AuthCard>
</template>
