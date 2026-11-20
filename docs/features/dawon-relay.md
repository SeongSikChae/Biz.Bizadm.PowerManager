# dawon-relay

파일: 없음(중계 모드 제거로 모든 파일을 지웠거나 다른 기능에만 속함)

제거된 기능이다. 장치와 다원 서버 사이의 실제 MQTT·REST 주고받음을 분석하려고 Server.Spa가 장치 요청을 다원 서버로 전달하고 양방향 원문을 기록하는 중계 모드(설정 `DawonRelay.Enabled`)를 두었다(dawon-relay 작업). 실제로 제대로 동작하지 않았고 실장비 확인도 하지 못했으며, 다원 서버와 직접 주고받으며 원문을 기록하는 가짜 장비(dawon-fake-device 기준 spec)가 생겨 실서버 데이터 확인은 그것으로 하므로 제거했다(dawon-relay-removal 작업). 제거 전 요구와 설계는 `docs/specs/dawon-relay/`에 있다.

## 요구(완료 조건)

없음(R1~R13 모두 뺌).

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 |
|---|---|---|---|
| 2026-11-20 | dawon-relay | R1~R13 처음 작성(새 기능) | docs/specs/dawon-relay/ |
| 2026-11-20 | dawon-relay-removal | R1~R13 뺌(중계 모드 제거) | docs/specs/dawon-relay-removal/ |
