# device-config-mcp 사용

`Biz.Bizadm.PowerManager.DeviceConfig.Mcp`는 MCP Streamable HTTP 서버다. 서버 프로세스 하나를 여러 MCP 클라이언트가 URL로 접속해 함께 쓴다. 같은 PC의 루프백(`127.0.0.1`, `::1`)에서만 접속할 수 있고 인증은 없다.

## 설정

```yaml
ContentRootPath: D:\DeviceConfigMcp      # 절대 경로
OsKms:
  CredentialService: https://deviceconfig.powermanager.local
  CredentialAccount: deviceconfig
  SecretsDekFilePath: deviceconfig.dek
Http:
  Port: 65532                            # 필수, 1~65535. 기본값 없음
Device:
  Port: 5000
WifiProfiles:                            # 선택. ConnectAp에 필요
  - Ssid: MyRouter
    Pass: plain                          # 기동 시 DEKENC:... 로 암호화해 다시 기록
Settings:                                # 선택. ConnectAp 기본값
  Protocol: V3                           # 선택. ConnectAp 흐름 V1|V2|V3|V4(대소문자 구분, 기본 V3)
  MqttKey: plain                         # V1일 때 필요. 기동 시 DEKENC:... 로 암호화해 다시 기록
```

ConnectAp 흐름(참고 구현 PowerManagerConfig의 Configrator 버전):

| `Protocol` | 대상(참고 구현 주석) | 장치 송수신(핸드셰이크·`apscan` 뒤) |
|---|---|---|
| `V1` | M130, B540 V1.0.26 | `mqtt_key`가 든 구형 설정 JSON만 보내고 닫음 |
| `V2` | B540 v1.0.28 | 설정 JSON → 응답 1개 → `{"delay":"0"}` → 닫음 |
| `V3`(기본) | B540 v1.0.30·v1.0.32 | 설정 JSON → `OK` → `connectap` → `OK` |
| `V4` | B550 v1.0.10 | 핸드셰이크에서 `START_OK` 앞 프레임 최대 2개 건너뜀, 설정 JSON → `connectap`(응답을 기다리지 않음) → 닫음 |

`run_device_commands`의 `settings.protocol`로 장치마다 흐름을 바꿀 수 있다. `MqttKey`는 tool 인자로 받지 않으며, 설정 파일에 없는데 V1로 `ConnectAp`를 보내면 PC Wi-Fi를 바꾸기 전에 오류가 난다.

MCP endpoint: `http://127.0.0.1:<Http.Port>/mcp`

## 콘솔 실행

```powershell
D:\DeviceConfigMcp\Biz.Bizadm.PowerManager.DeviceConfig.Mcp.exe --config D:\DeviceConfigMcp\config.yaml --log D:\DeviceConfigMcp\log
```

Ctrl+C로 종료한다. 종료 코드는 정상 0, 인수 오류 1, 설정 오류 2, 실행 오류(포트 사용 중 등) 3이다.

## Windows 서비스 등록

관리자 PowerShell에서 실행한다. 등호(`=`) 뒤에는 공백이 하나 있어야 한다.

```powershell
sc.exe create DeviceConfigMcp binPath= "D:\DeviceConfigMcp\Biz.Bizadm.PowerManager.DeviceConfig.Mcp.exe --config D:\DeviceConfigMcp\config.yaml --log D:\DeviceConfigMcp\log" start= auto obj= ".\<Windows 로그인 계정>" password= "<계정 비밀번호>"
```

`sc.exe`로 계정을 지정하면 그 계정에 "서비스로 로그온" 권한이 주어지지 않아 시작이 오류 1069로 실패할 수 있다. 시작하기 전에 서비스 관리 콘솔(`services.msc`)에서 DeviceConfigMcp 속성 > 로그온 탭을 열어 같은 계정과 비밀번호를 한 번 다시 입력한다(이때 권한이 함께 주어진다).

```powershell
sc.exe start DeviceConfigMcp
```

```powershell
sc.exe stop DeviceConfigMcp
```

```powershell
sc.exe delete DeviceConfigMcp
```

- **실행 계정**: `obj=`에 OsKms 자격 증명을 등록한 Windows 로그인 계정을 지정한다. 실행 계정이 다르면(예: LocalSystem) OsKms 자격 증명 금고가 달라져 Wi-Fi 프로필(`WifiProfiles`) 복호화가 실패한다.
- **절대 경로**: 서비스의 작업 폴더는 `C:\Windows\System32`다. `--config`, `--log`, `ContentRootPath`를 상대 경로로 쓰면 설정·DEK 파일·로그 위치가 달라지므로 절대 경로를 쓴다.
- **기동 실패 원인**: 설정 오류(OsKms 복호화 실패, `Http.Port` 누락 등)는 stderr로만 출력되므로 서비스로 실행하면 보이지 않는다. 서비스를 멈추고 같은 계정으로 로그인한 콘솔에서 같은 인수로 실행해 확인한다. 기동 뒤의 오류(포트 사용 중 등)는 `--log` 폴더의 `Biz.Bizadm.PowerManager.DeviceConfig.Mcp*.log`에 남는다.

## Claude Code 등록

```powershell
claude mcp add --transport http device-config http://127.0.0.1:65532/mcp
```

포트는 설정의 `Http.Port`와 맞춘다. 서버(서비스 또는 콘솔)가 먼저 실행 중이어야 한다.
