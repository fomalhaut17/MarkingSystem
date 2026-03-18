/**
 * LS XGT Cnet Serial Mock PLC Server
 * Serial Port: COM6 (com0com 가상 포트 쌍의 PLC 측)
 *
 * com0com 설정: COM5 ↔ COM6
 *   앱(CnetSerialPlcClient)은 COM5에 연결
 *   이 서버는 COM6에서 수신
 *
 * Cnet 프레임 구조 (ASCII 기반, BCC 없음 — 대문자 명령어 사용):
 *   요청: ENQ(05) + 국번(2) + 명령(1) + 타입(2) + 변수길이(2hex) + 변수명 + 데이터수(2hex) [+ 데이터] + EOT(04)
 *   ACK : ACK(06) + 국번(2) + 명령(1) + 타입(2) + 블록수(2hex) + 바이트수(2hex) [+ 데이터] + ETX(03)
 *   NAK : NAK(15) + 국번(2) + 명령(1) + 타입(2) + 에러코드(4hex) + ETX(03)
 *
 * PLC 메모리 맵 (appsettings.dev.json Plc.Memory 와 일치):
 *   %MW100 ~ %MW114  Lot 바코드 #1 (15 words = 30 bytes ASCII) — PC → PLC
 *   %MW120 ~ %MW134  Lot 바코드 #2 (15 words = 30 bytes ASCII) — PC → PLC
 *   %MW140           발행 요청     (1=요청, 0=대기)            — PLC → PC
 *   %MW150 ~ %MW164  스캐너 결과 #1 (15 words)                 — Scanner → PLC → PC
 *   %MW170 ~ %MW184  스캐너 결과 #2 (15 words)                 — Scanner → PLC → PC
 *
 * Mock 동작 흐름:
 *   1. 포트 열림 → 500ms 후 발행 요청(MW140=1) 세팅
 *   2. PC가 바코드 2개를 쓰고 MW140=0 클리어 → 각인 시뮬레이션 (1~2초)
 *   3. 각인 완료 → 스캐너 결과를 MW150/MW170에 기록 (90% 일치, 10% 불일치)
 *   4. PC가 MW150을 전체 0으로 클리어 → 300ms 후 다음 발행 요청(MW140=1) 세팅
 */

'use strict'

const { SerialPort } = require('serialport')

// ── 제어 코드 ─────────────────────────────────────────────────────────────────

const ENQ = 0x05
const ACK = 0x06
const NAK = 0x15
const ETX = 0x03
const EOT = 0x04

// ── 메모리 주소 (appsettings.dev.json Plc.Memory 기본값과 동일하게 유지) ──────

const MW_LOT_BARCODE1_START     = 100  // %MW100 ~ %MW114
const MW_LOT_BARCODE2_START     = 120  // %MW120 ~ %MW134
const MW_BARCODE_REQUEST        = 140  // %MW140
const MW_SCANNED_BARCODE1_START = 150  // %MW150 ~ %MW164
const MW_SCANNED_BARCODE2_START = 170  // %MW170 ~ %MW184
const LOT_BARCODE_WORD_COUNT    = 15

// ── 가상 PLC 메모리 (word index → uint16 value) ───────────────────────────────

const memory = new Map()
memory.set(MW_BARCODE_REQUEST, 0)

// ── 바코드 유틸 ───────────────────────────────────────────────────────────────

function readBarcode(startAddr) {
  const bytes = []
  for (let i = 0; i < LOT_BARCODE_WORD_COUNT; i++) {
    const w = memory.get(startAddr + i) || 0
    bytes.push(w & 0xFF)
    bytes.push((w >> 8) & 0xFF)
  }
  return Buffer.from(bytes).toString('ascii').replace(/\0+$/, '')
}

function writeBarcode(startAddr, str) {
  const src = Buffer.from(str, 'ascii')
  for (let i = 0; i < LOT_BARCODE_WORD_COUNT; i++) {
    const lo = src[i * 2]     || 0
    const hi = src[i * 2 + 1] || 0
    memory.set(startAddr + i, lo | (hi << 8))
  }
}

function clearBarcode(startAddr) {
  for (let i = 0; i < LOT_BARCODE_WORD_COUNT; i++)
    memory.set(startAddr + i, 0)
}

function isBarcodeClear(startAddr) {
  for (let i = 0; i < LOT_BARCODE_WORD_COUNT; i++)
    if ((memory.get(startAddr + i) || 0) !== 0) return false
  return true
}

// ── PLC 상태 머신 ─────────────────────────────────────────────────────────────

let engravingTimer = null

function triggerBarcodeRequest() {
  memory.set(MW_BARCODE_REQUEST, 1)
  console.log('  [PLC] 발행 요청 세팅 (MW140=1)')
}

function onBarcodeRequestCleared() {
  const bc1 = readBarcode(MW_LOT_BARCODE1_START)
  const bc2 = readBarcode(MW_LOT_BARCODE2_START)
  if (!bc1 || !bc2) return

  console.log(`  [PLC] 바코드 수신: #1="${bc1}", #2="${bc2}"`)

  if (engravingTimer) clearTimeout(engravingTimer)
  const delay = 1000 + Math.random() * 1000
  engravingTimer = setTimeout(() => {
    // 90% 일치, 10% 불일치 (마지막 문자 변조)
    const sc1 = Math.random() > 0.1 ? bc1 : bc1.slice(0, -1) + 'X'
    const sc2 = Math.random() > 0.1 ? bc2 : bc2.slice(0, -1) + 'X'

    writeBarcode(MW_SCANNED_BARCODE1_START, sc1)
    writeBarcode(MW_SCANNED_BARCODE2_START, sc2)

    const ok1 = sc1 === bc1 ? 'OK ✓' : 'NG ✗'
    const ok2 = sc2 === bc2 ? 'OK ✓' : 'NG ✗'
    console.log(`  [PLC] 스캐너 결과: #1="${sc1}" ${ok1},  #2="${sc2}" ${ok2}`)
    engravingTimer = null
  }, delay)
}

function onScannedBarcodesCleared() {
  setTimeout(triggerBarcodeRequest, 300)
}

// ── 주소 파싱 ─────────────────────────────────────────────────────────────────

function parseAddress(varName) {
  const m = varName.match(/%MW(\d+)/)
  return m ? parseInt(m[1], 10) : -1
}

// ── Cnet 프레임 처리 ──────────────────────────────────────────────────────────

/**
 * ENQ...EOT 프레임 하나를 파싱하여 ACK/NAK 응답을 반환한다.
 *
 * 요청 레이아웃 (모두 ASCII 문자열, ENQ/EOT 제외):
 *   [0]    ENQ  (0x05)
 *   [1..2] 국번       e.g. "01"
 *   [3]    명령       'R' 또는 'W'
 *   [4..5] 타입       "SB"
 *   [6..7] 변수길이   2 hex chars  e.g. "06"
 *   [8..8+varLen-1]   변수명       e.g. "%MW140"
 *   [8+varLen..+1]    데이터수     2 hex chars  e.g. "01"
 *   [8+varLen+2..-1]  데이터       write 시에만 존재 (wordCount × 4 hex chars)
 *   [-1]   EOT  (0x04)
 */
function processFrame(frame, port) {
  if (frame[0] !== ENQ || frame[frame.length - 1] !== EOT) return

  const str       = frame.toString('ascii')
  const stationNo = str.slice(1, 3)
  const cmd       = str[3]
  const type      = str.slice(4, 6)
  const varLen    = parseInt(str.slice(6, 8), 16)
  const varName   = str.slice(8, 8 + varLen)
  const wc        = parseInt(str.slice(8 + varLen, 8 + varLen + 2), 16)
  const dataStr   = str.slice(8 + varLen + 2, str.length - 1)  // EOT 직전까지

  const addr = parseAddress(varName)
  if (addr < 0) {
    port.write(buildNak(stationNo, cmd, type, '0003'))
    return
  }

  if (cmd === 'R') {
    // ── RSB 읽기 응답 ─────────────────────────────────────────────────────
    // 데이터: 각 word를 big-endian 4hex (high byte 먼저)
    // CnetSerialPlcClient.ReadWordsAsync 에서 Convert.ToUInt16("HHLL", 16) 로 파싱함
    const words = []
    for (let i = 0; i < wc; i++)
      words.push(memory.get(addr + i) || 0)

    const data = words.map(w => {
      const hi = (w >> 8) & 0xFF
      const lo =  w       & 0xFF
      return hi.toString(16).padStart(2, '0').toUpperCase()
           + lo.toString(16).padStart(2, '0').toUpperCase()
    }).join('')

    const byteCount = (wc * 2).toString(16).padStart(2, '0').toUpperCase()
    // body = 국번(2) + 명령(1) + 타입(2) + 블록수(2) + 바이트수(2) + 데이터
    const body = stationNo + cmd + type + '01' + byteCount + data

    const vals = words.slice(0, 4)
    console.log(`  [PLC] READ  ${varName}[${wc}] → [${vals.join(', ')}${wc > 4 ? '...' : ''}]`)
    port.write(buildAck(body))

  } else if (cmd === 'W') {
    // ── WSB 쓰기 처리 ─────────────────────────────────────────────────────
    const prevVal = memory.get(addr)

    for (let i = 0; i < wc; i++) {
      const val = parseInt(dataStr.slice(i * 4, i * 4 + 4), 16)
      memory.set(addr + i, val)
    }

    // 상태 변화 감지
    if (addr === MW_BARCODE_REQUEST) {
      const newVal = memory.get(MW_BARCODE_REQUEST)
      if (prevVal !== 0 && newVal === 0) onBarcodeRequestCleared()
    }
    if (addr === MW_SCANNED_BARCODE1_START && isBarcodeClear(MW_SCANNED_BARCODE1_START)) {
      onScannedBarcodesCleared()
    }

    const byteCount = (wc * 2).toString(16).padStart(2, '0').toUpperCase()
    const body = stationNo + cmd + type + '01' + byteCount

    if (wc <= 4) {
      const vals = Array.from({ length: wc }, (_, i) => memory.get(addr + i))
      console.log(`  [PLC] WRITE ${varName}[${wc}] ← [${vals.join(', ')}]`)
    } else {
      console.log(`  [PLC] WRITE ${varName}[${wc}] ← "${readBarcode(addr)}"`)
    }
    port.write(buildAck(body))

  } else {
    console.warn(`  [PLC] 알 수 없는 명령: '${cmd}'`)
    port.write(buildNak(stationNo, cmd, type, '0003'))
  }
}

// ── 응답 프레임 빌더 ──────────────────────────────────────────────────────────

function buildAck(body) {
  return Buffer.from([ACK, ...Buffer.from(body, 'ascii'), ETX])
}

function buildNak(stationNo, cmd, type, errCode) {
  const body = stationNo + cmd + type + errCode
  return Buffer.from([NAK, ...Buffer.from(body, 'ascii'), ETX])
}

// ── 시리얼 포트 ───────────────────────────────────────────────────────────────

const COM_PORT  = 'COM6'
const BAUD_RATE = 9600

const port = new SerialPort({ path: COM_PORT, baudRate: BAUD_RATE })
let rxBuf = Buffer.alloc(0)

port.on('open', () => {
  console.log('')
  console.log(`  Serial Mock PLC  ->  ${COM_PORT} @ ${BAUD_RATE} baud`)
  console.log('  (com0com 가상 포트 쌍: COM5=앱, COM6=이 서버)')
  console.log('  Memory map:')
  console.log('    %MW100 ~ %MW114  Lot 바코드 #1    (PC → PLC)')
  console.log('    %MW120 ~ %MW134  Lot 바코드 #2    (PC → PLC)')
  console.log('    %MW140           발행 요청        (PLC → PC, 1=요청)')
  console.log('    %MW150 ~ %MW164  스캐너 결과 #1   (Scanner → PLC → PC)')
  console.log('    %MW170 ~ %MW184  스캐너 결과 #2   (Scanner → PLC → PC)')
  console.log('')

  memory.set(MW_BARCODE_REQUEST, 0)
  clearBarcode(MW_SCANNED_BARCODE1_START)
  clearBarcode(MW_SCANNED_BARCODE2_START)
  setTimeout(triggerBarcodeRequest, 500)
})

port.on('data', chunk => {
  rxBuf = Buffer.concat([rxBuf, chunk])

  while (true) {
    const enqIdx = rxBuf.indexOf(ENQ)
    if (enqIdx < 0) { rxBuf = Buffer.alloc(0); break }
    if (enqIdx > 0) rxBuf = rxBuf.slice(enqIdx)  // ENQ 이전 쓰레기 제거

    const eotIdx = rxBuf.indexOf(EOT)
    if (eotIdx < 0) break  // EOT 미수신, 다음 청크 대기

    const frame = rxBuf.slice(0, eotIdx + 1)
    rxBuf = rxBuf.slice(eotIdx + 1)
    processFrame(frame, port)
  }
})

port.on('error', err => console.error(`  [PLC] 포트 오류: ${err.message}`))
port.on('close', () => {
  if (engravingTimer) { clearTimeout(engravingTimer); engravingTimer = null }
  console.log('  [PLC] 포트 닫힘')
})
