---
출처: R01-R1, R01-R2, R01-R3, R01-R4, R01-R5, R01-R6, R01-R7, R01-R8, R02-R9
상태: 끝남
---

# T01_01 등록 응답과 mqtt_key 발급

## 읽을 것

- 요구사항: `R01-R1`~`R01-R8`, `R02-R9`(그때는 접속을 거부하지 않는 관찰 로그였다. 인증 강제는 `T02_09`)
- 수용 기준: `R01-A1`~`R01-A8`, `R02-A9`
- 결정: 없음
- 계약: `C01-E1`, `C01-I1`, `C01-I2`, `C01-I4`
- 옛 작업 기록: `docs/specs/device-register-mqtt-key/`(지움. `15dc357`에 있음), 저장소 밖 작업 문서 `2026-11-19-device-register-mqtt-key.md`(기준점 `845c60a`, 처음 `60300ed`). 일감 #16, 커밋 `eef05d8`

## 고칠 파일

- `src/Biz.Bizadm.PowerManager.Core/Devices/DeviceVerify.cs`, `MqttKeyGenerator.cs`, `DeviceIdentity.cs`, `DeviceRegisterResponse.cs`: `verify` 계산, 키 생성, mac·장치 키·ClientId 해석, 응답 형식
- `Directory.Packages.props`, `src/Biz.Bizadm.PowerManager.Server.Spa/Biz.Bizadm.PowerManager.Server.Spa.csproj`: `Microsoft.EntityFrameworkCore.Sqlite`·`Design` 10.0.15
- `src/Biz.Bizadm.PowerManager.Server.Spa/AppConfiguration.cs`: `DeviceMqttServerAddr`, `DatabasePath`
- `src/Biz.Bizadm.PowerManager.Server.Spa/Data/`: `DeviceRegistration`, `PowerManagerDbContext`, 설계 시 팩터리, 마이그레이션 hosted service, `Migrations/InitialDevices`
- `src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceRegistrationService.cs`, `DevicesController.cs`: 등록·조회, 501 껍데기를 구현으로
- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs`: 접속 자격 증명 판정 로그
- `src/Biz.Bizadm.PowerManager.Server.Spa/Program.cs`: DB 팩터리·서비스·마이그레이션 등록
- 테스트: `Core.Tests`(`DeviceVerifyTests`, `DeviceIdentityTests`, `MqttKeyGeneratorTests`), `Server.Spa.Tests`(`TestApp`, `PipelineTests`, `AppConfigurationTests`, `DeviceEndpointTests`, `MqttBrokerTests`)
- `docs/features/device-register.md`(지움. `15dc357`에 있음)(새 파일), `docs/features/mqtt-broker.md`(지움. `15dc357`에 있음)

## 할 일

### 단계 1: Core 계산·생성·해석
- 치환표와 `Compute`(44자), `IsValidServerAddr`, `RandomNumberGenerator.GetString`으로 16자 키, mac·장치 키·ClientId 해석(`R01-R2`, `R01-R3`, `R01-R8`).

확인: 분석 서버에서 다원 응답과 일치를 확인한 참조 계산으로 만든 시험 값 4개와 `DeviceVerifyTests` 일치.

### 단계 2: 설정·저장
- 두 설정과 검사, SQLite 엔티티·마이그레이션, 등록 서비스(새 키 생성, 비어 있지 않은 값만 갱신, PK 충돌 시 한 번 재조회)(`R01-R3`, `R01-R5`, `R01-R6`).

확인: `dotnet ef migrations add InitialDevices` 성공, 빌드 성공.

### 단계 3: endpoint·MQTT
- `Register`(mac 400, 키·`verify` 없는 로그)와 `Connect`(200 `{"delay":"0"}`/404), CONNECT 판정 로그(`FixedTimeEquals`, 비밀번호 값 미기록)(`R01-R1`, `R01-R4`, `R01-R7`, `R01-R8`, `R02-R9`).

### 단계 4: 서버 테스트
- 임시 폴더 정리를 `TestApp.DeleteTempRoot`(연결 풀 비운 뒤 삭제)로 바꾸고 등록·connect·판정 테스트를 쓴다.

확인: `dotnet test --solution Biz.Bizadm.PowerManager.slnx` 통과.

### 단계 5: 기준 spec·실측·실장비
- 옛 기준 spec 작성, 실제 다원 응답으로 `RealDawonResponse_Matches`, 배포본 교체, B540 실장비 1 등록.

확인: 실제 응답 대조 일치. 실장비가 등록 200을 받고 MQTT에 접속, 판정 로그 `Match`.

## 위험

- SQLite 연결 풀이 파일을 잡아 테스트 임시 폴더 삭제가 실패할 수 있다(`DeleteTempRoot`로 막음).
- 설치된 `dotnet-ef`(11.0.0)와 EF 버전(10.0.15)이 달라 마이그레이션 생성이 실패할 수 있었다.
- MQTTnet 5의 `ValidatingConnectionAsync`를 구독해도 기본 `ReasonCode`가 성공인지 테스트로 확인한다.
- `DeviceMqttServerAddr`가 장치의 `server_addr`와 다르면 장치가 키를 받아들이지 않는다.

## 검증 방법

| 수용 기준 | 종류 | 대상 | 전제 | 순서 | 기대 결과 |
| --- | --- | --- | --- | --- | --- |
| `R01-A1` | 단위 테스트 | `DeviceEndpointTests` | `TestApp` | `dotnet test --solution Biz.Bizadm.PowerManager.slnx` | 200, `application/json`, 원문이 `{"verify":"`로 시작, 키 둘, `verify` 44자 |
| `R01-A2` | 단위 테스트 | `MqttKeyGeneratorTests`, `DeviceEndpointTests` | 없음 | 위와 같음 | 1000번 생성 모두 16자·`[A-Za-z0-9]`·서로 다름 |
| `R01-A3` | 단위 테스트 | `DeviceVerifyTests`, `DeviceEndpointTests`, `AppConfigurationTests` | 실측: 환경 변수 `POWERMANAGER_DAWON_RESPONSE`·`POWERMANAGER_DAWON_MAC` | 위 명령; 실측은 `RealDawonResponse_Matches`만 실행 | 시험 값 4개 일치, 응답 `verify` 재계산 일치, 잘못된 주소 예외, 실측 일치 |
| `R01-A4` | 단위 테스트 | `DeviceEndpointTests` | 없음 | 위 명령 | 잘못된 mac 400 `application/problem+json`, `Devices` 행 0, 대문자 mac은 소문자로 저장 |
| `R01-A5` | 단위 테스트 | `DeviceEndpointTests` | 같은 `ContentRootPath`로 재시작 | 위 명령 | 같은 키, DB 파일 있음, 빈 값 저장, 시각이 요청 전후 사이 |
| `R01-A6` | 단위 테스트 | `DeviceEndpointTests` | 두 요청 사이 짧은 지연 | 위 명령 | 같은 키, `LastRequestedAt` 증가, 빈 값은 유지 |
| `R01-A7` | 단위 테스트 | `DeviceEndpointTests`, `MqttBrokerTests` | 로그 파일 | 위 명령 | 로그 전체에 키·`verify` 문자열 없음 |
| `R01-A8` | 단위 테스트 | `DeviceEndpointTests` | 등록한 mac | 위 명령 | 두 장치 키 형식 200, 미등록·형식 오류 404 |
| `R02-A9` | 단위 테스트 | `MqttBrokerTests` | 등록한 mac | 위 명령 | `Match`·`Mismatch`·`Unknown` 판정과 로그, 비밀번호 문자열 없음(그때는 모두 접속 성공) |

## 원칙 점검

- 적용 규칙: 그때의 MCP `workflow/spec-driven.md`, MCP `git/github-issue.md`, MCP `dotnet/nuget-package-selection.md`, 전역 `portable-python.md`(참조 계산), 메모리 실장비 대상 확인
- 충돌: 없음
