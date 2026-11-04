import 'vue-router'

export {}

declare module 'vue-router' {
  interface RouteMeta {
    /** Shown in the browser tab as "<title> · Login App". */
    title?: string
    /** Only signed-in users may visit; everyone else is sent to the login page. */
    requiresAuth?: boolean
    /** Only signed-out users may visit (login, register); signed-in users are sent home. */
    guestOnly?: boolean
  }
}
