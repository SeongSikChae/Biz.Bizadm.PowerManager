---
상태: 유효
---

# D02_27 MQTTnet DLL 근거 문장을 고치고 정책 전달을 메타데이터 근거로 적는다

## 상황

plan 검토 1회차 지적 4. 검토 의견: plan 결정의 "MQTTnet.Server DLL에서 `CipherSuitesPolicy`가 들어간 이름은 속성과 `get_`·`set_` 셋뿐"이 DLL과 맞지 않는다. 형식 참조 `System.Net.Security.CipherSuitesPolicy`와 멤버 참조 `SslServerAuthenticationOptions.set_CipherSuitesPolicy`도 있다(결론인 빌더 메서드 없음은 맞음). 검토자는 DLL 메타데이터상 MQTTnet이 엔드포인트의 `CipherSuitesPolicy`를 `SslServerAuthenticationOptions`에 넘긴 뒤 `AuthenticateAsServerAsync`를 부른다고 덧붙였다(REA로 IL 확인). 왜 문제인지: 근거 문장이 출처와 맞지 않는다.

- 정한 단계: plan
- 관련: 옛 작업 스펙의 plan.md 결정(정책 적용 시점), 오류·위험 첫 항목(`docs/specs/mqtts-tls-policy/`(지움. `15dc357`에 있음)), 짝 태스크 `T02_10`

## 결정

- A. 근거·위험 항목 모두 고침: 결정의 근거를 "MQTTnet 쪽 이름은 셋뿐(형식 참조와 호출 참조 제외)"으로 고치고, 오류·위험 첫 항목을 메타데이터 근거(검토자 REA 확인)와 "실행 확인은 C6"으로 바꾼다.

## 이유

- 사용자가 말하지 않음
- 옛 결정 기록을 옮긴 것이다. 옛 기록은 사용자가 직접 말한 이유와 에이전트의 추천 이유를 나눠 적지 않았다.

## 버린 안

| 안 | 버린 이유 |
| --- | --- |
| B. 근거 문장만 고침: 오류·위험 첫 항목은 미확인으로 둔다. | 사용자가 말하지 않음 |

## 경위

- 2026-11-23 옛 작업 `docs/specs/mqtts-tls-policy/decisions.md`(지움. `15dc357`에 있음) D008에서 옮겼다(2026-11-23).

## 코드

- 옛 기록에 코드 위치가 없다. 짝 태스크 `T02_10`의 「고칠 파일」을 본다.
