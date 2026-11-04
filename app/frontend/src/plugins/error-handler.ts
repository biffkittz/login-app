import type { App, Plugin } from 'vue'

/**
 * Last line of defense for errors nothing else handled.
 *
 * Everything is logged to the console. This plugin is the single place to forward errors to a monitoring
 * service (Application Insights, Sentry, ...) should the project adopt one.
 */
export const errorHandlerPlugin: Plugin = {
  install(app: App) {
    app.config.errorHandler = (error, _instance, info) => {
      console.error(`[login-app] Unhandled error (${info}):`, error)
    }

    window.addEventListener('unhandledrejection', (event) => {
      console.error('[login-app] Unhandled promise rejection:', event.reason)
    })

    // After a deployment, a tab still running the previous build asks for code-split chunks that no longer exist.
    // Reloading picks up the new build instead of leaving navigation broken.
    window.addEventListener('vite:preloadError', () => {
      window.location.reload()
    })
  },
}
