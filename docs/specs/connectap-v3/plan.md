# ConnectAp 설정 JSON을 참고 구현 V3에 맞춤 계획

## 요약

`DeviceSettings.ToJson`에서 `mqtt_key` 인자와 키를 없애고, 직렬화 옵션을 `System.Text.Json` 기본 인코더로 바꾼다. `DeviceClient.ConnectApAsync`는 `MqttKeyGenerator` 호출을 없애고 나머지 흐름(핸드셰이크 → `apscan` → Wi-Fi 선택 → 설정 JSON → `connectap`)은 그대로 둔다. 쓰는 곳이 없어지는 `MqttKeyGenerator`와 그 테스트는 지운다. 테스트를 C1~C4에 맞게 고치고, 기능 `device-config`의 기준 spec을 새로 만든다. 마지막으로 MCP를 다시 게시·배포해 `DWD-LS120_501f96`로 실장비 시험을 한다(C6).

## 바꿀 파일·구조

- `src/Biz.Bizadm.PowerManager.DeviceConfig/Protocol/DeviceSettings.cs`: `ToJson(string mac, string ssid, string pass, bool masked = false)`로 시그니처 변경(`mqttKey` 제거). 사전에서 `mqtt_key` 항목 제거. `JsonOptions`(`UnsafeRelaxedJsonEscaping`) 필드와 `System.Text.Encodings.Web` using 제거, `JsonSerializer.Serialize(values)` 기본 옵션으로 직렬화. 주석을 V3 기준으로 고침
- `src/Biz.Bizadm.PowerManager.DeviceConfig/Transport/DeviceClient.cs`: `MqttKeyGenerator.Create()` 호출과 `mqttKey` 변수 제거, `ToJson` 호출 두 곳을 새 시그니처로
- `src/Biz.Bizadm.PowerManager.DeviceConfig/Transport/DeviceConnection.cs`: `RequestAsync` 주석의 "pass·mqtt_key 보호"를 "pass 보호"로
- `src/Biz.Bizadm.PowerManager.DeviceConfig/Protocol/MqttKeyGenerator.cs`: 삭제(쓰는 곳 없음)
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests/Protocol/MqttKeyGeneratorTests.cs`: 삭제
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests/Protocol/DeviceSettingsTests.cs`: 키 목록·순서 검사(`mqtt_key` 없음, 순서 일치), `ToJson_DoesNotEscapeSpecialCharacters`를 기본 인코더 결과 검사로 교체, 마스킹 검사에서 `mqtt_key` 제거
- `tests/Biz.Bizadm.PowerManager.DeviceConfig.Tests/Transport/DeviceClientTests.cs`: `ConnectApAsync_OneConnectionInOrder`에서 `mqtt_key` 검사를 "키 없음"으로 바꾸고 송신 순서 검사 유지, 비밀 노출 검사 두 곳에서 `mqtt_key` 부분을 빼고 이스케이프된 `pass`도 로그·예외에 없는지 검사
- `docs/features/device-config.md`: 새 기준 spec. `DeviceConfig` 라이브러리와 테스트 프로젝트 파일을 `파일:`에 두고, `docs/cli/0001`~`0003`와 이번 변경으로 지금 요구(R번호)를 처음 작성

`DeviceConfig.Mcp`·`DeviceConfig.App` 코드와 MCP tool 설명은 바꾸지 않는다(`mqtt_key`를 언급하지 않고, `apscan` 흐름도 그대로다). `docs/cli/0001`~`0003`는 지난 작업 기록이라 고치지 않는다(spec).

## 결정

- 기본 인코더 사용: V3(`JsonSerializer.Serialize(config)`)와 같은 결과를 내려면 옵션 없이 직렬화하면 된다 / 검토한 대안: `JavaScriptEncoder.Default`를 명시한 옵션 필드 — 결과는 같지만 필드가 남을 이유가 없어 고르지 않음
- `MqttKeyGenerator` 삭제: 유일한 호출자(`DeviceClient`)에서 빠져 죽은 코드가 된다. 서버 발급 키를 넣는 흐름은 서버 REST 구현 때 따로 정한다 / 검토한 대안: 남겨 두기 — 쓰는 곳이 없고 테스트만 유지 비용이 들어 고르지 않음
- `Dictionary<string,string>` 유지: 키 순서는 삽입 순서로 직렬화되며 지금도 이 동작에 기대고 있다. 순서를 테스트로 고정해(C1) 바뀌면 드러나게 한다 / 검토한 대안: V3처럼 속성 순서가 있는 record — 키 이름 매핑을 새로 만들어야 하고 이번 범위(데이터 형식)보다 커서 고르지 않음
- 배포: `src/Biz.Bizadm.PowerManager.DeviceConfig.Mcp/Properties/PublishProfiles/FolderProfile.pubxml`(Release, win-x64, 자체 포함 단일 파일)로 게시해 결과의 exe·pdb만 `C:\Bizadm.PowerManager.Config`에 덮어쓴다. `config.yml`·`deviceconfig.dek`·`log`는 건드리지 않는다. 덮어쓰기 전에 기존 exe·pdb를 스크래치에 백업한다. MCP 시작·중지는 사용자가 평소 방식(콘솔 또는 서비스 `DeviceConfigMcp`)으로 한다 / 검토한 대안: 서비스 재등록 — 등록 정보(실행 경로·계정)는 바뀌지 않아 불필요

## 인터페이스·데이터

- `DeviceSettings.ToJson(string mac, string ssid, string pass, bool masked = false) : string` (공개 시그니처 변경. 호출자는 `DeviceClient`뿐)
- 설정 JSON 한 줄 예: `{"mac":"…","api_server_addr":"…","api_server_port":"18443","server_addr":"…","server_port":"8883","ssl_support":"yes","ssid":"…","pass":"…","user_id":"…","company":"DAWONDNS","model":"B540_W","lati":"0","long":"0","topic":"dwd"}` + `\n`
- 로그(`TX`)는 같은 형식에서 `pass`만 `***`

## 오류·위험

- 장치 파서가 `\uXXXX`를 풀지 못하면, 비밀번호에 `+ & < > '`·비ASCII가 있을 때 저장값이 달라진다. V3가 같은 방식이라 받아들인다(spec). 지금 `KT-FTTH-P1-IOT` 비밀번호에 이 문자가 있는지는 모른다(평문을 보지 않는다).
- 실행 중인 MCP exe는 덮어쓸 수 없다. 배포 전에 MCP가 꺼져 있는지 확인하고, 켜져 있으면 사용자에게 중지를 요청한다.
- C6 관찰 수단(DNS 서버 `20.200.215.194`의 `tcpdump`)은 원격 서버 상태에 달려 있다. 접속하지 못하면 이 PC의 서버 포트 연결과 SoftAP 채널 변화만으로 관찰하고 그 사실을 기록한다.
- 실장비 상태 변경(`ConnectAp`)은 대상 SSID `DWD-LS120_501f96`을 사용자가 지정한 것으로만 한다.

## 검증 전략

- C1: `DeviceSettingsTests`에서 직렬화 결과의 속성 이름 배열을 기대 순서와 `CollectionAssert.AreEqual`로 비교하고 값이 모두 문자열인지 확인. `DeviceClientTests.ConnectApAsync_OneConnectionInOrder`에서 실제 송신 줄에 `mqtt_key`가 없음을 확인
- C2: `DeviceSettingsTests`에서 `pass`=`a+b&c<d>'e`의 직렬화 결과에 `"pass":"a\u002Bb\u0026c\u003Cd\u003E\u0027e"`가 있는지 확인
- C3: `DeviceSettingsTests` 마스킹 검사(`pass`만 `***`, 원문 없음), `DeviceClientTests`의 정상·시간 초과·연결 종료 세 경우에서 로그·예외에 원문 `pass`와 이스케이프된 `pass`가 없음을 확인
- C4: `DeviceClientTests.ConnectApAsync_OneConnectionInOrder`의 송신 다섯 줄 순서·`connectap` 줄 검사와 `ConnectApAsync_NotOk_Throws` 유지
- C5: `dotnet build Biz.Bizadm.PowerManager.slnx -warnaserror`와 `dotnet test --solution Biz.Bizadm.PowerManager.slnx` 실행 결과(Microsoft.Testing.Platform. `Device` 카테고리는 테스트 프로젝트의 `TestingPlatformCommandLineArguments`가 기본으로 뺀다)
- C6: 게시·배포 후 사용자가 MCP를 시작하면 `run_device_commands`로 `DWD-LS120_501f96`에 `ConnectAp`(`KT-FTTH-P1-IOT`, 설정 파일 값 그대로)를 보내고, 약 90초 동안 DNS 서버 `tcpdump`(53번)의 `dwapi`/`dwmqtt` 조회, 이 PC 서버 포트(`18443`·`8883`) 연결, 장치 SoftAP 유무·채널을 관찰해 작업 문서와 기준 spec 이력에 결과와 판정(접속하면 원인 확정, 아니면 보류)을 적는다

## 원칙 점검

- 적용 규칙:
  - MCP `workflow/spec-driven.md`: 세 문서, 기준 spec, 네 검토
  - MCP `git/github-issue.md`: #11 참조, 종결 조건
  - 전역 `language-preference.md`: 사용자에게 보이는 문장은 한국어, 코드·주석은 저장소 관례
  - 전역 `portable-python.md`: 이번 작업은 Python을 쓰지 않음
  - 메모리 `device-target-confirmation`: 상태 변경 명령 전 대상 SSID 확인
  - 저장소 `Directory.Build.props`: `Nullable` 사용, `ImplicitUsings` 사용(저장소에 CLAUDE.md·AGENTS.md 없음)
- 충돌: 없음
