import './assets/main.css'

import { createApp } from 'vue'
import { createPinia } from 'pinia'

import App from './app.vue'
import { errorHandlerPlugin } from './plugins/error-handler'
import router from './router'

const app = createApp(App)

app.use(errorHandlerPlugin)
// Pinia must be installed before the router: the router's first navigation already reads the auth store.
app.use(createPinia())
app.use(router)

app.mount('#app')
