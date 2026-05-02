<!-- 최종 수정: 2026-05-02 -->
# ffnote v2

게임 플레이 중 자유 캔버스 기반으로 텍스트·이미지·링크를 러프하게 기록하는 Windows 데스크톱 노트 앱.  
게임별 노트북 분리, 게임 프로세스 자동 감지, 글로벌 단축키, 반투명 오버레이 입력창을 제공한다.  
텍스트 노트는 마크다운(헤더·리스트·코드블록·이미지 임베드)을 표시 모드에서 렌더링하며, 컬러 이모지와 사용자 폰트 설정을 지원한다.

**기술 스택**: C# / .NET 10 / WPF + SQLite (Microsoft.Data.Sqlite) + CommunityToolkit.Mvvm + Markdig + Emoji.Wpf + Velopack

## wiki 페이지

- [architecture.md](architecture.md) — 디렉터리 구조 및 모듈 관계
- [components.md](components.md) — 파일·클래스·함수 역할 표
- [setup.md](setup.md) — 빌드·실행·배포 방법
- [data-flow.md](data-flow.md) — 데이터 흐름 (붙여넣기, 저장, 게임 감지)
- [devnotes.md](devnotes.md) — TODO 및 알려진 이슈
