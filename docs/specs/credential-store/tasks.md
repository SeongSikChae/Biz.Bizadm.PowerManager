# 서버 설정의 자격 증명 저장소 선택 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 설정과 적용
- [x] T001 [P] `CredentialStore` 속성과 값 검사를 더한다 (src/Biz.Bizadm.PowerManager.Server.Core/OsKmsConfiguration.cs) — C1, C4
- [x] T002 [P] `CredentialStoreEnvironment.Apply`를 만든다 (src/Biz.Bizadm.PowerManager.Server.Core/CredentialStoreEnvironment.cs) — C1, C2, C3
- [x] T003 `Protect` 첫 줄에서 `CredentialStoreEnvironment.Apply`를 부른다 (src/Biz.Bizadm.PowerManager.Server.Core/KmsSecretProtector.cs) — C2

체크포인트: `dotnet build Biz.Bizadm.PowerManager.slnx`가 경고 없이 통과

## 단계 2: 테스트
- [x] T004 [P] 허용 값·비어 있는 값·거부 값과 오류 문구를 확인하는 테스트를 더한다 (tests/Biz.Bizadm.PowerManager.Server.Core.Tests/ConfigurationTests.cs) — C1, C4
- [x] T005 [P] 환경 변수 규칙과 `Protect`의 적용 순서(Windows에서만)를 확인하는 테스트를 만든다. 바꾼 환경 변수를 되돌리고, 순서 시험은 새 서비스 이름과 임시 폴더를 쓰고 끝나면 저장소 항목을 지운다 (tests/Biz.Bizadm.PowerManager.Server.Core.Tests/CredentialStoreEnvironmentTests.cs) — C1, C2, C3
- [x] T006 [P] YAML을 읽고 다시 쓴 뒤에도 값이 남는지와, 잘못된 값이 설정 검사에서 막히는지 테스트를 더한다 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/AppConfigurationTests.cs) — C4, C5

체크포인트: 두 테스트 프로젝트(`Biz.Bizadm.PowerManager.Server.Core.Tests`, `Biz.Bizadm.PowerManager.Server.Spa.Tests`)가 통과하고, T003의 호출을 잠시 빼면 순서 시험이 실패하는 것을 확인한 뒤 되돌림

## 단계 3: 기준 spec
- [x] T007 기준 spec을 만든다. C1~C5와 `KmsSecretProtector.cs`의 기존 요구, `파일:` 줄, 이력을 적는다 (docs/features/server-config-secrets.md) — C8

체크포인트: 기준 spec에 C1~C5와 기존 요구가 모두 있고 `파일:` 줄의 경로가 모두 있음

## 단계 4: 확인
- [x] T008 솔루션 전체를 빌드하고 모든 테스트를 실행한다(`dotnet build`, `dotnet test --solution Biz.Bizadm.PowerManager.slnx`) — C6
- [x] T009 빌드한 서버를 `CredentialStore: plaintext`로 실행해 종료 코드 2와 오류 문구를 본다 — C4
- [x] T010 화면 없는 Linux 서버에서 직접 실행과 서비스 실행을 번갈아 확인한다. 재배포·설정 변경·유닛 변경·서비스 시작은 각각 사용자 승인 뒤에 한다 — C7

체크포인트: 작업 문서 `## 검증`에 C1~C8의 결과가 모두 적힘

## 의존 관계
- T003은 T001·T002 뒤: 두 파일의 코드를 쓴다.
- 단계 2는 단계 1 뒤: 만든 코드를 시험한다. T005의 순서 시험은 T003이 필요하다.
- T007은 단계 2 뒤: 시험으로 확인한 동작을 요구로 적는다.
- T008·T009는 T007 뒤, T010은 T008·T009 뒤: 로컬 확인이 끝난 것을 배포한다.
