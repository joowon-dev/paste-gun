import { describe, expect, it } from 'vitest'
import { FIRE, LIVE_CAP, STEP } from '../src/game/constants.js'
import { createEngine } from '../src/game/engine.js'
import { assign, cleanKeys, defaultKeys, hotkeys, isPaste, keyLabel } from '../src/game/keys.js'
import { createMagazine, push } from '../src/game/magazine.js'

/** 복사해 둔 것으로 현장 탄창을 채운 엔진. */
function loaded(rounds, saved) {
  const engine = createEngine(saved)
  for (const round of rounds) engine.copied(round)
  return engine
}

/** 방아쇠를 붙여넣기 키가 아닌 것으로 옮긴 단축키. */
const OWN_KEY = { ...defaultKeys('mac'), trigger: { code: 'KeyV', mods: ['alt', 'shift'] } }

const shots = (effects) => effects.filter((e) => e.type === 'shot')
const texts = (effects) => shots(effects).map((e) => e.text)
/** t 초를 프레임 크기로 잘라 흘린다. */
function run(engine, seconds, frame = 1 / 60) {
  for (let t = 0; t < seconds - 1e-9; t += frame) engine.step(frame)
}

describe('탄창', () => {
  it('넘치면 오래된 것을 버리고, 다음 자리도 같이 당긴다', () => {
    const mag = createMagazine('m', ['a', 'b', 'c'])
    mag.next = 2 // 다음에 나갈 것은 c
    push(mag, 'd', 3)
    expect(mag.rounds).toEqual(['b', 'c', 'd'])
    expect(mag.rounds[mag.next]).toBe('c') // 안 당기면 d 가 되어 c 를 건너뛴다
  })
})

describe('복사', () => {
  it('복사한 순서대로 현장 탄창에 쌓인다', () => {
    const engine = loaded(['하나', '둘', '셋'])
    expect(engine.state.mags[0].rounds).toEqual(['하나', '둘', '셋'])
  })

  it('빈 것과 공백뿐인 것은 총알이 아니다', () => {
    const engine = loaded(['', '   \n', null, 42, '진짜'])
    expect(engine.state.mags[0].rounds).toEqual(['진짜'])
  })

  it('같은 것을 연달아 복사하면 한 발이다. 사이에 다른 것이 끼면 두 발이다', () => {
    const engine = loaded(['a', 'a', 'b', 'a'])
    expect(engine.state.mags[0].rounds).toEqual(['a', 'b', 'a'])
  })

  it(`현장 탄창은 ${LIVE_CAP}발을 안 넘고, 가장 최근 것이 남는다`, () => {
    const engine = loaded(Array.from({ length: LIVE_CAP + 5 }, (_, i) => `r${i}`))
    const live = engine.state.mags[0].rounds
    expect(live).toHaveLength(LIVE_CAP)
    expect(live[0]).toBe('r5')
    expect(live.at(-1)).toBe(`r${LIVE_CAP + 4}`)
  })

  it('다른 탄창을 끼우고 있어도 복사는 현장 탄창으로 간다', () => {
    const engine = createEngine({ magazines: [{ name: '인사', rounds: ['안녕'] }] })
    engine.selectMagazine(1)
    engine.copied('새것')
    expect(engine.state.mags[0].rounds).toEqual(['새것'])
    expect(engine.state.mags[1].rounds).toEqual(['안녕'])
  })
})

/** 총을 들고 당긴 뒤 seconds 동안 나온 발들. */
function fire(engine, gun, seconds) {
  engine.setGun(gun)
  engine.drain()
  engine.trigger()
  run(engine, seconds)
  return shots(engine.drain())
}

describe('글자 하나가 총알 한 발이다', () => {
  it('권총은 한 번 당기면 글 하나를 한 글자씩 쏜다. 다음 글은 안 건드린다', () => {
    const engine = loaded(['안녕', '다음'])
    expect(fire(engine, 'pistol', 5)).toEqual([
      { type: 'shot', text: '안', after: null, how: 'type' },
      { type: 'shot', text: '녕', after: null, how: 'type' },
    ])
    expect(engine.chamber()).toBe('다음')
    expect(engine.state.firing).toBe(null)
  })

  it('이모지와 줄바꿈(CRLF)은 쪼개지지 않고 한 발이다', () => {
    const engine = loaded(['a👨‍👩‍👧b\r\nc'])
    expect(texts(fire(engine, 'pistol', 5))).toEqual(['a', '👨‍👩‍👧', 'b', '\r\n', 'c'])
  })

  it('쏜 글자를 이으면 원래 글이다 — 어떤 총이든 빠지거나 겹치는 글자가 없다', () => {
    const text = '가나다라마바사아자차카타파하 0123456789!'
    for (const gun of ['pistol', 'machinegun', 'shotgun']) {
      expect(texts(fire(loaded([text]), gun, 10)).join('')).toBe(text)
    }
  })

  it('첫 발은 당기는 순간에 나간다', () => {
    const engine = loaded(['abc'])
    engine.trigger()
    expect(texts(engine.drain())).toEqual(['a'])
  })

  it('가만히 두면 아무것도 안 나간다', () => {
    const engine = loaded(['a', 'b'])
    run(engine, 3)
    expect(engine.drain()).toEqual([])
  })
})

describe('연사 속도', () => {
  const long = 'x'.repeat(200)

  it('권총은 1초에 7발이다 — 셀 수 있는 빠르기', () => {
    // 0초에 1발 + 9칸(0.15초)마다 1발 → 1초(60칸) 안에 7발
    expect(fire(loaded([long]), 'pistol', 1)).toHaveLength(7)
  })

  it('기관총은 1초에 30발이다', () => {
    // 0초에 1발 + 2칸마다 1발 → 1초(60칸) 안에 31발
    expect(fire(loaded([long]), 'machinegun', 1)).toHaveLength(31)
  })

  it('기관총이 권총보다 세 배 넘게 빠르다', () => {
    expect(FIRE.pistol.interval / FIRE.machinegun.interval).toBeGreaterThan(3)
  })

  it('간격은 고정 스텝의 배수다 — 아니면 반올림돼서 적어 둔 값과 실제가 달라진다', () => {
    for (const { interval } of Object.values(FIRE)) {
      expect(interval / STEP).toBeCloseTo(Math.round(interval / STEP), 9)
    }
  })

  it('프레임이 느려도 같은 시간에 같은 발 수가 나간다', () => {
    const count = (frame) => {
      const engine = loaded([long])
      engine.setGun('machinegun')
      engine.trigger()
      run(engine, 1.2, frame)
      return shots(engine.drain()).length
    }
    expect(count(1 / 60)).toBe(37)
    expect(count(1 / 20)).toBe(37)
  })
})

describe('권총', () => {
  it('현장 탄창을 다 쏘고 당기면 방금 쏜 것을 다시 쏜다 — 보통의 붙여넣기처럼', () => {
    const engine = loaded(['a', 'b'])
    engine.trigger()
    engine.trigger()
    engine.drain()

    engine.trigger()
    expect(engine.drain()).toEqual([{ type: 'shot', text: 'b', after: null, how: 'type' }])
    engine.trigger()
    expect(texts(engine.drain())).toEqual(['b'])
  })

  it('저장한 탄창을 다 쏘고 당기면 장전하면서 처음 글을 쏜다', () => {
    const engine = createEngine({ magazines: [{ name: 'm', rounds: ['a', 'b'] }] })
    engine.selectMagazine(1)
    engine.drain()
    engine.trigger()
    engine.trigger()
    engine.drain()

    engine.trigger()
    expect(engine.drain()).toEqual([{ type: 'reload' }, { type: 'shot', text: 'a', after: null, how: 'type' }])
  })

  it('총알이 하나도 없으면 헛방이다', () => {
    const engine = createEngine()
    engine.trigger()
    expect(engine.drain()).toEqual([{ type: 'empty' }])
  })

  it('쏘는 중에 다시 당기면 멈춘다. 쏘다 만 글의 나머지는 버리고, 다음 글은 그대로다', () => {
    const engine = loaded(['abcdef', '다음'])
    engine.trigger()
    run(engine, FIRE.pistol.interval + STEP)
    expect(texts(engine.drain())).toEqual(['a', 'b'])
    expect(engine.firingRest()).toBe('cdef')

    engine.trigger()
    run(engine, 3)
    expect(engine.drain()).toEqual([])
    expect(engine.firingRest()).toBe(null)
    expect(engine.chamber()).toBe('다음')
  })
})

describe('샷건', () => {
  it('남은 글을 줄바꿈으로 묶어 여덟 글자씩 뭉텅이로 쏜다', () => {
    const engine = loaded(['먼저', 'abcdefgh', 'ijk'])
    fire(engine, 'pistol', 2) // 「먼저」는 권총으로
    expect(texts(fire(engine, 'shotgun', 5))).toEqual(['abcdefgh', '\nijk'])
    expect(engine.remaining()).toBe(0)
  })

  it('한 방과 다음 한 방 사이가 권총보다 길다', () => {
    const engine = loaded(['x'.repeat(100)])
    // 0초에 1방 + 14칸마다 1방 → 1초(60칸) 안에 5방
    expect(fire(engine, 'shotgun', 1)).toHaveLength(5)
  })
})

describe('기관총', () => {
  it('글 하나가 끝나면 쉬지 않고 다음 글로 넘어가 탄창을 다 비운다', () => {
    const engine = loaded(['ab', 'cd', 'e'])
    expect(texts(fire(engine, 'machinegun', 5))).toEqual(['a', 'b', 'c', 'd', 'e'])
    expect(engine.remaining()).toBe(0)
    expect(engine.state.firing).toBe(null)
  })

  it('구분 키는 글과 글 사이에만 누른다. 글 안에서도, 마지막 글 뒤에도 안 누른다', () => {
    const engine = loaded(['ab', 'cd', 'e'])
    expect(fire(engine, 'machinegun', 5).map((e) => e.after)).toEqual([null, 'tab', null, 'tab', null])
  })

  it('구분 키를 바꾸면 그 키가 나가고, 없음이면 아무것도 안 누른다', () => {
    const afters = (separator) => {
      const engine = loaded(['a', 'b', 'c'])
      engine.setSeparator(separator)
      return fire(engine, 'machinegun', 5).map((e) => e.after)
    }
    expect(afters('enter')).toEqual(['enter', 'enter', null])
    expect(afters('none')).toEqual([null, null, null])
  })

  it('쏟아내는 중에 다시 당기면 멈춘다. 아직 안 꺼낸 글은 그대로다', () => {
    const engine = loaded(['abcd', 'efgh', '남음'])
    engine.setGun('machinegun')
    engine.trigger()
    run(engine, 5 * FIRE.machinegun.interval + STEP)
    expect(texts(engine.drain())).toEqual(['a', 'b', 'c', 'd', 'e', 'f'])

    engine.trigger()
    run(engine, 2)
    expect(engine.drain()).toEqual([])
    expect(engine.chamber()).toBe('남음')
  })

  it('총이나 탄창을 바꾸면 쏟아내던 것이 멈춘다', () => {
    for (const change of [(e) => e.nextGun(), (e) => e.nextMagazine()]) {
      const engine = loaded(['abcdef', 'c'], { magazines: [{ name: 'x', rounds: ['z'] }] })
      engine.setGun('machinegun')
      engine.trigger()
      engine.drain()
      change(engine)
      run(engine, 2)
      expect(shots(engine.drain())).toEqual([])
    }
  })
})

describe('저격', () => {
  it('고른 슬롯의 글을 통째로 한 방에 쏜다 (한 글자씩이 아니다). 탄창은 줄지 않는다', () => {
    const engine = loaded(['a', 'b', '긴 글 통째로'])
    engine.snipe(3)
    engine.snipe(3)
    expect(engine.drain()).toEqual([
      { type: 'shot', text: '긴 글 통째로', after: null, how: 'paste' },
      { type: 'shot', text: '긴 글 통째로', after: null, how: 'paste' },
    ])
    expect(engine.remaining()).toBe(3)
  })

  it('없는 슬롯은 헛방이다', () => {
    const engine = loaded(['a'])
    engine.snipe(2)
    expect(engine.drain()).toEqual([{ type: 'empty' }])
  })

  it('1~9 밖의 번호는 아무 일도 없다', () => {
    const engine = loaded(Array.from({ length: 12 }, (_, i) => `r${i}`))
    for (const n of [0, 10, -1, 1.5]) engine.snipe(n)
    expect(engine.drain()).toEqual([])
  })

  it('저격총을 들면 방아쇠가 마지막으로 겨눈 슬롯을 다시 쏜다', () => {
    const engine = loaded(['a', 'b', 'c'])
    engine.snipe(2)
    engine.setGun('sniper')
    engine.drain()
    engine.trigger()
    engine.trigger()
    expect(texts(engine.drain())).toEqual(['b', 'b'])
  })
})

describe('총과 탄창 바꾸기', () => {
  it('총은 권총 → 기관총 → 샷건 → 저격총 → 권총 으로 돈다', () => {
    const engine = createEngine()
    const seen = []
    for (let i = 0; i < 5; i += 1) {
      seen.push(engine.state.gun)
      engine.nextGun()
    }
    expect(seen).toEqual(['pistol', 'machinegun', 'shotgun', 'sniper', 'pistol'])
  })

  it('탄창마다 쏜 자리를 따로 기억한다', () => {
    const engine = loaded(['a', 'b'], { magazines: [{ name: '인사', rounds: ['x', 'y'] }] })
    engine.trigger() // 현장 a
    engine.nextMagazine()
    engine.trigger() // 인사 x
    engine.nextMagazine() // 다시 현장
    engine.trigger()
    expect(texts(engine.drain())).toEqual(['a', 'x', 'b'])
  })
})

describe('무기고', () => {
  it('현장 탄창을 이름 붙여 남긴다. 그 뒤에 복사한 것은 남긴 탄창에 안 섞인다', () => {
    const engine = loaded(['a', 'b'])
    const index = engine.saveLiveAs('주소')
    engine.copied('c')
    expect(engine.state.mags[index]).toMatchObject({ name: '주소', rounds: ['a', 'b'] })
  })

  it('현장 탄창은 못 지운다', () => {
    const engine = loaded(['a'])
    engine.removeMagazine(0)
    expect(engine.state.mags).toHaveLength(1)
  })

  it('끼우고 있던 탄창을 지우면 현장 탄창으로 돌아간다. 앞의 것을 지우면 끼운 탄창이 안 바뀐다', () => {
    const saved = { magazines: [{ name: 'A', rounds: ['a'] }, { name: 'B', rounds: ['b'] }] }

    const one = createEngine(saved)
    one.selectMagazine(2)
    one.removeMagazine(2)
    expect(one.magazine().name).toBe('현장 탄창')

    const two = createEngine(saved)
    two.selectMagazine(2)
    two.removeMagazine(1)
    expect(two.magazine().name).toBe('B')
  })

  it('총알을 고치면 그 탄창은 처음부터 다시 나간다', () => {
    const engine = createEngine({ magazines: [{ name: 'A', rounds: ['a', 'b'] }] })
    engine.selectMagazine(1)
    engine.trigger()
    engine.setRounds(1, ['x', '', 'y'])
    engine.drain()
    engine.trigger()
    expect(texts(engine.drain())).toEqual(['x'])
    expect(engine.magazine().rounds).toEqual(['x', 'y'])
  })
})

describe('저장', () => {
  it('현장 탄창은 저장하지 않는다', () => {
    const engine = loaded(['비밀번호일 수도 있는 것'], { magazines: [{ name: 'A', rounds: ['a'] }] })
    expect(JSON.stringify(engine.snapshot())).not.toContain('비밀번호')
    expect(engine.snapshot().magazines).toEqual([{ name: 'A', rounds: ['a'] }])
  })

  it('저장한 것을 다시 읽으면 같은 총·같은 탄창·같은 설정이다', () => {
    // 방아쇠가 따로 있는 설정이다. 붙여넣기 키가 방아쇠면 끼운 탄창은 일부러 안 되살린다.
    const engine = createEngine({ keys: OWN_KEY, magazines: [{ name: 'A', rounds: ['a'] }] })
    engine.setGun('shotgun')
    engine.selectMagazine(1)
    engine.setSeparator('enter')
    engine.setSound(true)

    const again = createEngine(JSON.parse(JSON.stringify(engine.snapshot())))
    expect(again.state.gun).toBe('shotgun')
    expect(again.magazine().name).toBe('A')
    expect(again.state.separator).toBe('enter')
    expect(again.state.sound).toBe(true)
  })

  it('소리는 기본이 꺼짐이다', () => {
    expect(createEngine().state.sound).toBe(false)
  })

  it('망가진 저장 파일로도 죽지 않고 기본값으로 뜬다', () => {
    for (const junk of [null, 'x', 7, { gun: 'bazooka', magIndex: 99, magazines: 'no' },
      { magazines: [null, { rounds: [1, 'ok', ''] }] }]) {
      const engine = createEngine(junk)
      expect(engine.state.gun).toBe('pistol')
      expect(engine.state.magIndex).toBe(0)
      expect(engine.state.mags[0].name).toBe('현장 탄창')
    }
    const engine = createEngine({ magazines: [null, { rounds: [1, 'ok', ''] }] })
    expect(engine.state.mags[1].rounds).toEqual(['ok'])
  })
})

describe('붙여넣기 키가 방아쇠일 때', () => {
  it('쏠 것이 없으면 헛방 대신 원래의 붙여넣기를 흘려보낸다', () => {
    const engine = createEngine()
    engine.trigger(true)
    expect(engine.drain()).toEqual([{ type: 'pass' }])
  })

  it('방아쇠가 다른 키면 흘려보내지 않는다 — 누르지도 않은 붙여넣기가 나가면 안 된다', () => {
    const engine = createEngine()
    engine.trigger(false)
    expect(engine.drain()).toEqual([{ type: 'empty' }])
  })

  it('글이 아닌 것을 복사한 뒤에는 총알이 남아 있어도 흘려보낸다. 글을 복사하면 다시 쏜다', () => {
    const engine = loaded(['a'])
    engine.copiedOther()
    engine.trigger(true)
    expect(engine.drain()).toEqual([{ type: 'pass' }])

    engine.copied('b')
    engine.trigger(true)
    expect(texts(engine.drain())).toEqual(['a'])
  })

  it('저장한 탄창을 끼워 둔 채 무언가 복사하면 현장 탄창으로 돌아온다 — 방금 복사한 것이 이긴다', () => {
    const engine = createEngine({ magazines: [{ name: 'm', rounds: ['x'] }] })
    engine.selectMagazine(1)
    engine.trigger(true)
    expect(texts(engine.drain())).toEqual(['x'])

    engine.copied('y')
    expect(engine.state.magIndex).toBe(0)
    engine.trigger(true)
    expect(texts(engine.drain())).toEqual(['y'])

    engine.selectMagazine(1)
    engine.copiedOther()
    engine.trigger(true)
    expect(engine.drain().filter((e) => e.type !== 'switch')).toEqual([{ type: 'pass' }])
  })

  it('켤 때는 지난번 탄창이 아니라 현장 탄창이다. 방아쇠가 다른 키면 지난번 탄창 그대로', () => {
    const saved = { magIndex: 1, magazines: [{ name: 'm', rounds: ['x'] }] }
    expect(createEngine(saved).state.magIndex).toBe(0)
    expect(createEngine({ ...saved, keys: OWN_KEY }).state.magIndex).toBe(1)

    const engine = createEngine({ ...saved, keys: OWN_KEY })
    engine.copied('y') // 방아쇠가 따로 있으면 복사해도 탄창을 안 바꾼다
    expect(engine.state.magIndex).toBe(1)
  })

  it('쏘는 중에 누르면 흘려보내지 않고 멈춘다', () => {
    const engine = loaded(['abc'])
    engine.trigger(true)
    engine.copiedOther()
    engine.trigger(true)
    expect(engine.drain().some((e) => e.type === 'pass')).toBe(false)
    expect(engine.state.firing).toBe(null)
  })
})

describe('단축키', () => {
  const mac = defaultKeys('mac')

  it('기본 발사 키는 그 OS 의 붙여넣기다', () => {
    expect(mac.trigger).toEqual({ code: 'KeyV', mods: ['meta'] })
    expect(defaultKeys('win').trigger).toEqual({ code: 'KeyV', mods: ['ctrl'] })
    expect(isPaste(mac.trigger, 'mac')).toBe(true)
    expect(isPaste(mac.trigger, 'win')).toBe(false)
    expect(keyLabel(mac.trigger, 'mac')).toBe('⌘V')
    expect(keyLabel(defaultKeys('win').trigger, 'win')).toBe('Ctrl+V')
    expect(keyLabel(mac.snipe, 'mac', 'snipe')).toBe('⌥⇧1~9')
  })

  it('셸에 넘기는 번호 — 맥 V 는 9, 윈도우 V 는 0x56. 저격은 숫자 아홉 개', () => {
    const list = hotkeys(mac, 'mac')
    expect(list[0]).toEqual({ id: 0, key: 9, ctrl: false, alt: false, shift: false, meta: true })
    expect(list.filter((k) => k.id > 10).map((k) => k.key)).toEqual([18, 19, 20, 21, 23, 22, 26, 28, 25])
    expect(hotkeys(defaultKeys('win'), 'win')[0]).toMatchObject({ key: 0x56, ctrl: true, meta: false })
    expect(list).toHaveLength(13)
  })

  it('수식키 없는 글자, Shift 만 붙은 글자는 못 쓴다. F 키는 혼자서도 된다', () => {
    expect(assign(mac, 'trigger', { code: 'KeyV', mods: [] })).toEqual({ ok: false, why: 'weak' })
    expect(assign(mac, 'trigger', { code: 'KeyV', mods: ['shift'] })).toEqual({ ok: false, why: 'weak' })
    expect(assign(mac, 'trigger', { code: 'F6', mods: [] }).ok).toBe(true)
    expect(assign(mac, 'trigger', { code: 'Enter', mods: ['meta'] })).toEqual({ ok: false, why: 'bad' })
  })

  it('다른 동작이 쓰는 키는 못 쓴다. 저격은 그 수식키의 숫자 아홉 개를 다 차지한다', () => {
    expect(assign(mac, 'trigger', { code: 'KeyG', mods: ['shift', 'alt'] }))
      .toEqual({ ok: false, why: 'taken', with: 'nextGun' })
    expect(assign(mac, 'trigger', { code: 'Digit7', mods: ['alt', 'shift'] }))
      .toEqual({ ok: false, why: 'taken', with: 'snipe' })
    expect(assign(mac, 'snipe', { code: 'KeyS', mods: ['ctrl'] })).toEqual({ ok: false, why: 'digit' })
    expect(assign(mac, 'snipe', { code: 'Digit4', mods: ['ctrl'] }).keys.snipe).toEqual({ code: 'Digit1', mods: ['ctrl'] })
  })

  it('엔진에서 바꾸면 저장에 실리고, 다시 열면 그대로다', () => {
    const engine = createEngine()
    expect(engine.setKey('trigger', { code: 'KeyV', mods: ['alt', 'shift'] }).ok).toBe(true)
    expect(engine.setKey('panel', { code: 'KeyV', mods: ['alt', 'shift'] }).ok).toBe(false)
    const again = createEngine(JSON.parse(JSON.stringify(engine.snapshot())))
    expect(again.state.keys.trigger).toEqual({ code: 'KeyV', mods: ['alt', 'shift'] })
    again.resetKeys()
    expect(again.state.keys).toEqual(mac)
  })

  it('저장된 단축키가 이상하거나 서로 겹치면 전부 기본값으로 돌린다', () => {
    expect(cleanKeys({ ...mac, trigger: { code: 'KeyV', mods: [] } }, 'mac')).toEqual(mac)
    expect(cleanKeys({ ...mac, trigger: mac.panel }, 'mac')).toEqual(mac)
    expect(cleanKeys('x', 'mac')).toEqual(mac)
  })
})
