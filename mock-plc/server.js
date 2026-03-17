/**
 * LS XGT FENET Mock PLC Server
 * TCP Port: 2004
 *
 * PLC Memory Map (%MW = Memory Word, 2 bytes each):
 *   %MW100 ~ %MW114  Lot 바코드 #1 (15 words = 30 bytes ASCII) — PC → PLC
 *   %MW120 ~ %MW134  Lot 바코드 #2 (15 words = 30 bytes ASCII) — PC → PLC
 *   %MW140           발행 요청     (1 = 요청, 0 = 대기)        — PLC → PC
 *   %MW150 ~ %MW164  스캐너 바코드 #11 (15 words)              — Scanner → PLC → PC
 *   %MW170 ~ %MW184  스캐너 바코드 #12 (15 words)              — Scanner → PLC → PC
 *
 * NOTE: 위 주소는 appsettings.json Plc.Memory 기본값과 일치해야 한다 (현재 TBD 임시값).
 *
 * Mock 동작 흐름:
 *   1. PC 연결 시 → 500ms 후 발행 요청(MW140=1) 세팅
 *   2. PC가 바코드 2개를 쓰고 MW140=0 클리어 → 각인 시뮬레이션 (1~2초)
 *   3. 각인 완료 → 스캐너 결과를 MW150/MW170에 기록 (90% 일치, 10% 불일치)
 *   4. PC가 MW150을 전체 0으로 클리어 → 300ms 후 다음 발행 요청(MW140=1) 세팅
 */

'use strict'

const net = require('net')

// ── 프로토콜 상수 ─────────────────────────────────────────────────────────────

const HEADER_SIZE = 20
const CMD_READ    = 0x0054
const CMD_WRITE   = 0x0058

// ── 메모리 주소 (appsettings.json Plc.Memory 기본값과 동일하게 유지) ──────────

const MW_LOT_BARCODE1_START   = 100  // %MW100 ~ %MW114
const MW_LOT_BARCODE2_START   = 120  // %MW120 ~ %MW134
const MW_BARCODE_REQUEST      = 140  // %MW140
const MW_SCANNED_BARCODE1_START = 150 // %MW150 ~ %MW164
const MW_SCANNED_BARCODE2_START = 170 // %MW170 ~ %MW184
const LOT_BARCODE_WORD_COUNT  = 15

// ── 가상 PLC 메모리 (word index → uint16 value) ───────────────────────────────

const memory = new Map()
memory.set(MW_BARCODE_REQUEST, 0)

// ── 유틸 함수 ────────────────────────────────────────────────────────────────

function readBarcode(startAddr) {
  const bytes = []
  for (let i = 0; i < LOT_BARCODE_WORD_COUNT; i++) {
    const w = memory.get(startAddr + i) || 0
    bytes.push(w & 0xFF)
    bytes.push((w >> 8) & 0xFF)
  }
  return Buffer.from(bytes).toString('ascii').replace(/\0+$/, '')
}

function writeBarcode(startAddr, barcodeStr) {
  const src = Buffer.from(barcodeStr, 'ascii')
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

/** 발행 요청 메모리를 1로 세팅 (PLC가 바코드 요청) */
function triggerBarcodeRequest() {
  memory.set(MW_BARCODE_REQUEST, 1)
  console.log('  [PLC] 발행 요청 세팅 (MW140=1)')
}

/** PC가 발행 요청을 클리어(0)했을 때 → 각인 시뮬레이션 시작 */
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
    console.log(`  [PLC] 스캐너 결과: #11="${sc1}" ${ok1},  #12="${sc2}" ${ok2}`)
    engravingTimer = null
  }, delay)
}

/** PC가 스캐너 메모리를 클리어했을 때 → 다음 발행 요청 */
function onScannedBarcodesCleared() {
  setTimeout(triggerBarcodeRequest, 300)
}

// ── 주소 파싱 ─────────────────────────────────────────────────────────────────

function parseAddress(varName) {
  const m = varName.match(/%MW(\d+)/)
  return m ? parseInt(m[1], 10) : 0
}

// ── XGT FENET 헤더 파싱/빌드 ──────────────────────────────────────────────────

function parseHeader(buf) {
  if (buf.length < HEADER_SIZE) return null
  if (buf.slice(0, 8).toString('ascii') !== 'LSIS-XGT') return null
  return {
    invokeId: buf.readUInt16LE(15),
    dataLen:  buf.readUInt16LE(17),
  }
}

function buildRespHeader(invokeId, dataLen) {
  const h = Buffer.alloc(HEADER_SIZE)
  Buffer.from('LSIS-XGT').copy(h, 0)
  h[14] = 0x11
  h.writeUInt16LE(invokeId, 15)
  h.writeUInt16LE(dataLen,  17)
  return h
}

// ── Application Data 처리 ─────────────────────────────────────────────────────

function handleAppData(appData) {
  if (appData.length < 6) return errorResponse(0x0003)

  const cmd      = appData.readUInt16LE(0)
  const blockCnt = appData.readUInt16LE(4)
  let   offset   = 6

  if (cmd === CMD_READ) {
    const parts  = []
    const header = Buffer.alloc(4)
    header.writeUInt16LE(0,        0)
    header.writeUInt16LE(blockCnt, 2)
    parts.push(header)

    for (let b = 0; b < blockCnt; b++) {
      const nameLen = appData.readUInt16LE(offset);  offset += 2
      const varName = appData.slice(offset, offset + nameLen).toString('ascii'); offset += nameLen
      const wordCnt = appData.readUInt16LE(offset);  offset += 2

      const addrIdx = parseAddress(varName)
      const blk     = Buffer.alloc(2 + wordCnt * 2)
      blk.writeUInt16LE(wordCnt, 0)
      for (let i = 0; i < wordCnt; i++)
        blk.writeUInt16LE(memory.get(addrIdx + i) || 0, 2 + i * 2)
      parts.push(blk)

      const vals = Array.from({ length: wordCnt }, (_, i) => memory.get(addrIdx + i) || 0)
      console.log(`  [PLC] READ  ${varName}[${wordCnt}] → [${vals.slice(0, 4).join(', ')}${wordCnt > 4 ? '...' : ''}]`)
    }

    return Buffer.concat(parts)

  } else if (cmd === CMD_WRITE) {
    for (let b = 0; b < blockCnt; b++) {
      const nameLen = appData.readUInt16LE(offset);  offset += 2
      const varName = appData.slice(offset, offset + nameLen).toString('ascii'); offset += nameLen
      const wordCnt = appData.readUInt16LE(offset);  offset += 2

      const addrIdx = parseAddress(varName)
      const prevVal = memory.get(addrIdx)

      for (let i = 0; i < wordCnt; i++) {
        memory.set(addrIdx + i, appData.readUInt16LE(offset)); offset += 2
      }

      // 상태 변화 감지
      if (addrIdx === MW_BARCODE_REQUEST) {
        const newVal = memory.get(MW_BARCODE_REQUEST)
        if (prevVal !== 0 && newVal === 0) onBarcodeRequestCleared()
      }

      if (addrIdx === MW_SCANNED_BARCODE1_START && isBarcodeClear(MW_SCANNED_BARCODE1_START)) {
        onScannedBarcodesCleared()
      }

      const vals = Array.from({ length: wordCnt }, (_, i) => memory.get(addrIdx + i))
      if (wordCnt <= 4) {
        console.log(`  [PLC] WRITE ${varName}[${wordCnt}] ← [${vals.join(', ')}]`)
      } else {
        const bc = readBarcode(addrIdx)
        console.log(`  [PLC] WRITE ${varName}[${wordCnt}] ← "${bc}"`)
      }
    }

    const resp = Buffer.alloc(4)
    resp.writeUInt16LE(0,        0)
    resp.writeUInt16LE(blockCnt, 2)
    return resp

  } else {
    console.warn(`  [PLC] 알 수 없는 명령: 0x${cmd.toString(16).padStart(4, '0')}`)
    return errorResponse(0x0003)
  }
}

function errorResponse(errCode) {
  const buf = Buffer.alloc(4)
  buf.writeUInt16LE(errCode, 0)
  return buf
}

// ── TCP 서버 ──────────────────────────────────────────────────────────────────

const server = net.createServer(socket => {
  const remote = `${socket.remoteAddress}:${socket.remotePort}`
  console.log(`\n  [PLC] 연결됨: ${remote}`)

  // 연결 시 메모리 초기화 후 첫 발행 요청 트리거
  memory.set(MW_BARCODE_REQUEST, 0)
  clearBarcode(MW_SCANNED_BARCODE1_START)
  clearBarcode(MW_SCANNED_BARCODE2_START)
  setTimeout(triggerBarcodeRequest, 500)

  let rxBuf = Buffer.alloc(0)

  socket.on('data', chunk => {
    rxBuf = Buffer.concat([rxBuf, chunk])

    while (rxBuf.length >= HEADER_SIZE) {
      const hdr = parseHeader(rxBuf)
      if (!hdr) {
        console.warn('  [PLC] 잘못된 헤더, 버퍼 초기화')
        rxBuf = Buffer.alloc(0)
        break
      }

      const totalLen = HEADER_SIZE + hdr.dataLen
      if (rxBuf.length < totalLen) break

      const appData  = rxBuf.slice(HEADER_SIZE, totalLen)
      rxBuf = rxBuf.slice(totalLen)

      const respData = handleAppData(appData)
      socket.write(Buffer.concat([buildRespHeader(hdr.invokeId, respData.length), respData]))
    }
  })

  socket.on('close', () => {
    if (engravingTimer) { clearTimeout(engravingTimer); engravingTimer = null }
    console.log(`  [PLC] 연결 종료: ${remote}`)
  })
  socket.on('error', err => console.error(`  [PLC] 소켓 오류: ${err.message}`))
})

const PORT = 2004
server.listen(PORT, () => {
  console.log('')
  console.log('  PLC Mock Server  ->  TCP localhost:' + PORT)
  console.log('  Memory map (TBD - appsettings.json Plc.Memory 값과 일치):')
  console.log('    %MW100 ~ %MW114  Lot 바코드 #1    (PC → PLC)')
  console.log('    %MW120 ~ %MW134  Lot 바코드 #2    (PC → PLC)')
  console.log('    %MW140           발행 요청        (PLC → PC, 1=요청)')
  console.log('    %MW150 ~ %MW164  스캐너 결과 #11  (Scanner → PLC → PC)')
  console.log('    %MW170 ~ %MW184  스캐너 결과 #12  (Scanner → PLC → PC)')
  console.log('')
})
