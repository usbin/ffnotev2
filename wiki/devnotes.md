<!-- 최종 수정: 2026-05-17 -->
# 개발 노트

## 최근 변경 (2026-05-17, 링크 클릭 + 복붙 크기 유지 + 표 편집 열 순서/헤더 더블클릭)

- **표 편집 컬럼 헤더 더블클릭 이름 변경 안 되던 버그 수정**: `EventSetter`(`DataGridColumnHeader.PreviewMouseLeftButtonDown`) + `ClickCount==2` 방식은 `CanUserReorderColumns=True`에서 첫 클릭이 컬럼 리오더 드래그로 마우스를 캡처해 두 번째 클릭의 `ClickCount==2`가 같은 헤더 핸들러에 도달 못 함. DataGrid 레벨 `PreviewMouseDoubleClick`(터널) + `FindHeader`(OriginalSource에서 `VisualTreeWalker.GetAnyParent`로 `DataGridColumnHeader` 탐색)로 교체. XAML의 `DataGrid.Resources` 헤더 Style/EventSetter, `ColumnHeaderStyle` 제거.
- **표 편집 확인 시 컬럼 순서 미반영 수정**: `Confirm_Click`에서 `BuildMarkdown` 전에 `SyncColumnOrderFromGrid()` 호출 → 사용자가 드래그로 바꾼 화면 순서가 `_dt` 순서에 반영되어 마크다운이 그 순서로 출력됨.
- **표 편집(Ctrl+E) +열 시 기존 열 위치 흐트러짐 수정**: `CanUserReorderColumns=True`로 드래그한 컬럼 순서는 DataGrid의 `DisplayIndex`에만 반영되고 `DataTable` 순서는 그대로라, `RebindGrid`가 DataTable 순서로 컬럼을 재생성하며 사용자가 바꾼 순서가 사라짐. `SyncColumnOrderFromGrid()` 추가 — 현재 그리드 DisplayIndex 순서대로 `DataColumn.SetOrdinal`로 `_dt` 컬럼 순서를 먼저 맞춤. 구조 변경 직전(`AddCol_Click`/`DeleteCol_Click`/`ColumnHeader_DoubleClick`/`Confirm_Click`)에 호출 → 화면 순서가 곧 DataTable 순서가 되어 재생성 시 위치 보존 + 새 열은 맨 끝에 추가. `BuildMarkdown`도 화면 순서대로 출력됨(확인 시에도 sync).
- **복사·붙여넣기 시 노트 크기 유지**: 기존엔 Ctrl+C가 내용만 시스템 클립보드에 넣고 Ctrl+V가 기본 크기(텍스트 200×100 등)로 새 노트를 생성 → 원본 크기 손실. `MainViewModel._clipNote`(Type/Content/Width/Height 튜플) 내부 클립보드 추가 — `CopySelectedToClipboard`에서 복사 노트 스냅샷 저장, `PasteFromClipboard`에서 텍스트/링크는 Content 일치, 이미지는 Type==Image일 때 저장된 크기를 재적용. `AddTextNote`/`AddImageNote`/`AddLinkNote`에 `double? width=null, double? height=null` 선택 파라미터 추가(기본값이면 기존 동작 유지).
- **노트 본문 링크 클릭 안 되던 버그 수정**: 표시 모드(`FlowDocumentScrollViewer`)의 `TextDisplay_MouseLeftButtonDown`는 tunneling `PreviewMouseLeftButtonDown` 핸들러로 텍스트 선택 차단을 위해 무조건 `e.Handled=true`를 세팅했음. preview는 부모→자식 순이라 내부 `Hyperlink`가 mouse-down을 못 받아 `RequestNavigate`가 발화 안 됨 → 일반 클릭·Ctrl+클릭 모두 링크가 안 열림. 수정: `FindHyperlink(e.OriginalSource)`(`VisualTreeWalker.GetAnyParent`로 비-Visual Run/Span 부모까지 거슬러 올라가 `Hyperlink` 탐색)로 링크 클릭이면 `Process.Start`(`UseShellExecute=true`)로 직접 URL 열고 `e.Handled=true; return`. 링크가 아니면 기존 selection 차단 로직 유지.

## 최근 변경 (2026-05-15, 노트북 드래그 정렬 + 가장자리 자동 스크롤 + 미니맵 + 표 편집 버그 수정)

- **노트북 드래그 재정렬**: `Notebooks.SortOrder INTEGER` 컬럼 + 마이그레이션(초기값=Id, 신규 노트북은 SortOrder=Id로 맨 아래). `GetNotebooks()` `ORDER BY SortOrder, Id`. `DatabaseService.UpdateNotebookOrder(orderedIds)` 트랜잭션 일괄 저장, `MainViewModel.MoveNotebook(nb, newIndex)`. 사이드바 `ListBox`에 `AllowDrop` + `PreviewMouseLeftButtonDown/Move`(4px 임계 후 `DragDrop.DoDragDrop`) + `DragOver`/`Drop`. 드롭 위치는 `ComputeDropIndex`(각 `ListBoxItem` 중점 비교), 자기 자신 제거 보정. 삽입선은 `Controls/InsertionLineAdorner`(AdornerLayer). ⋯ 메뉴 버튼 위 클릭은 드래그 제외.
- **노트 드래그 중 가장자리 자동 스크롤**: `DraggableNoteControl`에 16ms `_dragTimer` — 드래그 시작(타이틀바/본문) 시 start, 종료/Unloaded 시 stop. 매 tick `MainWindow.AutoPanForDrag(Mouse.GetPosition(CanvasArea))` 호출 → 가장자리 56px 영역이면 침투 비율에 비례해(최대 24px/tick) `CanvasTranslate` 이동. 이어서 `ApplyDrag()`가 `Mouse.GetPosition(canvas)`(=월드 좌표, pan 반영됨) 기준으로 노트 위치 재계산 → 노트가 커서 따라 새 영역으로 끌려감. 기존 `TitleBar_MouseMove`는 `ApplyDrag()` 위임으로 리팩터링(MouseMove/타이머 공용).
- **미니맵**: 우하단 인디케이터에 🗺 토글 추가(기본 숨김). `MinimapToggle_Click`이 `MinimapHost` 표시 + 120ms 타이머로 `RefreshMinimap()` — 전체 노트/그룹 + 현재 뷰포트 bbox(+30 패딩)를 240×170 캔버스에 uniform scale 축소, 노트는 타입별 색 사각형, 그룹은 외곽선, 현재 화면은 파란 사각형. 타이머 단일 주기 갱신으로 pan/zoom/노트 이동/추가 모두 커버. 미니맵 클릭·드래그 → `CenterViewOnMinimapPoint`로 해당 월드 좌표를 화면 중앙으로(현재 줌 배율 반영).
- **표 편집 다이얼로그 버그 수정 3건**: ① 열 추가/삭제/이름변경 후 DataGrid에 즉시 반영 안 됨 → `RebindGrid()`(ItemsSource null 후 재설정)로 컬럼 강제 재생성. WPF DataGrid는 바인딩된 DataTable의 컬럼 변경을 자동 재생성하지 않음. ② placeholder 행이 실제보다 하나 더 보임 → `CanUserAddRows=False`(행 추가는 "+ 행" 버튼). ③ 컬럼 헤더 더블클릭이 안 먹힘 → `MouseDoubleClick` EventSetter를 `PreviewMouseLeftButtonDown` + `e.ClickCount==2` 검사로 교체(컬럼 리오더/리사이즈와 공존, 더블클릭만 Handled).
- **설정 단축키 안내 보강**: `Ctrl+E`(표 편집 다이얼로그) 항목 추가 + 노트북 드래그 정렬 / 미니맵 / 가장자리 자동 스크롤 안내 추가.

## 최근 변경 (2026-05-14, 표 편집 DataGrid 다이얼로그 + 저장 시 정렬)

- **`Dialogs/TableEditorDialog` 신규 (WPF DataGrid + DataTable)**: 표 편집을 raw 텍스트 가공 대신 WPF 순정 `DataGrid`에 위임. 마크다운 ↔ `DataTable` 양방향 변환 + 행/열 추가·삭제 버튼 + 컬럼 헤더 더블클릭 이름 변경. `DraggableNoteControl.EditTableAtCaret`이 캐럿 표 영역을 식별해 다이얼로그 호출 + 결과로 raw 마크다운 치환. **`Ctrl+E`** 단축키로 진입.
- **자동 정렬은 LostFocus(편집 종료) 시점만**: 매 키·Tab·Enter 정렬 모두 제거. 사용자 입력 중에는 raw 그대로 — IME/캐럿/컬럼 삭제 복구 문제 모두 해결. 편집 종료 시 `AlignAllTablesInText`가 노트 안의 모든 마크다운 표를 한 번에 정렬. 알고리즘은 px 측정(`FormattedText.Width`) 기반으로 컬럼별 max 폭 + 좌우 공백 1자 패딩 + separator는 같은 px 폭의 `---`.
- 자동 정렬 관련 헬퍼 대거 정리: `LocateCaretInTable`/`LocateCharInTable`/`DisplayWidth`/`IsWideChar` 제거 (캐럿 보존이 불필요해짐). `SplitTableCells`/`IsTableRowLineStr`/`IsSeparatorOnly`/`MeasurePx`는 정렬 함수에서 재사용.

## 최근 변경 (2026-05-14, 자동 정렬 트리거 단순화)

- **매 키 자동 정렬 → Tab/Enter/LostFocus 시점만**: TextChanged 디바운스(`DispatcherTimer`) 트리거 자체 제거. 한글 IME 합성 도중 Text 통째 set이 자모 상태를 깨뜨려 후속 space 입력이 누락되는 버그 해결. 사용자 입력 중에 표가 자꾸 흔들리지 않음. Tab 누를 때 / Enter로 새 행 만들 때 / 편집 종료(`TextEditor_LostFocus`) 시점에만 `AlignTableAtCaret()` 호출.
- **빈 셀 자동 추가 X (컬럼 삭제 가능)**: 기존엔 `colCount = max(all rows)`로 짧아진 행에 빈 셀이 자동 추가돼 사용자가 한 행에서 컬럼을 지워도 정렬 후 복구됨. 수정: 재조립 시 각 행을 `rows[ri].Length`만큼만 출력 — 짧은 행은 그대로 짧음. 사용자가 컬럼 삭제 의도면 그대로 유지됨.
- **active 셀 raw 보존 로직 제거 (단순화)**: 매 키 정렬이 없어졌으니 active 셀이 정렬 중에 변경되는 시점 = 사용자가 셀을 떠나는 시점(Tab/Enter/LostFocus). 그 시점엔 모든 셀 정렬되는 게 자연스러움. raw offset 추적, `SplitTableCellsRaw`, active 셀 분기 모두 제거.

## 최근 변경 (2026-05-14, 자동 정렬 정확도 + 활성 셀 보존)

- **px 측정 기반 정렬**: 글자 수(`DisplayWidth`) 기반은 monospace + 한글에서 폰트 fallback 비율이 정확히 2배가 아니라 ±0.x cell 어긋남. `FormattedText.Width`로 모든 셀 content를 px로 측정해 padding 공백 개수 = `round((maxPx - cellPx) / spaceW)`, separator dash 개수 = `round(maxPx / dashW)`로 정확히 같은 px 폭. 한글이 영문의 정확히 2배가 아니어도 ±0.5 cell 안으로 보정.
- **사용자 편집 중인 셀은 raw 그대로 (정렬 제외)**: 기존엔 active 셀도 trim+re-pad해서 캐럿이 우측 새 공백 자리로 강제 이동 → backspace 시 공백이 먼저 지워지는 문제. 수정: `LocateCaretInTable`가 raw offset과 content offset 둘 다 반환, `LocateCharInTable`에서 active 셀이면 raw offset 그대로 사용해 캐럿 위치·trailing 공백 그대로 유지. 다른 셀들만 padding 조정으로 컬럼 정렬. `SplitTableCellsRaw` 헬퍼 추가.

## 최근 변경 (2026-05-14, 편집 표 자동 정렬)

- **편집 모드 표 자동 정렬**: 캐럿이 표 행 안에 있을 때 `TextChanged` 후 300ms 디바운스(`DispatcherTimer`)로 표 전체를 재포맷. 컬럼별 max display width(`DisplayWidth` — 한글/CJK는 2, ASCII는 1)로 모든 셀에 좌우 공백 패딩. separator 행은 `---`로 같은 폭. 재조립 후 `TextEditor.Text` 통째 교체 + 캐럿을 같은 (row, cell, offsetInCell)로 복원(`LocateCaretInTable`/`LocateCharInTable`). 한글 IME 합성 중에 Text 교체 시 자모 분리 위험이 있으나 300ms 디바운스로 보통 합성 종료 후 동작. 자체 갱신이 재귀 호출 안 되도록 `_suppressAlign` 플래그. 새 헬퍼: `SplitTableCells`/`IsSeparatorOnly`/`DisplayWidth`/`IsWideChar`.

## 최근 변경 (2026-05-14, 표 그리드 fix + 셀 폭 측정)

- **편집 모드 그리드 정확도 fix**: v1.0.25에서 첫 행 `|` X로 전체 표 세로선을 그려 다른 행의 `|`와 어긋남. 또 cell 좌측 경계(`r.X`)를 사용해 monospace `|` glyph(cell 중앙)와 시작부터 misalign. 수정: 각 행을 독립 박스로 그리고 세로선 X = `r.X + r.Width / 2`(cell 중앙). 글자 늘어나도 그 행의 자기 `|` 위치 따라 사각형이 이동.
- **표시 모드 셀 폭 컨텐츠 기반 측정**: WPF FlowDocument의 `TableColumn.Width = GridLength.Auto`가 컨텐츠 폭에 맞춰 자동 조정되지 않아 균등 분배되는 문제. `FormattedText`로 헤더·각 데이터 행 셀 텍스트의 max 폭을 측정 후 `new GridLength(maxW + 16)`로 명시. `MeasureWidth(text, fontSize, isHeader)` 헬퍼 + `ExtractCellText(MdTableCell)`/`AppendInlineText` 추가. `Render` 시작 시 `_currentFontFamily` static에 저장해 측정 시점 폰트 일치. `BuildTable`/`BuildSqlResult` 양쪽 적용.
- **아이콘 경로 매크로화**: csproj `ApplicationIcon`/`Resource Include`가 `..\res\icon.ico` 상대 경로 사용 시 WPF temp project(`wpftmp.csproj`)에서 상대 해석이 깨져 빌드 실패. `$(MSBuildThisFileDirectory)..\res\icon.ico`로 절대화. `ApplicationIcon`은 **.ico 포맷만** 가능 — PNG 사용 불가, multi-resolution ico 권장.

## 최근 변경 (2026-05-14, 앱 아이콘 적용)

- **`res/icon.ico` 앱 아이콘 적용**: csproj에 `<ApplicationIcon>..\res\icon.ico</ApplicationIcon>` + `<Resource Include="..\res\icon.ico" Link="res\icon.ico" />` 추가. ApplicationIcon은 .exe PE 리소스로 박혀 Windows 탐색기·작업 표시줄·창 아이콘에 자동 반영. Resource는 pack URI(`pack://application:,,,/res/icon.ico`)로 런타임 로드용. `App.LoadAppIcon()`이 pack URI로 스트림을 열어 `System.Drawing.Icon` 생성 → `NotifyIcon.Icon`에 적용. 실패 시 `SystemIcons.Application` fallback.

## 최근 변경 (2026-05-14, 수동 업데이트 확인)

- **트레이 메뉴에 "업데이트 확인..." 추가**: 기존 자동 체크(메인 창 첫 가시 시 1회)에 더해 사용자가 임의 시점에 수동 호출 가능. `UpdateService.CheckAndPromptAsync`에 `bool manual = false` 파라미터 추가 — manual=true면 "이미 최신 버전입니다" / "개발 빌드는 미지원" / 네트워크 실패 메시지를 모두 사용자에게 표시(자동 체크 시엔 silent 유지). 트레이 메뉴 클릭 시 `App.CheckForUpdatesManual` → `ShowMain()` 호출 후 다이얼로그 owner를 메인 창으로 지정.

## 최근 변경 (2026-05-14, 편집 표 셀 그리드 오버레이)

- **편집 모드 표 셀 그리드**: `Controls/TableGridAdorner` 신규 — TextBox 위에 `AdornerLayer`로 표 셀 외곽선만 오버레이. `IsHitTestVisible=false`로 입력·캐럿·IME에 전혀 영향 없음. raw 마크다운 텍스트 그대로 보존. 표 행은 양 끝이 `|`이고 가운데에 `|`가 1개 이상인 줄로 판정. 첫 표 행의 각 `|` X 좌표(`GetRectFromCharacterIndex`)에서 표 시작 Y → 표 끝 Y까지 세로선, 각 행 상단에 가로선 + 표 하단. `DraggableNoteControl`이 BeginEdit 시 `AdornerLayer.GetAdornerLayer(TextEditor).Add(_tableAdorner)`, LostFocus 시 Remove. TextChanged/SizeChanged/ScrollChanged 모두 `_tableAdorner?.InvalidateVisual()` 트리거.

## 최근 변경 (2026-05-14, 설정 통합 + 편집 표 가독성)

- **설정 다이얼로그 통합**: `Dialogs/SettingsDialog` 신규 — 기존 `HotkeySettingsDialog` + `FontSettingsDialog` + 트레이 "자동 실행" 토글을 한 곳으로 합침. 진입점은 사이드바 "설정" 버튼 + 트레이 "설정..." 두 곳. 기존 두 다이얼로그·App의 `_autoStartMenuItem`/`OnAutoStartToggleClicked` 제거. `App.ShowHotkeySettings` → `ShowSettings`로 이름 변경.
- **편집 시 고정폭 폰트 옵션** (`AppSettings.EditorMonospace`, 기본 ON): 편집 모드만 Consolas 등 monospace 사용, 표시 모드는 `NoteFontFamily` 유지. 표 입력 시 셀 정렬에 유리.
- **표 셀 폭 컨텐츠 맞춤**: WPF `Table.Columns`의 `TableColumn.Width = GridLength.Auto`로 변경 (이전: 1* 균등 분포 → 컨텐츠 길이 짧아도 폭 넓게 차지). `BuildTable`/`BuildSqlResult` 둘 다 적용.
- **줄 번호 절대 좌표 배치**: 기존 단일 `TextBlock`이 LineHeight 차이로 본문과 누적 어긋남. `LineNumberBorder + Canvas`로 교체 후 각 logical 줄에 대해 `TextEditor.GetRectFromCharacterIndex`로 정확한 Y를 얻어 `Canvas.SetTop` 절대 배치. 스크롤 시 `ScrollChanged`에서 `UpdateLineNumbers` 재호출(좌표 자체가 visual 좌표).

## 최근 변경 (2026-05-14, Vi 제거 + 줄 번호 wrap 정렬)

- **Vi 모드 전체 제거**: `Services/ViController.cs` 삭제. `AppSettings`에서 `ViModeEnabled`/`ViStartInNormal` 필드 제거. `HotkeySettingsDialog`에서 Vi 체크박스 제거. `MainWindow.TryHandleCanvasVi` 메서드 + `_viPending` 필드 제거. `DraggableNoteControl`에서 `_vi` 필드 + EnsureViController/OnViStateChanged/OnViQuitRequested/UpdateViBadge 제거 + ViModeBadge/CommandBar XAML 제거. `Esc`로 편집 종료 복원(Shift+Enter 종료 분기는 제거). 향후 vi 지원은 신뢰도 높은 WPF 순정 플러그인이 등장하면 재검토. 그 전까지는 자체 구현하지 않음(검증 + 텍스트 객체/카운트/operator 결합 등 풀 vim grammar 비용이 큼).
- **줄 번호 wrap 정렬 fix**: 기존엔 `\n` 갯수로 줄 번호 계산 → `TextWrapping="Wrap"`인 TextBox에서 한 logical 줄이 시각적으로 여러 줄을 차지하면 정렬이 어긋남. 수정: `TextBox.LineCount`(wrap 포함 visual line) 개수만큼 라인 출력 + 각 visual line의 첫 char를 `GetCharacterIndexFromLineIndex`로 얻어 `text[charIdx-1] == '\n'`이면 logical 줄 시작으로 판정 → 그 행에만 숫자, wrap된 줄은 빈 라벨. `TextChanged`/`SizeChanged` 시 `Dispatcher.BeginInvoke(Loaded)`로 layout 후 재계산. gutter 폭 36 → 40으로 살짝 키움.

## 최근 변경 (2026-05-14, SQL 쿼리 노트)

- **마크다운 표 → SQLite → 쿼리 결과 표 (Obsidian Dataview-like)**: `Services/QueryEngine.cs` 신규. 모든 텍스트 노트를 스캔해 헤딩(`# / ## / ...`) 직후 마크다운 표를 메모리 SQLite의 동적 테이블로 매핑. 헤딩 텍스트가 테이블 이름, 헤더 셀이 컬럼명(모두 quoted identifier 사용 — 한글 OK). 텍스트 노트 안에 ` ```sql ` 펜스가 있으면 `MarkdownRenderer.BuildSqlResult`가 그 자리에 결과 표를 그린다. 편집 모드에선 raw SQL 그대로 보이고, 표시 모드에서만 결과 렌더링.
  - 자동 갱신: `MainViewModel.UpdateNoteContent`(편집 종료 시)에서 `RebuildQueryEngine()` 호출 + `QueryResultsInvalidated` 이벤트 발화. `DraggableNoteControl`이 이벤트 구독 — Content에 ` ```sql ` 포함하는 표시 모드 노트만 `RefreshDocument`로 다시 그림(비용 절감).
  - 노트북 전환 시(`OnCurrentNotebookChanged`)도 Rebuild.
  - SQL 오류는 결과 영역에 빨간 글씨로 표시. 결과 0행/0열은 회색 안내 문구.
  - 값은 모두 TEXT 저장 — 숫자 비교는 사용자가 `CAST("level" AS INTEGER) > 50` 식으로 작성.

## 최근 변경 (2026-05-14, 핫픽스)

- **편집 종료 트리거 변경**: Esc → Shift+Enter. Esc는 Vi Normal 모드 진입(혹은 Visual→Normal)으로 양보. Vi OFF 환경에서도 Esc는 무동작 — 명시적 종료는 Shift+Enter 또는 vi `:q`만. `DraggableNoteControl.TextEditor_PreviewKeyDown` 맨 앞에 Shift+Enter 분기 추가, 기존 Esc → FocusCanvas 종료 분기 제거.
- **표 Enter separator 자동 추가 버그 수정**: 데이터 행 마지막 줄에서 Enter치면 separator(`|---|---|`)가 또 생기던 문제. `nextLs >= text.Length`에서 무조건 needSeparator=true로 두던 로직을 `IsHeaderRow` 헬퍼로 교체 — 위쪽 줄이 이미 표 행이면 현재 줄은 헤더 아님으로 판정. 또한 헤더 행 끝에서 Enter 쳤는데 이미 separator가 아래에 있으면 새 데이터 행을 separator 다음에 삽입(표 구조 보존).
- **`ShowLineNumbers` 기본값 ON**: 옵션이 단축키 설정에 묻혀 있어 발견성 낮음 → 기본 활성. 끄려면 단축키 설정 → 편집기 옵션에서 체크 해제.

## 최근 변경 (2026-05-14)

- **마크다운 파이프 테이블 렌더링**: `Services/MarkdownRenderer.cs`의 `MarkdownPipeline`에 `UsePipeTables()` 추가. `ConvertBlock` switch에 `Markdig.Extensions.Tables.Table` 분기 + `BuildTable` 헬퍼 추가 — WPF `Table`/`TableRowGroup`/`TableRow`/`TableCell`로 매핑. 헤더 굵게 + #3A3A3A 배경, 본문 행 하단 1px #333 가로선, 컬럼 정렬은 `TableColumnDefinition.Alignment` → `TextAlignment` 변환.
- **표 편집 스마트 보조**: `Controls/DraggableNoteControl.xaml.cs`의 `TextEditor_PreviewKeyDown`에 Tab/Enter/Ctrl+T 분기. 표 행 판정 헬퍼(`IsTableRowLine`/`IsSeparatorLine`/`CountColumns`)로 캐럿이 표 안에 있을 때만 동작 — 그 외엔 기존 들여쓰기/기본 동작 유지. Tab: 다음 셀(첫 공백 1개 스킵), Shift+Tab: 이전 셀, 행 마지막에서 Tab은 다음 행 첫 셀 또는 자동 새 행 추가. Enter는 헤더 줄 끝에서 separator+빈 데이터 행 자동, 데이터 행 끝에선 빈 행 1개 추가. Ctrl+T는 `Dialogs/InsertTableDialog`로 N×M 빈 표 마크다운 삽입.
- **Mini-Vi 모드 (옵트인)**: `AppSettings.ViModeEnabled` + `ViStartInNormal` + `ShowLineNumbers` 추가, `HotkeySettingsDialog` 하단에 "편집기 옵션" 섹션. `Services/ViController.cs` 신규 — TextBox 한 개에 대한 상태 머신(`Mode { Insert, Normal, VisualChar, VisualLine, Command }`). `OnPreviewKeyDown(KeyEventArgs)`가 호스트의 `TextEditor_PreviewKeyDown`에서 호출되어 Handled 여부 반환. 자체 undo 스택(`LinkedList<TextSnapshot>` 양방향 — TextBox `IsUndoEnabled=false` 우회). Visual 모드는 `_visualAnchor`와 캐럿 사이 선택, VisualLine은 줄 단위 확장. `:` Command 모드는 `OnTextInput`으로 글자 누적, Enter로 실행(`:q`/`:wq`/`:x` → `QuitRequested` 이벤트, `:w` no-op). 한글 IME는 `Key.ImeProcessed`/`Key.DeadCharProcessed` 키를 무조건 양보해 충돌 회피.
  - 캔버스 매핑은 `MainWindow.TryHandleCanvasVi` — `Window_PreviewKeyDown` 앞부분에 vi 활성 + TextBox 밖 + ListBox 밖일 때 우선 처리. `h/j/k/l` 이동, `Ctrl+hjkl` nudge, `i`/`a`/`Enter` 편집, `o`/`O` 새 노트(아래/위), `x` 삭제, `dd`/`yy`/`p`/`u`/`Ctrl+r`/`gg`/`G`. 연속 키는 `_viPending` + 1초 만료.
  - 편집 모드 UI: 타이틀바 우측 모드 배지(`ViModeBadge`: INS/NRM/VIS/V-LN/CMD), Normal에서 `TextEditor.CaretBrush`를 주황(#FF9933)로, Command 모드는 하단 `CommandBar`에 `:cmd...` 표시.
  - **줄 번호 gutter**: `EditorContainer`를 Grid+ColumnDefinitions로 재구성 — 좌측 `LineNumberScroll(ScrollViewer)` + 우측 `TextEditor`. `ShowLineNumbers` ON일 때만 36px 폭. `UpdateLineNumbers`가 `TextChanged`에서 줄 수만큼 1..N 문자열 갱신, `HookEditorScroll`이 visual tree에서 TextBox 내부 ScrollViewer를 찾아 `ScrollChanged` → 좌측 gutter `ScrollToVerticalOffset` 미러링.

## 최근 변경 (2026-05-02)

- **마크다운 노트 첫 표시 성능 개선 + 이모지 흰색 tint fix (v1.0.18)**: 노트북 열 때 N개 텍스트 노트가 모두 동시에 동기 Markdig 파싱 + 임베드 이미지 디스크 read를 UI 스레드에서 수행해 첫 표시 freeze. 그리고 v1.0.17 시도(즉시 `EmojiInline.Child.Effect = null`)는 효과 없었던 이모지 흰색 tint도 같은 릴리즈에서 해결됨. 네 가지 fix:
  - `DraggableNoteControl.UserControl_Loaded`에서 `RefreshDocument`를 `Dispatcher.BeginInvoke(DispatcherPriority.Background)`로 미룸 — 첫 표시는 빈 FlowDocument 즉시, 마크다운은 백그라운드로 채워짐
  - `Services/ImageCache`(static, lock 보호) 신규 — 경로(+다운샘플 폭) 단위로 frozen `BitmapImage` 캐시. 같은 이미지를 여러 노트가 참조하면 디스크 read 1회. 노트 삭제 시 `Invalidate(path)` 호출
  - `MarkdownRenderer.TryBuildImage`가 `ImageCache.Get(path, 800)`로 변경 — 임베드 이미지에 다운샘플 적용 (이전엔 풀 해상도). `PathToImageConverter`도 동일 캐시 사용
  - 이모지 흰색 tint: 즉시 `Effect=null`에 더해 `EmojiInline.Loaded` 이벤트 + 그 안의 `Dispatcher.BeginInvoke(Loaded priority)`로 Loaded 이후에도 한 번 더 `Effect=null` 적용. Emoji.Wpf 라이브러리가 Loaded 이후 시점에 `TintEffect`를 재적용하는 것으로 추정 — 그 뒤에 덮어쓰는 패턴이 동작

  뒤로 미룬 것: 마크다운 AST/FlowDocument 캐시 (FlowDocument는 한 visual에 한 번만 attach 가능해 캐싱 까다로움), 노트 가상화 (Pan/Zoom 좌표 기반 spatial index 필요 — 노트 100+ 단위에서 의미). [향후 거대 노트북 사용 시 검토]

- **텍스트 노트 마크다운 렌더링 + 컬러 이모지 + 이미지 임베드 + 폰트 설정**: 표시 측 `TextBlock`을 `FlowDocumentScrollViewer`로 교체하고 `Services/MarkdownRenderer`(Markdig 0.42 + Emoji.Wpf 0.3.4)가 raw 마크다운을 `FlowDocument`로 변환. H1~H6, 불릿/번호 리스트, 코드 블록(회색 배경 Consolas), 인라인 코드, 강조, 링크, `![](...)` 이미지 임베드 지원. soft line break도 `LineBreak`으로 변환해 평문 노트 호환. 이모지 codepoint는 `Emoji.Wpf.EmojiInline`(InlineUIContainer 상속, Twemoji 컬러 이미지)로 분할 — ZWJ/skin tone modifier/variation selector를 묶어 한 inline으로 처리. 편집 측은 raw 마크다운 그대로 입력하는 `TextBox`(기존 IME 회피 구조 유지). 편집 중 `Ctrl+V`로 클립보드에 이미지가 있으면 `%APPDATA%\ffnotev2\images\{guid}.png`로 저장 후 `![](파일명)` 마크다운을 캐럿 위치에 자동 삽입(텍스트만 있으면 양보). `MainViewModel.SaveClipboardImageIfPresent()`/`ImagesDirectory`로 노출. `AppSettings`에 `NoteFontFamily`/`NoteFontSize` 추가, 사이드바 "폰트 설정" 버튼 + `Dialogs/FontSettingsDialog`(시스템 폰트 콤보 + 9~28 슬라이더 + 미리보기). 저장 시 `SettingsService.Save()` → `SettingsChanged` 이벤트 → 모든 `DraggableNoteControl`이 `OnSettingsChanged`로 RefreshDocument + ApplyEditorFont. `NoteItem.PropertyChanged`에 Content 구독 추가했지만 편집 중에는 `TextEditor.Visibility != Visible` 가드로 매 키스트로크 갱신을 스킵, 편집 종료(`LostFocus`) 시 1회 갱신.

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
- **마크다운 편집 모드는 컬러 이모지 미적용**: TextBox는 inline 객체를 못 담으므로 raw 편집 시엔 시스템 기본(흑백) 그대로. 표시(FlowDocument) 모드에서만 컬러 이모지
- **이모지 codepoint 판별 휴리스틱**: `MarkdownRenderer.IsEmojiCodepoint`는 주요 유니코드 이모지 블록만 매칭. Unicode 16+ 신규 이모지는 누락될 수 있음 → 필요 시 범위 확장

## TODO (다음 작업 후보)

배치 기능 로드맵:
- [ ] **C. 자동 밀집** (보류) — 가변 크기 노트 bin packing 알고리즘 검토 필요. 최상위 그룹 기준 정렬 + 그룹 단위 정렬도 같이 검토

기타:
- [ ] **Vi 모드 재검토** (보류) — 신뢰도 높은 WPF 순정/MS 공식 vim 플러그인이 등장하면 채택. 자체 구현은 ciw/V4j/d2w 같은 텍스트 객체·카운트·operator 결합 grammar 비용이 커 보류. AvalonEdit + vim 라이브러리도 TextBox 기반 ffnote 구조와 호환성이 낮아 큰 변경 필요
- [ ] **코드 사이닝** (공개 배포 시점에) — SignPath.io Foundation 신청(OSS 무료 EV) 또는 Microsoft Trusted Signing 가입. workflow에 서명 단계 추가
- [ ] 캔버스 뷰 상태(pan/zoom) 노트북별 DB 저장/복원
- [ ] 노트 색상 변경 기능
- [ ] 노트 검색 / 필터
- [ ] 백업·Export·Import (SQLite + 이미지 zip)
- [ ] DPI 스케일 변경 시 오버레이 위치 보정 (현재는 픽셀 단위 저장만)
- [ ] 다국어

## 완료된 작업 (참조)

- [x] **노트북 드래그 정렬 (사이드바)** — `SortOrder` 컬럼 + `MoveNotebook` + `InsertionLineAdorner`
- [x] **노트 드래그 중 가장자리 자동 화면 스크롤** — `_dragTimer` + `MainWindow.AutoPanForDrag`
- [x] **미니맵 ("여기로 이동" 대체)** — 우하단 🗺 토글, 전체 노트 영역 + 뷰포트 표시, 클릭/드래그로 이동
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
- **노트 텍스트는 FlowDocumentScrollViewer(표시·마크다운) ↔ TextBox(편집·raw) 스왑**: WPF `TextBox.IsReadOnly` 토글 시 IMM 컨텍스트가 분리된 채 재연결되지 않아 한글이 자모 분리되는 알려진 버그. 표시·편집을 별도 컨트롤로 분리해 IsReadOnly 전환 자체를 제거. 표시 측은 TextBlock에서 FlowDocumentScrollViewer로 격상되어 마크다운 렌더링과 컬러 이모지를 지원
- **마크다운 렌더링은 FlowDocument 직접 변환 (WebView2 미사용)**: 게임 위 다수 노트가 핵심 UX라 가벼움이 중요. 노트마다 WebView2를 띄우면 메모리·시작 지연 부담. Markdig AST를 직접 visitor로 FlowDocument 블록/inline에 매핑. soft line break도 hard로 처리해 평문 노트 호환 유지
- **컬러 이모지는 Emoji.Wpf**: WPF는 .NET 10까지도 Segoe UI Emoji의 컬러 글리프를 직접 렌더하지 못함. `Emoji.Wpf.EmojiInline`(`InlineUIContainer` 상속)이 codepoint를 Twemoji 이미지로 치환. `MarkdownRenderer.AppendTextWithEmoji`가 surrogate pair 단위 스캔으로 ZWJ/skin tone modifier/variation selector를 묶어 한 inline으로 처리
- **글로벌 단축키 사용자 설정**: `%APPDATA%\ffnotev2\settings.json`에 저장. 트레이 메뉴 → "단축키 설정" → 캡처 박스에서 키 조합 입력. 저장 시 `MainWindow.ReregisterHotkeys()`가 `HotkeyService.UnregisterAll()` 후 재등록
- **오버레이 초안 자동 저장**: `OverlayViewModel.OnQuickNoteTextChanged` partial 메서드에서 매 글자 변경 시 `SettingsService.Save()`. JSON 작은 파일이라 IO 부담 미미. 필요 시 디바운싱 추가 가능
- **Pan/Zoom은 `ItemsControl.RenderTransform`에 적용**: ItemsPanel(Canvas)가 아닌 ItemsControl 자체에 적용. `Canvas.ClipToBounds=False`로 노트가 영역 밖에서도 렌더, 부모 `CanvasArea.ClipToBounds=True`로 column 경계에서 자른다. `e.GetPosition(canvas)`는 RenderTransform과 무관하게 캔버스 로컬(world) 좌표를 반환하므로 드래그 로직은 그대로 동작
- **우클릭 팬 vs 컨텍스트 메뉴**: 4px 임계값으로 구분. 종료 처리는 `Canvas_MouseRightButtonUp`(`MouseUp`보다 먼저 발생할 수 있음)에서 직접 수행하고 `e.Handled=true`로 메뉴 억제. `LostMouseCapture`로 안전망
- **Ctrl+휠은 `PreviewMouseWheel`(터널)**: 노트 내부 `ScrollViewer`가 wheel을 소비하기 전에 가로채야 안정적
- **키 입력 단위 즉시 DB 저장**: `UpdateSourceTrigger=PropertyChanged` + 노트 `PropertyChanged` 구독. SQLite 단일 row UPDATE는 ms 미만이라 키 입력 빈도에서도 충분. 트레이 종료/창 닫기로 LostFocus가 안 떨어지는 시나리오에서도 손실 없음
- **10px 격자 스냅**: `MainViewModel.Snap()` 한 곳에 모음. 시각적 격자 배경 없이 동작만 적용 (사용자 결정). 드래그 중 매 프레임 스냅으로 사용자가 즉시 격자를 체감
