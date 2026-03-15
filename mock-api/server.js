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
  console.log('  ✔  wizMES Mock API  →  http://localhost:' + PORT)
  console.log('  엔드포인트:')
  console.log('    GET  /api/marking/materials/:materialBarcode')
  console.log('    POST /api/marking/issue-results')
  console.log('    GET  /api/marking/lots?materialBarcode=... | ?lotCode=...')
  console.log('    GET  /api/marking/production-lots/:lotCode')
  console.log('')
})
