<!-- 최종 수정: 2026-04-30 -->
# 개발 노트

## 최근 변경 (2026-04-30)

- **우클릭+휠 줌 후 드래그 점프 버그 수정**: 우클릭을 누른 채 휠로 줌한 뒤 그대로 드래그하면 뷰가 튀던 문제. 원인: 줌 시 `CanvasTranslate`는 앵커 보정으로 새 값이 되지만 팬 기준점(`_panStart`/`_panStartTx`/`_panStartTy`)은 버튼 누른 최초 시점 값 그대로여서, 이후 드래그 시 `_panStartTx + dx`가 줌 이전 위치로 점프. 수정: `CanvasArea_MouseWheel`에서 줌 적용 후 팬이 진행 중이면(`_panButton is not null`) 기준점을 현재 마우스 위치·현재 Translate로 갱신.
- **그룹 키보드 이동(Ctrl+화살표, Ctrl+Shift+화살표) 지원**: `TryHandleNoteNudge`가 `SelectedNotes`만 처리하고 `SelectedGroups`는 무시해 그룹 선택 시 방향키가 동작하지 않던 버그. 수정: `SelectedGroups`도 수집하고, `GetMembersOf`로 그룹의 멤버 노트·하위그룹도 포함 — 드래그와 동일한 방식. `HashSet`으로 중복 제거해 멤버 노트가 독립 선택된 경우에도 두 번 이동하지 않음. Ctrl+Shift(격자 정렬) pivot은 그룹 선택 시 첫 번째 그룹, 노트만 선택 시 첫 번째 노트 기준. Undo 스택에도 그룹 변경분 포함.

- **본문 드래그 capture leak 버그 수정 + 미선택 클릭+드래그 + 드래그 중 커서 + 우클릭 휠 줌 (v1.0.7)**:
   - **Capture leak**: UC `PreviewMouseLeftButtonUp`이 tunneling으로 먼저 fire해 `_isDragging=false`로 리셋 → 이후 타이틀바 Border의 bubbling `MouseLeftButtonUp`이 `if (!_isDragging) return;`로 즉시 종료해 `ReleaseMouseCapture` 못 호출 → 캡처가 타이틀바 Border에 남아 이후 모든 클릭이 그 Border로 라우팅되며 빈 캔버스 클릭조차 새 드래그 시작. 수정: UC의 Preview Move/Up 핸들러가 `IsMouseCaptured`가 true일 때만(=본문 드래그) cleanup. 타이틀바 드래그(타이틀바 Border가 캡처)는 자체 핸들러에 위임.
   - **미선택 클릭+드래그**: `_clickStartedSelected` 게이트 제거. 비선택 노트 첫 클릭 시 `SelectOnly`로 선택된 후 본문 드래그 후보 setup이 그대로 진행 → 4px 임계값 초과 시 같은 클릭에서 즉시 드래그로 이어짐(단순 click+release는 4px 미만이라 선택만).
   - **드래그 중 커서**: 본문 드래그는 UC가 캡처를 잡는데 UC.Cursor가 기본값이라 capture 중 SizeAll이 적용 안 됨. `Mouse.OverrideCursor = Cursors.SizeAll`로 전역 강제. 종료 시 `null`로 해제.
   - **우클릭+휠 줌**: `Ctrl+휠`에 더해 `우클릭 누른 채 휠`도 줌 트리거. `e.RightButton == Pressed` 검사. 우클릭 휠 후 우클릭 떼기 시 컨텍스트 메뉴 뜨지 않도록 `_panMoved=true` set.
- **본문 드래그를 UserControl Preview 레벨로 이동 (v1.0.6)**: v1.0.5의 body Grid Background=Transparent 추가만으로는 사용자 환경에서 BodyDrag MouseLeftButtonDown이 발화하지 않는 케이스 발견. 원인 정확히 특정 못 했지만(이벤트 라우팅/hit-test 변수), Preview 이벤트는 OriginalSource까지 무조건 tunneling하므로 컨테이너 hit-test 상태와 무관하게 발화 보장. body Grid의 BodyDrag_* 핸들러 제거하고 `UserControl_PreviewMouseLeftButtonDown/Move/Up`로 통합. body Grid에 `x:Name="BodyArea"`를 부여 + `IsOnBodyArea(visual)` 헬퍼로 OriginalSource가 본문 자손인지 검사 → 타이틀바·리사이즈 Thumb 클릭은 자동 제외(BodyArea 외부). 캡처는 UserControl 자신이 잡아 후속 Move/Up 라우팅 보장.
- **GroupDeleteDialog 키보드 포커스 (v1.0.6)**: 다이얼로그 열릴 때 "일괄 삭제" 버튼에 자동 포커스(`FocusManager.FocusedElement`) + `IsDefault=true`로 Enter 시 일괄 삭제. Tab으로 그룹만 삭제 / 취소 사이 이동.
- **본문 드래그 hit-test 수정 (v1.0.5)**: 본문 Grid에 `Background="Transparent"` 추가. 빈 영역(텍스트 글자 사이 여백, 짧은 텍스트 아래 공간 등)이 hit-test되지 않아 `BodyDrag_MouseLeftButtonDown`이 발화 안 되던 버그. 호버 시 SizeAll 커서는 보이지만(`Cursor` 속성 상속) 마우스 이벤트는 발화 안 됐음.
- **그룹·다중 노트 드래그 격자 어긋남 수정 (v1.0.5)**: 그룹 이동 시 멤버 노트가 한 칸씩 튀던 버그. 원인은 `MaybeSnap`을 그룹과 각 멤버에 **독립 적용**한 것 — 시작 위치가 격자 비정렬이면 각자 다른 격자로 라운딩돼 서로 다른 delta가 적용됨. **leader 기반 snap delta** 패턴으로 수정: 그룹 본인(또는 다중 노트 드래그 시 클릭한 노트 Item)을 leader로 한 번만 snap → 그 결과 actual delta를 멤버 전체에 동일 적용 → 상대 위치 보존. `GroupBoxControl.GroupBox_MouseMove`와 `DraggableNoteControl.TitleBar_MouseMove` 둘 다 같은 패턴으로 수정.
- **선택된 노트 본문 어디서든 드래그**: `IsSelected=true`인 노트는 타이틀바뿐 아니라 본문(Grid Grid.Row="1") 클릭 후 드래그도 이동을 발화. `BodyDrag_MouseLeftButtonDown/Move/Up`가 4px 임계값 기반으로 클릭/드래그를 구분 — Down 시점엔 캡처/Handled 안 해서 더블클릭 편집 진입과 Hyperlink 활성화는 그대로. 첫 클릭으로 선택 전환된 경우는 그 클릭으로 드래그 X(`_clickStartedSelected`로 사전 상태 보존). 편집 중엔 비활성. 선택 시 본문 커서 SizeAll (DataTrigger).
- **자동 업데이트 좀비 상태 버그 수정 (v1.0.2)**: v1.0.0/1.0.1까지 사용했던 `_mgr.ApplyUpdatesAndRestart(info)`가 `PublishSingleFile=true` 배포에서 자기 자신 exe의 file lock 때문에 **`sq.version` 매니페스트만 갱신되고 실제 exe 교체는 실패**하는 케이스 발생. 다음 launch에서 Velopack은 매니페스트만 보고 "이미 최신"이라 판단해 업데이트 사이클이 영원히 막힘 (좀비 상태). `WaitExitThenApplyUpdates(info)` + `Application.Current.Shutdown()`로 교체 — Update.exe를 wait-for-pid 모드로 띄우고 WPF를 깔끔히 종료해 부모 PID 사라진 뒤 lock 없이 안전하게 exe 교체 + 재시작. **v1.0.0/1.0.1 좀비 install이 있는 사용자는 한 번 수동으로 zip을 받아 install 폴더에 덮어써야 탈출 가능** — 좀비 상태에서 자동 업데이트로는 못 빠져나옴.
- **자동 업데이트 (Velopack + GitHub Releases, 포터블 전용)**: `Velopack` NuGet + `Services/UpdateService.cs`. `App.xaml.cs`에 명시적 `[STAThread] Main` 추가해 `VelopackApp.Build().Run()`이 WPF 시작 전에 실행. **체크 타이밍**: `MainWindow.OnSourceInitialized`에서 `IsVisibleChanged` 핸들러를 등록해 **메인 창이 처음 사용자에게 표시되는 순간에만 1회 체크**. AutoStart(트레이 전용 시작) 시점에는 체크/다이얼로그 안 뜸 — 로그인 직후 갑작스러운 팝업 방지. 새 버전 발견 시 다이얼로그(OK/Cancel) → OK 시 다운로드 + `ApplyUpdatesAndRestart` 즉시 재시작. `_mgr.IsInstalled=false`(개발 빌드)면 스킵. csproj는 `Version`/`AssemblyVersion`/`FileVersion`+`PublishSingleFile`+`SelfContained=false`+`RuntimeIdentifier=win-x64`+`StartupObject=ffnotev2.App` 추가, `App.xaml`은 `ApplicationDefinition` → `Page`로 빌드 액션 변경(auto-Main 충돌 방지). CI: `.github/workflows/release.yml`이 `v*.*.*` 태그 푸시 시 dotnet publish + `vpk pack --noInst`(포터블 zip만) + `vpk upload github`. 단일 인스턴스 mutex는 `VelopackApp.Run()` 이후에 처리되므로 훅 명령(`--veloapp-*`)과 충돌 없음. **코드 사이닝 미적용** — self-signed는 SmartScreen에 효과 없어 의도적 생략. 본인/소수 배포 수준에서는 MOTW 기반 SmartScreen 경고를 "추가 정보 → 실행"으로 우회하면 충분(`setup.md`에 안내). 향후 공개 배포 시 SignPath.io Foundation(OSS 무료 EV) 또는 Microsoft Trusted Signing($9.99/월) 검토.
- **편집 중 캐럿 이동 회복**: 직전 `UserControl_PreviewMouseLeftButtonDown` 가드의 `e.OriginalSource is not TextBox`는 TextBox 내부 visual(TextBoxView 등)을 못 잡음 → 글자 사이 클릭 시 편집 종료 버그. `IsInsideTextBox` visual-tree 헬퍼로 교체.
- **노트/그룹 4 코너 리사이즈 핸들 (대각선)**: 기존 우하단 가시 삼각형 핸들을 제거하고 4 코너(`TopLeft/TopRight/BottomLeft/BottomRight`) 모두 10×10 투명 Thumb로 통일. 엣지 thumb보다 z-order 위에 두어 코너 영역에서 우선. 단일 핸들러가 좌/우/상/하 플래그(`left/right/top/bottom`)로 분기 — 플래그가 동시에 활성되면 X/Y와 W/H 동시 변경. min 크기는 노트 80×40, 그룹 60×40.
- **그룹 삭제 다이얼로그**: `Dialogs/GroupDeleteDialog`. 멤버 노트가 있는 그룹 삭제 시 "포함된 노트 N건도 삭제하시겠습니까?" + "일괄 삭제 / 그룹만 삭제 / 취소". `MainWindow.DeleteGroupsWithPrompt(groups, extraNotes?)`이 모든 그룹 삭제 콜사이트(Ctrl+Shift+G, Delete 키, 우클릭 메뉴 통합 + 단일)를 통합. `extraNotes`(명시적 선택 노트)는 멤버와 중복 제거 후 항상 삭제. 멤버 0건이면 다이얼로그 없이 즉시 삭제. `MainViewModel.GetMemberNotesOf(IEnumerable<NoteGroup>)`이 합집합 반환(중복 제거).


- **그룹 UX 재설계**: `GroupBoxControl`이 노트와 같은 형태(헤더 + 점선 외곽 + 4 엣지 + 코너 리사이즈)로 변경. 본문은 hit-test 통과해 멤버 노트 클릭 보존. 헤더 드래그가 명확한 어포던스 역할. 점선 외곽은 `IsHitTestVisible=False` 시각 표시 전용. 그룹 상단 패딩은 `CreateGroupFromSelectedNotes`에서 30px(헤더 22 + 여유 8)로 설정 — 헤더가 멤버 노트에 가려지지 않도록.
- **Notes Canvas hit-test 통과 처리** (필독): `MainWindow.xaml`의 `NotesItemsControl` ItemsPanel `Canvas`의 `Background`를 `Transparent` → `{x:Null}`로 변경. WPF hit-test는 z-order 최상위에서 멈추므로 `Background="Transparent"`인 노트 Canvas가 그 뒤(z-order)의 그룹 헤더/리사이즈 핸들로 가는 클릭을 모두 가로채던 문제. null로 바꾸면 노트 없는 영역의 클릭이 그룹 element로 도달. 마키는 결국 `CanvasArea` Grid(`Background="#FF1E1E1E"`)에서 받으므로 그대로 동작. **노트 ItemsControl 패널 Background를 다시 Transparent로 되돌리지 말 것 — 그룹 클릭이 깨짐.**
- **우클릭 메뉴 통합**: `DraggableNoteControl`/`GroupBoxControl`의 자체 `MouseRightButtonUp` 핸들러 모두 제거. 모든 우클릭이 `Canvas_MouseRightButtonUp`로 버블링. 핸들러는 `e.OriginalSource`에서 `FindNoteFromVisual`/`FindGroupFromVisual`로 대상을 식별해 단일 메뉴를 빌드:
   - 항상: "새 텍스트 노트"
   - 선택된 노트가 있으면: "그룹 만들기 (N개)"
   - 선택된 그룹이 있으면: "그룹 해제 (N개)"
   - 우클릭 대상이 노트면: "삭제하기 (Delete)"
   - 우클릭 대상이 (선택 안 된) 그룹이면: "그룹 해제"
- **노트/그룹 위 우클릭 드래그 팬 + 컨텍스트 메뉴 공존**: 우클릭은 항상 팬 후보로 진입(노트/그룹 포함). down 시점에 `_rightClickOrigin`에 원본 visual을 저장하고, up 시점에 `_panMoved=false`(이동 없음)면 저장된 origin으로 노트/그룹을 식별해 메뉴 빌드. `_panMoved=true`면 팬으로 처리하고 메뉴 억제. CanvasArea가 마우스 캡처해 `MouseRightButtonUp.OriginalSource`가 캔버스로 바뀌어도 저장된 origin이 정확한 대상을 가리킴.
- **`IsOriginInsideNote`가 `GroupBoxControl`도 검사**: CanvasArea가 generic `MouseDown`으로 좌클릭을 처리해 자식의 specific `MouseLeftButtonDown` `e.Handled`로 마키 시작을 막을 수 없음. visual tree에서 `GroupBoxControl` 부모를 찾으면 마키/팬 모두 스킵.
- **Delete 키 그룹 삭제**: `Window_PreviewKeyDown` Delete 분기가 `SelectedNotes` + `SelectedGroups` 모두 일괄 삭제.
- **편집 커서 잔존 버그 수정**: 노트 편집 중 다른 노트(이미지·텍스트 헤더 등)를 클릭해도 이전 TextBox 커서가 남던 문제 해결. `DraggableNoteControl.UserControl_PreviewMouseLeftButtonDown`에서 클릭 소스가 TextBox가 아니면 `MainWindow.FocusCanvas()`로 키보드 포커스 이동 → 이전 TextBox `LostFocus` 트리거 → 표시 모드로 복귀. 자신의 TextEditor를 직접 클릭한 경우는 제외해서 편집 흐름 유지. 더블클릭으로 BeginEdit는 PreviewMouseLeftButtonDown 이후 `Keyboard.Focus(TextEditor)`로 다시 포커스를 잡으므로 정상 동작.
- **붙여넣기 후 비편집 선택 상태**: `MainViewModel.AddTextNote(autoEdit=true)` 파라미터 추가. `PasteFromClipboard`는 텍스트를 `autoEdit:false`로 만들고 `SelectOnly(note)`로 선택 표시. 이미지/링크도 `SelectOnly`로 통일.
- **노트 헤더 × 버튼 제거**: 우클릭 메뉴의 "삭제하기"로 대체.

## 알려진 제한 사항

- **이미지 비율 조정 없음**: 붙여넣은 이미지는 400×300으로 클램핑하지만 원본 비율 유지 안 됨
- **전체화면 배타 게임 위 표시 불가**: WPF `AllowsTransparency=True`는 윈도우 모드/보더리스에서만 보장
- **캔버스 뷰 상태(pan/zoom) 비저장**: 앱 재시작 시 zoom=1, translate=0으로 초기화 — 노트가 음수 좌표에 있으면 안 보일 수 있음
- **리사이즈 누적 오차**: `ResizeThumb_DragDelta`가 마지막 프레임 변화량만 누적해 스냅하므로 세션 동안 마우스와 약간 어긋날 수 있음(10px 격자라 체감 미미)

## TODO (다음 작업 후보)

배치 기능 로드맵:
- [ ] **C. 자동 밀집** (보류) — 가변 크기 노트 bin packing 알고리즘 검토 필요. 최상위 그룹 기준 정렬 + 그룹 단위 정렬도 같이 검토

기타:
- [ ] **코드 사이닝** (공개 배포 시점에) — SignPath.io Foundation 신청(OSS 무료 EV) 또는 Microsoft Trusted Signing 가입. workflow에 서명 단계 추가
- [ ] 캔버스 뷰 상태(pan/zoom) 노트북별 DB 저장/복원
- [ ] 노트 색상 변경 기능
- [ ] 노트북 드래그 정렬 (사이드바)
- [ ] 노트 검색 / 필터
- [ ] "여기로 이동" — 화면 밖 노트로 뷰 이동
- [ ] 백업·Export·Import (SQLite + 이미지 zip)
- [ ] 트레이 아이콘 커스텀 (현재 `SystemIcons.Application`)
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
- [x] **다중 선택 + 일괄 드래그/리사이즈** — `NoteItem.IsSelected`(transient). 빈 캔버스 좌클릭 드래그 = 마키(인터섹트, world 변환), Shift+클릭 = 토글, 단순 클릭/Esc = 해제. 선택된 노트 한 개를 드래그하면 모두 동기 이동(같은 Δ). 리사이즈는 `DragStarted`에서 시작 크기 캡처 + 누적 Δ로 모두 같은 px만큼 변경(절대). 선택 시각: 외곽 Border 파란 테두리(#5599FF, 두께 2)
- [x] **노트 외곽 4면 리사이즈 핸들** — 5px 투명 Thumb를 상/하/좌/우에 배치, sender.Tag로 단일 핸들러 분기. 좌/상 엣지는 X/Y와 W/H 동시 변경(반대편 엣지 고정). 코너(우하단) 14×14는 그대로 유지(외곽보다 위)
- [x] **방향키 노트 이동** — `MainViewModel.FindNeighborNote`(중심점 거리 + 방향 콘 필터, **모든 노트 타입** 포함). Window 레벨 통합 처리(`MainWindow.TryHandleArrowNavigation`):
   - 편집 중(TextBox 포커스): Alt+방향키 → 인접 노트로 편집 이동. 다음이 텍스트면 BeginEdit, 이미지/링크면 선택만.
   - 비편집(단일 선택): 방향키(Alt 포함, Ctrl/Shift 제외) → 선택 이동만
   - Esc로 편집 종료 시 `Keyboard.Focus(window)`로 Window에 키보드 포커스 부여 → 이후 화살표가 Window_PreviewKeyDown에 도달
   - 사이드바 ListBox 포커스 시 가로채지 않음
   - DraggableNoteControl `OnItemPropertyChanged`: IsEditing=true가 외부에서 설정되면 항상 false로 리셋(stuck 방지) + 텍스트일 때만 BeginEdit
- [x] **Enter로 선택 노트 편집 진입** — 비편집 상태에서 단일 선택 텍스트 노트가 있을 때 Enter → IsEditing=true 트리거
- [x] **단일 인스턴스 (Mutex)** — Local 네임스페이스 Mutex로 한 사용자당 한 프로세스만. 두 번째 인스턴스는 EventWaitHandle 신호로 첫 인스턴스의 ShowMain만 호출하고 즉시 종료. 첫 인스턴스는 IsBackground 스레드에서 신호 대기
- [x] **노트북별 오버레이 초안** — `Notebooks.OverlayDraft TEXT NOT NULL DEFAULT ''` 컬럼 + `NoteBook.OverlayDraft` ObservableProperty. MainVM의 NoteBook PropertyChanged 구독이 키 입력마다 DB 저장. OverlayViewModel은 노트북 전환 시 `_suppressDraftSave` 플래그로 단방향 로드. 기존 `AppSettings.OverlayDraft`는 제거
- [x] **노트북 빠른 전환 단축키** — `AppSettings.NotebookSwitches[10]`(기본 Ctrl+1..Ctrl+0). `HotkeyBinding.MatchesLocal`로 로컬 키 매칭. `MainWindow.TryHandleNotebookSwitch`가 인덱스 매칭 시 `CurrentNotebook = Notebooks[i]`. 사용자 정의 가능
- [x] **노트/그룹 미세/큰 단위 이동 단축키** — `MainWindow.TryHandleNoteNudge`. 비편집 + 선택 노트·그룹(다중 가능)에 대해 Ctrl+화살표=1px, Ctrl+Shift+화살표=10px. 그룹은 멤버 노트·하위그룹 동기 이동, HashSet 중복 제거. 즉시 DB 저장
- [x] **단축키 설정창 통합** — `HotkeySettingsDialog`를 ScrollViewer + ItemsControl로 확장. 글로벌 3개 + 노트북 10개 모두 캡처/저장. 변경 불가 단축키(Ctrl+화살표, Alt+화살표, Ctrl+G, 마키 등)는 참고용으로 표시. 트레이 메뉴 "단축키 설정..."에서 호출
- [x] **빈 캔버스 클릭 시 편집 포커스 해제** — CanvasArea 좌클릭 마키 시작 시점에 `FocusCanvas()` 호출 → 편집 중이던 TextBox LostFocus(저장)
- [x] **Ctrl+Shift+화살표 격자 정렬 이동** — 첫 선택 노트 기준 격자에 맞도록 dx/dy 계산 후 모든 선택 노트에 같은 Δ 적용. 비격자 위치도 격자선에 정렬되며 한 칸 이동, 다중 선택 시 상대 위치 유지
- [x] **Delete 키 선택 노트 삭제** — 비편집 + 선택 노트들 일괄 삭제 (Undo 가능)
- [x] **Ctrl+C 시스템 클립보드 복사** — Text/Link → `SetText`, Image → `SetImage(BitmapSource)`. 다른 앱과 호환. 다중 선택 시 첫 노트만
- [x] **Undo/Redo (Ctrl+Z / Ctrl+Y / Ctrl+Shift+Z)** — `UndoService`(LinkedList LIFO, max 200), `IUndoableAction` + 5종 액션:
   - `TransformItemsAction`(이동/리사이즈/그룹 동기, NoteItem+NoteGroup 혼합)
   - `AddNoteAction`/`DeleteNoteAction`/`AddGroupAction`/`DeleteGroupAction`
   - 콜사이트: 드래그/리사이즈 종료, Ctrl+화살표 nudge, Add/Delete 메서드 모두 push
   - 이미지 노트 삭제 시 파일은 즉시 삭제 안 함(Undo 복원 가능). 노트북 삭제 시 일괄 정리
   - 편집 중 TextBox에서는 Ctrl+Z를 가로채지 않음 (TextBox 자체 undo 사용)
   - BulkSnap, 텍스트 Content 변경은 미적용 (후속)
- [x] **그룹 (NoteGroup)** — DB 테이블 `NoteGroups`(Id/NotebookId/X/Y/W/H). 멤버십은 동적(bbox 완전 내포). 그룹 점선 사각형 시각, Stroke만 hit-test 되어 내부 노트 클릭/마키 통과. 드래그 시작 시 `GetMembersOf`로 멤버 스냅샷, 그룹+멤버 모두 같은 Δ로 이동. Ctrl+G로 선택 노트 bbox+10px 패딩으로 그룹 생성, Ctrl+Shift+G/우클릭 메뉴로 해제. 노트와 그룹이 RenderTransform을 공유하도록 `Grid CanvasContent`로 묶음 — 그룹은 노트 뒤, 마키는 노트와 그룹 모두 인터섹트로 선택. 스냅 토글에 따라 그룹 드래그도 격자 정렬
- [x] **일괄 스냅 (B, 애니메이션)** — `MainViewModel.BulkSnap()`. 전체 노트 좌상단 floor + 사이즈 ceil. (Y,X) 오름차순 정렬 후 충돌 시 dx/dy 최소 거리 계산해 짧은 방향 우선 시프트 (단순 우측 wrap 방식보다 노트가 멀리 사라지지 않음). DispatcherTimer 16ms × 100ms 선형 보간으로 이동/리사이즈 애니메이션, 완료 후 DB 저장. 우하단 ⊟ 버튼으로 발동
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
