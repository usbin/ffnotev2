<!-- 최종 수정: 2026-04-29 -->
# 빌드 및 실행

## 요구사항

- Windows 10 이상
- .NET 10 SDK (`dotnet --version` 으로 확인)
- Visual Studio 2022 17.10+ 또는 Rider (선택, `.slnx` 인식 필요)

## 빌드

```bash
# Visual Studio에서: ffnotev2.slnx 열기 → 빌드 (Ctrl+Shift+B)

# CLI에서:
dotnet restore ffnotev2.slnx
dotnet build ffnotev2.slnx -c Release
dotnet publish ffnotev2/ffnotev2.csproj -c Release -r win-x64 --self-contained false
```

## 실행

```bash
dotnet run --project ffnotev2/ffnotev2.csproj
# 또는 빌드 후 bin/Release/net10.0-windows/ffnotev2.exe 직접 실행
```

## 데이터 저장 위치

- DB: `%APPDATA%\ffnotev2\notes.db`
- 이미지: `%APPDATA%\ffnotev2\images\*.png`
- 설정: `%APPDATA%\ffnotev2\settings.json` (단축키, 오버레이 투명도, 오버레이 초안)

## NuGet 패키지

| 패키지 | 용도 |
|--------|------|
| Microsoft.Data.Sqlite 10.0.7 | SQLite 데이터베이스 |
| CommunityToolkit.Mvvm 8.4.2 | ObservableObject, RelayCommand |
| (System.Windows.Forms 내장) | NotifyIcon (트레이) |

## 주요 csproj 설정

- `TargetFramework`: `net10.0-windows`
- `UseWPF=true`, `UseWindowsForms=true` (트레이 아이콘용)
- `AllowUnsafeBlocks=true` (`LibraryImport` source generator 요구사항)
- `<Using Remove="System.Windows.Forms" />` (WPF/WinForms `MouseEventArgs` 충돌 방지)
