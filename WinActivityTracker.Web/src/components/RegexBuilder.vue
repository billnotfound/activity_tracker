<template>
  <div class="regex-builder">
    <div class="builder-mode" role="radiogroup" :aria-label="t('processActions.matcher.mode')">
      <button
        type="button"
        class="mode-button"
        :class="{ active: mode === 'guided' }"
        @click="mode = 'guided'"
      >
        <WandSparkles :size="16" />
        {{ t('processActions.matcher.guided') }}
      </button>
      <button
        type="button"
        class="mode-button"
        :class="{ active: mode === 'direct' }"
        @click="mode = 'direct'"
      >
        <Code2 :size="16" />
        {{ t('processActions.matcher.direct') }}
      </button>
    </div>

    <template v-if="mode === 'guided'">
      <div v-for="(part, index) in parts" :key="part.id" class="match-step">
        <div class="step-number">{{ index + 1 }}</div>
        <div class="step-body">
          <label :for="`match-${part.id}`">
            {{ index === 0 ? t('processActions.matcher.first') : t('processActions.matcher.and') }}
          </label>
          <input
            :id="`match-${part.id}`"
            v-model="part.value"
            type="text"
            class="action-input"
            :placeholder="t('processActions.matcher.placeholder')"
            @input="emitRegex"
          />
        </div>
        <button
          type="button"
          class="condition-action"
          :aria-label="index === parts.length - 1 ? t('processActions.matcher.addCondition') : t('common.delete')"
          @click="index === parts.length - 1 ? addPart() : removePart(index)"
        >
          <Transition name="icon-morph" mode="out-in">
            <Plus v-if="index === parts.length - 1" key="plus" :size="17" />
            <X v-else key="remove" :size="17" />
          </Transition>
        </button>
      </div>

      <button v-if="!excludes.length" type="button" class="exclude-trigger" @click="addExclude">
        <CircleMinus :size="16" /> {{ t('processActions.matcher.exclude') }}
      </button>
      <div v-for="(part, index) in excludes" :key="part.id" class="match-step exclude-step">
        <div class="step-number"><CircleMinus :size="15" /></div>
        <div class="step-body">
          <label :for="`exclude-${part.id}`">{{ index === 0 ? t('processActions.matcher.exclude') : t('processActions.matcher.excludeAnd') }}</label>
          <input
            :id="`exclude-${part.id}`"
            v-model="part.value"
            type="text"
            class="action-input"
            :placeholder="t('processActions.matcher.excludePlaceholder')"
            @input="emitRegex"
          />
        </div>
        <button type="button" class="condition-action" :aria-label="index === excludes.length - 1 ? t('processActions.matcher.addExclude') : t('common.delete')" @click="index === excludes.length - 1 ? addExclude() : removeExclude(index)">
          <Transition name="icon-morph" mode="out-in">
            <Plus v-if="index === excludes.length - 1" key="plus" :size="17" />
            <X v-else key="remove" :size="17" />
          </Transition>
        </button>
      </div>

      <label class="case-toggle">
        <input v-model="ignoreCase" type="checkbox" @change="emitRegex" />
        <span>{{ t('processActions.matcher.ignoreCase') }}</span>
      </label>
    </template>

    <div v-else class="direct-field">
      <label for="direct-regex">{{ t('processActions.matcher.regexLabel') }}</label>
      <input
        id="direct-regex"
        v-model="directRegex"
        type="text"
        class="action-input mono"
        :placeholder="t('processActions.matcher.regexPlaceholder')"
        @input="emitRegex"
      />
    </div>

    <div v-if="output || regexError" class="regex-preview" :class="{ invalid: regexError }">
      <code>{{ output }}</code>
      <small v-if="regexError">{{ t('processActions.matcher.invalid') }}</small>
    </div>
  </div>
</template>

<script setup>
import { computed, ref, watch } from 'vue'
import { CircleMinus, Code2, Plus, WandSparkles, X } from '@lucide/vue'
import { useI18n } from '../i18n/index.js'

const props = defineProps({
  modelValue: { type: String, default: '' },
  initialText: { type: String, default: '' },
})
const emit = defineEmits(['update:modelValue'])
const { t } = useI18n()

let partId = 0
const mode = ref(props.modelValue ? 'direct' : 'guided')
const parts = ref([{ id: ++partId, value: props.initialText || '' }])
const excludes = ref([])
const ignoreCase = ref(true)
const directRegex = ref(props.modelValue || '')

function escapeRegex(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')
}

const guidedRegex = computed(() => {
  const values = parts.value.map(part => part.value.trim()).filter(Boolean)
  const excludedValues = excludes.value.map(part => part.value.trim()).filter(Boolean)
  if (!values.length && !excludedValues.length) return ''
  const flags = ignoreCase.value ? '(?i)' : ''
  const conditions = values.map(value => `(?=.*${escapeRegex(value)})`).join('')
  const exclusions = excludedValues.map(value => `(?!.*${escapeRegex(value)})`).join('')
  return `${flags}^${conditions}${exclusions}.*$`
})

const output = computed(() => mode.value === 'direct' ? directRegex.value.trim() : guidedRegex.value)
const regexError = computed(() => {
  if (!output.value) return false
  try {
    // JavaScript does not support .NET's inline (?i) flag, so remove it for validation.
    const jsPattern = output.value.startsWith('(?i)') ? output.value.slice(4) : output.value
    new RegExp(jsPattern, output.value.startsWith('(?i)') ? 'i' : '')
    return false
  } catch {
    return true
  }
})

function addPart() {
  parts.value.push({ id: ++partId, value: '' })
  emitRegex()
}

function removePart(index) {
  parts.value.splice(index, 1)
  emitRegex()
}

function addExclude() {
  excludes.value.push({ id: ++partId, value: '' })
  emitRegex()
}

function removeExclude(index) {
  excludes.value.splice(index, 1)
  emitRegex()
}

function emitRegex() {
  emit('update:modelValue', regexError.value ? '' : output.value)
}

watch(mode, emitRegex)
watch(() => props.modelValue, value => {
  if (mode.value === 'direct' && value !== directRegex.value) directRegex.value = value || ''
})

emitRegex()
</script>

<style lang="scss" scoped>
.regex-builder {
  display: grid;
  gap: 12px;
}

.builder-mode {
  display: grid;
  grid-template-columns: 1fr 1fr;
  border: 2px solid var(--surface-200);
}

.mode-button {
  min-height: 40px;
  border: 0;
  background: var(--surface-card);
  color: var(--text-color-secondary);
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 7px;
  cursor: pointer;
  font-weight: 600;

  & + & { border-left: 2px solid var(--surface-200); }
  &.active { background: var(--primary-color); color: white; }
  &:focus-visible { outline: 3px solid var(--accent-color); outline-offset: -3px; }
}

.match-step {
  display: grid;
  grid-template-columns: 30px minmax(0, 1fr) 32px;
  gap: 10px;
  align-items: end;
}

.step-number {
  width: 30px;
  height: 30px;
  border: 2px solid var(--primary-color);
  color: var(--primary-color);
  display: grid;
  place-items: center;
  font: 700 0.85rem 'Ubuntu Mono', monospace;
  margin-bottom: 4px;
}

.step-body,
.direct-field {
  display: grid;
  gap: 6px;

  label { color: var(--text-color-secondary); font-size: 0.82rem; font-weight: 600; }
}

.action-input {
  width: 100%;
  min-height: 40px;
  padding: 8px 10px;
  border: 2px solid var(--surface-200);
  background: var(--surface-card);
  color: var(--text-color);

  &:focus { outline: none; border-color: var(--primary-color); }
}

.condition-action {
  border: 2px solid var(--surface-200);
  background: transparent;
  color: var(--text-color-secondary);
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  justify-content: center;
}
.condition-action { width: 32px; height: 32px; margin-bottom: 4px; overflow: hidden; }
.condition-action:hover { border-color: var(--primary-color); color: var(--primary-color); }
.exclude-trigger { justify-self: start; min-height: 34px; padding: 5px 9px; border: 2px solid var(--surface-200); background: transparent; color: var(--text-color-secondary); display: inline-flex; align-items: center; gap: 7px; cursor: pointer; }
.exclude-trigger:hover { border-color: var(--secondary-color); color: var(--secondary-color); }
.exclude-step .step-number { border-color: var(--secondary-color); color: var(--secondary-color); }
.icon-morph-enter-active, .icon-morph-leave-active { transition: opacity 130ms ease, transform 180ms cubic-bezier(.2,.8,.2,1); }
.icon-morph-enter-from { opacity: 0; transform: rotate(-90deg) scale(.55); }
.icon-morph-leave-to { opacity: 0; transform: rotate(90deg) scale(.55); }

.case-toggle {
  display: flex;
  align-items: center;
  gap: 9px;
  color: var(--text-color);
  font-size: 0.88rem;
  cursor: pointer;

  input { width: 18px; height: 18px; accent-color: var(--primary-color); }
}

.regex-preview {
  border-left: 4px solid var(--accent-color);
  background: var(--surface-100);
  padding: 9px 11px;
  display: grid;
  gap: 5px;

  code { color: var(--text-color); overflow-wrap: anywhere; }
  small { color: var(--danger-color); }
  &.invalid { border-left-color: var(--danger-color); }
}
</style>
