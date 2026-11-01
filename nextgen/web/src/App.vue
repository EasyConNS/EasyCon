<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, reactive, ref } from 'vue'
import { api, emptyDevice, type Selection, type Snapshot } from './api'
import ConsoleIcon from './components/ConsoleIcon.vue'
import DeviceCard from './components/DeviceCard.vue'
import WorkflowPicker from './components/WorkflowPicker.vue'

const picker = ref<InstanceType<typeof WorkflowPicker>>()
const workflow = ref('')
const selected = reactive({ gamepad: '', video: '', audio: '' })
const state = ref<Snapshot>({ gamepad: emptyDevice(), video: emptyDevice(), audio: emptyDevice(), rms: 0, dbfs: null })
const online = ref(false)
const initial = ref(true)
const controller = new AbortController()
let timer: ReturnType<typeof setTimeout> | undefined
let polling = false
const connectedCount = computed(() => [state.value.gamepad, state.value.video, state.value.audio].filter(s => s.phase === 'connected' && online.value).length)
const filename = computed(() => workflow.value.split(/[\\/]/).pop())

function selectWorkflow(selection: Selection) {
  workflow.value = selection.path
  if (!state.value.gamepad.id && selection.preferences.gamepad) selected.gamepad = selection.preferences.gamepad
  if (!state.value.video.id && selection.preferences.video) selected.video = selection.preferences.video
}

async function poll() {
  if (polling || controller.signal.aborted) return
  polling = true
  clearTimeout(timer)
  try {
    state.value = await api<Snapshot>('/state', { signal: AbortSignal.any([controller.signal, AbortSignal.timeout(5000)]) })
    online.value = true
  } catch { if (!controller.signal.aborted) online.value = false }
  finally {
    initial.value = false
    polling = false
    if (!controller.signal.aborted) timer = setTimeout(poll, online.value ? 100 : 1500)
  }
}

onMounted(poll)
onBeforeUnmount(() => { controller.abort(); clearTimeout(timer) })
</script>

<template>
  <a class="skip-link" href="#main-content">跳转到设备面板</a>
  <div class="app-shell">
    <header class="app-header">
      <a class="brand" href="/" aria-label="EasyCon 设备控制台"><span class="brand-mark">E<span /></span><strong>EasyCon</strong><span class="brand-divider" /><span class="brand-section">设备控制台</span></a>
      <span class="host-status" :class="{ online }"><i />{{ initial ? '正在连接服务' : online ? '本地服务已就绪' : '本地服务离线' }}<span class="local-tag">LOCAL</span></span>
    </header>

    <main id="main-content">
      <div class="page-heading"><div><p class="eyebrow">DEVICE WORKSPACE</p><h1>设备工作台<span>。</span></h1><p class="page-description">选择工作流，连接你的手柄与采集设备。</p></div><span class="workspace-label"><i />本地工作空间</span></div>

      <p v-if="!online && !initial" class="offline-banner" role="alert">无法连接本地服务。请确认 easycon-web 正在运行，页面会自动重连。</p>

      <section class="workflow-panel" aria-labelledby="workflow-title">
        <span class="workflow-icon"><ConsoleIcon name="file" /></span>
        <div class="workflow-info"><div class="workflow-label"><h2 id="workflow-title">工作流文件</h2><span v-if="workflow" class="valid-file"><ConsoleIcon name="check" />已校验</span><span v-else class="optional-label">尚未选择</span></div><strong v-if="workflow" class="workflow-name">{{ filename }}</strong><p class="workflow-path" :class="{ empty: !workflow }" :title="workflow">{{ workflow || '从项目目录选择 workflow.json' }}</p></div>
        <button class="button secondary file-button" :disabled="!online" @click="picker?.open()"><ConsoleIcon name="folder" />{{ workflow ? '更换文件' : '选择文件' }}</button>
      </section>

      <div class="section-heading"><h2>设备连接</h2><span><strong>{{ connectedCount.toString().padStart(2, '0') }}</strong> / 03 已连接</span></div>
      <div class="device-grid">
        <DeviceCard v-model="selected.gamepad" kind="gamepad" :state="state.gamepad" :online="online" @refresh="poll" />
        <DeviceCard v-model="selected.video" kind="video" :state="state.video" :online="online" @refresh="poll" />
        <DeviceCard v-model="selected.audio" kind="audio" :state="state.audio" :online="online" :dbfs="state.dbfs" @refresh="poll" />
      </div>
      <footer class="workspace-footer"><span><i />设备由本地服务管理</span><span>EASYCON <span class="footer-dot">/</span> WORKSPACE 01</span></footer>
    </main>
  </div>
  <WorkflowPicker ref="picker" @selected="selectWorkflow" />
</template>
