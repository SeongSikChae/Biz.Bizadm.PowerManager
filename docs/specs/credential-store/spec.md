# 서버 설정의 자격 증명 저장소 선택

기능: server-config-secrets(기준 spec 없음, 이번에 만듦)
이슈: GitHub #34

## 무엇을, 왜

서버는 설정의 보안값(TLS 인증서 비밀번호)을 로컬 DEK로 암호화하고, DEK를 보호하는 KEK를 만드는 패스워드를 OS 자격 증명 저장소에 둔다(`src/Biz.Bizadm.PowerManager.Server.Core/KmsSecretProtector.cs`). 저장소는 KMS 패키지가 환경 변수 `GCM_CREDENTIAL_STORE`로 고르며, 값이 비어 있으면 패키지가 Linux에서 `secretservice`를 넣는다(Biz.Bizadm.KMS.Credentials 1.0.2 linux `Biz.Bizadm.KMS.Credentials.Runtime.dll`의 `CredentialStoreKekCredentialProvider.EnsureCredentialStore`, 디컴파일로 확인). 화면 없는 Linux에서는 서버가 `Cannot use the 'secretservice' credential backing store without a graphical interface present.` 설정 오류로 끝난다(2026-11-23 화면 없는 Rocky Linux 9.8에서 직접 실행해 관찰). 패키지 README는 프로세스 시작 전에 `GCM_CREDENTIAL_STORE=gpg` 등을 설정할 수 있다고 안내한다(`README.md` 「Credentials」, `gpg`는 예시).

그래서 지금은 실행하는 쪽(systemd 유닛, 셸)이 환경 변수를 넣어야 하고, 터미널에서 직접 실행할 때와 서비스로 실행할 때의 동작이 달라진다. 설정 파일에서 저장소를 고르게 해 실행 방법과 상관없이 같은 저장소를 쓰게 한다.

gpg 저장소는 데스크톱 세션이 아니고 환경 변수 `GPG_TTY`와 `SSH_TTY`가 모두 없으면 `GPG_TTY is not set` 오류를 낸다(Devlooped.CredentialManager 2.7.0 `ValidateGpgPass`, 디컴파일로 확인). 둘 다 없으면 서버가 채운다(D001).

### 하지 않는 것

- `DeviceConfig`·`FakeDevice` 앱에는 적용하지 않는다.
- gpg 키와 저장소(`.gpg-id`)를 만드는 것은 서버를 설치하는 쪽이 한다. 저장소 위치는 환경 변수 `PASSWORD_STORE_DIR`, git 설정 `credential.gpgPassStorePath`, 홈의 `.password-store` 순으로 정해지므로(Devlooped.CredentialManager 2.7.0 `ValidateGpgPass`, 디컴파일로 확인) 직접 실행과 서비스 실행은 같은 OS 계정이고 같은 위치가 되어야 같은 키를 쓴다. gpg 키는 암호 입력 없이 쓸 수 있어야 한다(암호가 걸린 키의 동작은 미확인).
- 1024 미만 포트를 여는 권한은 코드로 다루지 않는다. 설치하는 쪽이 OS에서 준다.

## 완료 조건

- C1. 서버 설정 `OsKms`에 선택 항목 `CredentialStore`가 있다. 없거나 빈 값이거나 공백뿐이면 서버 코드는 환경 변수 `GCM_CREDENTIAL_STORE`·`GPG_TTY`를 바꾸지 않고, 저장소 선택은 지금처럼 KMS 패키지에 맡긴다.
- C2. `CredentialStore`가 `gpg` 또는 `secretservice`이면 자격 증명 저장소를 만들기 전에 서버 프로세스의 `GCM_CREDENTIAL_STORE`를 그 값으로 바꾼다. 이미 있던 환경 변수 값보다 설정 값이 우선한다.
- C3. `CredentialStore`가 `gpg`이고 `GPG_TTY`와 `SSH_TTY`가 모두 없으면 서버 프로세스에 `GPG_TTY=/dev/null`을 넣는다. 둘 중 하나라도 있으면(빈 값 포함) `GPG_TTY`를 바꾸지 않는다. `secretservice`이면 `GPG_TTY`를 바꾸지 않는다.
- C4. `CredentialStore`가 비어 있지 않은데(C1의 경우가 아닌데) `gpg`·`secretservice`가 아니면(대소문자가 다른 값, 앞뒤에 공백이 붙은 값, `plaintext`·`cache` 포함) 설정 오류로 끝난다(종료 코드 2). 오류 문구에 허용 값이 들어간다.
- C5. 서버가 설정 파일을 다시 쓸 때(`Salt` 기록, 보안값 암호화) `CredentialStore` 값이 남는다.
- C6. 다른 동작은 바뀌지 않는다. 기존 테스트가 모두 통과하고 빌드 경고가 없다.
- C7. 화면 없는 Linux 서버에 배포하고 `CredentialStore: gpg`를 넣은 뒤, 같은 OS 계정으로 (가) SSH 로그인 터미널에서 직접 실행과 (나) systemd 서비스 실행(`SSH_TTY` 없음)을 하되 둘 다 `GCM_CREDENTIAL_STORE`·`GPG_TTY`·`PASSWORD_STORE_DIR`를 따로 넣지 않는다. 각각 저장소 오류 없이 시작해 `/health`가 200을 반환한다. 두 실행은 같은 `Salt`·DEK를 쓰고, 직접 실행과 서비스 실행을 번갈아 해도 시작한다. (가)에서 1024 미만 포트를 쓰면 포트 권한을 준 뒤 확인한다.
- C8. 기준 spec `docs/features/server-config-secrets.md`를 만들어, C1~C5와 `KmsSecretProtector.cs`에 이미 있는 요구(KMS 처리는 TLS 사용 여부와 상관없이 실행, `Salt`가 비어 있으면 저장소의 KEK 패스워드를 확인해 없을 때만 새로 만들고 DEK보다 먼저 `Salt`를 기록, `UseTls`가 참이면 평문 TLS 비밀번호를 암호화해 설정 파일에 다시 쓰고 메모리에서는 복호화)를 요구로 적고 이력에 이 작업을 남긴다.
