---
출처: R02-R5
상태: 끝남
---

# T02_05 tisyn 시간 동기 응답

## 읽을 것

- 요구사항: `R02-R5`(그때는 중계 모드가 켜지면 응답하지 않았다. 그 조건은 `device-server/T01_03`이 지웠다)
- 수용 기준: `R02-A5`
- 결정: `D02_01`~`D02_04`(옛 `docs/specs/tisyn-response/decisions.md`(지움. `15dc357`에 있음)에서 옮김)
- 계약: `C02-I2`, `C02-I3`
- 옛 작업 기록: `docs/specs/tisyn-response/`(지움. `15dc357`에 있음), 저장소 밖 작업 문서 `2026-11-20-tisyn-response.md`(기준점 `3bac44e`, 실장비 판정). 일감 #21, 커밋 `01655ef`, 실장비 판정 `e451382`

## 고칠 파일

- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttServerAccessor.cs`(새 파일): 시작된 `MqttServer` 참조
- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs`: 시작 뒤 참조 설정, 종료 때 해제
- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/LoggingDeviceMessageHandler.cs`: `TimeProvider`로 응답 값을 만들어 주입, 응답 로그
- `src/Biz.Bizadm.PowerManager.Server.Spa/Program.cs`: `MqttServerAccessor` 등록
- `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/TimeSyncResponseTests.cs`(새 파일)
- `docs/features/mqtt-broker.md`(지움. `15dc357`에 있음), `docs/reference/b540-mqtt-topic.md`(4절·5절 관찰)

## 할 일

### 단계 1: 브로커 참조 전달

### 단계 2: tisyn 응답
- 요청 토픽의 접두·장치 키로 서버 → 장치 토픽을 만들어 retain 없이 `{"sid":…,"msg":{"o":"w","e":[{"n":"/99/0/1","sv":"<epoch 초>"}]}}`를 주입하고 로그 하나를 남긴다(`R02-R5`).

### 단계 3: 자동 테스트
- 고정 시각 `TimeProvider`로 페이로드를 문자열 비교, `sid` 없는 요청, 브로커 참조가 없을 때 예외 없이 Error 로그.

확인: `dotnet build` 경고 0, `dotnet test` 전체 통과.

### 단계 4: 옛 기준 spec

### 단계 5: 실장비 확인
- 배포 뒤 M130-W 장비별 첫 응답 로그부터 60분 동안 `tisyn` 시각과 끊김을 모아 판정하고 근거 문서에 관찰로 적는다.

확인: 실장비에서 동기 완료로 판정했다(`e451382`).

## 위험

- 주입한 응답도 `OnPublishAsync`를 지나지만 서버 → 장치 토픽은 해석에 실패해 핸들러로 다시 분배되지 않는다(그때는 R7 로그가 남았고 `T02_08`이 뺐다).
- 근거가 B540 분석이고 대상은 M130-W라 형식 추정이 틀릴 수 있었다.
- 응답 값은 서버 시계에 의존한다(서버 시계 동기는 범위 밖).

## 검증 방법

| 수용 기준 | 종류 | 대상 | 전제 | 순서 | 기대 결과 |
| --- | --- | --- | --- | --- | --- |
| `R02-A5` | 단위 테스트, 실행 | `TimeSyncResponseTests`; 배포 서버 로그 | 단위: 고정 시각 `TimeProvider` / 실행: M130-W 실장비 | `dotnet test --solution Biz.Bizadm.PowerManager.slnx`; 배포 뒤 60분 로그 수집 | 페이로드가 `{"sid":"7","msg":{"o":"w","e":[{"n":"/99/0/1","sv":"<epoch 초>"}]}}`, `sid` 없으면 `"0"`, 응답 로그 하나; 실장비 동기 완료 |

## 원칙 점검

- 적용 규칙: 그때의 MCP `workflow/spec-driven.md`, MCP `git/github-issue.md`
- 충돌: 없음
