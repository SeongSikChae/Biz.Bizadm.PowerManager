---
출처: R02-R1, R02-R18
상태: 끝남
---

# T02_10 MQTTS TLS 버전과 암호 목록

## 읽을 것

- 요구사항: `R02-R1`, `R02-R18`
- 수용 기준: `R02-A1`, `R02-A18`
- 결정: `D02_20`~`D02_31`(옛 `docs/specs/mqtts-tls-policy/decisions.md`(지움. `15dc357`에 있음)에서 옮김)
- 계약: `C02-I5`
- 옛 작업 기록: `docs/specs/mqtts-tls-policy/`(지움. `15dc357`에 있음), 저장소 밖 작업 문서 `2026-11-23-mqtts-tls-policy.md`(기준점 `7734985`). 일감 #35, 커밋 `15dc357`

## 고칠 파일

- `src/Biz.Bizadm.PowerManager.Server.Core/ServerKestrelExtensions.cs`: 암호 목록 해석을 `ResolveCipherSuites(…, warnIfNoTls13, isWindows)`로 분리
- `src/Biz.Bizadm.PowerManager.Server.Spa/Mqtt/MqttBrokerHostedService.cs`: `BuildServerOptions`에 로거, TLS 1.2·1.3, 암호 정책
- `tests/Biz.Bizadm.PowerManager.Server.Core.Tests/ServerKestrelExtensionsTests.cs`, `tests/Biz.Bizadm.PowerManager.Server.Spa.Tests/MqttTlsPolicyTests.cs`(새 파일), `MqttPacketFragmentationTests.cs`, `MqttBrokerTests.cs`
- `docs/features/mqtt-broker.md`(지움. `15dc357`에 있음)

## 할 일

### 단계 1: 암호 목록 해석 분리(Server.Core)

### 단계 2: MQTTS 옵션(Server.Spa)
- 인증서가 있으면 `Tls12 | Tls13`과 HTTPS와 같은 규칙의 `CipherSuitesPolicy`.

### 단계 3: 옛 기준 spec

### 단계 4: 리눅스 서버 배포 확인
- linux-x64 게시본의 실행 파일만 교체하고(설정·DB·인증서·로그 유지) 서비스 재시작, `openssl s_client -connect 127.0.0.1:8883`.

확인: `-tls1_2 -cipher AES256-SHA256`·`AES128-SHA256` 성공, `-tls1_3` 성공, 목록 밖 암호 거절.

## 위험

- MQTTnet이 엔드포인트 정책을 TLS 연결에 넘기는지는 DLL 메타데이터로만 봤다(리눅스 배포로 확인).
- Windows에서 `CipherSuitesPolicy`는 `PlatformNotSupportedException`이라 목록을 무시하고 경고한다.
- TLS 1.0·1.1 거절은 클라이언트 쪽이 막을 수 있어 작업 PC 테스트로 가리지 않았다.
- 실행 파일을 `cp -f`로 덮으면 포트 권한(setcap)이 사라진다(직접 실행할 때만 영향).

## 검증 방법

| 수용 기준 | 종류 | 대상 | 전제 | 순서 | 기대 결과 |
| --- | --- | --- | --- | --- | --- |
| `R02-A1` | 단위 테스트 | `MqttTlsPolicyTests`, `MqttBrokerTests#Mqtts_Tls12_…` | Windows 11 작업 PC | `dotnet test --solution Biz.Bizadm.PowerManager.slnx` | `SslProtocol == Tls12 \| Tls13`, TLS 1.2·1.3 핸드셰이크 성공 |
| `R02-A18` | 단위 테스트, 실행 | `MqttTlsPolicyTests`, `ServerKestrelExtensionsTests`; 리눅스 서버 `/PowerManager` | 실행: 사용자 승인, `Tls.IncludeCipherSuites`가 든 설정 | 테스트 명령; `openssl s_client` 조합 | 빈 목록은 정책 `null`·경고 없음, 잘못된 이름 경고·제외, Windows 경고·무시; 리눅스에서 목록 안 암호만 성공 |

## 원칙 점검

- 적용 규칙: 그때의 MCP `workflow/spec-driven.md`, MCP `git/github-issue.md`, 메모리 리눅스 서버 배포
- 충돌: 없음
