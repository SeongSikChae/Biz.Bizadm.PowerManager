# 장비 목록 화면 계획

## 요약

서버에 관리용 목록 API `GET /api/admin/devices`를 새 컨트롤러로 추가하고, 관리 포트가 아닌 포트에서 처리되는 요청은 리소스 필터로 404를 반환한다. 클라이언트는 앱 틀(사이드바 + 본문)과 장비 목록 화면을 만든다. 데이터는 이미 의존성에 있는 Redux Toolkit의 RTK Query로 불러오고, 거르기·정렬·페이지·시각 표기는 순수 함수로 분리해 테스트한다. UI 부품은 `components.json`에 설정된 shadcn CLI로 추가한다. 기준 spec `device-list.md`·`admin-ui.md`를 새로 만든다.

## 바꿀 파일·구조

서버:
- `src/Biz.Bizadm.PowerManager.Server.Spa/Admin/AdminPortOnlyAttribute.cs`(새 파일): 요청이 처리되는 로컬 포트(`HttpContext.Connection.LocalPort`)가 `AppConfiguration.HttpPort`·`HttpsPort`가 아니면 404 ProblemDetails로 끝내는 리소스 필터 attribute.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Devices/AdminDevicesController.cs`(새 파일): `[AdminPortOnly]`가 붙은 `GET /api/admin/devices`. `IDbContextFactory<PowerManagerDbContext>`로 조회해 `LastRequestedAt` 내림차순, `Mac` 오름차순으로 정렬한 `AdminDeviceResponse` 배열을 반환한다.
- `src/Biz.Bizadm.PowerManager.Server.Spa/Devices/AdminDeviceResponse.cs`(새 파일): 응답 record(필드 여섯 개).
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/AdminDeviceEndpointTests.cs`(새 파일): C1~C3 테스트.

클라이언트(`src/Biz.Bizadm.PowerManager.Client/` 아래):
- `package.json`, `package-lock.json`: 한글 글꼴 패키지 `@fontsource-variable/noto-sans-kr` 추가, shadcn CLI가 추가하는 의존성.
- `src/index.css`: 글꼴 import와 `--font-sans`에 한글 대체 글꼴 추가.
- `src/components/ui/button.tsx`, `input.tsx`, `table.tsx`, `skeleton.tsx`, `alert.tsx`(새 파일): `npx shadcn add button input table skeleton alert`로 추가.
- `src/components/AppShell.tsx`(새 파일): 사이드바(제품명 "PowerManager", "장비" 메뉴 `aria-current="page"`)와 본문. `flex-wrap`으로 좁은 폭에서 사이드바가 위로 쌓인다.
- `src/features/devices/devicesApi.ts`(새 파일): RTK Query `createApi`, `getDevices` 쿼리.
- `src/features/devices/deviceList.ts`(새 파일): 순수 함수 — 검색 거르기, 정렬, 페이지 자르기, 페이지 보정, 절대·상대 시각 표기.
- `src/features/devices/DevicesPage.tsx`(새 파일): 머리(제목·대수·새로고침), 검색, 표, 페이지 이동, 상태 화면.
- `src/features/devices/deviceList.test.ts`, `DevicesPage.test.tsx`(새 파일): C5~C10·C13 테스트.
- `src/app/store.ts`: 자리 표시 reducer를 지우고 `devicesApi` reducer·middleware 등록. 테스트가 매번 새 store를 쓰도록 `makeStore()`를 내보낸다.
- `src/App.tsx`: `AppShell` 안에 `DevicesPage`.
- `src/App.test.tsx`: 제목 테스트를 앱 틀 테스트(C4)로 바꾼다.

기준 spec:
- `docs/features/device-list.md`(새 파일): 목록 API·화면 요구.
- `docs/features/admin-ui.md`(새 파일): 디자인 기준과 관리 포트 제한 규칙.

## 결정

- 목록 API를 새 컨트롤러 `AdminDevicesController`로 둔다: `DevicesController`는 장치가 부르는 endpoint(device-register 기능)라 관리용 API와 섞지 않는다. 경로는 `/api/admin/...`로 장치 경로(`/api/v1/device(s)/...`)와 나눈다. / 검토한 대안: `DevicesController`에 액션 추가 — 기능 경계가 섞이고 device-register 기준 spec이 바뀌어야 함.
- 포트 제한은 리소스 필터 attribute로 한다: 이후 관리 API도 attribute 하나로 같은 규칙을 쓴다. 필터는 `RequestServices`에서 `AppConfiguration`을 꺼내 비교한다. 포트로 나누므로 Host 헤더 유무나 TLS 리다이렉트와 무관하게 장치 포트에서 처리되는 요청은 모두 막힌다(C3). / 검토한 대안: Kestrel endpoint별 라우팅(`RequireHost("*:port")`) — Host 헤더 기반이라 Host 없는 요청이나 위조된 Host를 막지 못함. 컨트롤러 안에서 직접 비교 — 재사용이 안 됨.
- 정렬은 DB에서 한다: `LastRequestedAt`은 UTC ticks(long)로 저장돼(`PowerManagerDbContext`) SQLite에서 정렬된다.
- 데이터 불러오기는 RTK Query: 이미 있는 `@reduxjs/toolkit`에 포함돼 새 의존성이 없고, `data`(마지막 성공 값 유지)·`isFetching`·`error`·`refetch`가 C10의 "받은 목록이 있는가" 규칙에 대응한다(받은 목록 있음 = `data !== undefined`). 오류 안내 판정은 `isError`가 아니라 `error !== undefined`로 한다: `isError`는 `status === "rejected"`라 다시 불러오기를 시작하면(pending) 바로 false가 되지만, `error`는 성공할 때(`writeFulfilledCacheEntry`의 `delete substate.error`)까지 남는다(설치된 2.14.0 `rtk-query.modern.mjs`에서 확인). / 검토한 대안: `useEffect` + `fetch` 직접 구현 — 상태 관리 코드를 새로 써야 함.
- 라우터를 들이지 않는다: 화면이 하나라 `/`에서 바로 목록을 보인다. 화면이 늘면 그때 넣는다.
- 거르기·정렬·페이지·시각 표기는 순수 함수(`deviceList.ts`)로 둔다: 상대 시간 경계, 정렬 동점 처리, 페이지 보정을 렌더링 없이 경계값으로 테스트한다. 상대 시간은 렌더링 시점의 `Date.now()`로 계산한다(1분마다 자동 갱신하지 않음. spec에 요구 없음).
- UI 부품은 shadcn CLI로 추가: `components.json`이 이미 설정돼 있고 테마 토큰을 그대로 쓴다. / 검토한 대안: 직접 작성 — 공식 부품과 달라질 수 있음.
- 한글 글꼴은 `@fontsource-variable/noto-sans-kr`: Geist와 같은 fontsource 방식이라 설치·번들 방식이 같고, unicode-range로 나뉘어 필요한 글자만 받는다. `--font-sans`를 `'Geist Variable', 'Noto Sans KR Variable', sans-serif`로 한다. / 검토한 대안: Pretendard — 별도 CSS 경로 방식이라 기존 방식과 다름. 설치 시 패키지가 없거나 이름이 다르면 멈추고 알린다.
- MAC 검색: 검색어와 MAC에서 `:`·`-`를 지우고 소문자로 비교한다. 모델·사용자는 소문자 부분 일치.
- 클라이언트 API 주소: RTK Query `fetchBaseQuery`는 jsdom 테스트에서 상대 URL로 `Request`를 만들지 못하므로 `baseUrl`을 `${window.location.origin}/api/admin`으로 둔다. 테스트는 `fetch`를 `vi.fn`으로 바꿔 응답을 준다.

## 인터페이스·데이터

`GET /api/admin/devices` (관리 포트만)

```json
[
  {
    "mac": "2ef4322ee918",
    "userId": "user-01",
    "modelId": "M130-W",
    "company": "DAWONDNS",
    "registeredAt": "2026-11-12T00:14:00+00:00",
    "lastRequestedAt": "2026-11-19T05:58:00+00:00"
  }
]
```

- 컨트롤러 기본 JSON 설정(camelCase)을 쓴다. `DateTimeOffset`은 오프셋이 붙은 ISO 8601로 직렬화된다.
- 장치 포트에서 처리되면 404 `application/problem+json`.

클라이언트 상태(`DevicesPage`):
- 서버 상태: RTK Query `useGetDevicesQuery()`의 `data`, `isFetching`, `error`, `refetch`. 오류 여부 `hasError = error !== undefined`.
- 화면 상태(`useState`): 검색어, 정렬(`{ key: 'lastRequestedAt' | 'registeredAt', dir: 'desc' | 'asc' }`), 현재 페이지.
- 받은 목록 없음(`data === undefined`): `isFetching`이면 스켈레톤, 아니고 `hasError`면 목록 자리 오류 안내.
- 받은 목록 있음: 기존 화면 유지, `isFetching` 동안 새로고침·다시 시도 비활성, `hasError`면 화면 위 오류 안내(다시 불러오는 동안에도 남고, 성공하면 `error`가 지워져 사라짐).

## 오류·위험

- shadcn CLI는 `ui.shadcn.com` 레지스트리에서 부품 소스를 받는다. 네트워크가 막히면 같은 부품을 shadcn 원본 형태로 직접 작성한다.
- 테스트의 TestServer에서는 `Connection.LocalPort`가 실제 포트가 아니다. 테스트는 `TestServer.SendAsync`로 요청 `HttpContext`의 `Connection.LocalPort`를 직접 정해 관리·장치 포트를 흉내 낸다. 실제 Kestrel에서 같은 값이 채워지는지는 C16 실행 확인 때 장치 포트로 직접 요청해 확인한다.
- `Connection.LocalPort` 비교라서 리버스 프록시 뒤에 두면 프록시가 연결하는 포트로 판정된다. 지금 배포는 Kestrel 직접 노출이라 해당하지 않는다.
- 로컬 실행(C16)은 설정 파일과 OS KMS 자격 증명이 필요하다. 스크래치에 임시 설정(높은 포트 번호, 임시 DB·KMS 파일)을 만들어 실행하고, Vite는 `SERVER_URL`로 그 관리 포트를 가리킨다. 서버가 뜨지 않으면 사용자에게 알리고 실행 방법을 묻는다.

## 검증 전략

- C1·C2: `AdminDeviceEndpointTests` — 장비를 `IDbContextFactory<PowerManagerDbContext>`로 DB에 직접 넣어 시각을 정하고(서로 다른 마지막 요청 시각, 같은 시각 두 개 포함. 등록 API는 서버 시계로 시각을 기록해 동점을 만들 수 없음. 기존 `DeviceEndpointTests`도 같은 방식을 씀) 관리 포트(80·443)로 조회. 200, `application/json`, 원소 필드 이름 집합이 정확히 여섯 개, `mqtt`가 들어간 키나 값으로서의 `mqtt_key`가 없음, 정렬 순서, ISO 8601 오프셋 형식을 확인. 빈 DB면 `[]`.
- C3: 같은 테스트 — `LocalPort` 18446·18443으로 조회하면 404 problem+json, 본문에 mac이 없음. 기존 `DeviceEndpointTests`(register·connect) 전체가 그대로 통과. 기존 테스트는 `GetTestClient()`라 `LocalPort`가 0이므로, `LocalPort` 80·443·18446·18443 각각에서 register가 200, 등록된 장치의 connect가 200인지 `DataRow` 테스트로 확인.
- C4·C10·C13: `DevicesPage.test.tsx`(Testing Library + `fetch` 모의) — 앱 틀(제품명, `aria-current`), 머리 문구와 대수, 처음 불러올 때 스켈레톤과 대수 숨김, 빈 목록 안내, 처음 실패 오류와 다시 시도, 목록 있음 상태의 새로고침 중 버튼 비활성·기존 표 유지, 새로고침 실패 시 표 위 오류, 실패 뒤 다시 불러오는 동안 오류 안내가 남아 있음(응답을 지연시킨 `fetch` 모의로 확인), 성공 뒤 해제, 검색 결과 없음과 지우기, 레이블·`aria-label`, 키보드(`user-event`의 Tab·Enter)로 정렬·새로고침 조작.
- C5~C9: `deviceList.test.ts` — 빈 값 "—", MAC 원본 표기, 절대 시각 형식, 상대 시간 경계(59초, 60초, 59분, 60분, 23시간 59분, 24시간, 미래), 정렬 기본값·첫 클릭 내림차순·두 번째 클릭 오름차순·동점 MAC 오름차순, 검색(대소문자, `:`·`-` 무시), 50대 단위 페이지와 보정. `DevicesPage.test.tsx`에서 `aria-sort`와 페이지 버튼·"현재 / 전체 페이지" 표시, 검색·정렬 변경 시 첫 페이지로.
- C11·C12·C16: 로컬 서버 + Vite 개발 서버를 띄우고 내장 브라우저로 1440px·390px에서 확인(사이드바 배치, 표 가로 스크롤·페이지 가로 스크롤 없음, 한글 글꼴이 Noto Sans KR로 적용 — 계산된 `font-family`와 로드된 글꼴 확인, 키보드 조작·포커스 표시). 장비 60대를 등록 API로 넣어 페이지 이동도 본다. 결과를 작업 문서에 기록.
- C14: 두 기준 spec 파일을 읽어 요구가 들어 있는지 확인.
- C15: `dotnet test`(Server.Spa.Tests), 클라이언트 `npm test`·`npm run build`·`npm run lint`.

## 원칙 점검

- 적용 규칙: 전역 `language-preference.md`(대화 한국어), `portable-python.md`(Python 미사용으로 해당 없음), Bizadm MCP `workflow/spec-driven.md`, `git/github-issue.md`(#17 사용), 저장소 기존 관례(컨트롤러의 ProblemDetails 응답, 테스트의 `TestApp` 도우미, 코드 주석 한국어).
- 충돌: 없음.
