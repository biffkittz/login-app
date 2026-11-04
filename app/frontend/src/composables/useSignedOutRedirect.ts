import { watch } from 'vue'
import { useRouter } from 'vue-router'

import { useAuthStore } from '@/stores/auth'

/**
 * Sends the user to the login page whenever their session ends while they are on a page that requires one:
 * after clicking "Sign out", or when the session expires in an idle tab.
 *
 * Centralising this means views never navigate after signing out themselves; they only change auth state.
 * Call once, from the root component.
 */
export function useSignedOutRedirect(): void {
  const auth = useAuthStore()
  const router = useRouter()

  watch(
    () => auth.isAuthenticated,
    async (isAuthenticated) => {
      if (!isAuthenticated && router.currentRoute.value.meta.requiresAuth) {
        await router.replace({ name: 'login' })
      }
    },
  )
}
