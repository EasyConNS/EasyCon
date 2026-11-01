<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import { api, message, type DeviceKind, type DeviceOption, type DeviceState } from '../api'
import ConsoleIcon from './ConsoleIcon.vue'
import VideoPreview from './VideoPreview.vue'
import AudioMeter from './AudioMeter.vue'

const props = defineProps<{ kind: DeviceKind; state: DeviceState; online: boolean; modelValue: string; dbfs?: number | null }>()
const emit = defineEmits<{ 'update:modelValue': [id: string]; refresh: [] }>()
const labels = {
  gamepad: { title: '手柄串口', caption: 'CONTROLLER', selector: '选择串口', empty: '未发现串口设备', number: '01' },
  video: { title: '视频源', caption: 'VIDEO INPUT', selector: '选择视频源', empty: '未发现视频设备', number: '02' },
  audio: { title: '音频', caption: 'AUDIO INPUT', selector: '选择音频源', empty: '未发现音频输入', number: '03' },
}
const label = computed(() => labels[props.kind])
const items = ref<DeviceOption[]>([])
const loading = ref(false)
const pending = ref(false)
const error = ref('')
const enumerationError = ref('')
const controller = new AbortController()
const busy = computed(() => pending.value || ['connecting', 'disconnecting'].includes(props.state.phase))
const owned = computed(() => !!props.state.id)
const connected = computed(() => props.online && props.state.phase === 'connected')
const phaseText = computed(() => {
  if (!props.online) return '服务离线'
  if (pending.value) return owned.value ? '断开中' : '连接中'
  return { disconnected: '未连接', connecting: '连接中', connected: '已连接', disconnecting: '断开中', error: '连接异常' }[props.state.phase]
})
const options = computed(() => props.modelValue && !items.value.some(v => v.id === props.modelValue)
  ? [...items.value, { id: props.modelValue, name: props.modelValue }]
  : items.value)

async function refresh() {
  if (loading.value) return
  loading.value = true
  enumerationError.value = ''
  try {
    items.value = await api<DeviceOption[]>(`/devices/${props.kind}`, { signal: controller.signal })
    if (!props.modelValue && items.value[0]) emit('update:modelValue', items.value[0].id)
  } catch (e) {
    if (!controller.signal.aborted) enumerationError.value = message(e)
  } finally { loading.value = false }
}

async function toggle() {
  pending.value = true
  error.value = ''
  try {
    await api(`/connections/${props.kind}`, {
      method: owned.value ? 'DELETE' : 'POST',
      ...(owned.value ? {} : { body: JSON.stringify({ id: props.modelValue }) }),
    })
  } catch (e) { error.value = message(e) }
  finally { emit('refresh'); pending.value = false }
}

watch(() => props.state.id, (id) => { if (id) emit('update:modelValue', id) }, { immediate: true })
watch(() => props.online, (online) => { if (online) void refresh() })
onMounted(refresh)
onBeforeUnmount(() => controller.abort())
</script>

<template>
  <section class="device-card" :class="[`card-${kind}`, { 'is-connected': connected }]" :aria-labelledby="`title-${kind}`">
    <header class="card-header">
      <span class="device-icon"><ConsoleIcon :name="kind" /></span>
      <div class="card-heading"><p class="eyebrow">{{ label.caption }}</p><h2 :id="`title-${kind}`">{{ label.title }}</h2></div>
      <span class="status" :class="{ connected, busy, fault: online && state.phase === 'error' }"><i />{{ phaseText }}</span>
    </header>
    <div class="device-controls">
      <label :for="`select-${kind}`">{{ label.selector }}</label>
      <div class="select-row">
        <select :id="`select-${kind}`" :value="modelValue" :disabled="busy || owned || !online" @change="emit('update:modelValue', ($event.target as HTMLSelectElement).value)">
          <option value="" disabled>{{ loading ? '正在查找设备…' : label.empty }}</option>
          <option v-for="item in options" :key="item.id" :value="item.id">{{ item.name }}</option>
        </select>
        <button class="icon-button refresh-button" :aria-label="`刷新${label.title}列表`" :disabled="loading || busy || !online" @click="refresh"><ConsoleIcon name="refresh" /></button>
      </div>
      <button class="button connect-button" :class="owned ? 'secondary' : 'primary'" :disabled="busy || !online || (!owned && !modelValue)" @click="toggle">
        <ConsoleIcon :name="owned ? 'close' : 'plug'" />{{ busy ? phaseText + '…' : owned ? '断开' : '连接' }}
      </button>
      <p v-if="error || state.error || enumerationError" class="error-message" role="alert">{{ error || state.error || enumerationError }}</p>
    </div>

    <div v-if="kind === 'gamepad'" class="controller-visual" aria-hidden="true">
      <svg viewBox="0 0 260 140" fill="none">
        <path d="M73 33c-11 0-19 6-23 17l-18 49c-5 17 10 27 22 17l26-20h100l26 20c12 10 27 0 22-17l-18-49c-4-11-12-17-23-17H73Z" class="pad-body" />
        <path d="M83 52v31m-15-15h30" class="pad-cross" />
        <circle cx="175" cy="53" r="5" /><circle cx="190" cy="68" r="5" /><circle cx="160" cy="68" r="5" /><circle cx="175" cy="83" r="5" />
        <path d="M119 62h6m10 0h6" class="pad-cross" />
        <circle cx="111" cy="88" r="9" /><circle cx="148" cy="88" r="9" />
      </svg>
      <span>{{ connected ? '手柄已就绪' : '等待手柄连接' }}</span>
    </div>
    <VideoPreview v-else-if="kind === 'video'" :active="connected" />
    <AudioMeter v-else :active="connected" :dbfs="dbfs ?? null" />

    <footer class="card-footer"><span class="module-number">{{ label.number }}</span><span>{{ state.detail || (kind === 'gamepad' ? '连接时自动识别串口协议' : kind === 'video' ? '连接后显示实时画面' : '连接后显示实时输入电平') }}</span></footer>
  </section>
</template>
