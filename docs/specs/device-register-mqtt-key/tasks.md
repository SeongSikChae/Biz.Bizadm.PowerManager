# 장치 등록 응답과 mqtt_key 발급·저장 작업

형식: `- [x] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: Core 계산·생성·해석

- [x] T001 치환표 상수, `Compute(serverAddr, mac, mqttKey)`(44자), `IsValidServerAddr`(비어 있지 않고 치환표 문자만) 작성 (src/Biz.Bizadm.PowerManager.Core/Devices/DeviceVerify.cs) — C3
- [x] T002 [P] `Create()` — 대문자·소문자·숫자 62자를 나열한 문자 목록 상수로 `RandomNumberGenerator.GetString(목록, 16)` (src/Biz.Bizadm.PowerManager.Core/Devices/MqttKeyGenerator.cs) — C2
- [x] T003 [P] `TryNormalizeMac`(12자리 16진수 → 소문자), `TryParseDeviceKey`(`DAWONDNS-{비어 있지 않은 모델}-{mac}`, mac은 마지막 `-` 뒤), `TryParseClientId`(`DAWONDNS-{mac}`) 작성 (src/Biz.Bizadm.PowerManager.Core/Devices/DeviceIdentity.cs) — C4, C8, C11
- [x] T004 [P] `DeviceRegisterResponse(Verify, MqttKey)`(선언 순서 verify→mqtt_key, JSON 이름 `verify`·`mqtt_key`), `DeviceConnectResponse(Delay)`(`delay`) 작성 (src/Biz.Bizadm.PowerManager.Core/Devices/DeviceRegisterResponse.cs) — C1, C11
- [x] T005 DevServer(`/mnt/analysis/bizadm/work/b540`)에서 다원 응답과 일치를 확인한 Python 참조 계산으로 가상 입력 4개(`server_addr` `dwmqtt.dawonai.com`·`192.168.0.13`·`iot.example.com`, 가짜 mac·키)의 `verify` 시험 값을 만든다(저장소 파일 없음, 입력·결과를 작업 문서에 기록) — C3
- [x] T006 시험 값 4개 대조, `IsValidServerAddr`(허용·빈 값·`:`·`_`·공백), `RealDawonResponse_Matches`(환경 변수 `POWERMANAGER_DAWON_RESPONSE`·`POWERMANAGER_DAWON_MAC` 없으면 `Assert.Inconclusive`) 작성 (tests/Biz.Bizadm.PowerManager.Core.Tests/DeviceVerifyTests.cs) — C3
- [x] T007 mac 정규화(대문자·11자·13자·비16진수·null), 장치 키(`DAWONDNS-B540_W-{mac}`·`DAWONDNS-M130-W-{MAC}` 성공, `DAWONDNS--{mac}`·`DAWONDNS-{mac}`·`X-B540_W-{mac}` 실패), ClientId 해석 테스트 (tests/Biz.Bizadm.PowerManager.Core.Tests/DeviceIdentityTests.cs) — C4, C8, C11
- [x] T008 1000번 생성해 16자·`[A-Za-z0-9]`·서로 다름 확인 (tests/Biz.Bizadm.PowerManager.Core.Tests/MqttKeyGeneratorTests.cs) — C2

체크포인트: `dotnet test tests/Biz.Bizadm.PowerManager.Core.Tests` 통과(실측 테스트는 Inconclusive)

## 단계 2: 설정·저장

- [x] T009 `Microsoft.EntityFrameworkCore.Sqlite`·`Microsoft.EntityFrameworkCore.Design` 10.0.15 추가(추가 전 Design 10.0.15 취약점·Deprecated 확인) (Directory.Packages.props) — C5
- [x] T010 두 패키지 참조 추가, Design은 `PrivateAssets=all` (src/Biz.Bizadm.PowerManager.Server.Spa/Biz.Bizadm.PowerManager.Server.Spa.csproj) — C5
- [x] T011 [P] `DeviceMqttServerAddr`(기본 `dwmqtt.dawonai.com`)·`DatabasePath`(기본 `powermanager.db`) 추가, `Validate`에서 `DeviceVerify.IsValidServerAddr` 위반 시 `ArgumentException` (src/Biz.Bizadm.PowerManager.Server.Spa/AppConfiguration.cs) — C3, C5
- [x] T012 엔티티 `DeviceRegistration`과 `PowerManagerDbContext`(테이블 `Devices`, PK `Mac`, 길이 제한, `DateTimeOffset` 변환) 작성 (src/Biz.Bizadm.PowerManager.Server.Spa/Data/DeviceRegistration.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Data/PowerManagerDbContext.cs) — C5
- [x] T013 `IDesignTimeDbContextFactory` 작성 (src/Biz.Bizadm.PowerManager.Server.Spa/Data/PowerManagerDbContextDesignFactory.cs) — C5
- [x] T014 시작 시 `MigrateAsync` hosted service 작성 (src/Biz.Bizadm.PowerManager.Server.Spa/Data/DatabaseMigrationHostedService.cs) — C5
- [x] T015 `dotnet tool restore` 후 `dotnet ef migrations add InitialDevices --output-dir Data/Migrations` 실행. 실패하면 멈추고 사용자에게 방법을 묻는다 (src/Biz.Bizadm.PowerManager.Server.Spa/Data/Migrations/*) — C5
- [x] T016 등록(조회·새 키 생성·비어 있지 않은 값만 갱신·`LastRequestedAt` 갱신·PK 충돌 시 한 번 재조회)과 mac으로 키 조회 작성 (src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceRegistrationService.cs) — C5, C6
- [x] T017 `AddDbContextFactory`(SQLite, `Path.Combine(ContentRootPath, DatabasePath)`), `DeviceRegistrationService` 싱글턴, `DatabaseMigrationHostedService`를 브로커 앞에 등록 (src/Biz.Bizadm.PowerManager.Server.Spa/Program.cs) — C5

체크포인트: 솔루션 빌드 성공, 마이그레이션 파일 생성 확인

## 단계 3: endpoint·MQTT

- [x] T018 `Register`(mac 검증 400, 서비스 호출, `DeviceVerify.Compute`로 응답, 키·verify 없는 로그)와 `Connect`(장치 키 해석·조회, 200 `{"delay":"0"}`/404) 구현, 501 제거 (src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DevicesController.cs) — C1, C3, C4, C6, C7, C11
- [x] T019 `ValidatingConnectionAsync` 구독·해제, `MQTT client connecting: clientId=…, userName=…, hasPassword=…, passwordCheck=…` 로그(`FixedTimeEquals`, 비밀번호 값 미기록, 접속 거부 없음) (src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs) — C7, C8

체크포인트: 솔루션 빌드 성공

## 단계 4: 서버 테스트

- [x] T020 `Configuration`에 두 새 설정 명시, `DeleteTempRoot`(풀 비운 뒤 삭제) 추가 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TestApp.cs) — C9
- [x] T021 정리를 `TestApp.DeleteTempRoot`로 바꿈 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/PipelineTests.cs) — C9
- [x] T022 새 설정 기본값, 잘못된 `DeviceMqttServerAddr`(`:`·`_`·빈 값) 예외 테스트 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/AppConfigurationTests.cs) — C3, C5
- [x] T023 register·connect 501 테스트를 교체: C1 200·`Content-Type` `application/json`·원문 순서·키 두 개·verify 44자, C2 응답 `mqtt_key` 16자·`[A-Za-z0-9]`, C3 응답 verify 재계산 일치, C4 400 경우들 뒤 `IDbContextFactory`로 행 수 0·대문자 mac 소문자 저장·user_id 등 없는 본문 200, C5 재시작 후 같은 키·DB 파일·`IDbContextFactory`로 빈 값 저장과 두 시각 확인, C6 같은 키·`IDbContextFactory`로 `RegisteredAt` 유지·`LastRequestedAt` 갱신·비어 있지 않은 값만 갱신, C7 로그에 키·verify 없음, C11 경우들. 메서드마다 `DatabasePath` 고유 파일명, 정리는 `DeleteTempRoot` (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DeviceEndpointTests.cs) — C1, C2, C3, C4, C5, C6, C7, C11
- [x] T024 C8 판정 셋(맞는 비밀번호 Match, 틀림·없음 Mismatch, 미등록·형식 아님 Unknown), 모두 접속 성공, 로그에 비밀번호 없음. 정리는 `DeleteTempRoot` (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs) — C7, C8

체크포인트: `dotnet test` 솔루션 전체 통과

## 단계 5: 기준 spec 반영

- [x] T025 [P] 기준 spec 새로 작성(파일 목록, R1~ 요구는 이번 C1~C8·C11 중 이 기능 몫, 이력 한 줄) (docs/features/device-register.md) — C1, C2, C3, C4, C5, C6, C7, C11
- [x] T026 [P] R9(CONNECT 관찰 로그) 추가, `파일:` 줄에 바뀐 파일 확인, 이력 한 줄 (docs/features/mqtt-broker.md) — C8

체크포인트: 두 기준 spec의 요구가 spec.md 완료 조건과 대응

## 단계 6: 실측·실장비 확인

- [x] T027 DevServer `dawon-resp.json`을 스크래치로 복사해 환경 변수로 `RealDawonResponse_Matches`만 실행, 결과·명령을 작업 문서에 기록하고 복사본 삭제 — C3
- [x] T028 `dotnet publish` 후 배포본(`C:\Bizadm.PowerManager`) 교체. 실행 방식 확인과 중지·교체는 사용자에게 알리고 승인 뒤 — C10
- [x] T029 ConnectAp에 넘길 `server_addr`(DeviceConfig 설정 `C:\Bizadm.PowerManager.Config\config.yml`의 `ServerAddr` 또는 덮어쓰기 인수)와 배포본 `DeviceMqttServerAddr`(설정에 없으면 기본값 `dwmqtt.dawonai.com`)가 같은지 확인하고, 다르면 실장비 시험 전에 사용자에게 알린다 — C10
- [x] T030 대상 SSID(`DWD-LS120_501f96`) 확인을 받은 뒤 실장비 등록(DeviceConfig ConnectAp), 서버 로그로 등록 200·MQTT 접속 확인, C8 로그를 작업 문서에 기록 — C10

체크포인트: 작업 문서에 T005·T027·T030 결과 기록

## 의존 관계

- 단계 1 → 단계 2 → 단계 3 → 단계 4 → 단계 5 → 단계 6 순서. 단계 2·3이 `Core` 타입을, 단계 4가 구현을, 단계 6이 배포본을 쓴다
- T005 → T001·T006(시험 값은 서버 구현과 독립되도록 구현 전에 만들고, 시험 값이 있어야 테스트 작성)
- T009 → T010 → T012·T013 → T015(패키지와 컨텍스트가 있어야 마이그레이션 생성), T017은 T014·T016 뒤
- T020 → T021~T024(정리 도우미·설정)
- T028 → T029 → T030(배포본과 server_addr 일치가 확인돼야 실장비 시험)
