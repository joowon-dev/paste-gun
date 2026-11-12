// 엔진과 셸과 화면을 잇는다. 규칙은 여기 없다 — 전부 ../game/engine.js 에 있다.
//
// 셸이 만들어 주는 window.sneaky 가 없으면(브라우저로 열었을 때) 붙여넣기 대신
// 화면 왼쪽 위 「사격장」에 찍는다. 브라우저에서는 단축키 설정과 상관없이: V 발사 · G 총 · M 탄창 · A 무기고 · 1~9 저격 · C 복사 흉내.

import { FIRE, GUNS, GUN_NAMES, LIVE_CAP, SEPARATORS, SNIPE_SLOTS } from '../game/constants.js'
import { createEngine } from '../game/engine.js'
import { ACTIONS, ACTION_NAMES, fromEvent, hotkeys, isPaste, keyLabel } from '../game/keys.js'
import { remaining } from '../game/magazine.js'
import { createGunView } from '../render/draw.js'
import { createFx } from '../render/fx.js'

const bridge = window.sneaky || null
const $ = (id) => document.getElementById(id)

const SEPARATOR_NAMES = { tab: 'Tab', enter: 'Enter', none: '없음' }
const platform = bridge && bridge.platform === 'win' ? 'win' : 'mac'

const saved = bridge ? await bridge.getState() : null
const engine = createEngine(saved, platform)
const view = createGunView($('gun'))
const fx = createFx($('fx'))
/** 셸이 없을 때 과녁으로 삼는 자리 — 마우스가 마지막으로 있던 곳. */
let mouse = { x: innerWidth / 2, y: innerHeight / 2 }

let panelOpen = false
let trusted = true
/** 무기고에서 고치고 있는 탄창과 그 초안. 빈 칸도 초안에는 남는다(엔진은 빈 총알을 버린다). */
let editing = 0
let draft = []
/** 새 키를 기다리고 있는 동작. 아니면 null. */
let recording = null
let keyError = ''

const label = (action) => keyLabel(engine.state.keys[action], platform, action)
/** 발사 키가 붙여넣기 키 그대로인가. 그러면 쏠 것이 없을 때 진짜 붙여넣기를 흘려보낸다. */
const pasteIsTrigger = () => isPaste(engine.state.keys.trigger, platform)

/** 지금의 단축키를 셸에 건다. 메뉴바에 적을 한 줄도 같이 보낸다. */
function applyHotkeys() {
  if (!bridge) return
  const hint = `${label('trigger')} 발사 · ${label('nextGun')} 총 · ${label('nextMagazine')} 탄창 · ${label('snipe')} 저격`
  bridge.setHotkeys(hotkeys(engine.state.keys, platform), hint, label('panel'))
}

// ── 소리 ─────────────────────────────────────────────────────────────────
// 파일을 싣지 않고 그 자리에서 만든다. 기본은 꺼짐이다 — 회사에서 쓰는 물건이다.

let audio = null
function noise(duration, frequency, gain) {
  if (!engine.state.sound) return
  try {
    audio = audio || new AudioContext()
    const length = Math.floor(audio.sampleRate * duration)
    const buffer = audio.createBuffer(1, length, audio.sampleRate)
    const data = buffer.getChannelData(0)
    for (let i = 0; i < length; i += 1) data[i] = (Math.random() * 2 - 1) * (1 - i / length) ** 2
    const source = audio.createBufferSource()
    source.buffer = buffer
    const filter = audio.createBiquadFilter()
    filter.type = 'lowpass'
    filter.frequency.value = frequency
    const volume = audio.createGain()
    volume.gain.value = gain
    source.connect(filter).connect(volume).connect(audio.destination)
    source.start()
  } catch {
    // 소리가 안 나는 것은 총이 안 나가는 것과 다른 일이다.
  }
}

const SOUNDS = {
  pistol: () => noise(0.12, 2400, 0.5),
  machinegun: () => noise(0.08, 3200, 0.4),
  shotgun: () => noise(0.3, 1200, 0.7),
  sniper: () => noise(0.4, 1800, 0.6),
  click: () => noise(0.03, 6000, 0.25),
}

// ── 일어난 일 처리 ───────────────────────────────────────────────────────

function save() {
  if (bridge) bridge.saveState(engine.snapshot())
}

/** HUD 를 한 번 흔든다. 같은 것을 연달아 해도 다시 돌게 클래스를 뗐다 붙인다. */
function nudge(kind) {
  const hud = $('hud')
  hud.classList.remove('dry', 'reloaded')
  void hud.offsetWidth
  hud.classList.add(kind)
}

/**
 * 지금 든 총이 「글자가 꽂힐 때까지 기다렸다 넣는」 총인가.
 * 다음 발이 그 전에 나오는 총(기관총)은 기다리면 줄이 밀린다. 그런 총은 글을 곧장 넣고
 * 글자만 날리되, 꽂힐 자리를 커서가 가는 빠르기만큼 앞질러 겨눈다(fx 의 predict).
 */
function waitsForLanding() {
  const rate = FIRE[engine.state.gun]
  return !rate || rate.interval > fx.lead() + 0.01
}

function handle(effect) {
  if (effect.type === 'shot') {
    // 날아간 글자가 꽂히는 순간에 진짜 글이 찍히도록, 셸이 그만큼 기다렸다 넣는다.
    // 다음 발이 그 전에 나오는 총(기관총)은 기다리면 줄이 밀리므로 안 기다린다 — 글자만 날린다.
    const lead = waitsForLanding() ? fx.lead() : 0
    if (bridge) bridge.fire({ text: effect.text, after: effect.after, how: effect.how, lead: Math.round(lead * 1000) })
    else {
      $('range').textContent += `${effect.text}${effect.after ? ` ⟨${effect.after}⟩\n` : ''}${effect.how === 'paste' ? '\n' : ''}`
      fx.shoot(engine.state.gun, mouse.x, mouse.y, effect.text, false)
    }
    view.fire(engine.state.gun === 'shotgun' ? 3 : 1)
    ;(SOUNDS[engine.state.gun] || SOUNDS.pistol)()
  } else if (effect.type === 'pass') {
    if (bridge) bridge.pass()
    else $('range').textContent += '⟨원래의 붙여넣기⟩\n'
  } else if (effect.type === 'reload') {
    SOUNDS.click()
    setTimeout(SOUNDS.click, 110)
    nudge('reloaded')
  } else if (effect.type === 'empty') {
    SOUNDS.click()
    nudge('dry')
  } else if (effect.type === 'switch') {
    SOUNDS.click()
    view.invalidate()
    save()
  }
}

// ── HUD ──────────────────────────────────────────────────────────────────

function preview(text) {
  const flat = text.replace(/\s+/g, ' ').trim()
  return flat.length > 30 ? `${flat.slice(0, 30)}…` : flat
}

let hudSignature = ''
function renderHud() {
  const { gun, aim, foreign, magIndex } = engine.state
  const mag = engine.magazine()
  const left = remaining(mag)
  const chamber = engine.chamber()
  const rest = engine.firingRest()
  const trigger = label('trigger')
  const signature = [gun, mag.name, mag.rounds.length, mag.next, aim, chamber, rest, trusted, foreign, trigger].join('\u0001')
  if (signature === hudSignature) return
  hudSignature = signature

  const sniper = gun === 'sniper'
  const live = magIndex === 0
  $('gun-name').textContent = GUN_NAMES[gun]
  $('mag-name-hud').textContent = mag.name
  $('trigger-key').textContent = trigger
  $('left').textContent = sniper ? (mag.rounds.length ? aim + 1 : '–') : left
  $('total').textContent = sniper ? '번' : `/${mag.rounds.length}`
  $('hud').classList.toggle('out', !sniper && left === 0)

  const pips = $('pips')
  pips.textContent = ''
  mag.rounds.forEach((_, i) => {
    const pip = document.createElement('span')
    if (sniper) {
      if (i === aim) pip.className = 'aim'
      else if (i >= SNIPE_SLOTS) pip.className = 'spent' // 단축키가 안 닿는 자리
    } else if (i < mag.next) {
      pip.className = 'spent'
    }
    pips.append(pip)
  })

  // 방아쇠를 당기면 무슨 일이 일어나는지 한 줄로.
  let state = ''
  let what = ''
  let text = ''
  if (rest !== null) {
    // 쏘는 중이다. 아직 안 나간 글자가 줄어드는 것이 보인다.
    state = 'firing'
    what = '쏘는 중'
    text = preview(rest)
  } else if (live && foreign && pasteIsTrigger()) {
    state = 'quiet'
    text = '글이 아닌 것을 복사했어요. 그대로 붙여넣어요'
  } else if (mag.rounds.length === 0) {
    state = 'quiet'
    text = live ? '복사하면 총알이 돼요' : '빈 탄창이에요. 무기고에서 채워요'
  } else if (chamber !== null && chamber !== undefined) {
    what = '다음'
    text = gun === 'shotgun' && left > 1 ? `${preview(chamber)} 외 ${left - 1}발` : preview(chamber)
  } else if (live && gun === 'pistol') {
    // 다 쐈다. 현장 탄창의 권총은 방금 것을 다시 쏜다.
    what = '한 번 더'
    text = preview(mag.rounds[mag.rounds.length - 1])
  } else {
    what = '처음부터'
    text = preview(mag.rounds[0])
  }
  $('next').className = state
  $('next-label').textContent = what
  $('next-text').textContent = text

  const notice = $('notice')
  notice.hidden = trusted
  if (!trusted) notice.textContent = '손쉬운 사용 권한이 있어야 쏠 수 있어요. 메뉴바 🔫 → 「권한 설정 열기」'

  if (bridge) bridge.setStatus(`${GUN_NAMES[gun]} · ${mag.name} ${left}/${mag.rounds.length}`)
}

// ── 무기고 ───────────────────────────────────────────────────────────────

function button(text, onClick, className = '') {
  const el = document.createElement('button')
  el.type = 'button'
  el.textContent = text
  el.className = className
  el.addEventListener('click', onClick)
  return el
}

function fill(id, children) {
  const el = $(id)
  el.textContent = ''
  el.append(...children)
}

function loadDraft() {
  if (editing >= engine.state.mags.length) editing = 0
  draft = [...engine.state.mags[editing].rounds]
}

/** 초안을 엔진에 넣는다. 화면은 다시 그리지 않는다 — 치는 중인 칸이 사라지면 안 된다. */
function commitDraft() {
  engine.setRounds(editing, draft)
  save()
}

function renderArmory() {
  const { state } = engine
  const mag = state.mags[editing]
  const live = editing === 0

  fill('guns', GUNS.map((gun) => button(GUN_NAMES[gun], () => {
    engine.setGun(gun)
    renderArmory()
  }, gun === state.gun ? 'on' : '')))

  fill('separators', SEPARATORS.map((key) => button(SEPARATOR_NAMES[key], () => {
    engine.setSeparator(key)
    save()
    renderArmory()
  }, key === state.separator ? 'on' : '')))

  fill('sound', [false, true].map((on) => button(on ? '켬' : '끔', () => {
    engine.setSound(on)
    save()
    if (on) SOUNDS.pistol()
    renderArmory()
  }, on === state.sound ? 'on' : '')))

  fill('mags', [
    ...state.mags.map((m, i) => {
      const chip = button(m.name, () => {
        editing = i
        engine.selectMagazine(i) // 고르는 것이 곧 끼우는 것이다
        loadDraft()
        renderArmory()
      }, i === editing ? 'on' : '')
      const count = document.createElement('i')
      count.textContent = m.rounds.length
      chip.append(count)
      return chip
    }),
    button('+ 새 탄창', () => {
      const index = engine.addMagazine('')
      if (index < 0) return
      editing = index
      engine.selectMagazine(index)
      draft = ['']
      save()
      renderArmory()
      $('mag-name').select()
    }),
  ])

  const name = $('mag-name')
  name.value = mag.name
  name.disabled = live

  const rounds = $('rounds')
  rounds.textContent = ''
  if (draft.length === 0) {
    const none = document.createElement('li')
    none.className = 'none'
    none.textContent = live ? `복사하면 여기에 쌓여요 (최대 ${LIVE_CAP}발)` : '아직 총알이 없어요'
    rounds.append(none)
  }
  draft.forEach((text, i) => {
    const li = document.createElement('li')
    const area = document.createElement('textarea')
    area.value = text
    area.spellcheck = false
    area.addEventListener('input', () => {
      draft[i] = area.value
      commitDraft()
    })
    li.append(area, button('×', () => {
      draft.splice(i, 1)
      commitDraft()
      renderArmory()
    }))
    rounds.append(li)
  })

  fill('actions', [
    button('+ 총알', () => {
      draft.push('')
      renderArmory()
      rounds.querySelector('li:last-child textarea').focus()
    }),
    ...(live
      ? [
          button('이름 붙여 저장', () => {
            const index = engine.saveLiveAs('')
            if (index < 0) return
            editing = index
            engine.selectMagazine(index)
            loadDraft()
            save()
            renderArmory()
            $('mag-name').select()
          }),
          button('비우기', () => {
            draft = []
            commitDraft()
            renderArmory()
          }, 'danger'),
        ]
      : [button('탄창 삭제', () => {
          engine.removeMagazine(editing)
          editing = engine.state.magIndex
          loadDraft()
          save()
          renderArmory()
        }, 'danger')]),
  ])

  renderBindings()
}

// ── 단축키 ───────────────────────────────────────────────────────────────

const WHY = {
  bad: '이 키는 단축키로 쓸 수 없어요',
  weak: platform === 'win' ? 'Ctrl·Alt·Win 중 하나를 같이 눌러요' : '⌘·⌃·⌥ 중 하나를 같이 눌러요',
  digit: `저격은 수식키와 숫자를 같이 눌러요 (예: ${platform === 'win' ? 'Alt+Shift+1' : '⌥⇧1'})`,
}

function renderBindings() {
  fill('bindings', ACTIONS.map((action) => {
    const row = document.createElement('div')
    row.className = 'row'
    const name = document.createElement('span')
    name.className = 'label'
    name.textContent = ACTION_NAMES[action]
    const on = recording === action
    row.append(name, button(on ? '새 키를 누르세요' : label(action), () => {
      if (on) stopRecording()
      else startRecording(action)
    }, on ? 'recording' : ''))
    return row
  }))

  const note = $('key-note')
  note.className = keyError ? 'error' : ''
  note.textContent = ''
  if (keyError) {
    note.textContent = keyError
  } else if (recording) {
    note.textContent = 'Esc 를 누르면 그만둬요.'
  } else {
    note.append(
      pasteIsTrigger()
        ? '발사가 붙여넣기 키 그대로예요. 쏠 글이 없거나 그림·파일을 복사했을 때는 평소처럼 붙여넣어요. '
        : '키를 누르면 바꿀 수 있어요. ',
      button('기본값으로', () => {
        engine.resetKeys()
        save()
        applyHotkeys()
        hudSignature = ''
        renderBindings()
      }, 'quiet'),
    )
  }
}

/** 새 키를 기다린다. 키를 받으려면 키보드를 가져와야 한다 — 그동안 셸은 단축키를 안 잡는다. */
function startRecording(action) {
  recording = action
  keyError = ''
  if (bridge) bridge.grabKeyboard()
  renderBindings()
}

function stopRecording() {
  recording = null
  keyError = ''
  const el = document.activeElement
  if (bridge && !(el && el.matches && el.matches('input, textarea'))) bridge.releaseKeyboard()
  renderBindings()
}

window.addEventListener('keydown', (event) => {
  if (recording === null) {
    // 무기고에서 글자를 치다가 Esc 를 누르면 닫는다.
    if (event.key === 'Escape' && panelOpen) {
      if (bridge) bridge.closePanel()
      else setPanel(false)
    }
    return
  }
  event.preventDefault()
  event.stopPropagation()
  if (event.key === 'Escape') {
    stopRecording()
    return
  }
  const binding = fromEvent(event)
  if (!binding) {
    // 수식키만 누른 것은 아직 누르는 중이다. 그 밖의 모르는 키는 못 쓴다고 알린다.
    if (!['Meta', 'Control', 'Alt', 'Shift'].includes(event.key)) {
      keyError = WHY.bad
      renderBindings()
    }
    return
  }
  const result = engine.setKey(recording, binding)
  if (!result.ok) {
    keyError = result.why === 'taken' ? `「${ACTION_NAMES[result.with]}」가 이미 쓰는 키예요` : WHY[result.why]
    renderBindings()
    return
  }
  save()
  applyHotkeys()
  hudSignature = ''
  stopRecording()
}, true)

/** 시험용. 셸의 디버그 훅이 부른다. */
window.__gunRecord = startRecording

$('mag-name').addEventListener('input', () => {
  engine.renameMagazine(editing, $('mag-name').value)
  save()
  hudSignature = ''
})
$('mag-name').addEventListener('change', renderArmory)

function setPanel(open) {
  panelOpen = open
  $('armory').hidden = !open
  if (open) {
    editing = engine.state.magIndex
    loadDraft()
    renderArmory()
  } else {
    if (recording !== null) stopRecording()
    if (document.activeElement instanceof HTMLElement) document.activeElement.blur()
  }
  sendPanelRect()
}

$('close').addEventListener('click', () => {
  if (bridge) bridge.closePanel()
  else setPanel(false)
})

// 키보드는 **글자 칸에 들어갔을 때만** 가져온다. 무기고를 열어 뒀다고 가져가면
// 열어 둔 채로 다른 창에 한 글자도 못 친다.
$('armory').addEventListener('focusin', (event) => {
  if (bridge && event.target.matches('input, textarea')) bridge.grabKeyboard()
})
$('armory').addEventListener('focusout', () => {
  setTimeout(() => {
    const el = document.activeElement
    if (bridge && recording === null && !(el && el.matches && el.matches('input, textarea'))) bridge.releaseKeyboard()
  }, 0)
})

/** 무기고가 차지한 네모를 셸에 알린다. 셸은 커서가 그 안일 때만 마우스를 받는다. */
let lastRect = ''
function sendPanelRect() {
  if (!bridge) return
  let rect = null
  if (panelOpen) {
    const box = $('armory').getBoundingClientRect()
    rect = { x: box.left, y: box.top, w: box.width, h: box.height }
  }
  const key = JSON.stringify(rect)
  if (key === lastRect) return
  lastRect = key
  bridge.setPanelRect(rect)
}

// ── 셸에서 오는 것 ───────────────────────────────────────────────────────

/** 무기고에서 치는 중이면 화면을 갈아엎지 않는다. */
function refreshArmory() {
  if (!panelOpen) return
  const el = document.activeElement
  if (el && el.matches && el.matches('textarea')) return
  editing = Math.min(editing, engine.state.mags.length - 1)
  loadDraft()
  renderArmory()
}

const commands = {
  trigger: () => engine.trigger(bridge !== null && pasteIsTrigger()),
  snipe: (n) => engine.snipe(n),
  nextGun: () => { engine.nextGun(); refreshArmory() },
  nextMagazine: () => {
    engine.nextMagazine()
    editing = engine.state.magIndex
    refreshArmory()
  },
  copied: (text) => { engine.copied(text); if (editing === 0) refreshArmory() },
  copiedOther: () => engine.copiedOther(),
}

if (bridge) {
  bridge.onTrigger(commands.trigger)
  bridge.onSnipe(commands.snipe)
  bridge.onNextGun(commands.nextGun)
  bridge.onNextMagazine(commands.nextMagazine)
  bridge.onCopied(commands.copied)
  bridge.onCopiedOther(commands.copiedOther)
  bridge.onPanel(setPanel)
  // 셸이 실제로 붙여넣는 순간에, 붙여넣는 자리를 알려 준다. 그 자리에 대고 쏜다.
  bridge.onShotAt((x, y, text) => fx.shoot(engine.state.gun, x, y, text, !waitsForLanding()))
  bridge.onTrusted((ok) => { trusted = ok })
  bridge.onHidden((hidden) => document.body.classList.toggle('hidden', hidden))
} else {
  $('range').hidden = false
  $('range').textContent = '사격장 — V 발사 · G 총 · M 탄창 · A 무기고 · 1~9 저격 · C 복사 흉내\n\n'
  let sample = 0
  window.addEventListener('mousemove', (event) => { mouse = { x: event.clientX, y: event.clientY } })
  window.addEventListener('keydown', (event) => {
    if (recording !== null || event.target.matches('input, textarea') || event.metaKey || event.ctrlKey) return
    const key = event.key.toLowerCase()
    if (key === 'v') commands.trigger()
    else if (key === 'g') commands.nextGun()
    else if (key === 'm') commands.nextMagazine()
    else if (key === 'a') setPanel(!panelOpen)
    else if (key === 'c') commands.copied(`복사한 글 ${(sample += 1)}`)
    else if (key >= '1' && key <= '9') commands.snipe(Number(key))
  })
}

// ── 돌린다 ───────────────────────────────────────────────────────────────

applyHotkeys()

let last = performance.now()
function frame(now) {
  // 잠들었다 깨면 dt 가 몇 분이 된다. 그만큼을 한꺼번에 쏟아내지 않는다.
  const dt = Math.min(0.25, (now - last) / 1000)
  last = now

  engine.step(dt)
  for (const effect of engine.drain()) handle(effect)

  renderHud()
  view.update(dt)
  view.draw(engine.state.gun)
  fx.update(dt)
  fx.draw()
  sendPanelRect()

  requestAnimationFrame(frame)
}
requestAnimationFrame(frame)
