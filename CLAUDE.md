# 만텍 각인 시스템 (Marking System)

## 프로젝트 개요
LS XGT PLC와 TCP 통신하여 Lot 바코드를 각인하는 WPF 데스크탑 앱.
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
| wizMES API | `MainViewModel.cs` | `ApiBaseUrl` 상수 |
| PLC IP | `MainViewModel.cs` | `PlcHost` 상수 |
| PLC 구현체 | `MainViewModel.cs` | `CreatePlcClient()` 팩토리 메서드 |

## PLC 통신 구조
- 프로토콜: LS XGT FENET, TCP 2004포트, 직접 구현 (`XgtRawPlcClient`)
- 인터페이스: `IPlcClient` — 구현체 교체 대비
- 메모리 맵:
  - `%MW100~%MW114` — Lot 바코드 (15 word = 30 bytes ASCII)
  - `%MW116` — 명령 (0=None, 1=Start, 2=Stop)
  - `%MW117` — 상태 (0=Idle, 1=Marking, 2=DoneOK, 3=DoneNG, 9=Error)
- 각인 흐름: `WriteLotBarcode → WriteStart → Poll(%MW117) → ClearCommand`

## PLC 라이브러리 채택 이력 (변경 전 확인)
- **HslCommunication** — 상업적 내부 사용도 유료 가능성 → 제외
- **XGCommLib** — 32비트 COM, win-x64 SingleFile과 충돌 → 제외
- **현재**: 직접 구현 유지. 실 PLC 최초 연결 시 Wireshark로 응답 프레임 검증 필요

## 실 PLC 연결 시 Wireshark 검증 항목
1. 응답 앞에 커맨드 에코(0x0055) 바이트 유무
2. 에러 코드 크기 (2바이트 vs 4바이트)
3. Write 후 응답 유무 (없으면 타임아웃 → `RecvAppDataAsync` 수정 필요)
4. 헤더 [14] 방향 바이트 (PLC→Host 응답 시 0x11 여부)

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
