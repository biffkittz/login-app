<script setup lang="ts">
import { computed, useId } from 'vue'

import BaseButton from '@/components/ui/base-button.vue'
import type { GroceryRun, GroceryRunAction, GroceryRunParticipant } from '@/types/grocery'
import { formatCountdown, formatClockTime } from '@/utils/grocery-format'

interface Props {
  /** The run under way: in last call or shopping. */
  run: GroceryRun
  /** The signed-in user is the shopper. */
  isShopper: boolean
  /** The signed-in user's place among the people asked, or `null` when they weren't asked. */
  myParticipation: GroceryRunParticipant | null
  /** Whole seconds until last call ends. */
  secondsLeft: number
  /** The action whose request is on its way, if any. */
  busy?: GroceryRunAction | null
}

const { run, isShopper, myParticipation, secondsLeft, busy = null } = defineProps<Props>()

const emit = defineEmits<{
  /** "I'm good", or one of the shopper's buttons. */
  action: [action: GroceryRunAction]
}>()

const titleId = useId()

const shopper = computed(() => run.shopperName ?? 'Someone')
const readyCount = computed(
  () => run.participants.filter((participant) => participant.ready).length,
)

const title = computed(() => {
  if (run.status === 'lastCall') {
    return isShopper
      ? "Last call: you're going shopping"
      : `Last call: ${shopper.value} is going shopping`
  }
  return isShopper ? "You're shopping" : `${shopper.value} is shopping`
})

const timing = computed(() => {
  if (run.status === 'lastCall') {
    return secondsLeft > 0 ? `${formatCountdown(secondsLeft)} left` : 'Wrapping up…'
  }
  return run.shoppingStartedAt === null ? null : `Since ${formatClockTime(run.shoppingStartedAt)}`
})

const message = computed(() => {
  if (run.status === 'shopping') {
    const why =
      run.lastCallOutcome === 'everyoneReady'
        ? 'Everyone was good to go.'
        : run.lastCallOutcome === 'shopperWentEarly'
          ? `${isShopper ? 'You' : shopper.value} went early.`
          : 'Last call is over.'
    return isShopper
      ? `${why} The list is final; check items off as you go, and tap Done when you're finished.`
      : `${why} The list is final, though you can still change it: ${shopper.value} sees changes as they happen.`
  }
  if (isShopper) {
    return run.participants.length === 0
      ? "Nobody else has the list open, so last call runs its full time. Start whenever you're ready."
      : 'Shopping starts when everyone asked is good to go, or when the time is up.'
  }
  if (myParticipation?.ready) {
    return "You're good to go. You can still add things until the list is final."
  }
  return 'Add anything you need now. Removing and moving items waits until the shopping starts.'
})

// Everyone asked, by name, with a check mark once they're good to go.
const participants = computed(() => (run.status === 'lastCall' ? run.participants : []))
</script>

<template>
  <section
    :aria-labelledby="titleId"
    class="mb-4 rounded-[1.75rem] border-2 border-walnut-800 bg-mustard-400/25 px-5 py-4 shadow-[6px_6px_0_0_var(--color-walnut-800)]"
    data-grocery-run
  >
    <div class="flex items-start gap-3">
      <svg
        class="mt-0.5 size-7 shrink-0 text-ember-700"
        viewBox="0 0 24 24"
        fill="none"
        aria-hidden="true"
      >
        <path
          d="M3 4h2l2.2 10.2a1.5 1.5 0 0 0 1.5 1.2h8.6a1.5 1.5 0 0 0 1.5-1.1L21 8H6.2"
          stroke="currentColor"
          stroke-width="2.25"
          stroke-linecap="round"
          stroke-linejoin="round"
        />
        <circle cx="9.5" cy="19.5" r="1.5" fill="currentColor" />
        <circle cx="17" cy="19.5" r="1.5" fill="currentColor" />
      </svg>
      <div class="min-w-0 flex-1">
        <h2 :id="titleId" class="font-display text-lg leading-snug font-bold text-walnut-900">
          {{ title }}
        </h2>
        <!-- Not live: a screen reader would read every second. The view announces what changes. -->
        <p
          v-if="timing"
          class="font-display text-sm font-semibold text-ember-800 tabular-nums"
          data-run-timing
        >
          {{ timing }}
        </p>
      </div>
    </div>

    <p class="mt-2 text-sm text-walnut-800">{{ message }}</p>

    <div v-if="participants.length > 0" class="mt-3">
      <p class="sr-only">{{ readyCount }} of {{ participants.length }} good to go:</p>
      <ul class="flex flex-wrap gap-2" aria-label="People asked">
        <li
          v-for="participant in participants"
          :key="participant.userId"
          class="inline-flex items-center gap-1 rounded-full border-2 px-2.5 py-0.5 text-sm font-semibold"
          :class="
            participant.ready
              ? 'border-sage-700 bg-cream-50 text-sage-700'
              : 'border-walnut-500/60 bg-cream-50/60 text-walnut-700'
          "
        >
          <svg
            v-if="participant.ready"
            class="size-3.5"
            viewBox="0 0 24 24"
            fill="none"
            aria-hidden="true"
          >
            <path
              d="M5 12.5l4.5 4.5L19 7.5"
              stroke="currentColor"
              stroke-width="3"
              stroke-linecap="round"
              stroke-linejoin="round"
            />
          </svg>
          {{ participant.displayName
          }}<span class="sr-only">{{ participant.ready ? ', good to go' : ', not yet' }}</span>
        </li>
      </ul>
    </div>

    <div class="mt-4 flex flex-wrap gap-3 empty:hidden">
      <BaseButton
        v-if="run.status === 'lastCall' && myParticipation !== null && !myParticipation.ready"
        :loading="busy === 'ready'"
        @click="emit('action', 'ready')"
      >
        I'm good
      </BaseButton>
      <template v-if="isShopper && run.status === 'lastCall'">
        <BaseButton :loading="busy === 'start-shopping'" @click="emit('action', 'start-shopping')">
          Start shopping now
        </BaseButton>
        <BaseButton
          variant="secondary"
          :loading="busy === 'cancel'"
          @click="emit('action', 'cancel')"
        >
          Call it off
        </BaseButton>
      </template>
      <BaseButton
        v-if="isShopper && run.status === 'shopping'"
        :loading="busy === 'finish'"
        @click="emit('action', 'finish')"
      >
        Done shopping
      </BaseButton>
    </div>
  </section>
</template>
