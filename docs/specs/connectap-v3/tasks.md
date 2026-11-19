# ConnectAp 설정 JSON을 참고 구현 V3에 맞춤 작업

형식: `- [x] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 설정 JSON 형식 변경

- [x] T001 `ToJson(mac, ssid, pass, masked)`로 바꾸고 `mqtt_key` 항목·`UnsafeRelaxedJsonEscaping` 옵션 제거, 기본 직렬화로 변경, 주석을 V3 기준으로 고침 (src/Biz.Bizadm.PowerManager.DeviceConfig/Protocol/DeviceSettings.cs) — C1, C2, C3
- [x] T002 `MqttKeyGenerator.Create()` 호출·`mqttKey` 변수 제거, `ToJson` 두 호출을 새 시그니처로. 송신 순서는 그대로 (src/Biz.Bizadm.PowerManager.DeviceConfig/Transport/DeviceClient.cs) — C1, C3, C4
- [x] T003 [P] `RequestAsync` 주석 "pass·mqtt_key 보호" → "pass 보호" (src/Biz.Bizadm.PowerManager.DeviceConfig/Transport/DeviceConnection.cs) — C3
- [x] T004 `MqttKeyGenerator` 삭제 (src/Biz.Bizadm.PowerManager.DeviceConfig/Protocol/MqttKeyGenerator.cs) — C1
- [x] T005 `MqttKeyGeneratorTests` 삭제 (tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests/Protocol/MqttKeyGeneratorTests.cs) — C5

체크포인트: `dotnet build src/Biz.Bizadm.PowerManager.DeviceConfig` 성공(테스트 프로젝트는 단계 2에서 맞춤)

## 단계 2: 테스트

- [x] T006 [P] 키 이름·순서 일치(`CollectionAssert.AreEqual`)와 값 문자열 검사, `mqtt_key` 없음, 기본 인코더 이스케이프 결과(`a\u002Bb\u0026c\u003Cd\u003E\u0027e`) 검사로 교체, 마스킹 검사에서 `pass`만 `***`·원문 없음, 같은 파일의 다른 `ToJson` 호출(`ToJson_SslSupportFalse_IsNo` 등)도 새 시그니처로 (tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests/Protocol/DeviceSettingsTests.cs) — C1, C2, C3, C5
- [x] T007 [P] `ConnectApAsync_OneConnectionInOrder`에서 `mqtt_key` 없음 확인·송신 다섯 줄 순서 유지, 정상·시간 초과·연결 종료 세 경우에서 로그·예외에 원문 `pass`와 이스케이프된 `pass`가 없음을 검사, `mqtt_key` 노출 검사 제거 (tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests/Transport/DeviceClientTests.cs) — C1, C3, C4, C5
- [x] T008 솔루션 빌드(`dotnet build Biz.Bizadm.PowerManager.slnx -warnaserror`)와 전체 테스트(`dotnet test --solution Biz.Bizadm.PowerManager.slnx`) 실행 (Biz.Bizadm.PowerManager.slnx) — C5

체크포인트: 빌드 경고 0, 테스트 실패 0

## 단계 3: 기준 spec

- [x] T009 기능 `device-config` 기준 spec 새로 작성: `파일:`에 DeviceConfig 라이브러리·테스트 프로젝트 파일(이번에 지운 두 파일 제외), `docs/cli/0001`~`0003`와 이번 변경으로 지금 요구(R번호), 이력에 이번 작업 한 줄 (docs/features/device-config.md) — C1, C2, C3, C4

체크포인트: `파일:` 줄의 경로가 모두 실제로 있고, C1~C4가 R번호 요구로 들어가 있음

## 단계 4: 배포·실장비 시험

- [x] T010 `FolderProfile`로 MCP 게시(`dotnet publish src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp -p:PublishProfile=FolderProfile`), MCP가 꺼져 있는지 확인, 기존 exe·pdb를 스크래치에 백업한 뒤 게시 결과의 exe·pdb만 덮어쓰기 (MCP 로컬 배포 폴더) — C6
- [x] T011 사용자가 MCP를 시작하면 DNS 서버 `tcpdump`(53번)를 시작하고 B540 실장비 1에 `ConnectAp`(사용자 공유기, 설정 파일 값 그대로) 전송, 약 90초 동안 `dwapi`/`dwmqtt` 조회·이 PC `18443`·`8883` 연결·SoftAP 유무와 채널을 관찰 — C6
- [x] T012 관찰 결과와 판정(접속하면 두 형식 차이를 원인으로 확정, 아니면 보류)을 기준 spec 이력 줄과 작업 문서에 기록 (docs/features/device-config.md) — C6

체크포인트: 작업 문서와 기준 spec에 C6 결과·판정이 있음

## 의존 관계

- T001 → T002(새 시그니처를 써야 함). T004는 T002 뒤(호출이 없어진 뒤 삭제). T003은 독립.
- 단계 2는 단계 1 뒤. T006·T007은 서로 다른 파일이라 동시 가능. T008은 T005~T007 뒤.
- 단계 3(T009)은 단계 2 뒤(바뀐 요구가 테스트로 확인된 뒤 반영).
- 단계 4는 단계 2 체크포인트 뒤. T011은 사용자의 MCP 시작이 필요하다. T012는 T009가 만든 기준 spec에 이력 결과를 덧붙인다.
