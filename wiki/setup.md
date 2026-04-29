<!-- 최종 수정: 2026-04-29 -->
# 빌드 및 실행

## 요구사항

- Windows 10 이상
- .NET 8 SDK (`dotnet --version` 으로 확인)
- Visual Studio 2022 또는 Rider (선택)

## 빌드

```bash
# Visual Studio에서: ffnotev2.sln 열기 → 빌드 (Ctrl+Shift+B)

# CLI에서:
cd ffnotev2
dotnet restore
dotnet build -c Release
dotnet publish -c Release -r win-x64 --self-contained false
```

## 실행

```bash
dotnet run --project ffnotev2/ffnotev2.csproj
# 또는 빌드 후 bin/Release/net8.0-windows/ffnotev2.exe 직접 실행
```

## 데이터 저장 위치

- DB: `%APPDATA%\ffnotev2\notes.db`
- 이미지: `%APPDATA%\ffnotev2\images\*.png`

## NuGet 패키지

| 패키지 | 용도 |
|--------|------|
| Microsoft.Data.Sqlite 8.0.0 | SQLite 데이터베이스 |
| CommunityToolkit.Mvvm 8.3.2 | ObservableObject, RelayCommand |
| (System.Windows.Forms 내장) | NotifyIcon (트레이) |
