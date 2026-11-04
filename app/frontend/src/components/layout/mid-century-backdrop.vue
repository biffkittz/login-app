<!--
  Decorative background shapes (a low sun, a boomerang, sparkles and a dotted orbit) in the spirit of
  mid-century modern prints. Hidden from assistive technology, ignored by the pointer, and rendered once (v-once)
  because nothing in it ever changes.
-->
<template>
  <div v-once class="pointer-events-none absolute inset-0 -z-10 overflow-hidden" aria-hidden="true">
    <svg class="absolute -right-28 -top-28 size-80 sm:size-[30rem]" viewBox="0 0 200 200">
      <circle cx="100" cy="100" r="96" class="fill-mustard-400/30" />
      <circle cx="100" cy="100" r="70" class="fill-ember-400/30" />
      <circle cx="100" cy="100" r="44" class="fill-ember-500/30" />
    </svg>

    <svg class="absolute -bottom-12 -left-20 w-80 sm:w-[32rem]" viewBox="0 0 240 140">
      <path
        d="M12 128C52 40 140 8 232 22C160 36 96 70 54 132C44 140 20 140 12 128Z"
        class="fill-ember-300/35"
      />
    </svg>

    <svg
      class="absolute bottom-[14%] left-[10%] hidden w-56 md:block"
      viewBox="0 0 200 80"
      fill="none"
    >
      <ellipse
        cx="100"
        cy="40"
        rx="96"
        ry="30"
        class="stroke-walnut-700/25"
        stroke-width="2"
        stroke-dasharray="2 9"
        stroke-linecap="round"
      />
    </svg>

    <svg class="absolute left-[9%] top-[22%] hidden size-10 sm:block" viewBox="0 0 100 100">
      <path
        d="M50 0C54 34 66 46 100 50C66 54 54 66 50 100C46 66 34 54 0 50C34 46 46 34 50 0Z"
        class="fill-ember-500/60"
      />
    </svg>
    <svg class="absolute bottom-[24%] right-[9%] size-7" viewBox="0 0 100 100">
      <path
        d="M50 0C54 34 66 46 100 50C66 54 54 66 50 100C46 66 34 54 0 50C34 46 46 34 50 0Z"
        class="fill-walnut-700/40"
      />
    </svg>
    <svg class="absolute right-[26%] top-[13%] hidden size-5 md:block" viewBox="0 0 100 100">
      <path
        d="M50 0C54 34 66 46 100 50C66 54 54 66 50 100C46 66 34 54 0 50C34 46 46 34 50 0Z"
        class="fill-mustard-400/80"
      />
    </svg>
  </div>
</template>
