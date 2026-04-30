<!-- 최종 수정: 2026-04-30 -->
# 데이터 흐름

## 붙여넣기 (Ctrl+V)

```
사용자 Ctrl+V (메인 창)
  → MainWindow.Window_PreviewKeyDown
  → 편집 중인 TextBox(IsReadOnly=false)면 패스 — 기본 붙여넣기 살림
  → MainViewModel.PasteFromClipboard(x, y)
       1) "PNG" 포맷 → PngBitmapDecoder → 파일 저장 → AddImageNote
       2) DIB 포맷 → BMP 헤더 붙여 BitmapDecoder → 파일 저장 → AddImageNote
       3) Clipboard.GetImage() → 파일 저장 → AddImageNote
       4) Bitmap 직접 → AddImageNote
       5) 텍스트 → URL 패턴이면 AddLinkNote, 아니면 AddTextNote
       6) 모두 실패 → 사용 가능한 클립보드 포맷을 MessageBox로 안내
```

## 노트 드래그 / 리사이즈 (10px 격자 스냅)

```
타이틀바 MouseLeftButtonDown
  → CaptureMouse, _startX/Y 저장
  → MouseMove: NoteItem.X/Y = MainViewModel.Snap(start + Δ)
       (Canvas.Left/Top TwoWay 바인딩 자동 이동, 격자 단위로 또각또각)
  → MouseLeftButtonUp: MainViewModel.UpdateNotePosition() — DB 저장

우하단 Thumb DragDelta
  → NoteItem.Width/Height = max(min, Snap(현재 + Δ)) (min 80×40)
  → DragCompleted: MainViewModel.UpdateNoteContent() — DB 전체 갱신
```

새 노트 생성 좌표(`AddTextNote/AddImageNote/AddLinkNote`)도 진입부에서 `Snap()` 적용 — 클립보드 붙여넣기·우클릭 메뉴·오버레이 빠른 입력 모두 자동 정렬.

## 텍스트 편집 (TextBlock ↔ TextBox 스왑) + 즉시 저장

```
표시 모드: TextBlock (선택 불가, 단순 표시)
  → 더블클릭(ClickCount==2) 또는 새로 생성 시 자동(IsEditing=true)
  → BeginEdit(): TextBlock.Visibility=Collapsed, TextBox.Visibility=Visible
  → Dispatcher.BeginInvoke(Input)로 Keyboard.Focus(TextBox), 캐럿을 끝으로
편집 중: TextBox (IME 컨텍스트 정상 — IsReadOnly 토글 회피)
  → 키 입력마다 (UpdateSourceTrigger=PropertyChanged)
       Item.Content 즉시 갱신
       → NoteItem.PropertyChanged → MainViewModel.OnNotePropertyChanged
       → e.PropertyName == nameof(Content)면 _db.UpdateNote(note) 즉시 저장
  → ESC 또는 다른 곳 클릭 (LostFocus)
  → TextBox.Visibility=Collapsed, TextBlock.Visibility=Visible
  → 추가로 한 번 더 UpdateNoteContent() 호출 (defense in depth)
```

키 입력 단위로 DB에 반영되므로 사용자가 트레이 종료/창 닫기를 해도 마지막 글자까지 보존됨.

## 새 노트 자동 편집 진입

```
사용자 우클릭 → "새 텍스트 노트"
  → MainViewModel.AddTextNote(content="", x, y)
       NoteItem 생성 시 IsEditing = true (DB 미저장 transient 플래그)
       → CurrentNotebook.Notes.Add(note) → DataTemplate으로 DraggableNoteControl 생성
  → DraggableNoteControl.Loaded
       Item.IsEditing == true && Type == Text 이면
         IsEditing = false 후 BeginEdit() — 즉시 편집 모드로 진입, TextBox에 포커스
```

## 캔버스 Pan/Zoom (무한 캔버스)

```
ItemsControl.RenderTransform = TransformGroup
  ├── ScaleTransform CanvasScale (ScaleX/ScaleY 기본 1)
  └── TranslateTransform CanvasTranslate (X/Y 기본 0)

세 가지 입력 → 같은 transform 변경:

[휠 클릭 드래그] (즉시 팬 모드)
  CanvasArea_MouseDown(Middle) → _panButton=Middle, 즉시 Cursor=SizeAll, CaptureMouse
  MouseMove → CanvasTranslate.X/Y = startTx/Ty + Δ
  MouseUp(Middle) → EndPan()

[우클릭 드래그] (4px 임계값 후 팬)
  MouseDown(Right) → _panButton=Right, _panMoved=false, CaptureMouse
  MouseMove → 임계값 초과 시 _panMoved=true → translate 갱신
  MouseRightButtonUp (MouseUp보다 먼저 발생할 수 있어 여기서 직접 정리)
    → wasPan이면 e.Handled=true (컨텍스트 메뉴 억제) + EndPan()
    → wasPan 아니면 컨텍스트 메뉴 표시 (단순 우클릭)

[Ctrl+휠] PreviewMouseWheel — 자식 ScrollViewer가 먹기 전 터널 단계에서 처리
  → 마우스 위치를 world 좌표로 환산 → 새 scale (Clamp 0.2~4.0)
  → translate 보정으로 같은 world 점이 화면 같은 위치에 유지 (마우스 앵커 줌)
  → ZoomLabel "{N}%"

[좌표 변환]
  screen → world: world = (screen - translate) / scale
  Ctrl+V 붙여넣기, 우클릭 메뉴 모두 ScreenToWorld() 후 AddNote (그리고 AddNote가 Snap)

[안전망]
  CanvasArea.LostMouseCapture → 컨텍스트 메뉴 등에 캡처를 빼앗기면 EndPan()
  뷰 초기화 버튼 (⟳) → scale=1, translate=0, ZoomLabel="100%"
```

## 사이드바 토글

```
사이드바 헤더 ◀ 버튼 클릭
  → SidebarColumn.Width = 0, Sidebar.Visibility=Collapsed
  → SidebarReopenButton(▶) Visible (캔버스 좌상단)
다시 ▶ 클릭
  → SidebarColumn.Width = 220, 원복
```

## 시작 흐름 (백그라운드 우선)

```
App.OnStartup
  → DatabaseService, SettingsService, AutoStartService 초기화
  → MainWindow 생성 + WindowInteropHelper.EnsureHandle()
       (창은 안 보임. 단 HWND가 만들어져 OnSourceInitialized 발생 → 글로벌 핫키 등록)
  → OverlayWindow 생성 (Show 안 함)
  → SetupTray (트레이 아이콘 + 컨텍스트 메뉴)
  → GameDetectionService.Start
```

따라서 Windows 부팅 자동 실행 + 트레이만 떠있는 상태가 기본. 사용자는 단축키/트레이 메뉴로 메인 창을 띄우거나, 게임이 감지되면 자동으로 떠오름.

## 게임 자동 감지 + 자동 표시

```
GameDetectionService (System.Threading.Timer 3초)
  → MainViewModel.Notebooks의 ProcessName 목록을 Provider로 받아옴
  → Process.GetProcesses() 중 MainWindowTitle 있는 것만 매칭
  → 매칭 결과가 _lastDetected와 다르면 GameDetected 이벤트
       (게임 종료 시 _lastDetected = null로 리셋되므로,
        같은 게임 재시작 시에도 다시 이벤트 발생)
  ├─ MainViewModel.OnGameDetected → Dispatcher.BeginInvoke로 CurrentNotebook = matched
  ├─ OverlayViewModel.OnMainChanged → AttachToCurrentNotebook → CurrentNotebookName 갱신
  └─ App.OnGameDetectedShowWindows → Dispatcher.Invoke로
        _mainWindow.Show()  (숨겨져있으면)
        _overlayWindow.Show()  (숨겨져있으면)
```

## 오버레이 빠른 입력 (멀티라인 + 초안 자동 저장)

```
사용자 텍스트 입력
  → TextBox.Text {Binding QuickNoteText TwoWay PropertyChanged}
  → OverlayViewModel.OnQuickNoteTextChanged (CommunityToolkit.Mvvm partial method)
  → SettingsService.Settings.OverlayDraft = value, SettingsService.Save() (settings.json)

Enter 키
  → OverlayViewModel.SubmitCommand
  → MainViewModel.AddTextNote (60±320, 60±200 랜덤 위치)
  → QuickNoteText = "" → 초안도 비워 저장

Shift+Enter
  → TextBox 기본 동작으로 줄바꿈만 삽입

ESC
  → OverlayWindow.Hide()

TextChanged
  → SizeToContent 리셋으로 글자 줄어들 때 세로도 자동 축소
```

## 글로벌 단축키 (사용자 설정 가능)

```
[등록]
SettingsService.Settings (settings.json)
  ↓
MainWindow.OnSourceInitialized → ReregisterHotkeys()
  → HotkeyService.UnregisterAll()
  → 3개 등록: ShowMain / ToggleOverlay / ToggleClickThrough

[변경]
트레이 → "단축키 설정"
  → HotkeySettingsDialog (캡처 박스에 키 누르면 PreviewKeyDown으로 저장)
  → 저장 시 SettingsService.Save() + MainWindow.ReregisterHotkeys()
  → 실패하면(다른 앱 점유) MessageBox
```

## 오버레이 클릭 패스스루

```
Ctrl+Alt+Z (또는 사용자 지정)
  → App.ToggleOverlayClickThrough()
  → OverlayWindow.SetClickThrough(true)
  → user32.dll SetWindowLongPtr(GWL_EXSTYLE, +WS_EX_TRANSPARENT|WS_EX_LAYERED)
  → 마우스 이벤트가 오버레이 통과 → 뒤 창에 도달
  → ClickThroughIndicator "🖱 패스스루" 표시
  → 끄려면 Ctrl+Alt+Z 다시 (단축키만 받음, 마우스 클릭은 통과 중)
```

## Windows 자동 실행 토글

```
트레이 → "Windows 시작 시 자동 실행" 체크 (CheckOnClick=true)
  → AutoStart.SetEnabled(true)
  → HKCU\Software\Microsoft\Windows\CurrentVersion\Run
       \ffnote = "C:\path\to\ffnotev2.exe"
  → SettingsService.Settings.AutoStartOnLogin = true → settings.json

앱 시작 시 동기화:
  settings.AutoStartOnLogin == true && Run 키 없음 → 등록
  settings.AutoStartOnLogin == false && Run 키 있음 → 삭제
  settings.AutoStartOnLogin == true && Run 키 있음 → 경로 갱신 (이동했을 수 있어)
```

## 오버레이 투명도 (자동 저장)

```
우클릭 (TextBox 영역 외) → ContextMenu
  → +10% / -10% / 기본 투명도
  → AdjustOpacity → Window.Opacity (0.2~1.0 클램핑)
  → SaveOpacityToSettings() → settings.json
앱 재시작
  → OverlayWindow.Loaded → SettingsService.Settings.OverlayOpacity 복원
```

## 오버레이 위치 (자동 저장)

```
사용자 드래그 (Border MouseLeftButtonDown → DragMove)
  → Window.LocationChanged 이벤트 (픽셀마다 발생)
  → DispatcherTimer 500ms 디바운서 (각 LocationChanged마다 Stop+Start)
  → 0.5초간 정지하면 Tick → SaveLocationToSettings()
  → settings.OverlayLeft / OverlayTop → settings.json

앱 재시작
  → OverlayWindow.Loaded
  → ClampToVisibleScreen(savedLeft, savedTop)
       모든 모니터 작업 영역 중 어느 하나라도 겹치면 그대로
       모두 밖이면 기본값(40, 40) — 해상도/모니터 변경 대응
  → _suppressLocationSave 가드로 복원 시 LocationChanged 무시
```
