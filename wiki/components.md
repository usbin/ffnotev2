<!-- 최종 수정: 2026-04-30 -->
# 주요 컴포넌트

## 모델

| 파일 | 클래스 | 설명 |
|------|--------|------|
| Models/NoteItem.cs | `NoteItem` | X/Y/Width/Height + Type(Text\|Image\|Link) + Content + `IsEditing`(transient — 새 노트 자동 편집 진입) + `IsSelected`(transient — 다중 선택). `ObservableObject` 상속으로 X/Y/Width/Height 변경 시 Canvas 자동 이동/리사이즈, `Content` 변경은 `MainViewModel`이 구독해 즉시 DB 저장 |
| Models/NoteBook.cs | `NoteBook` | Name, ProcessName, Notes/Groups 컬렉션, `SnapEnabled`(격자 스냅 토글, DB 영속), `OverlayDraft`(오버레이 초안, DB 영속, 키 입력마다 저장). `ObservableProperty Name/ProcessName/SnapEnabled/OverlayDraft` |
| Models/NoteGroup.cs | `NoteGroup` | 단순 사각형(X/Y/Width/Height) + `IsSelected`(transient). 멤버십은 동적 — bbox 완전 내포로 판정. 드래그 시작 시점 스냅샷으로 멤버 동기 이동 |
| Models/AppSettings.cs | `AppSettings` | `ShowMain`, `ToggleOverlay`, `ToggleClickThrough`(글로벌), `NotebookSwitches[10]`(노트북 1~10 전환, 기본 Ctrl+1~Ctrl+0) + `OverlayOpacity` + `AutoStartOnLogin` + `OverlayLeft`/`OverlayTop`. (오버레이 초안은 노트북별로 분리되어 `NoteBook.OverlayDraft`로 이동) |
| Models/HotkeyBinding.cs | `HotkeyBinding` | Modifiers + VirtualKey + `DisplayString` ("Ctrl+Alt+Z") + `MatchesLocal(KeyEventArgs)` (NoRepeat 무시한 로컬 키 매칭) |

## 서비스

| 파일 | 클래스 | 핵심 메서드 |
|------|--------|-------------|
| Services/DatabaseService.cs | `DatabaseService` | `LoadAllNotebooks()`, `SaveNoteItem()`, `UpdateNotePosition()`, `DeleteNoteItem()`, `SetNotebookSnapEnabled()`, `SetNotebookOverlayDraft()`, `AddGroup()`/`UpdateGroup()`/`DeleteGroup()`. `InitializeSchema()`에 `NoteGroups` 테이블 + `Notebooks.SnapEnabled`/`OverlayDraft` 컬럼 ALTER 마이그레이션 + `ColumnExists` 헬퍼 |
| Services/GameDetectionService.cs | `GameDetectionService` | `Start()` — 3초 폴링; `GetRunningWindowedProcesses()` — 창 있는 프로세스 목록 반환 |
| Services/HotkeyService.cs | `HotkeyService` | `Initialize(Window)`, `Register(modifiers, vk, callback)`, `UnregisterAll()` — Win32 RegisterHotKey 래퍼 (LibraryImport source-generated P/Invoke) |
| Services/SettingsService.cs | `SettingsService` | `%APPDATA%\ffnotev2\settings.json` 로드/저장. `AppSettings` 노출 (HotkeyBinding 3개) |
| Services/AutoStartService.cs | `AutoStartService` | `HKCU\...\Run\ffnote` 레지스트리 R/W. `Enable()`/`Disable()`/`IsEnabled` |
| Services/UndoService.cs | `UndoService` + 액션 클래스 | LinkedList 기반 LIFO Undo/Redo (max 200). 액션: `TransformItemsAction`(이동/리사이즈/그룹 동기 이동, NoteItem+NoteGroup 혼합), `AddNoteAction`/`DeleteNoteAction`, `AddGroupAction`/`DeleteGroupAction`. `App.Undo`로 전역 접근 |

## 뷰모델

| 파일 | 클래스 | 역할 |
|------|--------|------|
| ViewModels/MainViewModel.cs | `MainViewModel` | Notebooks 관리, `AddTextNote(text, x, y, autoEdit=true)`/`AddImageNote/AddLinkNote`(생성 좌표 조건부 스냅 — 붙여넣기는 `autoEdit:false`로 비편집 + `SelectOnly`), `DeleteNote`, `UpdateNotePosition`, `UpdateNoteContent`, `PasteFromClipboard` (이미지·링크·텍스트 자동 분기), 노트 `PropertyChanged` 구독으로 `Content` 변경 시 즉시 DB 저장, NoteBook `PropertyChanged` 구독으로 `SnapEnabled` 변경 시 DB 저장. 다중 선택 헬퍼: `SelectedNotes`/`SelectedGroups`/`ClearSelection()`/`SelectOnly(NoteItem)`/`SelectOnlyGroup(NoteGroup)`. 그룹 관리: `CreateGroupFromSelectedNotes(padding)`/`DeleteGroup`/`UpdateGroupPosition`/`GetMembersOf(group)`(완전 내포 노트·그룹 반환). `BulkSnap()` — 전체 노트 좌상단 floor + 사이즈 ceil + 우/하 충돌 해결. 방향 이동: `FindNeighborNote`. `GridSize=10` 상수 + `Snap(double)` 정적 헬퍼 + `MaybeSnap(double)` 인스턴스 메서드 |
| ViewModels/OverlayViewModel.cs | `OverlayViewModel` | `QuickNoteText`(현재 노트북의 `OverlayDraft`에 양방향 연결 — MainVM이 PropertyChanged 구독해 DB에 키 입력마다 저장), 노트북 전환 시 새 노트북의 초안을 단방향 로드(저장 트리거 억제), `Submit()` → MainViewModel.AddTextNote 후 초안 비움 |

## 컨트롤·창

| 파일 | 설명 |
|------|------|
| Controls/DraggableNoteControl | 타이틀바 드래그로 이동(조건부 격자 스냅, 선택된 모든 노트 동기 이동), 4 외곽(상/하/좌/우 5px) + 4 코너(10×10 투명, 대각선) Thumb로 리사이즈(누적 델타 + 조건부 스냅, 좌/상은 X/Y와 W/H 동시 변경, 선택된 모든 노트 동기 변경, min 80×40, 단일 핸들러가 `sender.Tag`(`Top/Bottom/Left/Right/TopLeft/TopRight/BottomLeft/BottomRight`)로 분기), 타입별 내용 표시(텍스트는 TextBlock↔TextBox 스왑/이미지/Hyperlink), 더블클릭 또는 자동(`IsEditing=true`)으로 텍스트 편집 진입, DataContext PropertyChanged 구독으로 외부 IsEditing 변화 감지(방향키 이동 시 BeginEdit 트리거), TextBox는 `UpdateSourceTrigger=PropertyChanged`로 키 입력 즉시 `Content` 갱신, `PreviewMouseLeftButtonDown`에서 클릭 소스가 TextBox가 아니면 `MainWindow.FocusCanvas()`로 다른 노트의 LostFocus 트리거(커서 잔존 방지) + Shift 토글/단일 선택 처리, `IsSelected`에 따라 외곽 Border 파란 테두리(DataTrigger). 자체 우클릭 핸들러 없음 — 우클릭은 캔버스로 버블링되어 통합 메뉴에서 "삭제하기" 항목 추가. 헤더 × 버튼 없음 |
| Controls/GroupBoxControl | 노트 형태 — 점선 외곽(`IsHitTestVisible=False`, 시각 표시만) + 상단 22px 헤더(드래그) + 4 엣지(5px 투명) + 4 코너(10×10 투명, 대각선) Thumb. 본문은 hit-test 통과해 멤버 노트 클릭 보존. 헤더 드래그: `GetMembersOf` 스냅샷으로 그룹·멤버 모두 같은 Δ 이동(조건부 스냅), Up 시 DB 저장 + Undo 기록. 엣지/코너 리사이즈: 그룹 bbox만 변경(멤버 이동 없음 — 멤버십은 동적), min 60×40, Mouse.GetPosition(canvas) 안정 좌표. 자체 우클릭 핸들러 없음 — 캔버스 통합 메뉴에서 "그룹 해제" 추가 |
| MainWindow | 전체 레이아웃 (토글 가능 사이드바 + Pan/Zoom 캔버스), 사이드바 ◀/▶ 토글, Ctrl+V 붙여넣기(world 변환), **통합 우클릭 컨텍스트 메뉴** (`Canvas_MouseRightButtonUp`): 모든 우클릭이 여기로 버블링 → 항상 "새 텍스트 노트", 선택된 노트/그룹 있으면 "그룹 만들기/해제 (N개)", 우클릭 대상이 노트면 "삭제하기", 비선택 그룹이면 "그룹 해제" 추가. 우클릭 드래그는 노트/그룹 위에서도 팬 — `_rightClickOrigin`을 down 시 저장해 캔버스 캡처 후에도 메뉴 대상 식별 가능. `_panMoved=true`면 메뉴 억제. 휠클릭/우클릭 드래그 → 팬, **빈 캔버스 좌클릭 드래그 → 마키 선택** (인터섹트, 노트+그룹 모두 선택), 단순 좌클릭/Esc → 선택 해제, **Ctrl+G 그룹 만들기 / Ctrl+Shift+G 그룹 해제**, **Delete → 선택 노트/그룹 모두 삭제**, **Alt+화살표(편집 중) 또는 화살표(비편집·단일 선택) → 인접 노트 이동**, Ctrl+휠 → 마우스 앵커 줌(0.2x~4.0x), 캔버스 콘텐츠는 `Grid CanvasContent` 하나로 묶여 RenderTransform 공유 — 그룹 ItemsControl(뒤) + 노트 ItemsControl(앞, **`Canvas Background="{x:Null}"` 필수** — 뒤 그룹 hit-test 통과용), 우하단 인디케이터: 일괄 정렬 ⊟ + 격자 스냅 토글(노트북별) ⊞ + 줌 % + 뷰 초기화, 글로벌 단축키 등록, 닫기 시 트레이로 숨김. `IsOriginInsideNote`는 `DraggableNoteControl`+`GroupBoxControl` 모두 검사(마키/팬 가드) |
| OverlayWindow | Topmost 반투명 창, 멀티라인 입력 (Shift+Enter 줄바꿈, Enter 제출), Esc 숨기기, 드래그 이동, 우클릭 → 투명도 ±10%/기본값, 클릭 패스스루 토글 |
| Dialogs/GamePickerDialog | 실행 중 프로세스 목록 표시, 더블클릭 또는 선택 버튼으로 연동 |
| Dialogs/RenameDialog | 노트북 이름 변경 입력창 |
| Dialogs/GroupDeleteDialog | 그룹 삭제 시 멤버 노트 처리 선택 — `Choice` ∈ `Cancel/DeleteAll/GroupOnly`. `MainWindow.DeleteGroupsWithPrompt`가 멤버 0건이면 미호출, 멤버 있으면 호출 |
| Dialogs/HotkeySettingsDialog | 글로벌 단축키 3개 + 노트북 전환 10개 캡처/저장. ItemsControl로 노트북 슬롯 동적 렌더, 고정 단축키(Ctrl+화살표/Alt+화살표/Ctrl+G/Esc/Enter/마키/팬/줌 등)는 참고 섹션으로 노출. ScrollViewer로 길이 증가 대응 |

## 컨버터

| 파일 | 설명 |
|------|------|
| Converters/NoteTypeToVisibilityConverter | `NoteType` enum → `Visibility` (XAML `ConverterParameter`로 매칭 타입 지정) |
| Converters/PathToImageConverter | 파일 경로 → `BitmapImage` (`BitmapCacheOption.OnLoad`로 파일 잠금 해제) |
| Converters/InverseBooleanToVisibilityConverter | bool → `Visibility` 반전 (`HasCurrentNotebook` 빈 화면 안내용) |

## 단축키

### 사용자 변경 가능 (트레이 → 단축키 설정)

**글로벌**: `Ctrl+Shift+N` 메인 창 / `Ctrl+Shift+M` 오버레이 / `Ctrl+Alt+Z` 클릭 패스스루
**노트북 전환**: `Ctrl+1` ~ `Ctrl+9`, `Ctrl+0` → Notebooks[0..9] (해당 인덱스 없으면 무시)

### 기본 단축키 (변경 불가)

- 노트 이동: `Ctrl+화살표` 1px / `Ctrl+Shift+화살표` 격자 정렬 후 10px / `화살표` 인접 노트로 선택 이동
- 편집: `Enter` 편집 시작 / `Esc` 편집/선택 종료 / `Alt+화살표`(편집 중) 인접 노트로 편집 이동 / 더블클릭
- 그룹: `Ctrl+G` 만들기 / `Ctrl+Shift+G` 해제
- 선택/삭제: 빈 캔버스 좌클릭 드래그(마키) / `Shift+클릭` 토글 / `Delete` 선택 노트·그룹 삭제 / 노트·그룹 우클릭 → 통합 메뉴(삭제하기/그룹 해제)
- 클립보드: `Ctrl+C` 선택 노트 복사 (Text/Link → SetText, Image → SetImage) / `Ctrl+V` 붙여넣기
- 실행 취소: `Ctrl+Z` Undo / `Ctrl+Y` 또는 `Ctrl+Shift+Z` Redo
- 캔버스: 휠클릭/우클릭 드래그(팬) / `Ctrl+휠`(줌)
