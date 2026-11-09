import { createRouter, createWebHistory } from 'vue-router'

import { useAuthStore } from '@/stores/auth'

import { resolveAuthNavigation } from './guards'

const APP_NAME = 'Nibbles'

const router = createRouter({
  history: createWebHistory(import.meta.env.BASE_URL),
  routes: [
    {
      path: '/',
      name: 'home',
      // Route-level code splitting: each page is its own chunk, downloaded only when first visited. The list's chunk
      // carries the realtime (SignalR) client, so the sign-in pages never download it.
      component: () => import('@/views/grocery-list-view.vue'),
      meta: { title: 'Grocery list', requiresAuth: true },
    },
    {
      path: '/login',
      name: 'login',
      component: () => import('@/views/login-view.vue'),
      meta: { title: 'Sign in', guestOnly: true },
    },
    {
      path: '/register',
      name: 'register',
      component: () => import('@/views/register-view.vue'),
      meta: { title: 'Create an account', guestOnly: true },
    },
    {
      path: '/:pathMatch(.*)*',
      name: 'not-found',
      component: () => import('@/views/not-found-view.vue'),
      meta: { title: 'Page not found' },
    },
  ],
})

router.beforeEach(async (to) => {
  const auth = useAuthStore()
  // Resolves immediately after the first navigation; the session is only fetched once per page load.
  await auth.restoreSession()
  return resolveAuthNavigation(to, auth.isAuthenticated)
})

router.afterEach((to) => {
  document.title = to.meta.title ? `${to.meta.title} · ${APP_NAME}` : APP_NAME
})

export default router
