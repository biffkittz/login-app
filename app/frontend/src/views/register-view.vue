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
import {
  DISPLAY_NAME_MAX_LENGTH,
  EMAIL_MAX_LENGTH,
  PASSWORD_MAX_LENGTH,
  PASSWORD_MIN_LENGTH,
  PASSWORD_MIN_UNIQUE_CHARACTERS,
} from '@/validation/account-policy'
import {
  email,
  matchesField,
  maxLength,
  minLength,
  minUniqueCharacters,
  required,
} from '@/validation/rules'

// A type alias rather than an interface: form models must be assignable to Record<string, string>.
type RegistrationForm = {
  displayName: string
  email: string
  password: string
  confirmPassword: string
}

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
const redirectTarget = useRedirectTarget()
const form = useTemplateRef<HTMLFormElement>('form')
const focusFirstInvalidField = useFocusFirstInvalidField(form)

const details = reactive<RegistrationForm>({
  displayName: '',
  email: '',
  password: '',
  confirmPassword: '',
})

const passwordHint = `At least ${PASSWORD_MIN_LENGTH} characters. A short phrase is easy to remember and hard to guess.`

const { errors, touch, validateAll, setServerErrors } = useFormValidation(details, {
  displayName: [
    required('Enter your name.'),
    maxLength(DISPLAY_NAME_MAX_LENGTH, `Use ${DISPLAY_NAME_MAX_LENGTH} characters or fewer.`),
  ],
  email: [
    required('Enter your email address.'),
    email('Enter an email address like name@example.com.'),
    maxLength(EMAIL_MAX_LENGTH, `Use ${EMAIL_MAX_LENGTH} characters or fewer.`),
  ],
  password: [
    required('Choose a password.'),
    minLength(PASSWORD_MIN_LENGTH, `Use at least ${PASSWORD_MIN_LENGTH} characters.`),
    maxLength(PASSWORD_MAX_LENGTH, `Use ${PASSWORD_MAX_LENGTH} characters or fewer.`),
    minUniqueCharacters(
      PASSWORD_MIN_UNIQUE_CHARACTERS,
      `Use at least ${PASSWORD_MIN_UNIQUE_CHARACTERS} different characters.`,
    ),
  ],
  confirmPassword: [
    required('Enter your password again.'),
    matchesField<RegistrationForm>('password', "The passwords don't match."),
  ],
})

const {
  run: createAccount,
  isPending,
  error,
} = useAsyncAction((signal) =>
  auth.register(
    {
      displayName: details.displayName.trim(),
      email: details.email.trim(),
      password: details.password,
    },
    { signal },
  ),
)

const formError = computed(() => (error.value ? formLevelMessage(error.value) : null))

async function onSubmit(): Promise<void> {
  if (!validateAll()) {
    await focusFirstInvalidField()
    return
  }

  const result = await createAccount()
  if (result.ok) {
    await router.replace(redirectTarget.value)
  } else if (result.error) {
    // e.g. "An account with this email address already exists." lands under the email field.
    setServerErrors(result.error.fieldErrors)
    await focusFirstInvalidField()
  }
}
</script>

<template>
  <AuthCard
    eyebrow="First time here?"
    title="Create your account"
    subtitle="It only takes a minute. Then make yourself at home."
  >
    <form ref="form" class="flex flex-col gap-5" novalidate @submit.prevent="onSubmit">
      <FormAlert v-if="formError">{{ formError }}</FormAlert>

      <TextField
        v-model="details.displayName"
        label="Your name"
        autocomplete="name"
        required
        autofocus
        :error="errors.displayName"
        @blur="touch('displayName')"
      />

      <TextField
        v-model="details.email"
        label="Email address"
        type="email"
        autocomplete="username"
        inputmode="email"
        required
        :error="errors.email"
        @blur="touch('email')"
      />

      <PasswordField
        v-model="details.password"
        label="Password"
        autocomplete="new-password"
        required
        :hint="passwordHint"
        :error="errors.password"
        @blur="touch('password')"
      />

      <PasswordField
        v-model="details.confirmPassword"
        label="Confirm password"
        autocomplete="new-password"
        required
        :error="errors.confirmPassword"
        @blur="touch('confirmPassword')"
      />

      <BaseButton type="submit" block :loading="isPending" class="mt-2">
        {{ isPending ? 'Creating your account…' : 'Create account' }}
      </BaseButton>
    </form>

    <template #footer>
      Already have an account?
      <RouterLink :to="{ name: 'login', query: route.query }" class="text-link">Sign in</RouterLink>
    </template>
  </AuthCard>
</template>
