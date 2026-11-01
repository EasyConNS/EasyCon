<script setup lang="ts">
import { computed } from 'vue'

const props = defineProps<{ active: boolean; dbfs: number | null }>()
const level = computed(() => props.active && props.dbfs !== null ? Math.max(0, Math.min(1, (props.dbfs + 60) / 60)) : 0)
const reading = computed(() => !props.active ? '待连接' : props.dbfs === null ? '−∞' : props.dbfs.toFixed(1))
</script>

<template>
  <div class="audio-meter" :class="{ active }">
    <div class="meter-caption"><span>输入电平</span><span class="meter-live"><i /> {{ active ? '正在采样' : '未采样' }}</span></div>
    <div class="meter-reading"><strong>{{ reading }}</strong><span v-if="active">dBFS</span></div>
    <div class="meter-track" role="meter" aria-label="实时音频强度" aria-valuemin="-60" aria-valuemax="0" :aria-valuenow="active ? Math.max(-60, Math.min(0, dbfs ?? -60)) : -60" :aria-valuetext="active ? `${reading} dBFS` : '未连接'">
      <div class="meter-color" />
      <div class="meter-cover" :style="{ transform: `translateX(${level * 100}%)` }" />
      <div class="meter-segments" aria-hidden="true"><i v-for="n in 32" :key="n" /></div>
    </div>
    <div class="meter-scale" aria-hidden="true"><span>−60</span><span>−40</span><span>−20</span><span>0 dB</span></div>
  </div>
</template>
