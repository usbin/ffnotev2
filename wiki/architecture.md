<!-- 최종 수정: 2026-04-29 -->
# 아키텍처

## 디렉터리 구조

```
ffnotev2/
  ffnotev2.slnx                       ← XML 신포맷 솔루션
  ffnotev2/
    ffnotev2.csproj
    App.xaml / App.xaml.cs            ← 앱 진입점, 트레이, 서비스 생성, 단축키 설정 다이얼로그
    MainWindow.xaml / .cs             ← 메인 캔버스 창, 글로벌 단축키 등록/재등록, 캔버스 우클릭 메뉴
    OverlayWindow.xaml / .cs          ← 반투명 빠른 입력 오버레이, 클릭 패스스루, 투명도 우클릭 메뉴
    Models/
      NoteItem.cs                     ← 개별 노트 (Text/Image/Link)
      NoteBook.cs                     ← 게임별 노트북
      AppSettings.cs                  ← 단축키 3개 + 오버레이 투명도/초안
      HotkeyBinding.cs                ← Modifiers + VirtualKey + DisplayString
    ViewModels/
      MainViewModel.cs                ← Notebooks/CurrentNotebook, 클립보드 분기
      OverlayViewModel.cs             ← QuickNoteText (자동 저장), Submit
    Services/
      DatabaseService.cs              ← SQLite CRUD
      GameDetectionService.cs         ← 프로세스 모니터링
      HotkeyService.cs                ← Win32 전역 단축키 (LibraryImport)
      SettingsService.cs              ← settings.json 로드/저장
      AutoStartService.cs             ← HKCU\...\Run 레지스트리 R/W (Windows 자동 실행)
    Controls/
      DraggableNoteControl.xaml/.cs   ← 드래그/리사이즈 가능, 텍스트는 TextBlock↔TextBox 스왑
    Dialogs/
      GamePickerDialog.xaml/.cs       ← 게임 프로세스 선택
      RenameDialog.xaml/.cs           ← 노트북 이름 변경
      HotkeySettingsDialog.xaml/.cs   ← 단축키 3개 캡처/저장
    Converters/
      NoteTypeToVisibilityConverter.cs
      PathToImageConverter.cs
      InverseBooleanToVisibilityConverter.cs
  wiki/
```

## 모듈 관계

```
App.xaml.cs
  ├── DatabaseService          (SQLite, %APPDATA%\ffnotev2\)
  ├── SettingsService          (settings.json: 단축키, 투명도, 초안)
  ├── GameDetectionService     (Timer 3초 폴링)
  ├── MainViewModel            (Notebooks, CurrentNotebook)
  │     └── OverlayViewModel  (← SettingsService, QuickNote → MainViewModel.AddTextNote)
  ├── MainWindow               (Canvas, HotkeyService → SettingsService)
  └── OverlayWindow            (Topmost, 반투명, 투명도 SettingsService 동기화)

MainWindow (Canvas)
  └── ItemsControl
        └── DraggableNoteControl × N  (NoteItem DataContext)
```

- `App.MainVM` / `App.OverlayVM` — static 프로퍼티로 뷰모델 공유
- 이미지는 파일로 저장 (`%APPDATA%\ffnotev2\images\{guid}.png`), DB에는 경로만 저장
- `HotkeyService`는 `LibraryImport` (source-generated P/Invoke) 사용, csproj에 `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` 필요
- 단축키는 `MainWindow.ReregisterHotkeys()`로 `SettingsService` 값에 따라 (재)등록
