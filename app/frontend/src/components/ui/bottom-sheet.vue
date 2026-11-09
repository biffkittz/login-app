<script setup lang="ts">
import { onBeforeUnmount, onMounted, useId, useTemplateRef, watch } from 'vue'

interface Props {
  /** The sheet's heading, which also names the dialog for assistive technology. */
  title: string
}

const { title } = defineProps<Props>()

/** Whether the sheet is open (two-way bound with `v-model:open`). */
const open = defineModel<boolean>('open', { required: true })

defineSlots<{
  /** The sheet's content, typically a form whose buttons sit at the bottom. */
  default: () => unknown
}>()

const dialog = useTemplateRef<HTMLDialogElement>('dialog')
const titleId = useId()

// The native modal <dialog> provides what an accessible modal needs: focus moves in and is trapped, Escape closes it,
// the rest of the page becomes inert, and focus returns to where it was on close. The model stays the source of
// truth; the element is only kept in step with it.
function syncWithModel(isOpen: boolean): void {
  const element = dialog.value
  if (element === null) {
    return
  }
  if (isOpen && !element.open) {
    element.showModal()
  } else if (!isOpen && element.open) {
    element.close()
  }
  // Without this, iOS scrolls the page behind the sheet when a finger drags inside it.
  document.documentElement.style.overflow = isOpen ? 'hidden' : ''
}

watch(open, syncWithModel, { flush: 'post' })
onMounted(() => syncWithModel(open.value))
onBeforeUnmount(() => {
  document.documentElement.style.overflow = ''
})

// Escape fires `cancel`. Closing through the model keeps the parent's state authoritative.
function onCancel(event: Event): void {
  event.preventDefault()
  open.value = false
}

// A tap on the dimmed backdrop lands on the <dialog> element itself; taps on the content land on its children.
function onClick(event: MouseEvent): void {
  if (event.target === dialog.value) {
    open.value = false
  }
}
</script>

<template>
  <dialog
    ref="dialog"
    :aria-labelledby="titleId"
    class="mx-0 mt-auto mb-0 w-full max-w-full overscroll-contain rounded-t-[1.75rem] border-2 border-b-0 border-walnut-800 bg-cream-100 p-0 text-walnut-900 backdrop:bg-walnut-900/45 motion-safe:animate-sheet-in sm:m-auto sm:max-w-lg sm:rounded-[1.75rem] sm:border-b-2 sm:shadow-[8px_8px_0_0_var(--color-walnut-800)]"
    @cancel="onCancel"
    @close="open = false"
    @click="onClick"
  >
    <div class="flex max-h-[90dvh] flex-col">
      <header
        class="flex items-center justify-between gap-3 border-b-2 border-dashed border-cream-300 py-3 pr-3 pl-5"
      >
        <h2 :id="titleId" class="font-display text-xl font-bold text-walnut-900">{{ title }}</h2>
        <button
          type="button"
          class="grid size-11 place-items-center rounded-full text-walnut-700 transition hover:bg-cream-200 focus-visible:outline-3 focus-visible:outline-offset-1 focus-visible:outline-ember-600 motion-reduce:transition-none"
          @click="open = false"
        >
          <svg class="size-6" viewBox="0 0 24 24" fill="none" aria-hidden="true">
            <path
              d="M6 6l12 12M18 6L6 18"
              stroke="currentColor"
              stroke-width="2.5"
              stroke-linecap="round"
            />
          </svg>
          <span class="sr-only">Close</span>
        </button>
      </header>
      <slot />
    </div>
  </dialog>
</template>
