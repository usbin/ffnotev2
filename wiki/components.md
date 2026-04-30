<!-- 최종 수정: 2026-04-30 -->
# 주요 컴포넌트

## 모델

| 파일 | 클래스 | 설명 |
|------|--------|------|
| Models/NoteItem.cs | `NoteItem` | X/Y/Width/Height + Type(Text\|Image\|Link) + Content + `IsEditing`(transient, DB 미저장 — 새 노트 자동 편집 진입용). `ObservableObject` 상속으로 X/Y/Width/Height 변경 시 Canvas 자동 이동/리사이즈, `Content` 변경은 `MainViewModel`이 구독해 즉시 DB 저장 |
| Models/NoteBook.cs | `NoteBook` | Name, ProcessName, Notes 컬렉션. `ObservableProperty Name` |
| Models/AppSettings.cs | `AppSettings` | `ShowMain`, `ToggleOverlay`, `ToggleClickThrough` (HotkeyBinding) + `OverlayOpacity` + `OverlayDraft` + `AutoStartOnLogin` + `OverlayLeft`/`OverlayTop` |
| Models/HotkeyBinding.cs | `HotkeyBinding` | Modifiers + VirtualKey + `DisplayString` ("Ctrl+Alt+Z") |

## 서비스

| 파일 | 클래스 | 핵심 메서드 |
|------|--------|-------------|
| Services/DatabaseService.cs | `DatabaseService` | `LoadAllNotebooks()`, `SaveNoteItem()`, `UpdateNotePosition()`, `DeleteNoteItem()` |
| Services/GameDetectionService.cs | `GameDetectionService` | `Start()` — 3초 폴링; `GetRunningWindowedProcesses()` — 창 있는 프로세스 목록 반환 |
| Services/HotkeyService.cs | `HotkeyService` | `Initialize(Window)`, `Register(modifiers, vk, callback)`, `UnregisterAll()` — Win32 RegisterHotKey 래퍼 (LibraryImport source-generated P/Invoke) |
| Services/SettingsService.cs | `SettingsService` | `%APPDATA%\ffnotev2\settings.json` 로드/저장. `AppSettings` 노출 (HotkeyBinding 3개) |
| Services/AutoStartService.cs | `AutoStartService` | `HKCU\...\Run\ffnote` 레지스트리 R/W. `Enable()`/`Disable()`/`IsEnabled` |

## 뷰모델

| 파일 | 클래스 | 역할 |
|------|--------|------|
| ViewModels/MainViewModel.cs | `MainViewModel` | Notebooks 관리, `AddTextNote/AddImageNote/AddLinkNote`(생성 좌표 자동 스냅), `DeleteNote`, `UpdateNotePosition`, `UpdateNoteContent`, `PasteFromClipboard` (이미지·링크·텍스트 자동 분기), 노트 `PropertyChanged` 구독으로 `Content` 변경 시 즉시 DB 저장. `GridSize=10` 상수 + `Snap(double)` 정적 헬퍼 노출 |
| ViewModels/OverlayViewModel.cs | `OverlayViewModel` | `QuickNoteText` (변경 시 settings.json에 자동 저장), `Submit()` → MainViewModel.AddTextNote |

## 컨트롤·창

| 파일 | 설명 |
|------|------|
| Controls/DraggableNoteControl | 타이틀바 드래그로 이동(10px 격자 스냅), 우하단 Thumb로 리사이즈(10px 스냅, min 80×40), 타입별 내용 표시(텍스트는 TextBlock↔TextBox 스왑/이미지/Hyperlink), 더블클릭 또는 자동(`IsEditing=true`)으로 텍스트 편집 진입, TextBox는 `UpdateSourceTrigger=PropertyChanged`로 키 입력 즉시 `Content` 갱신, × 삭제 |
| MainWindow | 전체 레이아웃 (토글 가능 사이드바 + Pan/Zoom 캔버스), 사이드바 ◀/▶ 토글, 사이드바 노트북 항목에 연동 ProcessName 표시, Ctrl+V 붙여넣기 분기(world 좌표 변환), 캔버스 우클릭 → "새 텍스트 노트"(드래그 없을 때만), 휠클릭/우클릭 드래그 → 팬, Ctrl+휠 → 마우스 앵커 줌(0.2x~4.0x), 우하단 줌 % + 뷰 초기화, 글로벌 단축키 등록(`OnSourceInitialized` + `ReregisterHotkeys`), 닫기 시 트레이로 숨김 |
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
