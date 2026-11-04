import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { h } from 'vue'
import { createMemoryHistory, createRouter, type Router } from 'vue-router'

import { ApiError } from '@/services/api-error'
import * as authApi from '@/services/auth-api'
import { useAuthStore } from '@/stores/auth'

import LoginView from '../login-view.vue'

vi.mock('@/services/auth-api')

const Placeholder = { render: () => h('p', 'placeholder') }

async function mountLoginView(path = '/login'): Promise<{
  wrapper: ReturnType<typeof mount>
  router: Router
}> {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', name: 'home', component: Placeholder },
      { path: '/login', name: 'login', component: LoginView },
      { path: '/register', name: 'register', component: Placeholder },
      { path: '/settings', name: 'settings', component: Placeholder },
    ],
  })
  await router.push(path)
  await router.isReady()

  // attachTo puts the form in the document so focus can be asserted.
  const wrapper = mount(LoginView, {
    global: { plugins: [router] },
    attachTo: document.body,
  })
  return { wrapper, router }
}

describe('LoginView', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    document.body.innerHTML = ''
  })

  it('shows field errors and focuses the first invalid field on an empty submit', async () => {
    const { wrapper } = await mountLoginView()

    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.text()).toContain('Enter your email address.')
    expect(wrapper.text()).toContain('Enter your password.')
    expect(document.activeElement).toBe(wrapper.get('input[type="email"]').element)
    expect(authApi.login).not.toHaveBeenCalled()
  })

  it('signs in with a trimmed email and returns to the page the user asked for', async () => {
    vi.mocked(authApi.login).mockResolvedValue({
      id: 'user-1',
      email: 'ada@example.com',
      displayName: 'Ada',
      sessionExpiresAt: new Date(Date.now() + 60_000).toISOString(),
    })
    const { wrapper, router } = await mountLoginView('/login?redirect=/settings')

    await wrapper.get('input[type="email"]').setValue('  ada@example.com ')
    await wrapper.get('input[type="password"]').setValue('correct horse battery staple')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(authApi.login).toHaveBeenCalledWith(
      { email: 'ada@example.com', password: 'correct horse battery staple' },
      expect.objectContaining({ signal: expect.any(AbortSignal) }),
    )
    expect(router.currentRoute.value.fullPath).toBe('/settings')
  })

  it('shows the server message when the credentials are wrong', async () => {
    vi.mocked(authApi.login).mockRejectedValue(
      new ApiError('Incorrect email or password.', { kind: 'http', status: 401 }),
    )
    const { wrapper, router } = await mountLoginView()

    await wrapper.get('input[type="email"]').setValue('ada@example.com')
    await wrapper.get('input[type="password"]').setValue('wrong password')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(wrapper.get('[role="alert"]').text()).toBe('Incorrect email or password.')
    expect(router.currentRoute.value.name).toBe('login')
  })

  it('explains why the user was signed out, once', async () => {
    useAuthStore().$patch({ signOutReason: 'session-expired' })

    const { wrapper } = await mountLoginView()

    expect(wrapper.get('[role="status"]').text()).toContain('Your session timed out')
    expect(useAuthStore().signOutReason).toBeNull()
  })
})
