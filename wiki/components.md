<!-- 최종 수정: 2026-04-29 -->
# 주요 컴포넌트

## 모델

| 파일 | 클래스 | 설명 |
|------|--------|------|
| Models/NoteItem.cs | `NoteItem` | X/Y/Width/Height + Type(Text\|Image\|Link) + Content. `ObservableObject` 상속으로 X/Y 변경 시 Canvas 자동 이동 |
| Models/NoteBook.cs | `NoteBook` | Name, ProcessName, Notes 컬렉션 |

## 서비스

| 파일 | 클래스 | 핵심 메서드 |
|------|--------|-------------|
| Services/DatabaseService.cs | `DatabaseService` | `LoadAllNotebooks()`, `SaveNoteItem()`, `UpdateNotePosition()`, `DeleteNoteItem()` |
| Services/GameDetectionService.cs | `GameDetectionService` | `Start()` — 3초 폴링; `GetRunningWindowedProcesses()` — 창 있는 프로세스 목록 반환 |
| Services/HotkeyService.cs | `HotkeyService` | `Initialize(Window)`, `Register(modifiers, vk, callback)` — Win32 RegisterHotKey 래퍼 |

## 뷰모델

| 파일 | 클래스 | 역할 |
|------|--------|------|
| ViewModels/MainViewModel.cs | `MainViewModel` | Notebooks 관리, `AddTextNote/AddImageNote/AddLinkNote`, `DeleteNoteItem`, `UpdateNotePosition` |
| ViewModels/OverlayViewModel.cs | `OverlayViewModel` | `QuickNoteText`, `Submit()` → MainViewModel.AddTextNote 호출 |

## 컨트롤·창

| 파일 | 설명 |
|------|------|
| Controls/DraggableNoteControl | 타이틀바 드래그로 이동, 타입별 내용 표시, 더블클릭 텍스트 편집, × 삭제 |
| MainWindow | 전체 레이아웃 (사이드바 + Canvas), Ctrl+V 붙여넣기, 글로벌 단축키 등록 |
| OverlayWindow | Topmost 반투명 창, Enter로 빠른 메모 저장, Esc 숨기기, 드래그 이동 |
| Dialogs/GamePickerDialog | 실행 중 프로세스 목록 표시, 더블클릭 또는 선택 버튼으로 연동 |
| Dialogs/RenameDialog | 노트북 이름 변경 입력창 |

## 글로벌 단축키

| 단축키 | 동작 |
|--------|------|
| `Ctrl+Shift+N` | 메인 창 표시 |
| `Ctrl+Shift+M` | 오버레이 표시/숨기기 |
