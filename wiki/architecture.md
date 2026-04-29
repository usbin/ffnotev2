<!-- 최종 수정: 2026-04-29 -->
# 아키텍처

## 디렉터리 구조

```
ffnotev2/
  ffnotev2.sln
  ffnotev2/
    ffnotev2.csproj
    App.xaml / App.xaml.cs          ← 앱 진입점, 트레이, 서비스 생성
    MainWindow.xaml / .cs           ← 메인 캔버스 창
    OverlayWindow.xaml / .cs        ← 반투명 빠른 입력 오버레이
    Models/
      NoteItem.cs                   ← 개별 노트 (Text/Image/Link)
      NoteBook.cs                   ← 게임별 노트북
    ViewModels/
      MainViewModel.cs              ← 메인 상태·커맨드
      OverlayViewModel.cs           ← 오버레이 상태·커맨드
    Services/
      DatabaseService.cs            ← SQLite CRUD
      GameDetectionService.cs       ← 프로세스 모니터링
      HotkeyService.cs              ← Win32 전역 단축키
    Controls/
      DraggableNoteControl.xaml/.cs ← 드래그 가능한 노트 카드
    Dialogs/
      GamePickerDialog.xaml/.cs     ← 게임 프로세스 선택
      RenameDialog.xaml/.cs         ← 노트북 이름 변경
    Converters/
      NoteTypeToVisibilityConverter.cs
      PathToImageConverter.cs
  wiki/
```

## 모듈 관계

```
App.xaml.cs
  ├── DatabaseService          (SQLite, %APPDATA%\ffnotev2\)
  ├── GameDetectionService     (Timer 3초 폴링)
  ├── MainViewModel            (Notebooks, CurrentNotebook)
  │     └── OverlayViewModel  (QuickNote → MainViewModel.AddTextNote)
  ├── MainWindow               (Canvas, HotkeyService)
  └── OverlayWindow            (Topmost, 반투명)

MainWindow (Canvas)
  └── ItemsControl
        └── DraggableNoteControl × N  (NoteItem DataContext)
```

- `App.MainVM` / `App.OverlayVM` — static 프로퍼티로 뷰모델 공유
- 이미지는 파일로 저장 (`%APPDATA%\ffnotev2\images\{guid}.png`), DB에는 경로만 저장
