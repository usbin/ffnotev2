<!-- 최종 수정: 2026-04-30 -->
# 개발 노트

## 알려진 제한 사항

- **이미지 비율 조정 없음**: 붙여넣은 이미지는 400×300으로 클램핑하지만 원본 비율 유지 안 됨
- **전체화면 배타 게임 위 표시 불가**: WPF `AllowsTransparency=True`는 윈도우 모드/보더리스에서만 보장
- **캔버스 뷰 상태(pan/zoom) 비저장**: 앱 재시작 시 zoom=1, translate=0으로 초기화 — 노트가 음수 좌표에 있으면 안 보일 수 있음
- **리사이즈 누적 오차**: `ResizeThumb_DragDelta`가 마지막 프레임 변화량만 누적해 스냅하므로 세션 동안 마우스와 약간 어긋날 수 있음(10px 격자라 체감 미미)

## TODO (다음 작업 후보)

배치/선택 기능 로드맵 (확정 순서):
- [ ] **D. 다중 선택** — 좌클릭 마키 드래그 + Shift+클릭 토글, 일괄 드래그/리사이즈
- [ ] **E. 그룹** — 다중 선택 후 Ctrl+G 또는 우클릭 메뉴, 완전 내포 판정, 그룹 드래그 시 내부 동기 이동, DB 모델
- [ ] **C. 자동 밀집** — 가변 크기 노트 packing (shelf/skyline 검토). 최상위 그룹/그룹 단위 정렬
- [ ] **B. 일괄 스냅** — 좌상단 floor + 사이즈 ceil + 2D 충돌 해결 (알고리즘 검토 필요)

기타:
- [ ] 캔버스 뷰 상태(pan/zoom) 노트북별 DB 저장/복원
- [ ] 노트 색상 변경 기능
- [ ] 노트북 드래그 정렬 (사이드바)
- [ ] 노트 검색 / 필터
- [ ] "여기로 이동" — 화면 밖 노트로 뷰 이동
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
- [x] **캔버스 Pan/Zoom (무한 캔버스)** — RenderTransform(Scale+Translate). 휠클릭 즉시 팬, 우클릭 4px 후 팬, Ctrl+휠 마우스 앵커 줌(0.2~4.0), 줌 % 인디케이터 + 뷰 초기화. `PreviewMouseWheel`로 자식 ScrollViewer 우선 처리 회피
- [x] **사이드바 접기/펼치기 토글** (◀/▶ 버튼)
- [x] **새 노트 자동 편집 진입** — `NoteItem.IsEditing` 임시 플래그 + `DraggableNoteControl.UserControl_Loaded`에서 자동 `BeginEdit()`
- [x] **키 입력 즉시 DB 저장** — `UpdateSourceTrigger=PropertyChanged` + `MainViewModel`이 노트 `PropertyChanged` 구독해 `Content` 변경 시 즉시 저장 (앱 종료 시 손실 방지)
- [x] **10px 격자 스냅 (노트북별 토글, 기본 OFF)** — `MainViewModel.GridSize=10` + `Snap()`/`MaybeSnap()`. `NoteBook.SnapEnabled` 컬럼 + DB 마이그레이션. 우하단 ⊞ 토글 버튼 (TwoWay 바인딩, 변경 시 즉시 DB 영속화)
- [x] **WPF 포커스 사각형 제거** — ItemsControl/ItemContainer/CanvasArea의 `FocusVisualStyle="{x:Null}"`

## 설계 결정

- **이미지를 DB BLOB 대신 파일로 저장**: 스크린샷은 수백 KB~수 MB로 SQLite 성능에 영향을 줄 수 있어 파일 시스템 저장 선택
- **Canvas.Left/Top을 NoteItem.X/Y에 직접 바인딩**: ViewModel에서 X/Y를 ObservableProperty로 선언해 드래그 중 레이아웃이 즉시 반응하도록 설계 (드래그 시 `Canvas.SetLeft`/`SetTop` 직접 호출 금지 — 바인딩과 충돌)
- **타이틀바만 드래그 가능**: 텍스트 노트 내용을 선택·복사할 수 있어야 하므로 콘텐츠 영역은 드래그에서 제외
- **`LibraryImport` 사용**: .NET 10에서 P/Invoke 표준. `DllImport`보다 trim/AOT 친화적이고 빌드 경고 없음. 단, `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` 필요
- **`ShutdownMode=OnExplicitShutdown` + 닫기 가로채기**: 메인 창을 닫아도 앱이 살아있어야 트레이/단축키가 유지됨. `MainWindow.OnClosing`에서 `e.Cancel = true; Hide()`
- **노트 텍스트는 TextBlock(표시) ↔ TextBox(편집) 스왑**: WPF `TextBox.IsReadOnly` 토글 시 IMM 컨텍스트가 분리된 채 재연결되지 않아 한글이 자모 분리되는 알려진 버그. 표시·편집을 별도 컨트롤로 분리해 IsReadOnly 전환 자체를 제거
- **글로벌 단축키 사용자 설정**: `%APPDATA%\ffnotev2\settings.json`에 저장. 트레이 메뉴 → "단축키 설정" → 캡처 박스에서 키 조합 입력. 저장 시 `MainWindow.ReregisterHotkeys()`가 `HotkeyService.UnregisterAll()` 후 재등록
- **오버레이 초안 자동 저장**: `OverlayViewModel.OnQuickNoteTextChanged` partial 메서드에서 매 글자 변경 시 `SettingsService.Save()`. JSON 작은 파일이라 IO 부담 미미. 필요 시 디바운싱 추가 가능
- **Pan/Zoom은 `ItemsControl.RenderTransform`에 적용**: ItemsPanel(Canvas)가 아닌 ItemsControl 자체에 적용. `Canvas.ClipToBounds=False`로 노트가 영역 밖에서도 렌더, 부모 `CanvasArea.ClipToBounds=True`로 column 경계에서 자른다. `e.GetPosition(canvas)`는 RenderTransform과 무관하게 캔버스 로컬(world) 좌표를 반환하므로 드래그 로직은 그대로 동작
- **우클릭 팬 vs 컨텍스트 메뉴**: 4px 임계값으로 구분. 종료 처리는 `Canvas_MouseRightButtonUp`(`MouseUp`보다 먼저 발생할 수 있음)에서 직접 수행하고 `e.Handled=true`로 메뉴 억제. `LostMouseCapture`로 안전망
- **Ctrl+휠은 `PreviewMouseWheel`(터널)**: 노트 내부 `ScrollViewer`가 wheel을 소비하기 전에 가로채야 안정적
- **키 입력 단위 즉시 DB 저장**: `UpdateSourceTrigger=PropertyChanged` + 노트 `PropertyChanged` 구독. SQLite 단일 row UPDATE는 ms 미만이라 키 입력 빈도에서도 충분. 트레이 종료/창 닫기로 LostFocus가 안 떨어지는 시나리오에서도 손실 없음
- **10px 격자 스냅**: `MainViewModel.Snap()` 한 곳에 모음. 시각적 격자 배경 없이 동작만 적용 (사용자 결정). 드래그 중 매 프레임 스냅으로 사용자가 즉시 격자를 체감
