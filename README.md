# 만텍 각인 시스템 — 개발/테스트 실행 가이드

## 사전 조건

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 설치

---

## 빠른 시작 (local 모드 — TCP Mock PLC)

### 방법 A — 콘솔 (bat 파일)

```
run-local.bat
```

Mock 서버(HTTP :3000 + TCP :2004)와 앱이 순서대로 실행됩니다.
Mock 서버 창이 먼저 열리고, 3초 후 앱 창이 열립니다.

### 방법 B — Visual Studio

**최초 1회 — 여러 프로젝트 동시 시작 설정:**

1. Solution Explorer에서 솔루션 우클릭 → **Properties**
2. **Common Properties** → **Startup Project**
3. **Multiple startup projects** 선택
4. `MarkingSystem.Mock`과 `MarkingSystem` 모두 Action을 **Start**로 변경
5. `MarkingSystem.Mock`이 목록 위에 오도록 순서 조정 (↑ 버튼)
6. **OK**

이후: **빌드(Ctrl+Shift+B) 후 F5**

---

### 로그인 정보

| 항목 | 값 |
|---|---|
| 업체코드 | `MANNTEK` |
| ID | `admin` |
| PW | `1234` |

### 테스트용 물류 바코드

| 바코드 | 설명 |
|---|---|
| `12345678901234` | 10개 컨테이너 (P2063121-B1-A Lot) |
| `98765432109876` | 5개 컨테이너 (P2063121-B1-B Lot) |
| `11111111111111` | 10개 컨테이너 (**`12345678901234`와 같은 Lot** — Ser 연속 발행 확인용) |

---

## 테스트 초기화

앱 우측 상단 **테스트 초기화** 버튼을 누르면 발행 이력이 모두 삭제되어
Ser `000001`부터 다시 시작할 수 있습니다.
(local / dev 모드에서만 표시됩니다.)

---

## dev 모드 전환 (Serial PLC 경로 검증 — 선택)

가상 COM 포트(com0com)를 이용해 RS-232 Cnet 프로토콜 코드 경로를 함께 검증합니다.

### 추가 사전 조건

- [com0com](https://sourceforge.net/projects/com0com/) 설치 → 가상 포트 쌍 **COM5 ↔ COM6** 생성

### 전환 절차

1. `MarkingSystem/appsettings.json` → `"AppMode": "dev"`
2. `MarkingSystem.Mock/appsettings.json` → `"AppMode": "dev"`
3. 빌드 (Visual Studio: Ctrl+Shift+B / CLI: `dotnet build`)

**방법 A — 콘솔:**
```
run-dev.bat
```

**방법 B — Visual Studio:** F5

local 모드로 돌아올 때는 두 파일 모두 `"AppMode": "local"`로 되돌린 후 다시 빌드합니다.

---

## 문제 해결

| 증상 | 조치 |
|---|---|
| 앱 실행 직후 API 오류 | Mock 서버가 아직 기동 중. 잠시 후 재시도 |
| PLC 연결 실패 | Mock 서버 창이 닫혀 있음. bat 파일 재실행 또는 F5 재시작 |
| 포트 이미 사용 중 | bat 파일이 자동 처리하지만 실패 시 작업 관리자에서 `MarkingSystem.Mock` 수동 종료 후 재실행 |
| `dotnet` 명령어를 찾을 수 없음 | .NET 8 SDK 설치 여부 확인 (`dotnet --version`) |
| dev 모드인데 Mock이 local로 뜸 | `MarkingSystem.Mock/appsettings.json` 변경 후 빌드했는지 확인 |
