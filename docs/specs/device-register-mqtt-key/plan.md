# 장치 등록 응답과 mqtt_key 발급·저장 계획

## 요약

순수 계산(`verify` 계산, `mqtt_key` 생성, mac·장치 키 해석)은 의존성 없는 `Core`에 두고 단위 테스트한다. 저장은 `Server.Spa`에 EF Core 10 + SQLite(`IDbContextFactory`)로 두고, 마이그레이션 파일을 만들어 서버 시작 시 적용한다. `DevicesController`의 두 껍데기(`Register`, `Connect`)를 구현하고, `MqttBrokerHostedService`에 `ValidatingConnectionAsync` 관찰 로그를 더한다(접속은 거부하지 않음). 설정에 `DeviceMqttServerAddr`(기본 `dwmqtt.dawonai.com`)와 `DatabasePath`(기본 `powermanager.db`)를 더한다. 마지막으로 기준 spec(`device-register` 새로, `mqtt-broker` R9 추가)을 반영하고 배포본으로 실장비 시험을 한다.

## 바꿀 파일·구조

- `Directory.Packages.props`: `Microsoft.EntityFrameworkCore.Sqlite` 10.0.15, `Microsoft.EntityFrameworkCore.Design` 10.0.15 추가
- `src/Biz.Bizadm.PowerManager.Core/Devices/DeviceVerify.cs`(새): 치환표 상수와 `Compute(string serverAddr, string mac, string mqttKey)`, `IsValidServerAddr(string)`
- `src/Biz.Bizadm.PowerManager.Core/Devices/MqttKeyGenerator.cs`(새): `Create()` — 대문자·소문자·숫자 62자를 나열한 문자 목록 상수(`"ABC…XYZabc…xyz0123456789"`)로 `RandomNumberGenerator.GetString(목록, 16)`
- `src/Biz.Bizadm.PowerManager.Core/Devices/DeviceIdentity.cs`(새): `TryNormalizeMac(string?, out string)`(12자리 16진수 → 소문자), `TryParseDeviceKey(string, out string mac)`(`DAWONDNS-{모델}-{mac}`), `TryParseClientId(string?, out string mac)`(`DAWONDNS-{mac}`)
- `src/Biz.Bizadm.PowerManager.Core/Devices/DeviceRegisterResponse.cs`(새): `record DeviceRegisterResponse([property: JsonPropertyName("verify")] string Verify, [property: JsonPropertyName("mqtt_key")] string MqttKey)`, `record DeviceConnectResponse([property: JsonPropertyName("delay")] string Delay)`
- `src/Biz.Bizadm.PowerManager.Server.Spa/Biz.Bizadm.PowerManager.Server.Spa.csproj`: `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design`(`PrivateAssets=all`) 참조
- `src/Biz.Bizadm.PowerManager.Server.Spa/AppConfiguration.cs`: `DeviceMqttServerAddr`(STRING, 기본 `dwmqtt.dawonai.com`), `DatabasePath`(STRING, 기본 `powermanager.db`, `ContentRootPath` 기준 상대 경로 허용) 추가, `Validate`에서 `DeviceVerify.IsValidServerAddr` 위반 시 `ArgumentException`
- `src/Biz.Bizadm.PowerManager.Server.Spa/Data/PowerManagerDbContext.cs`(새): `DbSet<DeviceRegistration> Devices`, 테이블 `Devices`, PK `Mac`
- `src/Biz.Bizadm.PowerManager.Server.Spa/Data/DeviceRegistration.cs`(새): `Mac`(12), `UserId`, `ModelId`, `Company`, `MqttKey`(32), `RegisteredAt`, `LastRequestedAt`(`DateTimeOffset` → SQLite는 UTC ticks 변환)
- `src/Biz.Bizadm.PowerManager.Server.Spa/Data/PowerManagerDbContextDesignFactory.cs`(새): `IDesignTimeDbContextFactory` — `dotnet ef`가 `--config` 없이 컨텍스트를 만들게 함
- `src/Biz.Bizadm.PowerManager.Server.Spa/Data/DatabaseMigrationHostedService.cs`(새): 시작 시 `Database.MigrateAsync()`. 브로커보다 먼저 등록
- `src/Biz.Bizadm.PowerManager.Server.Spa/Data/Migrations/*`(새, `dotnet ef migrations add InitialDevices`로 생성)
- `src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceRegistrationService.cs`(새): 등록(조회·생성·갱신·동시 생성 충돌 재조회), mac으로 키 조회
- `src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DevicesController.cs`: `Register`·`Connect` 구현, 501 제거
- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs`: `ValidatingConnectionAsync` 구독·해제, 관찰 로그
- `src/Biz.Bizadm.PowerManager.Server.Spa/Program.cs`: `AddDbContextFactory<PowerManagerDbContext>`(SQLite, `Path.Combine(ContentRootPath, DatabasePath)`), `DeviceRegistrationService` 싱글턴, `DatabaseMigrationHostedService`를 브로커 앞에 등록
- `tests/Biz.Bizadm.PowerManager.Core.Tests/DeviceVerifyTests.cs`(새): 참조 시험 값 대조, `IsValidServerAddr`, 실측 대조(환경 변수 게이트)
- `tests/Biz.Bizadm.PowerManager.Core.Tests/DeviceIdentityTests.cs`(새), `MqttKeyGeneratorTests.cs`(새)
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DeviceEndpointTests.cs`: register·connect 501 테스트를 C1·C4~C7·C11 테스트로 교체(FOTA 501 테스트는 그대로). 클래스가 임시 폴더를 공유하므로 등록 관련 테스트는 메서드마다 `DatabasePath`를 고유 파일명(`devices-{Guid}.db`)으로 바꾼 설정을 써서 DB를 서로 격리한다(C5 재시작 테스트는 같은 설정 객체로 두 번 띄움). `MqttBrokerTests`는 `[TestInitialize]`에서 테스트마다 임시 폴더를 새로 만들어 DB가 이미 격리되므로 추가 작업이 없다
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/AppConfigurationTests.cs`: 새 설정 기본값·잘못된 `DeviceMqttServerAddr` 테스트
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs`: C8 판정 셋·비밀번호 미노출 테스트
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TestApp.cs`: `Configuration`에 `DeviceMqttServerAddr = "dwmqtt.dawonai.com"`, `DatabasePath = "powermanager.db"` 명시(이 설정은 `Validate`를 거치지 않아 기본값이 채워지지 않음). 정리 도우미 `DeleteTempRoot(string)` — `SqliteConnection.ClearAllPools()` 후 폴더 삭제
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/PipelineTests.cs`: 정리(`ClassCleanup`)의 `Directory.Delete`를 `TestApp.DeleteTempRoot`로 바꿈(앱이 DB 파일을 만들기 때문). `DeviceEndpointTests`·`MqttBrokerTests` 정리도 같은 도우미로 바꿈
- `docs/features/device-register.md`(새): 기준 spec
- `docs/features/mqtt-broker.md`: R9 추가, 이력 한 줄

## 결정

- 계산·생성·해석을 `Core`에 둠: 의존성 없는 순수 함수라 `Core.Tests`에서 빠르게 검증하고, 서버·향후 도구가 함께 쓴다 / 검토한 대안: `Server.Spa`에 둠 — 테스트에 웹 호스트가 필요 없는데도 끌려 들어가 고르지 않음
- EF Core 10.0.15: `net10.0` 호환 최신 패치, 취약점·Deprecated 없음. 11.0.0은 `net11.0` 전용 / 검토한 대안: 11.0.0 — TFM 불일치
- 마이그레이션 + 시작 시 `MigrateAsync`: 이후 장치 표가 늘어날 때 스키마를 안전하게 바꾼다. 저장소에 이미 `dotnet-ef` 도구 매니페스트(미추적, 사용자 파일)가 있다 / 검토한 대안: `EnsureCreated` — 스키마 변경 불가로 고르지 않음
- `IDbContextFactory` 싱글턴 + `DeviceRegistrationService` 싱글턴: 브로커(싱글턴 hosted service)와 컨트롤러가 같은 경로로 DB를 쓴다 / 검토한 대안: scoped `DbContext` — 브로커에서 scope를 따로 만들어야 해 고르지 않음
- 재요청 시 키 유지·`verify` 재계산(C6): `server_addr` 설정이 바뀌어도 응답이 맞게 한다
- 동시 첫 등록: PK 충돌(`DbUpdateException`) 시 한 번 다시 조회해 그 키로 응답한다(키가 둘로 갈리지 않음)
- 응답 순서(C1): `System.Text.Json`은 record 생성자 매개변수 순서(선언 순서)로 직렬화하므로 `Verify`를 먼저 선언한다. 테스트에서 원문 문자열로 순서를 확인한다
- 400 응답: `Problem(statusCode: 400, title: "Invalid mac")`(ProblemDetails). `[ApiController]` 모델 검증은 쓰지 않는다(모든 속성 nullable)
- 404 응답(C11): `Problem(statusCode: 404, title: "Device not found")`
- C8 로그 형식: `MQTT client connecting: clientId={ClientId}, userName={UserName}, hasPassword={HasPassword}, passwordCheck={PasswordCheck}` — `PasswordCheck`는 `Match`·`Mismatch`·`Unknown`. 비밀번호 비교는 `CryptographicOperations.FixedTimeEquals`(UTF-8 바이트)
- C3 실측 대조: `DeviceVerifyTests.RealDawonResponse_Matches`는 환경 변수 `POWERMANAGER_DAWON_RESPONSE`(응답 JSON 파일 경로)와 `POWERMANAGER_DAWON_MAC`이 있을 때만 실행하고 없으면 `Assert.Inconclusive`. 검증 때 분석 서버의 실제 다원 응답 캡처를 스크래치로 복사해 이 테스트만 실행하고, 복사본은 끝나면 지운다. 실제 값은 저장소·로그에 남기지 않는다 / 검토한 대안: 별도 콘솔 실행 — 서버 구현을 그대로 부르는 데 테스트가 더 간단해 고르지 않음
- C3 자동 테스트 시험 값: 구현 전에 분석 서버에서 Python 참조 계산(다원 응답과 일치 확인한 것)으로 가상 입력 4개(`server_addr` `dwmqtt.dawonai.com`·사설망 IP 주소·`iot.example.com`, mac·키는 가짜)의 `verify`를 만들어 테스트 상수로 넣는다

## 인터페이스·데이터

- `POST /api/v1/devices/register/create` → 200 `{"verify":"…44자…","mqtt_key":"…16자…"}` / 400 ProblemDetails
- `GET /api/v1/device/connect/{deviceKey}` → 200 `{"delay":"0"}` / 404 ProblemDetails
- 설정(YAML): `DeviceMqttServerAddr: dwmqtt.dawonai.com`, `DatabasePath: powermanager.db`
- 테이블 `Devices`: `Mac TEXT PK(12)`, `UserId TEXT NOT NULL`, `ModelId TEXT NOT NULL`, `Company TEXT NOT NULL`, `MqttKey TEXT NOT NULL(32)`, `RegisteredAt INTEGER`, `LastRequestedAt INTEGER`

## 오류·위험

- SQLite 연결 풀이 파일을 잡고 있어 테스트 임시 폴더 삭제가 실패할 수 있다 → `TestApp`으로 앱을 띄우는 모든 테스트 클래스(`PipelineTests`·`DeviceEndpointTests`·`MqttBrokerTests`)의 정리에서 `TestApp.DeleteTempRoot`(풀 비운 뒤 삭제)를 쓴다
- 설치된 `dotnet-ef`는 11.0.0이고 프로젝트 EF는 10.0.15다. 마이그레이션 생성이 실패하면 `dotnet-ef` 10.0.15를 도구 매니페스트 없이 `dotnet tool run` 대신 임시로 쓰는 방법을 사용자에게 묻는다(미추적 매니페스트는 사용자 파일이라 고치지 않음)
- MQTTnet 5의 `ValidatingConnectionEventArgs`에 이벤트를 구독하면 기본 `ReasonCode`가 성공인지 확인해야 한다. 테스트(C8)에서 모든 경우 접속 성공을 함께 확인한다
- 배포본(서버 로컬 배포 폴더)은 실행 중일 수 있다. 덮어쓰기 전에 실행 방식(직접 실행·IIS)을 확인하고 사용자에게 알린 뒤 멈추고 바꾼다. DB 파일은 `ContentRootPath`(서버 로컬 배포 폴더의 `powermanager.db`)에 생긴다
- 실장비 설정(ConnectAp)·재부팅은 장치 상태를 바꾸므로 대상 SSID(B540 실장비 1의 장치 AP)를 사용자에게 확인받은 뒤 한다

## 검증 전략

- C1: `DeviceEndpointTests` — 응답 200, `Content-Type` `application/json`, 원문이 `{"verify":"` 로 시작하고 키가 정확히 두 개(`JsonDocument`), `verify` 44자
- C2: `MqttKeyGeneratorTests` — 1000번 생성해 모두 16자·`[A-Za-z0-9]`, 서로 다름 / `DeviceEndpointTests`에서 응답 키도 같은 형식
- C3: `DeviceVerifyTests` — 참조 시험 값 4개 일치, `IsValidServerAddr`(허용·빈 값·`:`·`_`·공백) / `DeviceEndpointTests`에서 응답 `verify == DeviceVerify.Compute(설정 server_addr, mac, 응답 키)` / `AppConfigurationTests`에서 잘못된 값이면 `ConfigurationValidator.Validate`가 예외 / 실측 대조는 환경 변수로 `RealDawonResponse_Matches` 실행, 명령과 결과를 작업 문서에 기록
- C4: `DeviceEndpointTests` — mac 없음·빈 값·11자·13자·`zz…` → 400 `application/problem+json`, 이후 앱의 `IDbContextFactory<PowerManagerDbContext>`로 `Devices` 행 수가 0임을 직접 확인(저장 안 됨). 대문자 mac → 200, 소문자로 저장(connect 소문자로 200). user_id 등 없는 본문 → 200
- C5: `DeviceEndpointTests` — 등록 후 앱 정지·같은 `ContentRootPath`로 재시작해 다시 등록하면 같은 키, DB 파일 존재. user_id·model_id·company가 없는 본문으로 처음 등록한 뒤 앱의 `IDbContextFactory<PowerManagerDbContext>`로 행을 읽어 세 값이 빈 문자열이고 `RegisteredAt`·`LastRequestedAt`이 요청 전후 시각 사이임을 확인
- C6: `DeviceEndpointTests` — 두 번째 요청이 같은 키. `IDbContextFactory`로 행을 읽어 `RegisteredAt`은 그대로, `LastRequestedAt`은 두 번째 요청을 보내기 직전에 잰 시각 이상이면서 첫 요청 값보다 큼(두 요청 사이에 짧은 지연을 둠), user_id를 바꿔 보내면 갱신·빈 값·없는 값이면 유지(model_id·company도 같음)를 확인
- C7: 등록·connect·MQTT 접속 테스트 후 로그 파일 전체에 응답 키·`verify` 문자열이 없음
- C8: `MqttBrokerTests` — 등록한 mac으로 ClientId `DAWONDNS-{MAC 대문자}`·맞는 비밀번호 → `Match`, 틀린 비밀번호 → `Mismatch`, 비밀번호 없음 → `Mismatch`, 미등록 mac → `Unknown`, 형식 아님 → `Unknown`. 모두 접속 성공, 로그에 비밀번호 문자열 없음
- C9: `dotnet test` 솔루션 전체 통과
- C10: 배포 후 실장비 등록 → 서버 로그의 등록 200 기록·MQTT 접속 로그 확인, C8 로그를 작업 문서에 기록
- C11: `DeviceEndpointTests` — 등록 후 `DAWONDNS-B540_W-{mac}`·`DAWONDNS-M130-W-{MAC 대문자}` → 200 `{"delay":"0"}`, 미등록 → 404, `DAWONDNS--{mac}`·`DAWONDNS-{mac}`·`X-B540_W-{mac}` → 404

## 원칙 점검

- 적용 규칙: `workflow/spec-driven.md`, `git/github-issue.md`(이슈 #16), `dotnet/nuget-package-selection.md`(TFM 호환·최신 패치·비취약), `dotnet/server-spa-template.md`·`dotnet/server-api-template.md`(YAML 설정 검증·ProblemDetails·MSTest+TestServer), `dotnet/project-creation.md`(중앙 패키지 관리 `Directory.Packages.props`), `dotnet/kms-config-secrets.md`(새 설정은 보안값이 아니라 해당 없음), `git/commit-message.md`(커밋 시)
- 충돌: 없음
