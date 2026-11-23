---
상태: 유효
---

# D04_31 현재 메뉴는 `Link`와 `useMatch`로 판정한다

## 상황

plan 검토 5회차 지적 1. plan.md는 메뉴를 `NavLink` 두 개로 바꾸고 장비 제어 화면에서는 "대시보드"에 `aria-current`를 붙인다고 적었고, `react-router`를 쓰는 이유로 현재 메뉴 표시를 직접 만들지 않아도 된다고 적었다. 그러나 `NavLink`의 활성 판정은 C16과 다르다. `to="/"`는 루트 경로에서만 활성이고, `to="/devices"`는 `end`가 없으면 `/devices/{mac}`에서도 활성이며, 활성이 아니면 prop으로 넘긴 `aria-current`를 `undefined`로 덮어쓴다(react-router 7.9.1 `packages/react-router/lib/dom/lib.tsx`의 `NavLink`). 적힌 대로 구현하면 장비 제어 화면에서 "장비"가 현재 메뉴로 표시되어 C16을 채우지 못하고 `App.test.tsx` 확인이 실패한다.

- 정한 단계: plan
- 관련: 옛 작업 스펙의 plan.md 바꿀 파일·구조(AppShell.tsx), 결정(클라이언트 라우팅), tasks.md T028(`docs/specs/device-control/`(지움. `15dc357`에 있음)), 짝 태스크 `T04_02`

## 결정

- A. `Link`와 `useMatch`로 판정: 두 메뉴를 `Link`로 만들고 `useMatch`로 현재 메뉴를 판정해 `aria-current="page"`를 붙인다. `/devices`면 "장비", `/`와 `/devices/:mac`이면 "대시보드"다.

## 이유

- 사용자가 말하지 않음
- 옛 결정 기록을 옮긴 것이다. 옛 기록은 사용자가 직접 말한 이유와 에이전트의 추천 이유를 나눠 적지 않았다.

## 버린 안

| 안 | 버린 이유 |
| --- | --- |
| B. 장비만 `NavLink`: "장비"는 `end`를 준 `NavLink`로, "대시보드"는 `Link`와 `useMatch`로 판정한다. | 사용자가 말하지 않음 |

## 경위

- 2026-11-22 옛 작업 `docs/specs/device-control/decisions.md`(지움. `15dc357`에 있음) D023에서 옮겼다(2026-11-23).

## 코드

- 옛 기록에 코드 위치가 없다. 짝 태스크 `T04_02`의 「고칠 파일」을 본다.
