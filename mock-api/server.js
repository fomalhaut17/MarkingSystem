const jsonServer = require('json-server')
const server     = jsonServer.create()
const router     = jsonServer.router('db.json')
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

// ── 인증 Mock 상태 ────────────────────────────────────────────────────────────
// 테스트 계정: companyCode=MANNTEK / admin / 1234
const MOCK_USERS = [
  { companyCode: 'MANNTEK', username: 'admin', password: '1234' },
]

// 발급된 refresh token 저장소 (인메모리)
const refreshTokenStore = new Set()

function makeAccessToken()  { return 'mock-access-'  + Math.random().toString(36).slice(2) }
function makeRefreshToken() { return 'mock-refresh-' + Math.random().toString(36).slice(2) }

// ── AUTH 1: 로그인  POST /auth/login ─────────────────────────────────────────

server.post('/auth/login', (req, res) => {
  const { companyCode, username, password } = req.body || {}

  if (!companyCode || !username || !password) {
    return badRequest(res, 'INVALID_REQUEST', 'companyCode, username, password 가 필요합니다.')
  }

  const user = MOCK_USERS.find(u => u.companyCode === companyCode && u.username === username && u.password === password)
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

  // Rotation: 기존 토큰 폐기 후 새 토큰 발급
  refreshTokenStore.delete(refreshToken)
  const newAccessToken  = makeAccessToken()
  const newRefreshToken = makeRefreshToken()
  refreshTokenStore.add(newRefreshToken)

  console.log(`[auth/refresh] token rotated`)
  ok(res, { accessToken: newAccessToken, refreshToken: newRefreshToken, expiresIn: 3600 })
})

// ── API 1: 자재 정보 조회  GET /api/marking/materials/:materialBarcode ─────────

server.get('/api/marking/materials/:materialBarcode', (req, res) => {
  const db       = router.db
  const barcode  = req.params.materialBarcode
  const material = db.get('materials').find({ materialBarcode: barcode }).value()

  if (!material) {
    return notFound(res, 'MATERIAL_NOT_FOUND', '물류 바코드에 해당하는 자재를 찾을 수 없습니다.')
  }
  ok(res, material)
})

// ── API 2: 발행 결과 일괄 전송  POST /api/marking/issue-results ──────────────

server.post('/api/marking/issue-results', (req, res) => {
  const { materialBarcode, results } = req.body

  if (!materialBarcode || !Array.isArray(results)) {
    return badRequest(res, 'INVALID_REQUEST', 'materialBarcode 와 results 배열이 필요합니다.')
  }

  console.log(`[issue-results] materialBarcode=${materialBarcode}, count=${results.length}`)
  results.forEach(r => console.log(`  ${r.lotBarcode} → ${r.inspectionResult}`))

  ok(res, { savedCount: results.length })
})

// ── API 3: Lot 목록 조회  GET /api/marking/lots ───────────────────────────────

server.get('/api/marking/lots', (req, res) => {
  const { materialBarcode, lotCode } = req.query

  if (!materialBarcode && !lotCode) {
    return badRequest(res, 'MISSING_QUERY_PARAM', 'materialBarcode 또는 lotCode 파라미터가 필요합니다.')
  }

  // TODO: wizMES 실서버 연동 시 실제 발행 이력 반환
  ok(res, { items: [], totalCount: 0 })
})

// ── API 4: 생산 Lot 상세 조회  GET /api/marking/production-lots/:lotCode ──────

server.get('/api/marking/production-lots/:lotCode', (req, res) => {
  const db      = router.db
  const lotCode = req.params.lotCode
  const material = db.get('materials').find({ lotCode }).value()

  if (!material) {
    return notFound(res, 'LOT_NOT_FOUND', '해당 Lot 코드를 찾을 수 없습니다.')
  }

  ok(res, {
    lotCode:             material.lotCode,
    productName:         material.productName,
    manufactureDate:     material.manufactureDate,
    productionEquipment: material.productionEquipment,
    productionMold:      material.productionMold,
    lotProductionQty:    material.lotProductionQty,
    okCount:             0,
    ngCount:             0,
    injectionConditions: []
  })
})

// ── 시작 ──────────────────────────────────────────────────────────────────────

const PORT = 3000
server.listen(PORT, () => {
  console.log('')
  console.log('  wizMES Mock API  ->  http://localhost:' + PORT)
  console.log('  [Auth]')
  console.log('    POST /auth/login              (admin / 1234)')
  console.log('    POST /auth/refresh')
  console.log('  [Marking]')
  console.log('    GET  /api/marking/materials/:materialBarcode')
  console.log('    POST /api/marking/issue-results')
  console.log('    GET  /api/marking/lots?materialBarcode=... | ?lotCode=...')
  console.log('    GET  /api/marking/production-lots/:lotCode')
  console.log('')
})
