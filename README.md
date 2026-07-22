# TileCLI

윈도우 데스크톱에 떠 있는 **터미널 창들을 골라 한 번에 자동 정렬**하는 단일 포터블 유틸리티.
tmux/wmux 같은 "한 창 안 패널 분할"이 아니라 **실제 OS 창을 이동·리사이즈**해서 화면에 타일링한다.
Claude Code·GPT CLI를 여러 개 띄워 쓰는 작업 환경을 한 번에 정돈하는 데 특화됐다.

C# / .NET 9 / WinForms · 자체포함 단일 exe(win-x64).

---

## 주요 기능

### 창 탐지 · 분류
- **자동 탐지 + 수동 체크**: 실행 중인 터미널(Windows Terminal·cmd·PowerShell·Git Bash 등)을 콘솔 창 휴리스틱으로 폭넓게 탐지해 목록으로 보여주고, 체크박스로 정렬 대상을 고른다(아무것도 체크 안 하면 전체 대상).
- **Claude / GPT / 서버 구분**: 각 터미널이 Claude(`claude`)·GPT CLI(`codex` 등)·일반 콘솔 중 무엇인지 "종류" 열에 표시(Claude=주황, GPT=초록 강조). 2단계 판별 —
  1. **창 제목 접두 마커**(창별 정확): 제목이 `✳`로 시작→Claude, `codex`/`gpt`로 시작→GPT. Windows Terminal은 모든 창이 **한 프로세스**라 프로세스 트리로는 창별 구분이 안 되므로 제목으로 판별한다.
  2. **프로세스 트리**(폴백): 제목에 마커가 없으면 창의 프로세스 자손에 `claude`/`codex`가 있는지로 판별(cmd/conhost 등 프로세스 분리 창에 정확).
  - 마커/프로세스명은 `config.env`(`ClaudeTitleMarkers`·`GptTitleMarkers`·`GptProcessNames`)에서 조정 가능.
- **드래그 순서 변경**: 목록에서 창을 드래그해 배치 순서를 바꾼다(위→아래 순서대로 타일 배치).

### 정렬 · 타일링
- **⚡ 자동 배치**: 대상 개수·영역 가로세로 비율에 맞는 방향(가로/세로/그리드)을 자동 선택해 정렬.
- **방향 정렬**: `가로 분할`(좌우 N열) / `세로 분할`(상하 N행) / `그리드`(균형 격자). 칸 수는 대상 창 개수에 맞춰 자동 산출.
- **딱 붙는 타일(틈 제거)**: 항상 간격 0으로 작업영역을 꽉 채운다. 인접 창의 내부 경계를 1px 겹쳐 직선 경계의 틈을 없애고, DWM 확장 프레임을 보정해 보이는 테두리를 밀착한다. (참고: 타일 창 모서리 각지게 시도는 cmd/conhost엔 적용되지만 **Windows Terminal은 자체 렌더링으로 무시**해 둥근 모서리를 유지 — WT 한계.)
- **인접 창 연동(붙여서 조정)**: 타일 배치 후 창들이 공유 경계로 묶인다. 한 창의 경계를 드래그하면 **드래그 도중 실시간으로** 맞닿은 옆 창들이 같은 경계선을 따라 함께 조정돼 빈틈·겹침 없이 붙어 있는다(Windows 스냅 그룹처럼). 정렬 그룹 체크박스로 on/off(기본 on).
- **일괄 제어**: `전체 최소화` / `전체 다시 보이기` / `정렬 직전 복원`(정렬 취소 — 각 창의 정렬 직전 위치·크기로 무손실 복귀).

### 멀티 모니터
- **실제 배치 맵**: 연결된 모니터를 실제 배열(가로·세로 스택)대로 정사각형 박스로 표시하고 클릭해 대상 모니터를 고른다.
- **Windows 번호 일치**: 박스 번호는 Windows 디스플레이 설정의 식별 번호(`\\.\DISPLAYN`의 N)와 동일. 주 모니터는 "주" 표시.
- **걸쳐 배치**: 여러 모니터를 하나의 영역으로 걸쳐(`모니터 걸침` 체크) 배치. 꺼져 있으면 한 개만 선택(라디오처럼).

### 작업 세트(창 배치 저장/복원)
- 현재 열린 터미널들의 **위치·크기·모니터를 통째로 저장**하고 **원클릭으로 복원**한다.
- 저장 시 열려 있던 Claude 창을 최근 세션(cwd)과 연결해 저장한다.
- **적용(복원)**: 열린 창은 저장 위치로 이동하고, **창이 닫혀 있던 Claude 세션은 `claude -c`로 재실행**한 뒤 새 터미널이 뜨는 대로 저장 위치에 자동 배치한다. → "저장 → 다 끄기 → 적용" 시 세션까지 되살아난다.
  - 한계: Windows Terminal 단일 프로세스 특성상 "어느 창=어느 세션" 정확 매칭은 불가라, 세션 집합은 정확하되 위치 대응은 최근성 기준 근사다.
- `worksets.json`(exe 옆)에 저장. 적용 직전 스냅샷을 남겨 `정렬 직전 복원`으로 되돌릴 수 있다.

### 창 자동 배치(감시 모드)
- 터미널 창이 **열리거나 닫히면** 마지막 방향으로 **자동 재배치**한다(정렬 그룹 `창 자동 배치` 체크). 저주파 폴링 방식이라 UI를 막지 않는다.

### 최근 세션 복구/관리
- `~/.claude/projects`를 읽어 Claude로 실행했던 최근 세션을 프로젝트(cwd) 단위로 표시.
- **복구**(또는 더블클릭): `wt.exe -d <cwd> cmd /k claude -c`로 새 터미널을 띄운다. 상속된 `CLAUDECODE` 등 변수를 제거하고 컬러 힌트를 넣어 색상이 정상 출력된다.
- 우클릭 메뉴: `복구`(이어서 `claude -c`) / `새 세션으로 열기`(`claude`) / `탐색기에서 폴더 열기` / `앱에서 삭제`(목록 숨김, 파일 보존) / `세션 저장폴더 삭제`(`~/.claude/projects/<encoded>`를 **휴지통**으로, 코드 폴더는 안 건드림).
- 목록 다중 선택 지원.

### 편의 · 옵션
- **옵션 창**(톱니 아이콘): 트레이 최소화 · **Windows 시작 시 자동 실행** · 테마(다크/라이트) · 전역 단축키 · 프로파일을 한곳에 모았다.
- **Windows 시작 시 자동 실행**: `HKCU\...\Run`에 현재 exe 경로로 등록. exe 경로가 바뀌면 다음 실행 시 현재 경로로 자동 갱신(관리자 권한 불필요).
- **전역 단축키**: 자동/가로/세로/그리드에 전역 단축키 바인딩. 저장 시 앱 내부 중복·다른 프로그램 충돌 검사. 트레이에 숨겨져 있어도 동작.
- **트레이 최소화**: X를 누르면 트레이로 숨고 작업표시줄에 남지 않는다(완전 종료는 트레이 메뉴). 체크박스로 이 동작을 끄면 X가 즉시 종료.
- **프로파일**: 배치 스타일(방향+모니터)을 이름 붙여 저장하고 다시 적용. `profiles.json`.
- **테마**: 다크 프리미엄(네이비+골드) / 라이트 전환. 손으로 그린 GDI+ 벡터 아이콘(폰트 글리프 미사용), 라운드 버튼(누름 애니), 0.5초 툴팁.

---

## 요구 사항
- Windows 10 / 11 (x64)
- 실행에 별도 설치·런타임 불필요(자체포함 단일 exe)

## 빌드
`.NET 9 SDK` 필요.

```powershell
# 단일 포터블 exe 생성
dotnet publish src/TileCLI/TileCLI.csproj -c Release
# 산출물: src/TileCLI/bin/Release/net9.0-windows/win-x64/publish/TileCLI.exe
```

또는 저장소 루트의 `build.bat` 실행.

## 사용
1. `TileCLI.exe` 실행 → 탐지된 터미널 목록이 뜬다.
2. 정렬할 터미널을 체크(전체 대상이면 체크 안 함). 필요하면 드래그로 순서 변경.
3. 모니터를 클릭해 고르고, 필요하면 `모니터 걸침` 체크.
4. `⚡ 자동 배치` 또는 `가로/세로 분할`·`그리드`로 정렬. `정렬 직전 복원`으로 되돌리기.
5. 배치가 마음에 들면 작업 세트 `저장` → 나중에 `적용`으로 복원(닫힌 Claude 세션은 재실행됨).
6. 옵션에서 자동 실행·단축키·테마·프로파일 설정.

---

## 헤드리스 검증
GUI를 직접 보지 않고 로직/구조를 확인하는 모드:

```powershell
TileCLI.exe --selftest   # 타일링 무겹침·꽉참 + 작업세트 왕복 + 감시자 + 배치결정(Planner) 검증 → PASS/FAIL, selftest-result.txt
TileCLI.exe --uitest     # MainForm 생성·Load/Shown 스모크(예외 없이 뜨는지) → UITEST: OK
TileCLI.exe --uidump     # 컨트롤 트리(그룹·버튼·라벨·위치) + 그룹 내 겹침 검사 텍스트 덤프 → uidump.txt
```

## 설정 파일
모두 exe 옆에 저장(쓰기 불가 시 `%APPDATA%\TileCLI\`로 폴백):
- **`config.env`** (KEY=VALUE, 편집 가능): UI 옵션·최근 상태 — 트레이 최소화·인접 연동·창 자동 배치(감시)·걸쳐 배치, 최근 방향, **선택 모니터(DeviceName)**, 선택 프로파일 + 종류 판별 마커(`GptProcessNames`·`GptTitleMarkers`·`ClaudeTitleMarkers`). 변경/정렬/종료 시 자동 저장, 시작 시 복원.
- **`settings.json`** (JSON): 전역 단축키 바인딩 + 숨긴 세션 목록.
- **`profiles.json`**: 이름 붙인 배치 프로파일.
- **`worksets.json`**: 작업 세트(창 배치 + 세션 연결).
- 자동 실행은 파일이 아니라 레지스트리(`HKCU\Software\Microsoft\Windows\CurrentVersion\Run\TileCLI`).

## 구조
```
src/TileCLI/
  Program.cs               진입점 + --selftest / --uitest / --uidump
  Native/NativeMethods.cs  user32/dwmapi/kernel32 P/Invoke (SetWindowPos·WinEvent 훅·RegisterHotKey·DWM·Toolhelp32)
  Models/                  LayoutDirection · TerminalWindow(+Kind) · TerminalKind · MonitorTarget(+DisplayNumber)
                           · LayoutProfile · WorkSet · HotkeyAction/Binding · AppSettings · ClaudeSession · WindowSnapshot
  Services/                WindowDiscovery · MonitorService · TilingEngine(+AutoDirection·꽉참) · SnapshotStore
                           · BulkController · ProfileStore · WorkSetStore · WorkSetPlanner(배치 결정, 순수 함수)
                           · WindowWatcher(감시 폴링) · ProcessTreeInspector · HotkeyManager · AppSettingsStore
                           · AutoStartService(자동 실행) · ClaudeSessionService(최근 세션)
                           · TileGroupTracker(인접 창 실시간 연동 — WinEvent 훅 + 순수 ComputeReflow) · ConfigEnv
  UI/MainForm.cs           관리 창(목록·모니터·정렬/일괄/작업세트·세션·트레이·단축키)
  UI/OptionsForm.cs        옵션(트레이·자동실행·테마·단축키·프로파일)
  UI/HotkeySettingsForm.cs 전역 단축키 설정(중복·충돌 검사)
  UI/MonitorMapControl.cs  실제 배치 정사각형 모니터 맵(Windows 번호)
  UI/InputDialog.cs        작고 테마 적용된 입력 다이얼로그
  UI/Theme.cs · Icons.cs   다크/라이트 테마 · 손그림 GDI+ 아이콘 · 라운드 버튼
```
