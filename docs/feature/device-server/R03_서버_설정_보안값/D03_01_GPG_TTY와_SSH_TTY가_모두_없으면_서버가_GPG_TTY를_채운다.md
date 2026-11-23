---
상태: 유효
---

# D03_01 GPG_TTY와 SSH_TTY가 모두 없으면 서버가 GPG_TTY를 채운다

## 상황

gpg 저장소는 `CredentialStore` 값 외에 `GPG_TTY` 또는 `SSH_TTY`가 필요할 수 있다. 쓰는 패키지(Devlooped.CredentialManager 2.7.0)의 DLL 문자열에 두 이름과 `GPG_TTY is not set` 오류 문구가 있다(정확한 판정 조건은 미확인). `CredentialStore`만 추가하면 SSH 터미널에서는 될 가능성이 크지만, `SSH_TTY`가 없는 systemd나 `sudo -u` 실행에서는 실행하는 쪽에서 `GPG_TTY`를 계속 넣어야 할 수 있다. 누가 넣을 것인가.

- 정한 단계: spec
- 관련: 옛 작업 스펙의 spec.md C3(`docs/specs/credential-store/`(지움. `15dc357`에 있음)), 짝 태스크 `T03_01`

## 결정

- A. 서버가 채움: `CredentialStore`가 `gpg`이고 `GPG_TTY`와 `SSH_TTY`가 모두 없을 때만 서버가 자기 프로세스에 `GPG_TTY=/dev/null`을 넣는다. 둘 중 하나라도 있으면 건드리지 않는다. 유닛의 `GCM_CREDENTIAL_STORE`와 `GPG_TTY` 줄을 모두 뺄 수 있다. 암호가 걸린 gpg 키를 터미널 없이 쓰면 암호를 물을 수 없어 실패한다.

## 이유

- 사용자가 말하지 않음
- 옛 결정 기록을 옮긴 것이다. 옛 기록은 사용자가 직접 말한 이유와 에이전트의 추천 이유를 나눠 적지 않았다.

## 버린 안

| 안 | 버린 이유 |
| --- | --- |
| B. 유닛에 남김: 코드는 `CredentialStore`만 처리한다. 유닛의 `GPG_TTY=/dev/null` 줄은 남기고 `GCM_CREDENTIAL_STORE` 줄만 뺀다. `SSH_TTY`가 없는 직접 실행에서는 `GPG_TTY`를 직접 넣어야 할 수 있다. | 사용자가 말하지 않음 |

## 경위

- 2026-11-23 옛 작업 `docs/specs/credential-store/decisions.md`(지움. `15dc357`에 있음) D001에서 옮겼다(2026-11-23).

## 코드

- 옛 기록에 코드 위치가 없다. 짝 태스크 `T03_01`의 「고칠 파일」을 본다.
