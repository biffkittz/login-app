<!--
  The app's mark: an "atomic" starburst, a staple of 1950s and 60s design. Purely decorative, so it is hidden
  from assistive technology; the text next to it carries the meaning.
-->
<template>
  <svg viewBox="0 0 64 64" aria-hidden="true" focusable="false">
    <g class="stroke-ember-600" stroke-width="3.5" stroke-linecap="round">
      <line x1="32" y1="5" x2="32" y2="59" />
      <line x1="5" y1="32" x2="59" y2="32" />
      <line x1="15" y1="15" x2="49" y2="49" />
      <line x1="49" y1="15" x2="15" y2="49" />
    </g>
    <g class="fill-walnut-800">
      <circle cx="32" cy="5" r="3.5" />
      <circle cx="32" cy="59" r="3.5" />
      <circle cx="5" cy="32" r="3.5" />
      <circle cx="59" cy="32" r="3.5" />
    </g>
    <circle cx="32" cy="32" r="9" class="fill-mustard-400 stroke-walnut-800" stroke-width="2.5" />
  </svg>
</template>
