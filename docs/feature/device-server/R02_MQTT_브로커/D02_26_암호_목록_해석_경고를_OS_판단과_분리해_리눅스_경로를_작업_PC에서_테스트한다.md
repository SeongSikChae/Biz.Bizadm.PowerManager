---
상태: 유효
---

# D02_26 암호 목록 해석·경고를 OS 판단과 분리해 리눅스 경로를 작업 PC에서 테스트한다

## 상황

plan 검토 1회차 지적 3. 검토 의견: `CreateCipherSuitesPolicy`는 Windows이면 이름 해석·HTTP/3 경고 전에 끝나, 작업 PC 테스트로는 `warnIfNoTls13: false` 경로가 실행되지 않는다. plan은 이를 C6 로그로 본다고 했지만 C6 설정에는 잘못된 이름도 없고 TLS 1.3 암호도 있어 두 경고 모두 나오지 않는다. 왜 문제인지: C3의 리눅스 동작(`D02_24`(옛 D005) 포함)이 어디서도 실행되지 않는데 확인되는 것처럼 적혀 검증 기록이 틀리게 된다.

- 정한 단계: plan
- 관련: 옛 작업 스펙의 plan.md 바꿀 파일·구조(ServerKestrelExtensions.cs, ServerKestrelExtensionsTests.cs), 결정, 인터페이스·데이터, 검증 전략 C3(`docs/specs/mqtts-tls-policy/`(지움. `15dc357`에 있음)), 짝 태스크 `T02_10`

## 결정

- A. 해석·경고를 분리: Server.Core에 `internal` 함수 `ResolveCipherSuites(string?, ILogger, bool warnIfNoTls13, bool isWindows)`(암호 목록 또는 `null`)를 두고, `CreateCipherSuitesPolicy`는 `OperatingSystem.IsWindows()`를 넘겨 그 결과로 정책만 만든다. 테스트는 `isWindows: false`로 잘못된 이름 경고와 HTTP/3 경고 끔을 작업 PC에서 확인한다.

## 이유

- 사용자가 말하지 않음
- 옛 결정 기록을 옮긴 것이다. 옛 기록은 사용자가 직접 말한 이유와 에이전트의 추천 이유를 나눠 적지 않았다.

## 버린 안

| 안 | 버린 이유 |
| --- | --- |
| B. 확인 못함으로 적기: 검증 전략 C3에 "리눅스 분기(잘못된 이름 경고·HTTP/3 경고 끔)는 확인하지 못한다"고 적는다. | 사용자가 말하지 않음 |

## 경위

- 2026-11-23 옛 작업 `docs/specs/mqtts-tls-policy/decisions.md`(지움. `15dc357`에 있음) D007에서 옮겼다(2026-11-23).

## 코드

- 옛 기록에 코드 위치가 없다. 짝 태스크 `T02_10`의 「고칠 파일」을 본다.
