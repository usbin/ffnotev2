<!-- 최종 수정: 2026-05-02 -->
# 빌드·실행·배포

## 사용자 (포터블 다운로드)

1. GitHub Releases 페이지에서 최신 `ffnotev2-win-Portable.zip` 다운로드:
   <https://github.com/usbin/ffnotev2/releases/latest>
2. 원하는 폴더(USB·바탕화면 등)에 압축 해제
3. 폴더 안 `ffnotev2.exe` 실행
4. 시작 시 새 버전이 있으면 다이얼로그가 뜸 → `확인` 누르면 자동 다운로드 + 재시작

요구사항: .NET 10 Desktop Runtime (없으면 첫 실행 시 안내). 다운로드 가능: <https://dotnet.microsoft.com/download/dotnet/10.0>

### SmartScreen "Windows의 PC 보호" 경고 발생 시

코드 사이닝 인증서 미적용으로 첫 실행 시 SmartScreen 경고가 표시될 수 있음. 우회 방법 (3가지 중 택1):

- **빠른 방법**: 경고창에서 `추가 정보` 클릭 → `실행` 버튼이 노출됨
- **압축 해제 전 차단 해제**: 다운받은 zip 파일 우클릭 → `속성` → 하단 `보안: 차단 해제` 체크 → `확인` → 압축 해제하면 추출된 exe에 MOTW(Mark of the Web)가 붙지 않음
- **PowerShell 일괄 해제**: 압축 해제 후 폴더에서 `Get-ChildItem -Recurse | Unblock-File`

## 개발자 — 로컬 빌드/실행

### 요구사항

- Windows 10 이상
- .NET 10 SDK (`dotnet --version` 확인)
- Visual Studio 2022 17.10+ 또는 Rider (선택)

### 빌드

```bash
dotnet restore ffnotev2.slnx
dotnet build ffnotev2.slnx -c Release
```

### 실행 (개발 빌드)

```bash
dotnet run --project ffnotev2/ffnotev2.csproj
```

개발 빌드(설치 안 됨)는 `UpdateService.CheckAndPromptAsync`가 `_mgr.IsInstalled=false`를 보고 즉시 반환 — 자동 업데이트 흐름 방해 X.

## 개발자 — 새 버전 배포 (포터블)

릴리스 절차는 단순화: **태그 푸시만**. csproj의 `<Version>1.0.0</Version>`은 dev 빌드용 placeholder이고, CI가 태그명을 `-p:Version=X.Y.Z`로 오버라이드해서 `AssemblyVersion`/`FileVersion`을 자동 파생시킨다(`<Version>` 변경 불필요).

```bash
# 1) 변경사항 커밋
git commit -am "feat v1.2.3: ..."
git push origin main

# 2) 태그 push
git tag v1.2.3
git push origin v1.2.3
```

`v*.*.*` 태그가 푸시되면 `.github/workflows/release.yml`이 자동으로:
1. .NET 10 + Velopack CLI(`vpk`) 설치
2. `dotnet publish ... --no-self-contained -p:Version=<태그명>` (framework-dependent 단일 파일)
3. `vpk pack ... --noInst` (포터블 zip만 생성, Setup.exe 미생성)
4. `vpk upload github` 으로 Release 자동 생성 + 자산 업로드

사용자의 기존 설치는 다음 시작 시 새 버전 다이얼로그를 받음.

CI 빌드 실패 여부는 [GitHub Actions](https://github.com/usbin/ffnotev2/actions) 페이지에서 확인. 자동 업데이트가 안 오면 release artifact 자체가 안 만들어진 것 — 사용자 install된 exe의 파일 버전이 마지막 성공 release에 머물러있다.

### 로컬 수동 패키지 (Actions 없이 시험용)

```bash
dotnet tool install -g vpk
dotnet publish ffnotev2/ffnotev2.csproj -c Release -r win-x64 --no-self-contained -o publish -p:Version=0.1.1
vpk pack -u ffnotev2 -v 0.1.1 -p publish -e ffnotev2.exe --framework net10.0-x64-desktop --noInst
# → Releases/ 폴더에 ffnotev2-win-Portable.zip 생성
```

> bash 환경에서는 `/p:`가 별도 인자로 파싱되는 case가 있어 `-p:` 사용. PowerShell/cmd는 둘 다 OK.

## 데이터 저장 위치

- DB: `%APPDATA%\ffnotev2\notes.db`
- 이미지: `%APPDATA%\ffnotev2\images\*.png`
- 설정: `%APPDATA%\ffnotev2\settings.json` (단축키, 오버레이 투명도/위치)

업데이트 후에도 이 위치는 보존되므로 노트 데이터 손실 없음.

## NuGet 패키지

| 패키지 | 용도 |
|--------|------|
| Microsoft.Data.Sqlite 10.0.7 | SQLite |
| CommunityToolkit.Mvvm 8.4.2 | ObservableObject, RelayCommand |
| Velopack 0.0.* | GitHub Releases 자동 업데이트 |
| (System.Windows.Forms 내장) | NotifyIcon (트레이) |

## 주요 csproj 설정

- `TargetFramework=net10.0-windows`
- `UseWPF=true`, `UseWindowsForms=true` (트레이 아이콘용)
- `AllowUnsafeBlocks=true` (`LibraryImport` source generator)
- `<Using Remove="System.Windows.Forms" />` (WPF/WinForms `MouseEventArgs` 충돌 방지)
- `<Version>1.0.0</Version>` — dev 빌드 placeholder. CI는 `-p:Version=<태그명>` 오버라이드. `AssemblyVersion`/`FileVersion`은 자동 파생 (명시 X — 명시 시 `-p:Version` 오버라이드가 거기까지 못 미쳐 메타데이터 미스매치 발생)
- `PublishSingleFile=true`, `RuntimeIdentifier=win-x64`, `SelfContained=false` (포터블 단일 exe)
- `StartupObject=ffnotev2.App` + `<ApplicationDefinition Remove="App.xaml"/><Page Include="App.xaml"/>` — WPF auto-Main 비활성화하고 명시적 `[STAThread] Main`을 사용 (Velopack 훅이 가장 먼저 실행되도록)
