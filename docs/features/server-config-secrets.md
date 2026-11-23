# server-config-secrets

파일: src/Biz.Bizadm.PowerManager.Server.Core/OsKmsConfiguration.cs, src/Biz.Bizadm.PowerManager.Server.Core/KmsSecretProtector.cs, src/Biz.Bizadm.PowerManager.Server.Core/CredentialStoreEnvironment.cs, tests/Biz.Bizadm.PowerManager.Server.Core.Tests/ConfigurationTests.cs, tests/Biz.Bizadm.PowerManager.Server.Core.Tests/CredentialStoreEnvironmentTests.cs, tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/AppConfigurationTests.cs

서버(Server.Spa) 설정의 보안값(TLS 인증서 비밀번호)을 Biz.Bizadm.KMS로 보호한다. 보안값은 로컬 DEK(`OsKms.SecretsDekFilePath`)로 암호화하고, DEK를 보호하는 KEK는 OS 자격 증명 저장소에 둔 패스워드와 `OsKms.Salt`로 만든다. 처리는 설정 검사 뒤 `KmsSecretProtector.Protect`가 하며, 실패하면 설정 오류(종료 코드 2)로 끝난다(`Program.RunAsync`).

## 요구(완료 조건)

- R1. KMS 처리(자격 증명 저장소, `Salt`, KEK·DEK 준비)는 TLS 사용 여부와 상관없이 기동할 때마다 실행한다.
- R2. `Salt`가 비어 있으면 저장소의 KEK 패스워드를 확인해 없을 때만 새로 만들고, DEK를 만들기 전에 `Salt`를 만들어 설정 파일에 기록한다. 이미 있는 KEK 패스워드는 다시 쓴다.
- R3. `UseTls`가 참이면 평문 TLS 인증서 비밀번호를 암호화(`DEKENC:`)해 설정 파일에 다시 쓰고, 메모리에서는 복호화한 값을 쓴다. 이미 암호문이면 다시 쓰지 않는다. `UseTls`가 거짓이면 비밀번호를 건드리지 않는다.
- R4. `OsKms.CredentialService`가 절대 URI가 아니면 설정 오류로 끝난다.
- R5. `OsKms.CredentialStore`는 선택 항목이다. 없거나 빈 값이거나 공백뿐이면 서버 코드는 환경 변수 `GCM_CREDENTIAL_STORE`·`GPG_TTY`를 바꾸지 않고, 저장소 선택은 KMS 패키지에 맡긴다(환경 변수 `GCM_CREDENTIAL_STORE`, 없으면 OS별 기본값. Linux는 `secretservice`).
- R6. `CredentialStore`가 `gpg` 또는 `secretservice`이면 자격 증명 저장소를 만들기 전에 서버 프로세스의 `GCM_CREDENTIAL_STORE`를 그 값으로 바꾼다. 이미 있던 환경 변수 값보다 설정 값이 우선한다.
- R7. `CredentialStore`가 `gpg`이고 `GPG_TTY`와 `SSH_TTY`가 모두 없으면 서버 프로세스에 `GPG_TTY=/dev/null`을 넣는다. 둘 중 하나라도 있으면(빈 값 포함) `GPG_TTY`를 바꾸지 않는다. `secretservice`이면 `GPG_TTY`를 바꾸지 않는다.
- R8. `CredentialStore`가 비어 있지 않은데 `gpg`·`secretservice`가 아니면(대소문자가 다른 값, 앞뒤에 공백이 붙은 값, `plaintext`·`cache` 포함) 설정 오류로 끝난다(종료 코드 2). 오류 문구에 허용 값이 들어간다.
- R9. 서버가 설정 파일을 다시 쓸 때(R2·R3) `CredentialStore` 값이 남는다.

gpg 저장소를 쓸 때 직접 실행과 서비스 실행이 같은 키를 쓰려면 같은 OS 계정이고 저장소 위치(환경 변수 `PASSWORD_STORE_DIR`, git 설정 `credential.gpgPassStorePath`, 홈의 `.password-store` 순)가 같아야 한다. gpg 키는 암호 입력 없이 쓸 수 있어야 한다(암호가 걸린 키의 동작은 미확인). 첫 기동 뒤 `CredentialStore`를 바꾸면 KEK 패스워드를 다른 저장소에서 찾아 실패한다.

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 |
|---|---|---|---|
| 2026-11-23 | credential-store | R1~R4 처음 작성(기존 동작), R5~R9 추가(설정으로 자격 증명 저장소 선택. #34) | docs/specs/credential-store/ |

표에 없는 이전 변경: 기록 없음(docs/server/0001~0003 참조. #1 최초 구현)
