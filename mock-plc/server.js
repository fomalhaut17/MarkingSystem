/**
 * LS XGT FENET Mock PLC Server
 * TCP Port: 2004
 *
 * PLC Memory Map (%MW = Memory Word, 2 bytes each):
 *   %MW100 ~ %MW114  Lot barcode (15 words = 30 bytes ASCII)
 *   %MW116           Command  : 0=None, 1=Start, 2=Stop
 *   %MW117           Status   : 0=Idle, 1=Marking, 2=DoneOK, 3=DoneNG, 9=Error
 *
 * XGT FENET Frame:
 *   Header (20 bytes):
 *     [0-9]   Company ID  "LSIS-XGT\0\0"
 *     [10-11] Reserved    0x0000
 *     [12]    PLC Info    0x00
 *     [13]    CPU Info    0x00
 *     [14]    Direction   0x33=Host→PLC / 0x11=PLC→Host
 *     [15-16] Invoke ID   (uint16 LE)
 *     [17-18] Data Length (uint16 LE)
 *     [19]    FEnet Slot  0x00
 *   Application Data: follows header
 */

'use strict'

const net = require('net')

// ── 프로토콜 상수 ─────────────────────────────────────────────────────────────

const HEADER_SIZE = 20
const CMD_READ    = 0x0054
const CMD_WRITE   = 0x0058

// ── 가상 PLC 메모리 (word index → uint16 value) ───────────────────────────────

const MW_LOT_START = 100   // %MW100 ~ %MW114 : Lot 바코드 (15 words)
const MW_COMMAND   = 116   // %MW116 : 명령
const MW_STATUS    = 117   // %MW117 : 상태

const memory = new Map()
memory.set(MW_COMMAND, 0)
memory.set(MW_STATUS,  0)

// ── PLC 상태 머신 ─────────────────────────────────────────────────────────────

let markingTimer = null

function onCommandChanged(newCmd) {
  if (newCmd === 1) {
    // 발행 시작
    if (markingTimer) clearTimeout(markingTimer)
    memory.set(MW_STATUS, 1)  // Marking

    const barcode = readLotBarcode()
    console.log(`  [PLC] 각인 시작: "${barcode}"`)

    const delay = 1000 + Math.random() * 1000
    markingTimer = setTimeout(() => {
      const isOk = Math.random() > 0.1  // 90% OK, 10% NG
      memory.set(MW_STATUS,  isOk ? 2 : 3)
      memory.set(MW_COMMAND, 0)           // 명령 자동 클리어
      console.log(`  [PLC] 각인 완료: ${isOk ? 'OK ✓' : 'NG ✗'}  (${barcode})`)
      markingTimer = null
    }, delay)

  } else if (newCmd === 2) {
    // 발행 정지
    if (markingTimer) { clearTimeout(markingTimer); markingTimer = null }
    memory.set(MW_STATUS,  0)  // Idle
    memory.set(MW_COMMAND, 0)
    console.log('  [PLC] 발행 정지')
  }
}

function readLotBarcode() {
  const bytes = []
  for (let i = 0; i < 15; i++) {
    const w = memory.get(MW_LOT_START + i) || 0
    bytes.push(w & 0xFF)
    bytes.push((w >> 8) & 0xFF)
  }
  return Buffer.from(bytes).toString('ascii').replace(/\0+$/, '')
}

// ── 주소 파싱 ─────────────────────────────────────────────────────────────────

function parseAddress(varName) {
  // '%MW100' → 100
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
  h[14] = 0x11                         // Direction: PLC → Host
  h.writeUInt16LE(invokeId, 15)
  h.writeUInt16LE(dataLen,  17)
  return h
}

// ── Application Data 처리 ─────────────────────────────────────────────────────

function handleAppData(appData) {
  if (appData.length < 6) return errorResponse(0x0003)

  const cmd      = appData.readUInt16LE(0)
  // [2-3] data type (ignored, we handle WORD only)
  const blockCnt = appData.readUInt16LE(4)
  let   offset   = 6

  if (cmd === CMD_READ) {
    // ── 읽기 요청 ─────────────────────────────────────────────────────────────
    // Response: [errCode(2LE)] [blockCnt(2LE)] per block: [wordCnt(2LE)] [data...]
    const parts = []
    const header = Buffer.alloc(4)
    header.writeUInt16LE(0, 0)         // errCode = 0
    header.writeUInt16LE(blockCnt, 2)
    parts.push(header)

    for (let b = 0; b < blockCnt; b++) {
      const nameLen = appData.readUInt16LE(offset);  offset += 2
      const varName = appData.slice(offset, offset + nameLen).toString('ascii'); offset += nameLen
      const wordCnt = appData.readUInt16LE(offset);  offset += 2

      const addrIdx = parseAddress(varName)
      const blk     = Buffer.alloc(2 + wordCnt * 2)
      blk.writeUInt16LE(wordCnt, 0)
      for (let i = 0; i < wordCnt; i++) {
        blk.writeUInt16LE(memory.get(addrIdx + i) || 0, 2 + i * 2)
      }
      parts.push(blk)

      const vals = Array.from({ length: wordCnt }, (_, i) => memory.get(addrIdx + i) || 0)
      console.log(`  [PLC] READ  ${varName}[${wordCnt}] → [${vals.join(', ')}]`)
    }

    return Buffer.concat(parts)

  } else if (cmd === CMD_WRITE) {
    // ── 쓰기 요청 ─────────────────────────────────────────────────────────────
    // Response: [errCode(2LE)] [blockCnt(2LE)]
    for (let b = 0; b < blockCnt; b++) {
      const nameLen = appData.readUInt16LE(offset);  offset += 2
      const varName = appData.slice(offset, offset + nameLen).toString('ascii'); offset += nameLen
      const wordCnt = appData.readUInt16LE(offset);  offset += 2

      const addrIdx = parseAddress(varName)
      const prevCmd = addrIdx === MW_COMMAND ? memory.get(MW_COMMAND) : null

      for (let i = 0; i < wordCnt; i++) {
        memory.set(addrIdx + i, appData.readUInt16LE(offset)); offset += 2
      }

      // 명령 레지스터 변화 감지 → 상태 머신 트리거
      if (addrIdx === MW_COMMAND) {
        const newCmd = memory.get(MW_COMMAND)
        if (newCmd !== prevCmd) onCommandChanged(newCmd)
      }

      const vals = Array.from({ length: wordCnt }, (_, i) => memory.get(addrIdx + i))
      console.log(`  [PLC] WRITE ${varName}[${wordCnt}] ← [${vals.join(', ')}]`)
    }

    const resp = Buffer.alloc(4)
    resp.writeUInt16LE(0, 0)          // errCode = 0
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
      if (rxBuf.length < totalLen) break   // 아직 데이터 부족

      const appData  = rxBuf.slice(HEADER_SIZE, totalLen)
      rxBuf = rxBuf.slice(totalLen)

      const respData = handleAppData(appData)
      socket.write(Buffer.concat([buildRespHeader(hdr.invokeId, respData.length), respData]))
    }
  })

  socket.on('close', () => console.log(`  [PLC] 연결 종료: ${remote}`))
  socket.on('error', err => console.error(`  [PLC] 소켓 오류: ${err.message}`))
})

const PORT = 2004
server.listen(PORT, () => {
  console.log('')
  console.log('  PLC Mock Server  ->  TCP localhost:' + PORT)
  console.log('  Memory map:')
  console.log('    %MW100 ~ %MW114  Lot barcode  (15 words = 30 bytes ASCII)')
  console.log('    %MW116           Command      (0=None, 1=Start, 2=Stop)')
  console.log('    %MW117           Status       (0=Idle, 1=Marking, 2=OK, 3=NG, 9=Error)')
  console.log('')
})
