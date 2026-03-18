const jsonServer  = require('json-server')
const server      = jsonServer.create()
const router      = jsonServer.router('db.json')
const middlewares = jsonServer.defaults({ logger: true })

server.use(middlewares)
server.use(jsonServer.bodyParser)

// ── 헬퍼 ─────────────────────────────────────────────────────────────────────

function ok(res, data) {
  res.json({ success: true, data, error: null })
}

function notFound(res, code, message) {
  res.status(404).json({ success: false, data: null, error: { code, message } })
}

function badRequest(res, code, message) {
  res.status(400).json({ success: false, data: null, error: { code, message } })
}

function unauthorized(res, code, message) {
  res.status(401).json({ success: false, data: null, error: { code, message } })
}

// ── DB 접근 헬퍼 ──────────────────────────────────────────────────────────────

function db() { return router.db }

// mock_issue_results: 시작 시 db.json에서 로드, POST/reset 시 db.json에 flush
const issueResultsStore = router.db.get('mock_issue_results').value().slice()
let   nextIssueId       = issueResultsStore.reduce((max, r) => Math.max(max, r.id), 0) + 1

function flushIssueResults() {
  router.db.set('mock_issue_results', issueResultsStore).write()
}

// ── 인증 Mock 상태 ────────────────────────────────────────────────────────────
// 테스트 계정: companyCode=MANNTEK / admin / 1234
const MOCK_USERS = [
  { companyCode: 'MANNTEK', username: 'admin', password: '1234' },
]

const refreshTokenStore = new Set()

function makeAccessToken()  { return 'mock-access-'  + Math.random().toString(36).slice(2) }
function makeRefreshToken() { return 'mock-refresh-' + Math.random().toString(36).slice(2) }

// ── AUTH 1: 로그인  POST /auth/login ─────────────────────────────────────────

server.post('/auth/login', (req, res) => {
  const { companyCode, username, password } = req.body || {}

  if (!companyCode || !username || !password) {
    return badRequest(res, 'INVALID_REQUEST', 'companyCode, username, password 가 필요합니다.')
  }

  const user = MOCK_USERS.find(u =>
    u.companyCode === companyCode && u.username === username && u.password === password)
  if (!user) {
    return unauthorized(res, 'INVALID_CREDENTIALS', '업체코드·아이디·비밀번호가 올바르지 않습니다.')
  }

  const accessToken  = makeAccessToken()
  const refreshToken = makeRefreshToken()
  refreshTokenStore.add(refreshToken)

  console.log(`[auth/login] companyCode=${companyCode} username=${username} → tokens issued`)
  ok(res, { accessToken, refreshToken, expiresIn: 3600 })
})

// ── AUTH 2: 토큰 갱신  POST /auth/refresh ────────────────────────────────────

server.post('/auth/refresh', (req, res) => {
  const { refreshToken } = req.body || {}

  if (!refreshToken) {
    return badRequest(res, 'INVALID_REQUEST', 'refreshToken 이 필요합니다.')
  }

  if (!refreshTokenStore.has(refreshToken)) {
    return unauthorized(res, 'INVALID_REFRESH_TOKEN', 'Refresh Token 이 만료되었거나 유효하지 않습니다.')
  }

  refreshTokenStore.delete(refreshToken)
  const newAccessToken  = makeAccessToken()
  const newRefreshToken = makeRefreshToken()
  refreshTokenStore.add(newRefreshToken)

  console.log(`[auth/refresh] token rotated`)
  ok(res, { accessToken: newAccessToken, refreshToken: newRefreshToken, expiresIn: 3600 })
})

// ── API 1: 자재 정보 조회  GET /api/marking/materials/:materialBarcode ─────────
// mock_materials → mock_lots → mock_products 조인
// 미등록 바코드: 첫 번째 Lot에 연결한 합성 자재로 처리 (테스트 편의)
// lastIssuedSer: mock_issue_results 에서 해당 Lot 전체 기준 MAX(lotSer) 동적 계산

server.get('/api/marking/materials/:materialBarcode', (req, res) => {
  const barcode = req.params.materialBarcode
  let   material = db().get('mock_materials').find({ materialBarcode: barcode }).value()
  let   synthetic = false

  if (!material) {
    // 미등록 바코드 → 첫 번째 Lot에 연결한 합성 자재
    const defaultLot = db().get('mock_lots').first().value()
    if (!defaultLot) {
      return notFound(res, 'MATERIAL_NOT_FOUND', '물류 바코드에 해당하는 자재를 찾을 수 없습니다.')
    }
    material = { materialBarcode: barcode, lotCode: defaultLot.lotCode, containerQty: '10' }
    synthetic = true
  }

  const lot = db().get('mock_lots').find({ lotCode: material.lotCode }).value()
  if (!lot) {
    return notFound(res, 'LOT_NOT_FOUND', '연결된 Lot 정보를 찾을 수 없습니다.')
  }

  const product = db().get('mock_products').find({ productCode: lot.productCode }).value()
  if (!product) {
    return notFound(res, 'PRODUCT_NOT_FOUND', '연결된 품목 정보를 찾을 수 없습니다.')
  }

  // lastIssuedSer: 해당 Lot 전체(모든 물류 바코드)의 최대 Ser 값
  const lotBarcodes = db().get('mock_materials').filter({ lotCode: material.lotCode }).map('materialBarcode').value()
  if (synthetic) lotBarcodes.push(barcode)
  const lotResults    = issueResultsStore.filter(r => lotBarcodes.includes(r.materialBarcode))
  const lastIssuedSer = lotResults.length > 0
    ? lotResults.map(r => r.lotBarcode.slice(-6)).reduce((max, ser) => ser > max ? ser : max, '000000')
    : '000000'

  console.log(`[materials] barcode=${barcode}${synthetic ? ' (synthetic)' : ''} lotCode=${material.lotCode} lastIssuedSer=${lastIssuedSer}`)

  ok(res, {
    materialBarcode:     barcode,
    productName:         product.productName,
    manufactureDate:     lot.manufactureDate,
    containerQty:        material.containerQty,
    lotCode:             material.lotCode,
    lastIssuedSer,
    productionEquipment: lot.productionEquipment,
    productionMold:      lot.productionMold,
    lotProductionQty:    lot.lotProductionQty,
  })
})

// ── API 2: 발행 결과 일괄 전송  POST /api/marking/issue-results ──────────────
// mock_issue_results 에 INSERT 후 db.json 에 flush

server.post('/api/marking/issue-results', (req, res) => {
  const { materialBarcode, results } = req.body || {}

  if (!materialBarcode || !Array.isArray(results)) {
    return badRequest(res, 'INVALID_REQUEST', 'materialBarcode 와 results 배열이 필요합니다.')
  }

  const savedAt = new Date().toISOString().slice(0, 19)

  results.forEach(r => {
    issueResultsStore.push({
      id:               nextIssueId++,
      materialBarcode,
      lotBarcode:       r.lotBarcode,
      inspectionResult: r.inspectionResult,
      savedAt,
    })
  })

  flushIssueResults()

  console.log(`[issue-results] materialBarcode=${materialBarcode} count=${results.length}`)
  results.forEach(r => console.log(`  ${r.lotBarcode} → ${r.inspectionResult}`))

  ok(res, { savedCount: results.length })
})

// ── API 3: Lot 목록 조회  GET /api/marking/lots ───────────────────────────────

server.get('/api/marking/lots', (req, res) => {
  const { materialBarcode, lotCode } = req.query

  if (!materialBarcode && !lotCode) {
    return badRequest(res, 'MISSING_QUERY_PARAM', 'materialBarcode 또는 lotCode 파라미터가 필요합니다.')
  }

  let results
  if (materialBarcode) {
    results = issueResultsStore.filter(r => r.materialBarcode === materialBarcode)
  } else {
    const barcodes = db().get('mock_materials').filter({ lotCode }).map('materialBarcode').value()
    results = issueResultsStore.filter(r => barcodes.includes(r.materialBarcode))
  }

  const items = results.map(r => ({
    materialBarcode:  r.materialBarcode,
    lotBarcode:       r.lotBarcode,
    inspectionResult: r.inspectionResult,
  }))

  console.log(`[lots] query=${JSON.stringify(req.query)} → ${items.length} items`)
  ok(res, { items, totalCount: items.length })
})

// ── API 4: 생산 Lot 상세 조회  GET /api/marking/production-lots/:lotCode ──────

server.get('/api/marking/production-lots/:lotCode', (req, res) => {
  const lotCode = req.params.lotCode

  const lot = db().get('mock_lots').find({ lotCode }).value()
  if (!lot) {
    return notFound(res, 'LOT_NOT_FOUND', '해당 Lot 코드를 찾을 수 없습니다.')
  }

  const product = db().get('mock_products').find({ productCode: lot.productCode }).value()

  const injectionConditions = db().get('mock_injection_conditions').filter({ lotCode }).value()

  const barcodes   = db().get('mock_materials').filter({ lotCode }).map('materialBarcode').value()
  const allResults = issueResultsStore.filter(r => barcodes.includes(r.materialBarcode))
  const okCount    = allResults.filter(r => r.inspectionResult === 'OK').length
  const ngCount    = allResults.filter(r => r.inspectionResult === 'NG').length

  console.log(`[production-lots] lotCode=${lotCode} ok=${okCount} ng=${ngCount}`)

  ok(res, {
    lotCode,
    productName:         product?.productName ?? '',
    manufactureDate:     lot.manufactureDate,
    productionEquipment: lot.productionEquipment,
    productionMold:      lot.productionMold,
    lotProductionQty:    lot.lotProductionQty,
    okCount,
    ngCount,
    injectionConditions,
  })
})

// ── TEST: 초기화  DELETE /api/marking/test/reset ──────────────────────────────
// 테스트 모드(local/dev)에서만 사용. issueResultsStore 전체 삭제 후 db.json flush.

server.delete('/api/marking/test/reset', (req, res) => {
  const { materialBarcode } = req.query
  if (!materialBarcode) {
    return badRequest(res, 'MISSING_QUERY_PARAM', 'materialBarcode 파라미터가 필요합니다.')
  }
  const before = issueResultsStore.length
  const indices = issueResultsStore
    .map((r, i) => r.materialBarcode === materialBarcode ? i : -1)
    .filter(i => i >= 0)
    .reverse()
  indices.forEach(i => issueResultsStore.splice(i, 1))
  const cleared = before - issueResultsStore.length
  flushIssueResults()
  console.log(`[test/reset] materialBarcode=${materialBarcode} cleared=${cleared}`)
  ok(res, { clearedCount: cleared })
})

// ── 시작 ──────────────────────────────────────────────────────────────────────

const PORT = 3000
server.listen(PORT, () => {
  console.log('')
  console.log('  wizMES Mock API  ->  http://localhost:' + PORT)
  console.log('  [Auth]')
  console.log('    POST   /auth/login              (MANNTEK / admin / 1234)')
  console.log('    POST   /auth/refresh')
  console.log('  [Marking]')
  console.log('    GET    /api/marking/materials/:materialBarcode  (* 미등록 바코드도 수용)')
  console.log('    POST   /api/marking/issue-results              (db.json 영속화)')
  console.log('    GET    /api/marking/lots?materialBarcode=... | ?lotCode=...')
  console.log('    GET    /api/marking/production-lots/:lotCode')
  console.log('  [Test]')
  console.log('    DELETE /api/marking/test/reset?materialBarcode= (해당 물류 바코드 issueResults 삭제)')
  console.log('')
})
