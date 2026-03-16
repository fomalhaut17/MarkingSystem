# 만텍 각인 시스템 (Marking System)

## 프로젝트 개요
LS XBC-DN64H PLC와 통신하여 Lot 바코드를 각인하는 WPF 데스크탑 앱.
고객사 내부 사용 목적 (외부 판매 아님).

## 기술 스택
- .NET 8.0-windows, WPF, C#, MVVM (외부 라이브러리 없이 직접 구현)
- SQLite (Microsoft.Data.Sqlite), NanumSquare 폰트 임베드
- 배포: `dotnet publish -c Release` → 단일 exe (SelfContained, win-x64)

## 개발 실행
```
run-dev.bat
```
- Mock API 서버 (HTTP :3000) + Mock PLC 서버 (TCP :2004) 를 먼저 띄운 뒤 앱 실행
- Mock API: `mock-api/server.js` (json-server)
- Mock PLC: `mock-plc/server.js` (Node.js TCP, 의존성 없음)

## 외부 연동 전환 방법
| 항목 | 파일 | 변경 위치 |
|---|---|---|
| wizMES API URL | `appsettings.json` | `Api.BaseUrl` |
| 인증 API URL | `appsettings.json` | `Api.AuthBaseUrl` |
| PLC 통신 방식 | `appsettings.json` | `Plc.Mode` (`Tcp` / `Serial`) |
| PLC TCP 주소 | `appsettings.json` | `Plc.Tcp.Host`, `Plc.Tcp.Port` |
| PLC 시리얼 포트 | `appsettings.json` | `Plc.Serial.PortName`, `BaudRate`, `StationNo` |

## PLC 통신 구조
- **모델**: LS XBC-DN64H (확정, 2026-03-16), 이더넷 미지원 → RS-232 필요
- **구현체 선택**: `appsettings.json`의 `Plc.Mode` 값으로 런타임 분기 (`PlcClientFactory`)
  - `"Tcp"` (기본값) → `XgtRawPlcClient` — FENET 프로토콜, Mock PLC / 이더넷 모듈 장착 PLC
  - `"Serial"` → `CnetSerialPlcClient` — XGT Cnet 프로토콜, 실 PLC RS-232 직접 연결
- **인터페이스**: `IPlcClient` — Connect/Disconnect/WriteLotBarcode/WriteStart/WriteStop/ReadStatus
- **메모리 맵** (두 구현체 공통):
  - `%MW100~%MW114` — Lot 바코드 (15 word = 30 bytes ASCII)
  - `%MW116` — 명령 (0=None, 1=Start, 2=Stop)
  - `%MW117` — 상태 (0=Idle, 1=Marking, 2=DoneOK, 3=DoneNG, 9=Error)
- **각인 흐름**: `WriteLotBarcode → WriteStart → Poll(%MW117) → ClearCommand`
- **Cnet 참조 문서**: `사용설명서_XGB Cnet_국문_V2.3_20251124.pdf` §7 (프레임 구조 + 명령어 상세)

## PLC 라이브러리 채택 이력 (변경 전 확인)
- **HslCommunication** — 상업적 내부 사용도 유료 가능성 → 제외
- **XGCommLib** — 32비트 COM, win-x64 SingleFile과 충돌 → 제외
- **현재**: 직접 구현 2종 유지 (`XgtRawPlcClient` TCP + `CnetSerialPlcClient` RS-232)

## 코드 규칙
- MVVM: ViewModel은 View를 직접 참조하지 않음. 팝업은 이벤트로 요청
- 비동기: DB/API/PLC 호출은 모두 async. UI 이벤트 핸들러는 async void 허용
- 스타일: 모든 색상·폰트는 `FluentLight.xaml` 리소스 키 사용 (하드코딩 금지)
- 날짜 형식: `"yyyy년 M월 d일"` (M, d 앞에 0 없음)

## 알려진 XAML 주의사항
- `DataTemplate.Triggers`는 DataTemplate 직접 자식으로만 (MC3015)
- `Style` 속성: 어트리뷰트와 `<Element.Style>` 태그 중 하나만 사용 (MC3024)
- ScrollViewer 안에서 `*` 열 → 0px가 됨, 고정 픽셀로 대체
- `Window.Resources` 외부 Dict 병합 시 `<ResourceDictionary.MergedDictionaries>` 필수
- `.bat` echo에 한글 사용 시 인코딩 깨짐 → 영문만 사용
