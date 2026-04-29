<!-- 최종 수정: 2026-04-29 -->
# 개발 노트

## 알려진 제한 사항

- **이미지 비율 조정 없음**: 붙여넣은 이미지는 400×300으로 클램핑하지만 원본 비율 유지 안 됨
- **Canvas 무한 영역 없음**: 노트가 메인 창 가시 영역 밖으로 나가면 접근 불가 (현재 캔버스는 메인 창 크기에 고정)
- **전체화면 배타 게임 위 표시 불가**: WPF `AllowsTransparency=True`는 윈도우 모드/보더리스에서만 보장
- **오버레이 위치 비저장**: 앱 재시작 시 좌상단(40, 40)으로 초기화

## TODO (다음 작업 후보)

- [ ] **Canvas 무한 영역 / 줌 / 팬** ← 다음 작업 예정. 아래 "캔버스 무한 확장 옵션"의 3번(Pan/Zoom RenderTransform) 방식
- [ ] 노트 색상 변경 기능
- [ ] 노트북 드래그 정렬 (사이드바)
- [ ] 노트 검색 / 필터
- [ ] 백업·Export·Import (SQLite + 이미지 zip)
- [ ] 트레이 아이콘 커스텀 (현재 `SystemIcons.Application`)
- [ ] 단일 인스턴스 보장 (Mutex)
- [ ] DPI 스케일 변경 시 오버레이 위치 보정 (현재는 픽셀 단위 저장만)
- [ ] 다국어

## 완료된 작업 (참조)

- [x] 노트 리사이즈 핸들 (Thumb, 우하단)
- [x] 클립보드 이미지 견고화 (PNG/DIB/Bitmap fallback)
- [x] 오버레이 멀티라인 + 자동 축소
- [x] 오버레이 클릭 패스스루 토글 (Ctrl+Alt+Z 기본)
- [x] 오버레이 투명도 우클릭 조정 + 자동 저장
- [x] 오버레이 초안(QuickNoteText) 자동 저장
- [x] 오버레이 위치 자동 저장/복원 (LocationChanged 디바운스 500ms, 화면 밖 Clamp)
- [x] 글로벌 단축키 사용자 설정 다이얼로그 + JSON 영속화
- [x] Windows 시작 시 자동 실행 토글 (HKCU\...\Run, AutoStartService)
- [x] 한글 IME 버그 — TextBlock/TextBox 스왑으로 회피
- [x] 노트북 이름 변경이 오버레이 표시에 즉시 반영

## 캔버스 무한 확장 옵션 (TODO 상세)

현재: `MainWindow`의 우측 Grid에 `ItemsControl`(`ItemsPanelTemplate=Canvas`) 직접 배치 — Canvas 크기가 부모 Grid 크기와 같음. 노트 좌표가 가시 영역 밖이면 보이지 않음.

세 가지 접근:

1. **ScrollViewer + 큰 고정 Canvas** (가장 단순)
   - `ScrollViewer`로 감싸고 Canvas의 Width/Height를 충분히 크게 (예 5000×5000)
   - 자동으로 가로/세로 스크롤바
   - 단점: 무한이 아니라 큰 유한 / 빈 영역이 보임

2. **동적 확장 Canvas**
   - 노트 추가/이동 시 노트 좌표 최대값 + 여유분으로 Canvas 크기 자동 확장
   - 1번과 비슷하지만 빈 영역 최소화

3. **Pan/Zoom (RenderTransform)** — Figma/draw.io/Excalidraw 스타일
   - Canvas에 `MatrixTransform` 적용
   - 마우스 휠 → 줌 (커서 위치 기준 확대/축소)
   - 우클릭 또는 스페이스+드래그 → 팬
   - 사실상 무한 영역 (변환 행렬만 갱신)
   - 노트 좌표는 "월드 좌표" 그대로 보존, 화면 좌표는 변환 후

권장: **3번 (Pan/Zoom)**. 게임 기록 노트는 영역이 넓어질 수 있어 자유로운 시점이 유용하고, 1·2번보다 게임 도구처럼 자연스러움. 구현 비용은 약간 높지만 라이브러리 없이 200줄 정도면 가능.

## 설계 결정

- **이미지를 DB BLOB 대신 파일로 저장**: 스크린샷은 수백 KB~수 MB로 SQLite 성능에 영향을 줄 수 있어 파일 시스템 저장 선택
- **Canvas.Left/Top을 NoteItem.X/Y에 직접 바인딩**: ViewModel에서 X/Y를 ObservableProperty로 선언해 드래그 중 레이아웃이 즉시 반응하도록 설계 (드래그 시 `Canvas.SetLeft`/`SetTop` 직접 호출 금지 — 바인딩과 충돌)
- **타이틀바만 드래그 가능**: 텍스트 노트 내용을 선택·복사할 수 있어야 하므로 콘텐츠 영역은 드래그에서 제외
- **`LibraryImport` 사용**: .NET 10에서 P/Invoke 표준. `DllImport`보다 trim/AOT 친화적이고 빌드 경고 없음. 단, `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` 필요
- **`ShutdownMode=OnExplicitShutdown` + 닫기 가로채기**: 메인 창을 닫아도 앱이 살아있어야 트레이/단축키가 유지됨. `MainWindow.OnClosing`에서 `e.Cancel = true; Hide()`
- **노트 텍스트는 TextBlock(표시) ↔ TextBox(편집) 스왑**: WPF `TextBox.IsReadOnly` 토글 시 IMM 컨텍스트가 분리된 채 재연결되지 않아 한글이 자모 분리되는 알려진 버그. 표시·편집을 별도 컨트롤로 분리해 IsReadOnly 전환 자체를 제거
- **글로벌 단축키 사용자 설정**: `%APPDATA%\ffnotev2\settings.json`에 저장. 트레이 메뉴 → "단축키 설정" → 캡처 박스에서 키 조합 입력. 저장 시 `MainWindow.ReregisterHotkeys()`가 `HotkeyService.UnregisterAll()` 후 재등록
- **오버레이 초안 자동 저장**: `OverlayViewModel.OnQuickNoteTextChanged` partial 메서드에서 매 글자 변경 시 `SettingsService.Save()`. JSON 작은 파일이라 IO 부담 미미. 필요 시 디바운싱 추가 가능
