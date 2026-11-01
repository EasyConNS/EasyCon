<script setup lang="ts">
import { nextTick, onBeforeUnmount, ref } from 'vue'
import { api, message, type FileListing, type Selection } from '../api'
import ConsoleIcon from './ConsoleIcon.vue'

const emit = defineEmits<{ selected: [selection: Selection] }>()
const dialog = ref<HTMLDialogElement>()
const listing = ref<FileListing>()
const pathInput = ref('')
const busy = ref(false)
const error = ref('')
let request: AbortController | undefined

async function browse(path?: string) {
  request?.abort()
  const controller = new AbortController()
  request = controller
  busy.value = true
  error.value = ''
  try {
    listing.value = await api<FileListing>(`/files${path ? `?path=${encodeURIComponent(path)}` : ''}`, { signal: controller.signal })
    pathInput.value = listing.value.directory
  } catch (e) {
    if (!controller.signal.aborted) error.value = message(e)
  } finally { if (request === controller) busy.value = false }
}

async function open() {
  await nextTick()
  dialog.value?.showModal()
  await browse(listing.value?.directory)
}

async function select(path: string) {
  busy.value = true
  error.value = ''
  try {
    emit('selected', await api<Selection>('/workflow', { method: 'POST', body: JSON.stringify({ path }) }))
    dialog.value?.close()
  } catch (e) { error.value = message(e) }
  finally { busy.value = false }
}

onBeforeUnmount(() => request?.abort())
defineExpose({ open })
</script>

<template>
  <dialog ref="dialog" class="file-dialog" aria-labelledby="picker-title" @click="(e) => { if (e.target === dialog) dialog?.close() }">
    <header class="dialog-header">
      <div><p class="eyebrow">WORKFLOW</p><h2 id="picker-title">选择工作流</h2></div>
      <button class="icon-button" aria-label="关闭文件选择器" @click="dialog?.close()"><ConsoleIcon name="close" /></button>
    </header>
    <form class="directory-form" @submit.prevent="browse(pathInput)">
      <button class="icon-button" type="button" aria-label="上一级目录" :disabled="busy || !listing?.parent" @click="browse(listing?.parent ?? undefined)"><ConsoleIcon name="up" /></button>
      <input v-model="pathInput" aria-label="目录路径" placeholder="输入项目目录路径" spellcheck="false" />
      <button class="button secondary" :disabled="busy" type="submit">前往</button>
    </form>
    <p v-if="error" class="error-message picker-error" role="alert">{{ error }}</p>
    <div class="file-list" :aria-busy="busy">
      <p v-if="busy" class="list-hint">正在读取…</p>
      <template v-else>
        <button v-for="entry in listing?.entries" :key="entry.path" class="file-entry" @click="entry.directory ? browse(entry.path) : select(entry.path)">
          <ConsoleIcon :name="entry.directory ? 'folder' : 'file'" />
          <span>{{ entry.name }}</span>
          <ConsoleIcon v-if="entry.directory" name="chevron" class="file-chevron" />
          <span v-else class="file-type">JSON</span>
        </button>
        <p v-if="!listing?.entries.length" class="list-hint">此目录没有文件夹或 JSON 文件</p>
      </template>
    </div>
    <footer class="dialog-footer">选择 workflow.json 或其他 IR 工作流文件</footer>
  </dialog>
</template>
