<script setup lang="ts">
defineProps<{ name: string }>()
const paths: Record<string, string> = {
  gamepad: 'M6 8h12l3 9a2 2 0 0 1-3 2l-4-3h-4l-4 3a2 2 0 0 1-3-2l3-9ZM7 10v5m-2-2.5h4m6-1h.01m3 2h.01',
  video: 'M4 5h16v12H4zM9 21h6m-3-4v4',
  audio: 'M9 5a3 3 0 0 1 6 0v7a3 3 0 0 1-6 0V5Zm-3 6v1a6 6 0 0 0 12 0v-1m-6 7v4m-3 0h6',
  folder: 'M3 6h7l2 2h9v12H3V6Z',
  file: 'M6 3h8l4 4v14H6V3Zm8 0v5h4M9 12h6m-6 4h6',
  refresh: 'M20 7v5h-5M4 17v-5h5m10.5-2a8 8 0 0 0-13-5m-2 9a8 8 0 0 0 13 5',
  close: 'm6 6 12 12M6 18 18 6',
  up: 'm5 10 7-7 7 7m-7-7v18',
  chevron: 'm9 5 7 7-7 7',
  plug: 'M8 3v5m8-5v5M6 8h12v3a6 6 0 0 1-12 0V8Zm6 9v5',
  check: 'm5 12 4 4L19 6',
  expand: 'M8 3H3v5m13-5h5v5M3 16v5h5m13-5v5h-5',
}
</script>

<template>
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
    <path :d="paths[name] || paths.file" />
  </svg>
</template>
