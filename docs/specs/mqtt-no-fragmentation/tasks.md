# MQTT 브로커 패킷 분할 끄기 작업

형식: `- [ ] T001 [P] 설명 (파일 경로) — C번호`
`[P]`: 다른 파일을 고치고 끝나지 않은 작업에 의존하지 않아 다른 작업과 동시에 할 수 있음

## 단계 1: 테스트 도우미 옮기기
- [x] T001 `MqttBrokerTests`의 private `WritePfx`를 `TestApp.WritePfx(string path, string password)`로 옮기고, `MqttBrokerTests`는 `TestApp.WritePfx(path, PfxPassword)`를 부른다 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TestApp.cs, tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttBrokerTests.cs) — C3

체크포인트: `dotnet test --filter MqttBrokerTests` 통과(동작 같음)

## 단계 2: 분할 끄기 구현
- [x] T002 `public static MqttServerOptions BuildServerOptions(AppConfiguration, X509Certificate2?)`를 추가해 `StartAsync`의 옵션 빌더 코드를 옮기고 `.WithoutPacketFragmentation()`을 더한다. `StartAsync`는 인증서를 읽은 뒤 이 메서드로 옵션을 만든다. 클래스 요약 주석에 분할을 끄는 내용과 이유를 더한다 (src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs) — C1, C2

체크포인트: `dotnet build` 경고 0·오류 0

## 단계 3: 테스트
- [x] T003 C2 테스트: `BuildServerOptions`를 인증서 없이·있게 각각 불러 `DefaultEndpointOptions.AllowPacketFragmentation`과 `TlsEndpointOptions.AllowPacketFragmentation`이 모두 `false`인지 단언 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttPacketFragmentationTests.cs) — C2
- [x] T004 C1 테스트: TLS 설정으로 앱을 시작하고, 원시 수신 바이트를 기록하는 `Stream` 래퍼 위의 `SslStream`(TLS 1.2, 지문 확인)으로 MQTT 3.1 CONNECT·서버 → 장치 토픽 QoS 0 SUBSCRIBE를 보낸 뒤, 비TLS 포트 MQTTnet 클라이언트(V310)의 `tisyn`으로 응답을 받게 한다. PUBLISH 전체를 읽어 토픽·`"o":"w"`를 확인하고, 표시 위치부터의 원시 바이트가 TLS 레코드 하나(형식 23)이며 남는 바이트가 없는지 단언 (tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttPacketFragmentationTests.cs) — C1
- [x] T005 변형 확인: `.WithoutPacketFragmentation()`을 잠시 빼면 T003·T004가 실패하는지 보고 되돌린다(되돌린 뒤 BOM·줄 끝 변화 없음 확인) (src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs) — C1, C2

체크포인트: `dotnet test --filter MqttPacketFragmentationTests` 통과, T005 변형에서 두 테스트 실패 확인

## 단계 4: 기준 spec 반영
- [x] T006 R14 추가(plan.md 인터페이스·데이터의 안), 머리말 한 문장, `파일:` 줄에 tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttPacketFragmentationTests.cs, 이력 한 줄(`mqtt-no-fragmentation`, R14 추가) (docs/features/mqtt-broker.md) — C4

체크포인트: 기준 spec을 읽어 R14·머리말·`파일:` 줄·이력 확인

## 단계 5: 전체 검증과 실장비
- [x] T007 `dotnet build --no-incremental` 경고 0·오류 0, `dotnet test` 전체 통과(기존 건너뜀 1건 제외) — C3
- [x] T008 Release 빌드를 서버 로컬 배포 폴더에 배포(설정·DB·`secrets.dek`·인증서·log 제외), 사용자가 서버 시작, `tshark`로 TLS 포트 8883을 약 140초 캡처해 `tisyn`을 보낸 장비마다 응답이 TLS 레코드 하나인지 확인, 재요청 간격 관찰을 작업 문서에 기록 — C5

체크포인트: T007 결과, T008 캡처 결과를 작업 문서에 기록

## 의존 관계
- T001 → T004: C1 테스트가 `TestApp.WritePfx`를 쓴다.
- T002 → T003·T004: 테스트가 `BuildServerOptions`와 분할 끔 동작에 기댄다.
- T003·T004 → T005: 변형 확인은 두 테스트가 있어야 한다.
- T006은 단계 2·3 뒤(새 테스트 파일 이름이 정해진 뒤) 마지막 구현 단계로 한다.
- T007은 T001~T006 뒤, T008은 T007 뒤(검증한 빌드를 배포).
