# 서버 설정의 자격 증명 저장소 선택 결정

### D001 GPG_TTY와 SSH_TTY가 모두 없으면 서버가 GPG_TTY를 채운다
- 단계: spec
- 질문: gpg 저장소는 `CredentialStore` 값 외에 `GPG_TTY` 또는 `SSH_TTY`가 필요할 수 있다. 쓰는 패키지(Devlooped.CredentialManager 2.7.0)의 DLL 문자열에 두 이름과 `GPG_TTY is not set` 오류 문구가 있다(정확한 판정 조건은 미확인). `CredentialStore`만 추가하면 SSH 터미널에서는 될 가능성이 크지만, `SSH_TTY`가 없는 systemd나 `sudo -u` 실행에서는 실행하는 쪽에서 `GPG_TTY`를 계속 넣어야 할 수 있다. 누가 넣을 것인가.
- 선택지:
  - A. 서버가 채움: `CredentialStore`가 `gpg`이고 `GPG_TTY`와 `SSH_TTY`가 모두 없을 때만 서버가 자기 프로세스에 `GPG_TTY=/dev/null`을 넣는다. 둘 중 하나라도 있으면 건드리지 않는다. 유닛의 `GCM_CREDENTIAL_STORE`와 `GPG_TTY` 줄을 모두 뺄 수 있다. 암호가 걸린 gpg 키를 터미널 없이 쓰면 암호를 물을 수 없어 실패한다.
  - B. 유닛에 남김: 코드는 `CredentialStore`만 처리한다. 유닛의 `GPG_TTY=/dev/null` 줄은 남기고 `GCM_CREDENTIAL_STORE` 줄만 뺀다. `SSH_TTY`가 없는 직접 실행에서는 `GPG_TTY`를 직접 넣어야 할 수 있다.
- 결정: A
- 이유: 사용자가 말하지 않음
- 반영: spec.md C3

### D002 C7의 전제를 적고 직접 실행은 SSH 로그인 터미널로 정한다
- 단계: spec
- 질문: spec 검토 1회차 지적 2. 검토 의견은 C7·「무엇을, 왜」·「하지 않는 것」에 두 실행이 같은 OS 계정이어야 한다는 조건과 gpg 키를 암호 입력 없이 쓸 수 있어야 한다는 조건이 없고, "터미널 직접 실행"이 SSH 로그인인지 `sudo -u` 같은 실행인지 정해지지 않았다는 것이다. `ValidateGpgPass`는 저장소를 `PASSWORD_STORE_DIR` 또는 홈의 `.password-store`로 정하므로, 계정이 다르면 C7이 실패해도 결함인지 전제 위반인지 가릴 수 없다.
- 선택지:
  - A. 전제·SSH 명시: 「하지 않는 것」에 같은 OS 계정과 암호 없는 gpg 키 전제(암호 걸린 키는 미확인)를 적고, C7 (가)를 SSH 로그인 터미널 직접 실행, (나)를 `SSH_TTY`가 없는 서비스 실행으로 정한다.
  - B. A + 추가 실행 확인: A에 더해 `SSH_TTY`와 `GPG_TTY`를 뺀 직접 실행을 세 번째로 확인한다.
  - C. 고치지 않음: 전제와 실행 종류를 적지 않는다.
- 결정: A
- 이유: 사용자가 말하지 않음
- 반영: spec.md 「하지 않는 것」, C7

### D003 gpg 저장소 위치 문장에서 읽는 파일을 빼고 위치 순서만 적는다
- 단계: spec
- 질문: spec 검토 3회차 지적 1. 검토 의견은 "git 설정 `credential.gpgPassStorePath`(홈의 `.gitconfig`)"의 괄호가 적은 출처 `ValidateGpgPass`에 없고, 실제로는 `NoGit`이 파일을 정하며 `Config.Build`가 `.gitconfig` 외의 파일도 읽는다는 것이다. 적힌 출처로 확인할 수 없는 설명이 남는다.
- 선택지:
  - A. 괄호 삭제: "(홈의 `.gitconfig`)"를 뺀다.
  - B. 출처 추가: "홈의 `.gitconfig` 등, `CommandContextAdapter.NoGit`"으로 쓴다.
  - C. 고치지 않음: 괄호를 그대로 둔다.
- 결정: A
- 이유: 사용자가 말하지 않음
- 반영: spec.md 「하지 않는 것」

### D004 GPG_TTY·SSH_TTY의 빈 값은 있는 것으로 본다
- 단계: spec
- 질문: spec 검토 3회차 전체 점검 지적 2. D001과 「무엇을, 왜」는 두 변수가 모두 없을 때만 서버가 채운다고 하는데 C3은 "없거나 빈 값"까지 채운다. `ValidateGpgPass`는 변수가 있으면 값이 비어도 있는 것으로 본다(`ContainsKey`). 어느 쪽으로 맞출 것인가.
- 선택지:
  - A. 빈 값 조건 삭제: C3을 "모두 없으면 넣는다. 하나라도 있으면(빈 값 포함) 바꾸지 않는다"로 고친다.
  - B. 빈 값도 채움: C3을 두고 「무엇을, 왜」를 넓히며 D001을 대체하는 결정을 만든다.
  - C. 고치지 않음: 두 문장을 그대로 둔다.
- 결정: A
- 이유: 사용자가 말하지 않음
- 반영: spec.md C3

### D005 환경 변수 규칙은 별도 클래스로 둔다
- 단계: plan
- 질문: plan 검토 1회차 지적 3. 검토 의견은 plan.md 「결정」의 "`KmsSecretProtector` 안의 메서드 — 시험하려면 실제 저장소가 필요"가 사실과 다르다는 것이다. `InternalsVisibleTo`가 있어 내부 메서드도 저장소 없이 시험할 수 있으므로 근거만 틀린 채 남는다. 이유를 고칠 것인가, 메서드로 옮길 것인가.
- 선택지:
  - A. 이유 고침: 클래스를 두고 이유를 "환경 변수 규칙을 저장소·DEK 처리와 나눠 읽고 시험하기 쉽게 한다"로, 대안의 이유를 "동작은 같지만 `Protect`와 한 파일에 섞인다"로 바꾼다.
  - B. 메서드로 옮김: 새 파일을 없애고 `KmsSecretProtector`의 내부 메서드로 둔다.
  - C. 고치지 않음: 이유를 그대로 둔다.
- 결정: A
- 이유: 사용자가 말하지 않음
- 반영: plan.md 결정
