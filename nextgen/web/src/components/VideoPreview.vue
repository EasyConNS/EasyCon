<script setup lang="ts">
import { onBeforeUnmount, ref, watch } from 'vue'
import ConsoleIcon from './ConsoleIcon.vue'

const props = defineProps<{ active: boolean }>()
const monitor = ref<HTMLDialogElement>()
const expanded = ref(false)
const visible = ref(document.visibilityState !== 'hidden')
const url = ref('')
const error = ref(false)
let generation = 0
let retryStream = () => {}

function visibilityChanged() { visible.value = document.visibilityState !== 'hidden' }
document.addEventListener('visibilitychange', visibilityChanged)
onBeforeUnmount(() => document.removeEventListener('visibilitychange', visibilityChanged))

watch([() => props.active, visible], ([active, visible], _, cleanup) => {
  let timer: ReturnType<typeof setTimeout> | undefined
  let stopped = false
  cleanup(() => { stopped = true; clearTimeout(timer); url.value = '' })
  error.value = false
  if (!active) monitor.value?.close()
  if (!active || !visible) return

  const connect = () => {
    if (!stopped) url.value = `/api/video/stream?view=${++generation}`
  }
  // One browser-decoded multipart stream, without per-frame JS or Blob URLs.
  connect()
  retryStream = () => {
    if (stopped) return
    error.value = true
    url.value = ''
    clearTimeout(timer)
    timer = setTimeout(connect, 1000)
  }
}, { immediate: true })

function openMonitor() {
  monitor.value?.showModal()
  expanded.value = true
}
</script>

<template>
  <div v-if="expanded" class="video-preview preview-empty"><ConsoleIcon name="video" /><span>画面正在监视窗口中显示</span></div>
  <Teleport :to="monitor || 'body'" :disabled="!expanded">
    <div class="video-preview" :class="{ 'monitor-picture': expanded }">
      <img v-if="url" :src="url" alt="视频源实时画面" @load="error = false" @error="retryStream()" />
      <div v-else class="preview-empty">
        <ConsoleIcon name="video" />
        <span>{{ !active ? '等待视频源连接' : !visible ? '预览已暂停' : '等待视频流' }}</span>
      </div>
      <span v-if="active && url && !error" class="preview-badge"><i /> 实时视频流</span>
      <span v-if="error" class="preview-warning">视频流连接中断，正在重连</span>
      <button v-if="active && !expanded" class="preview-expand" aria-label="打开视频监视窗口" @click="openMonitor"><ConsoleIcon name="expand" />监视窗口</button>
      <span class="preview-corner top-left" /><span class="preview-corner bottom-right" />
    </div>
  </Teleport>
  <dialog ref="monitor" class="video-dialog" aria-labelledby="video-monitor-title" @close="expanded = false">
    <header class="dialog-header">
      <div><p class="eyebrow">LIVE MONITOR</p><h2 id="video-monitor-title">视频监视</h2></div>
      <button class="icon-button" aria-label="关闭视频监视窗口" @click="monitor?.close()"><ConsoleIcon name="close" /></button>
    </header>
    <footer class="monitor-footer">保持画面比例<span>按 Esc 返回设备面板</span></footer>
  </dialog>
</template>
