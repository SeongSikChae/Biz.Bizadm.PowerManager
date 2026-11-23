# 서버 설정의 자격 증명 저장소 선택 계획

## 요약

`OsKmsConfiguration`에 `CredentialStore`를 더하고 `Validate`에서 허용 값을 검사한다(C1·C4). `KmsSecretProtector.Protect`는 자격 증명 저장소를 만들기 전에 새 클래스 `CredentialStoreEnvironment`로 프로세스 환경 변수를 맞춘다(C2·C3). 설정 파일 다시 쓰기는 설정 객체 전체를 직렬화하므로(`src/Biz.Bizadm.PowerManager.Server.Spa/Program.cs` `RunAsync`) 속성만 더하면 값이 남고, 테스트로 확인한다(C5).

## 바꿀 파일·구조

- src/Biz.Bizadm.PowerManager.Server.Core/OsKmsConfiguration.cs: 선택 속성 `CredentialStore`와 값 검사
- src/Biz.Bizadm.PowerManager.Server.Core/CredentialStoreEnvironment.cs: 새 파일. 설정 값에 맞춰 `GCM_CREDENTIAL_STORE`·`GPG_TTY`를 프로세스에 넣는 내부 정적 클래스
- src/Biz.Bizadm.PowerManager.Server.Core/KmsSecretProtector.cs: `Protect` 첫 줄에서 위 클래스를 부름
- tests/Biz.Bizadm.PowerManager.Server.Core.Tests/ConfigurationTests.cs: `CredentialStore` 값 검사 테스트
- tests/Biz.Bizadm.PowerManager.Server.Core.Tests/CredentialStoreEnvironmentTests.cs: 새 파일. 환경 변수 규칙과 `Protect`에서 저장소보다 먼저 적용되는지 테스트
- tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/AppConfigurationTests.cs: YAML 읽기·다시 쓰기에서 값이 남는지, 잘못된 값이 설정 검사에서 막히는지 테스트
- docs/features/server-config-secrets.md: 새 기준 spec

## 결정

- 환경 변수를 프로세스 안에서 바꾼다: KMS 패키지가 저장소를 고르는 입구가 `GCM_CREDENTIAL_STORE`뿐이고(Biz.Bizadm.KMS.Credentials 1.0.2 `EnsureCredentialStore`, Devlooped.CredentialManager 2.7.0 `EnsureBackingStore`, 디컴파일로 확인), 저장소를 만들 때 환경 변수를 읽으므로 만들기 전에 바꾸면 된다(`Environment.GetEnvironmentVariables` 호출 위치, 디컴파일로 확인) / 검토한 대안: 패키지에 저장소를 넘기는 API — 공개 API에 없음. 패키지를 고치는 것 — 이 저장소 밖이고 범위가 커짐
- 값 검사는 `OsKmsConfiguration.Validate`에 둔다: 설정 오류(종료 코드 2)로 처리되는 자리이고 `CredentialService` 검사와 같은 곳이다 / 검토한 대안: `KmsSecretProtector`에서 검사 — 저장소 단계에서 실패해 원인이 늦게 드러남
- 환경 변수 규칙을 별도 클래스로 뺀다: 환경 변수 규칙을 저장소·DEK 처리와 나눠 읽고 시험하기 쉽게 한다 / 검토한 대안: `KmsSecretProtector` 안의 메서드 — 동작은 같지만 `Protect`와 한 파일에 섞인다 (D005)
- `GPG_TTY`·`SSH_TTY`는 변수가 있는지로 보고 빈 값도 있는 것으로 본다 (D004)
- 두 변수가 모두 없으면 `GPG_TTY=/dev/null`을 넣는다 (D001)

## 인터페이스·데이터

설정(YAML):

```yaml
OsKms:
  CredentialService: https://powermanager.local
  CredentialAccount: powermanager
  # 비우거나 빼면 지금처럼 KMS 패키지가 고름(환경 변수 GCM_CREDENTIAL_STORE, 없으면 OS별 기본값). 화면 없는 Linux에서는 gpg
  CredentialStore: gpg
```

`OsKmsConfiguration`:

```csharp
[Property(PropertyType.STRING)]
public string? CredentialStore { get; set; }
```

- `Validate`: `CredentialStore`가 null·빈 값·공백뿐이면 통과. 그 외에는 `gpg`·`secretservice`와 대소문자까지 같아야 하며, 아니면 `ArgumentException`(`ParamName` = `CredentialStore`, 문구에 허용 값)

`CredentialStoreEnvironment.Apply(string? credentialStore)`:

- null·빈 값·공백뿐이면 아무것도 하지 않음
- `GCM_CREDENTIAL_STORE`를 값으로 덮어씀
- 값이 `gpg`이고 `GPG_TTY`와 `SSH_TTY`가 모두 null이면 `GPG_TTY=/dev/null`

## 오류·위험

- `Validate`를 거치지 않고 `Protect`를 부르면 허용하지 않는 값이 환경 변수에 들어갈 수 있다. 서버는 `Program.RunAsync`에서 `ConfigurationValidator.Validate` 뒤에 `Protect`를 부르므로 해당하지 않는다.
- 환경 변수는 서버 프로세스 전체에 영향을 준다. 서버는 KMS 처리 외에 자격 증명 저장소를 쓰지 않는다(`src/Biz.Bizadm.PowerManager.Server.Core`·`src/Biz.Bizadm.PowerManager.Server.Spa`에서 자격 증명 저장소를 쓰는 곳은 `KmsSecretProtector`뿐).
- Windows에서 `gpg`를 넣으면 패키지가 `Can only use the 'gpg' credential store on POSIX systems.`로 실패한다(디컴파일로 확인). 설정 오류로 끝나므로 서버가 따로 막지 않는다.
- 암호가 걸린 gpg 키의 동작은 미확인이다. SSH 직접 실행에서는 터미널이 있어 통과하거나 입력을 기다릴 수 있고, 서비스 실행에서만 실패할 수 있다(Devlooped.CredentialManager 2.7.0 `Gpg.DecryptFile`은 제한 시간 없이 기다리고 `Gpg.PrepareEnvironment`는 `SSH_TTY`를 `GPG_TTY`로 넘김, 디컴파일로 확인. gpg의 실제 동작은 미확인). 시작이 끝나지 않거나 저장소 오류가 나는 것으로 알아챈다. 실패 시점은 `Salt`를 기록한 뒤일 수 있다.
- 이미 첫 기동을 한 서버에서 `CredentialStore`를 바꾸면 KEK 패스워드를 다른 저장소에서 찾아 실패한다. 저장소 이전은 다루지 않는다. 이번 서버는 첫 기동 전이다(`Salt`가 비어 있고 DEK 파일이 없음, 2026-11-23 조회).

## 검증 전략

- C1~C3: `CredentialStoreEnvironmentTests`. 환경 변수를 저장했다 되돌리며, 값 없음·빈 값·공백, 두 허용 값, 기존 값 덮어쓰기, `GPG_TTY`·`SSH_TTY`의 있음·빈 값·없음 조합을 확인. 환경 변수는 프로세스 전체 값이므로 `[DoNotParallelize]`
- C2의 순서: Windows에서 `Protect`에 `CredentialStore: gpg`와 빈 `Salt`를 주면 패키지의 POSIX 오류가 나고 `Salt`와 설정 저장이 일어나지 않음을 확인(저장소보다 먼저 적용되지 않으면 Windows 기본 저장소로 진행해 오류가 나지 않는다). Windows에서만 실행. 실패했을 때 실제 항목이 남지 않게 시험마다 새로 만든 서비스 이름과 임시 폴더를 쓰고 끝나면 저장소 항목을 지운다(`RemovePassword`)
- C4: `ConfigurationTests`(허용 값·거부 값·문구)와, 빌드한 서버에 `CredentialStore: plaintext`를 준 실행의 종료 코드 2·표준 오류 확인
- C5: `AppConfigurationTests`에서 YAML 읽기 → 쓰기 → 읽기 뒤 값이 같음
- C6: `dotnet build`(경고 0)와 전체 테스트(`dotnet test --solution`)
- C7: 서버에 재배포한 뒤 `config.yaml`에 `CredentialStore: gpg`를 넣고 유닛의 `GCM_CREDENTIAL_STORE`·`GPG_TTY` 줄을 빼고 확인한다. 직접 실행 → 서비스 실행 → 직접 실행 순서로 시작해 `/health` 200, `Salt`와 DEK 파일 해시가 첫 기동 뒤 바뀌지 않음을 본다. 재배포·설정 변경·유닛 변경·서비스 시작은 각각 사용자 승인 뒤에 하고, 실행 파일의 SELinux 라벨과 포트 권한(setcap)은 사용자가 준다
- C8: 기준 spec을 읽어 C1~C5와 기존 요구가 있고 `파일:` 줄의 경로가 모두 있는지 확인

## 원칙 점검

- 적용 규칙: `workflow/spec-driven.md`와 하위 문서, `git/github-issue.md`, `workflow/repository-content.md`, `dotnet/kms-config-secrets.md`(KMS 패키지만 사용, 평문 우회 경로 금지, 민감값을 로그·예외에 남기지 않음), `git/commit-message.md`
- 충돌: 없음. `plaintext`·`cache`를 막아 평문 우회 경로를 만들지 않으며, 예외 문구에는 저장소 이름만 들어간다
