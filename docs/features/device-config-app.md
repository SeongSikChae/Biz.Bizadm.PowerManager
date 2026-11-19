# device-config-app

파일: src/Biz.Bizadm.PowerManager.DeviceConfig.App/Biz.Bizadm.PowerManager.DeviceConfig.App.csproj, src/Biz.Bizadm.PowerManager.DeviceConfig.App/Program.cs, src/Biz.Bizadm.PowerManager.DeviceConfig.App/AppConfiguration.cs, src/Biz.Bizadm.PowerManager.DeviceConfig.App/ConsoleDeviceConfigSelector.cs, src/Biz.Bizadm.PowerManager.DeviceConfig.App/ExitCode.cs, src/Biz.Bizadm.PowerManager.DeviceConfig.App/Security/ConfigSecretProtector.cs

B540 장치 설정 라이브러리(`device-config`)를 콘솔에서 실행하는 App. 설정 파일의 `Commands`를 순서대로 실행하고, 무선 어댑터·장치 AP·공유기 Wi-Fi는 콘솔 번호로 고른다. 상세 설계는 `docs/cli/0001`~`0003`에 있다.

## 요구(완료 조건)

- R1. 실행 인수는 `--config <yaml> --log <dir>`이며 종료 코드는 정상 0, 인수 오류 1, 설정 오류 2, 실행 오류(명령 실패 포함) 3이다. 명령이 실패하면 PC Wi-Fi를 복구한 뒤 종료 코드 3으로 끝난다.
- R2. YAML 설정(키는 속성 이름 그대로)은 `ContentRootPath`·`OsKms` 필수, `Device` 기본값(`ApPrefix` `DWD-`, `Host` `192.168.244.1`, `Port` 5000, `TimeoutSeconds` 10, `ApConnectTimeoutSeconds` 30), `Commands` 1개 이상 필수다. `Commands`에 `ConnectAp`가 있으면 `WifiProfiles`(SSID 중복 금지)와 `Settings`가 필수다.
- R3. `Settings`는 `ApiServerAddr`·`ApiServerPort`·`ServerAddr`·`ServerPort`·`UserId` 필수(포트 1~65535), `SslSupport`(false)·`Company`(DAWONDNS)·`Model`(B540_W)·`Lati`("")·`Long`("")·`Topic`(dwd)·`Protocol`(V3)·`MqttKey` 선택이다. `Protocol`은 `V1`|`V2`|`V3`|`V4`(대소문자 구분)이며 그 밖의 값, 또는 `ConnectAp`가 있고 V1인데 `MqttKey`가 없으면 설정 오류(종료 코드 2)로 PC Wi-Fi를 바꾸기 전에 끝난다.
- R4. `WifiProfiles[].Pass`와 `Settings.MqttKey`는 기동 시 OsKms(`dotnet/kms-config-secrets.md`)로 암호화해 설정 파일에 다시 쓰고, 복호화한 값만 실행 옵션에 넣는다(YAML 모델의 암호문을 장치로 보내지 않음).
- R5. 무선 어댑터·장치 AP·공유기 Wi-Fi(장치 `apscan`에 보인 등록 Wi-Fi, RSSI 순)를 콘솔 번호로 고른다. 표준 입력이 닫히면 실행 오류다.

## 이력

| 날짜 | 작업 | 바꾼 요구 | 변경 폴더 | 작업 문서 |
|---|---|---|---|---|
| 2026-11-19 | device-config-versions | R1~R5 처음 작성. R3에 `Settings.Protocol`·`Settings.MqttKey`(V1 필수), R4에 `MqttKey` 암호화 추가 | docs/specs/device-config-versions/ | 2026-11-19-device-config-versions.md |

표에 없는 이전 변경: 기록 없음(docs/cli/0001~0003 참조. #6 최초 구현)
