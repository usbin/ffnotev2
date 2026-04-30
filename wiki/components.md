<!-- 최종 수정: 2026-04-30 -->
# 주요 컴포넌트

## 모델

| 파일 | 클래스 | 설명 |
|------|--------|------|
| Models/NoteItem.cs | `NoteItem` | X/Y/Width/Height + Type(Text\|Image\|Link) + Content + `IsEditing`(transient — 새 노트 자동 편집 진입) + `IsSelected`(transient — 다중 선택). `ObservableObject` 상속으로 X/Y/Width/Height 변경 시 Canvas 자동 이동/리사이즈, `Content` 변경은 `MainViewModel`이 구독해 즉시 DB 저장 |
| Models/NoteBook.cs | `NoteBook` | Name, ProcessName, Notes/Groups 컬렉션, `SnapEnabled`(노트북별 격자 스냅 토글, DB 영속, 기본 false). `ObservableProperty Name/ProcessName/SnapEnabled` |
| Models/NoteGroup.cs | `NoteGroup` | 단순 사각형(X/Y/Width/Height) + `IsSelected`(transient). 멤버십은 동적 — bbox 완전 내포로 판정. 드래그 시작 시점 스냅샷으로 멤버 동기 이동 |
| Models/AppSettings.cs | `AppSettings` | `ShowMain`, `ToggleOverlay`, `ToggleClickThrough` (HotkeyBinding) + `OverlayOpacity` + `OverlayDraft` + `AutoStartOnLogin` + `OverlayLeft`/`OverlayTop` |
| Models/HotkeyBinding.cs | `HotkeyBinding` | Modifiers + VirtualKey + `DisplayString` ("Ctrl+Alt+Z") |

## 서비스

| 파일 | 클래스 | 핵심 메서드 |
|------|--------|-------------|
| Services/DatabaseService.cs | `DatabaseService` | `LoadAllNotebooks()`, `SaveNoteItem()`, `UpdateNotePosition()`, `DeleteNoteItem()`, `SetNotebookSnapEnabled()`, `AddGroup()`/`UpdateGroup()`/`DeleteGroup()`. `InitializeSchema()`에 `NoteGroups` 테이블 + `Notebooks.SnapEnabled` 컬럼 ALTER 마이그레이션 + `ColumnExists` 헬퍼 |
| Services/GameDetectionService.cs | `GameDetectionService` | `Start()` — 3초 폴링; `GetRunningWindowedProcesses()` — 창 있는 프로세스 목록 반환 |
| Services/HotkeyService.cs | `HotkeyService` | `Initialize(Window)`, `Register(modifiers, vk, callback)`, `UnregisterAll()` — Win32 RegisterHotKey 래퍼 (LibraryImport source-generated P/Invoke) |
| Services/SettingsService.cs | `SettingsService` | `%APPDATA%\ffnotev2\settings.json` 로드/저장. `AppSettings` 노출 (HotkeyBinding 3개) |
| Services/AutoStartService.cs | `AutoStartService` | `HKCU\...\Run\ffnote` 레지스트리 R/W. `Enable()`/`Disable()`/`IsEnabled` |

## 뷰모델

| 파일 | 클래스 | 역할 |
|------|--------|------|
| ViewModels/MainViewModel.cs | `MainViewModel` | Notebooks 관리, `AddTextNote/AddImageNote/AddLinkNote`(생성 좌표 조건부 스냅), `DeleteNote`, `UpdateNotePosition`, `UpdateNoteContent`, `PasteFromClipboard` (이미지·링크·텍스트 자동 분기), 노트 `PropertyChanged` 구독으로 `Content` 변경 시 즉시 DB 저장, NoteBook `PropertyChanged` 구독으로 `SnapEnabled` 변경 시 DB 저장. 다중 선택 헬퍼: `SelectedNotes`/`SelectedGroups`/`ClearSelection()`/`SelectOnly(NoteItem)`/`SelectOnlyGroup(NoteGroup)`. 그룹 관리: `CreateGroupFromSelectedNotes(padding)`/`DeleteGroup`/`UpdateGroupPosition`/`GetMembersOf(group)`(완전 내포 노트·그룹 반환). `BulkSnap()` — 전체 노트 좌상단 floor + 사이즈 ceil + 우/하 충돌 해결. 방향 이동: `FindNeighborNote`. `GridSize=10` 상수 + `Snap(double)` 정적 헬퍼 + `MaybeSnap(double)` 인스턴스 메서드 |
| ViewModels/OverlayViewModel.cs | `OverlayViewModel` | `QuickNoteText` (변경 시 settings.json에 자동 저장), `Submit()` → MainViewModel.AddTextNote |

## 컨트롤·창

| 파일 | 설명 |
|------|------|
| Controls/DraggableNoteControl | 타이틀바 드래그로 이동(조건부 격자 스냅, 선택된 모든 노트 동기 이동), 우하단 코너 + 4 외곽(상/하/좌/우) 5px 투명 Thumb로 리사이즈(누적 델타 + 조건부 스냅, 좌/상 엣지는 X/Y와 W/H 동시 변경, 선택된 모든 노트 동기 변경, min 80×40, 단일 핸들러가 `sender.Tag`로 분기), 타입별 내용 표시(텍스트는 TextBlock↔TextBox 스왑/이미지/Hyperlink), 더블클릭 또는 자동(`IsEditing=true`)으로 텍스트 편집 진입, 편집 중 Alt+방향키로 인접 텍스트 노트 이동(`FindNeighborNote` + DataContext PropertyChanged 구독으로 외부 IsEditing 변화 감지), TextBox는 `UpdateSourceTrigger=PropertyChanged`로 키 입력 즉시 `Content` 갱신, `PreviewMouseLeftButtonDown`에서 Shift 토글/단일 선택 처리, `IsSelected`에 따라 외곽 Border 파란 테두리(DataTrigger), × 삭제 |
| Controls/GroupBoxControl | 점선 회색 사각형(`Stroke`만, `Fill`=null로 내부 클릭 통과). 선택 시 파란 점선. 점선 클릭으로 그룹 자체 선택/드래그. 드래그 시작 시점 `GetMembersOf`로 멤버(완전 내포된 노트·그룹) 스냅샷 후 그룹·멤버 모두 같은 Δ로 이동(스냅 토글 ON일 때 격자), Up 시 DB 저장. 우클릭 → "그룹 해제" 메뉴 |
| MainWindow | 전체 레이아웃 (토글 가능 사이드바 + Pan/Zoom 캔버스), 사이드바 ◀/▶ 토글, Ctrl+V 붙여넣기(world 변환), 캔버스 우클릭 → "새 텍스트 노트"/"그룹 만들기"/"그룹 해제"(선택 상태에 따라), 휠클릭/우클릭 드래그 → 팬, **빈 캔버스 좌클릭 드래그 → 마키 선택** (인터섹트, 노트+그룹 모두 선택), 단순 좌클릭/Esc → 선택 해제, **Ctrl+G 그룹 만들기 / Ctrl+Shift+G 그룹 해제**, Ctrl+휠 → 마우스 앵커 줌(0.2x~4.0x), 캔버스 콘텐츠는 `Grid CanvasContent` 하나로 묶여 RenderTransform 공유 — 그룹 ItemsControl(뒤) + 노트 ItemsControl(앞), 우하단 인디케이터: 일괄 정렬 ⊟ + 격자 스냅 토글(노트북별) ⊞ + 줌 % + 뷰 초기화, 글로벌 단축키 등록, 닫기 시 트레이로 숨김 |
| OverlayWindow | Topmost 반투명 창, 멀티라인 입력 (Shift+Enter 줄바꿈, Enter 제출), Esc 숨기기, 드래그 이동, 우클릭 → 투명도 ±10%/기본값, 클릭 패스스루 토글 |
| Dialogs/GamePickerDialog | 실행 중 프로세스 목록 표시, 더블클릭 또는 선택 버튼으로 연동 |
| Dialogs/RenameDialog | 노트북 이름 변경 입력창 |
| Dialogs/HotkeySettingsDialog | 글로벌 단축키 3개 캡처/저장 |

## 컨버터

| 파일 | 설명 |
|------|------|
| Converters/NoteTypeToVisibilityConverter | `NoteType` enum → `Visibility` (XAML `ConverterParameter`로 매칭 타입 지정) |
| Converters/PathToImageConverter | 파일 경로 → `BitmapImage` (`BitmapCacheOption.OnLoad`로 파일 잠금 해제) |
| Converters/InverseBooleanToVisibilityConverter | bool → `Visibility` 반전 (`HasCurrentNotebook` 빈 화면 안내용) |

## 글로벌 단축키

| 단축키 (기본값) | 동작 |
|--------|------|
| `Ctrl+Shift+N` | 메인 창 표시 |
| `Ctrl+Shift+M` | 오버레이 표시/숨기기 |
| `Ctrl+Alt+Z` | 오버레이 클릭 패스스루 토글 |

> 트레이 메뉴 → "단축키 설정"에서 모두 변경 가능
