# device-register

파일: src/Biz.Bizadm.PowerManager.Core/Devices/DeviceVerify.cs, src/Biz.Bizadm.PowerManager.Core/Devices/MqttKeyGenerator.cs, src/Biz.Bizadm.PowerManager.Core/Devices/DeviceIdentity.cs, src/Biz.Bizadm.PowerManager.Core/Devices/DeviceRegisterResponse.cs, src/Biz.Bizadm.PowerManager.Core/Devices/DeviceRegisterRequest.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DevicesController.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Devices/DeviceRegistrationService.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Data/DeviceRegistration.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Data/PowerManagerDbContext.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Data/PowerManagerDbContextDesignFactory.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Data/DatabaseMigrationHostedService.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Data/Migrations/, src/Biz.Bizadm.PowerManager.Server.Spa/AppConfiguration.cs, src/Biz.Bizadm.PowerManager.Server.Spa/Program.cs, tests/Biz.Bizadm.PowerManager.Core.Tests/DeviceVerifyTests.cs, tests/Biz.Bizadm.PowerManager.Core.Tests/DeviceIdentityTests.cs, tests/Biz.Bizadm.PowerManager.Core.Tests/MqttKeyGeneratorTests.cs, tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/DeviceEndpointTests.cs, tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/AppConfigurationTests.cs

장치 등록(`POST /api/v1/devices/register/create`)과 접속 조회(`GET /api/v1/device/connect/{deviceKey}`). 서버가 장치별 MQTT 키(`mqtt_key`)를 발급해 SQLite(EF Core)에 저장하고, 장치가 키를 받아들이게 하는 `verify`를 계산해 응답한다. `verify` 계산식은 B540 펌웨어 분석으로 찾아 실제 다원 서버 응답과 일치함을 확인했다(`docs/specs/device-register-mqtt-key/spec.md`). 중계 모드(`DawonRelay.Enabled`)가 켜져 있으면 `DevicesController`가 `DawonRestRelay`로 다원에 보내고 `DeviceRegistrationService.StoreRelayedKeyAsync`로 다원 키를 저장한다(dawon-relay 기준 spec).

## 요구(완료 조건)

- R1. 올바른 요청에 register가 200과 `application/json` 본문 `{"verify":"<44자>","mqtt_key":"<키>"}`를 반환한다. 키는 이 두 개뿐이고 `verify`가 `mqtt_key`보다 앞이다. 중계 모드가 켜져 있으면 상태 코드·`Content-Type`·본문은 dawon-relay R2·R3을 따른다.
- R2. 새로 발급하는 `mqtt_key`는 암호학적 난수로 만든 16자이며 문자는 `[A-Za-z0-9]`에서만 고른다. 중계 모드가 켜져 있으면 발급하지 않고 다원 키를 쓴다(dawon-relay R2).
- R3. `verify`는 치환표 `XGAqW.Ibj3xdCRFzf60oMKNDYPaTQHLUhls4v1mei5S9w-r8n=kJtV/O+7ZEu2cgypB`(67자)로, `s = DeviceMqttServerAddr + mac(소문자)`, `k = mqtt_key`에 대해 i = 0..43마다 `A[(pos(s[i mod |s|])·sum(k) + pos(k[i mod |k|])·sum(s)) mod 67]`이다. `DeviceMqttServerAddr`(기본 `dwmqtt.dawonai.com`)는 장치에 설정된 `server_addr`와 같아야 하며, 치환표 문자로만 이뤄지지 않으면(빈 값 포함) 시작 시 설정 오류다.
- R4. `device.mac`이 12자리 16진수가 아니면(없거나 빈 값 포함) 400 ProblemDetails를 반환하고 저장하지 않는다. mac은 소문자로 정규화한다. `account.user_id`·`device.model_id`·`device.company`는 없거나 빈 값이어도 거부하지 않는다.
- R5. 발급한 키를 mac별로 SQLite(`DatabasePath`, 기본 `powermanager.db`, `ContentRootPath` 기준)에 저장한다(mac, user_id, model_id, company, mqtt_key, 처음 등록 시각, 마지막 요청 시각). 처음 등록 때 없는 값은 빈 값으로 저장한다. 시작 시 마이그레이션을 적용하며, 재시작해도 값이 남는다.
- R6. 이미 저장된 mac으로 다시 요청하면 저장된 키로 응답하고(`verify`는 현재 설정으로 다시 계산), 마지막 요청 시각을 갱신하며, user_id·model_id·company는 비어 있지 않은 값이 왔을 때만 바꾼다. 같은 mac의 첫 등록이 동시에 들어오면 저장된 키 하나로 응답한다. 중계 모드가 켜져 있으면 다원 키로 바꾸고 동시 첫 등록 문장은 적용하지 않는다(dawon-relay R2).
- R7. 서버 로그에 `mqtt_key`와 `verify` 값을 남기지 않는다.
- R8. connect는 `deviceKey`가 `DAWONDNS-{모델}-{mac}`(모델은 비어 있지 않고 `-` 포함 가능, mac은 마지막 `-` 뒤 12자리 16진수)이고 그 mac(대소문자 무관)이 등록돼 있으면 200 `{"delay":"0"}`, 아니면 404 ProblemDetails를 반환한다. 중계 모드가 켜져 있으면 다원 응답을 그대로 돌려준다(dawon-relay R4).

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 |
|---|---|---|---|
| 2026-11-19 | device-register-mqtt-key | R1~R8 처음 작성(register·connect 501 껍데기를 구현으로 대체) | docs/specs/device-register-mqtt-key/ |
| 2026-11-20 | dawon-relay | R1·R2·R6·R8 바꿈(중계 모드 켜짐이면 dawon-relay 기준) | docs/specs/dawon-relay/ |

표에 없는 이전 변경: 기록 없음(docs/server/0001~0003의 endpoint 껍데기, #1)
