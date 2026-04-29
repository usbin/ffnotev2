<!-- 최종 수정: 2026-04-29 -->
# 데이터 흐름

## 붙여넣기 (Ctrl+V)

```
사용자 Ctrl+V
  → MainWindow.OnPreviewKeyDown
  → Clipboard.ContainsImage() → BitmapSource → MainViewModel.AddImageNote()
                                                → 이미지 파일 저장 (PNG)
                                                → DatabaseService.SaveNoteItem()
                                                → CurrentNotebook.Notes 추가
                                                → Canvas 자동 갱신
  → Clipboard.ContainsText()
      → URL 패턴 → AddLinkNote()
      → 일반 텍스트 → AddTextNote()
```

## 노트 드래그

```
DraggableNoteControl 타이틀바 MouseDown
  → _isDragging = true, 마우스 캡처
  → MouseMove: NoteItem.X/Y 갱신 (ObservableProperty → Canvas.Left/Top 바인딩 자동 이동)
  → MouseUp: DatabaseService.UpdateNotePosition() 저장
```

## 텍스트 편집

```
TextBox 더블클릭 → IsReadOnly = false
  → 사용자 편집
  → LostFocus → IsReadOnly = true → DatabaseService.SaveNoteItem()
```

## 게임 자동 감지

```
GameDetectionService (Timer 3초)
  → Process.GetProcesses() 스캔
  → ProcessName 일치하는 NoteBook 발견
  → GameDetected 이벤트
  → MainViewModel: CurrentNotebook 변경
  → OverlayViewModel: CurrentNotebookName 갱신
```

## 오버레이 빠른 입력

```
사용자 텍스트 입력 → Enter
  → OverlayViewModel.Submit()
  → MainViewModel.AddTextNote() (랜덤 위치)
  → DB 저장 + Canvas 추가
```
