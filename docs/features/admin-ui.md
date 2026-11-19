# admin-ui

파일: src/Biz.Bizadm.PowerManager.Server.Spa/Admin/AdminPortOnlyAttribute.cs, src/Biz.Bizadm.PowerManager.Client/package.json, src/Biz.Bizadm.PowerManager.Client/package-lock.json, src/Biz.Bizadm.PowerManager.Client/src/index.css, src/Biz.Bizadm.PowerManager.Client/src/components/ui/button.tsx, src/Biz.Bizadm.PowerManager.Client/src/components/ui/input.tsx, src/Biz.Bizadm.PowerManager.Client/src/components/ui/table.tsx, src/Biz.Bizadm.PowerManager.Client/src/components/ui/skeleton.tsx, src/Biz.Bizadm.PowerManager.Client/src/components/ui/alert.tsx, src/Biz.Bizadm.PowerManager.Client/src/components/AppShell.tsx, src/Biz.Bizadm.PowerManager.Client/src/App.tsx

관리 화면(`Biz.Bizadm.PowerManager.Client`) 전체가 따르는 디자인 기준과 앱 틀, 관리 API의 포트 제한. 디자인은 2026-11-19 시안(https://claude.ai/artifact/LQMfPZ4gsT4t5kJYuKMBPU 의 "A. 테이블안")으로 확정했다. 관리 화면에는 아직 인증이 없으며, 인증은 별도 일감이다(그 전까지 관리 포트를 외부에 노출하지 않는 운영을 전제로 한다).

## 요구(완료 조건)

- R1. 테마 토큰: 색·모서리 반경은 `src/index.css`의 shadcn 토큰(`radix-nova` 스타일, `neutral` 기본색, 라이트)만 쓴다. 화면 코드에 색 값을 직접 쓰지 않고 토큰 클래스(`bg-background`, `text-muted-foreground`, `border` 등)를 쓴다. 다른 디자인 시스템이나 UI 라이브러리를 들이지 않는다. 다크 모드는 아직 지원하지 않는다.
- R2. 글꼴: `--font-sans`는 `'Geist Variable', 'Noto Sans KR Variable', sans-serif`이며, 두 글꼴 모두 fontsource 패키지로 번들한다(사용자 PC 글꼴에 의존하지 않음). 식별자 값(MAC 등)은 고정폭(`font-mono`)으로 표시한다.
- R3. 앱 틀(`AppShell`): 왼쪽 사이드바(제품명 "PowerManager", 메뉴, 현재 메뉴 `aria-current="page"`)와 오른쪽 본문(`main`)이다. 폭 1440px에서는 나란히, 둘이 한 줄에 들어가지 않는 폭(390px 등)에서는 사이드바가 본문 위로 쌓이며 페이지 가로 스크롤이 생기지 않는다. 화면이 없는 메뉴는 넣지 않는다.
- R4. UI 부품: 버튼·입력·표·스켈레톤·알림은 `components.json` 설정으로 shadcn CLI가 추가한 `src/components/ui/` 부품을 쓴다. 넓은 표는 테두리 상자 안에서 가로 스크롤한다. 본문 머리는 제목(`h1`) + 보조 문구, 오른쪽에 주요 동작 버튼을 둔다.
- R5. 상태 화면: 처음 불러올 때는 결과와 같은 모양의 스켈레톤(`role="status"`와 이름), 빈 목록·검색 결과 없음은 점선 테두리 안내(아이콘·제목·설명·필요하면 동작 버튼), 오류는 destructive 알림(`role="alert"`, 원인, "다시 시도")으로 보인다. 아이콘만 있는 버튼에는 `aria-label`, 장식 아이콘에는 `aria-hidden`을 붙이고, 모든 조작에 키보드 포커스 표시가 보인다.
- R6. 관리 API 포트 제한: 관리 API(`/api/admin/...`)에는 `[AdminPortOnly]`를 붙인다. 이 필터는 연결이 들어온 로컬 포트가 관리 포트(`HttpPort`·`HttpsPort`)가 아니면 404 ProblemDetails로 끝낸다(Host 헤더와 무관, TLS 리다이렉트 기존 동작은 그대로).

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 | 작업 문서 |
|---|---|---|---|---|
| 2026-11-19 | device-list-ui | R1~R6 처음 작성(관리 화면 디자인 기준·앱 틀·관리 포트 제한 신설, #17) | docs/specs/device-list-ui/ | 2026-11-19-device-list-ui.md |
