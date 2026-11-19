# B540 REA 추가 분석 — 도구 적용성 및 바이트 엔트로피

- 작성: 2026-11-03 (KST)
- 대상: B540 플래시 덤프(1,048,576바이트)
- 방식: REA MCP 도구를 로컬 Windows 호스트에서 직접 호출. 정적 분석이며 장치 실행·통신 관찰은 하지 않았다.
- 표기: **관찰**(도구 출력에 직접 존재), **추정**(관찰을 구조·동작과 연결한 해석), **미확인**.
- 관련 문서: [분석 색인](b540-rea-analysis.md) · [앱 초기화](b540-rea-app-init.md) · [MQTT 토픽](b540-mqtt-topic.md) · [REST API](b540-rest-api.md)

## 1. REA 도구 적용성 (관찰)

이 Windows 호스트에서 REA로 할 수 있는 것과 없는 것을 먼저 확정했다.

| 시도 | 결과 | 근거 |
|---|---|---|
| `open_binary` (덤프 원본) | **거부** | `target_unavailable`: "ESP8266 firmware images are not supported: they share the 0xE9 magic but have an 8-byte header without the ESP32 extended header" |
| `open_binary` (IROM을 `raw-binary`로, `architecture=xtensa`) | **거부** | 스키마가 허용하는 architecture는 `6502 / x86 / x86_64 / arm / arm64 / m68k`뿐. **Xtensa 미지원** |
| `inspect_firmware_regions` (Binwalk) | **불가** | Linux + util-linux prlimit + Binwalk 3.1.x 필요. 현재 호스트는 Windows |
| `inspect_byte_entropy` (오프라인) | **가능** | 외부 도구 불필요, 경로 기반. 아래 2절 결과 |

**결론(관찰):** 현재 REA 빌드의 심층 provider(Ghidra 계열)는 **Xtensa LX106 디컴파일/디스어셈블을 지원하지 않는다.** 따라서 이 덤프의 앱 코드(릴레이/계량 GPIO, MQTT client_id·username·password 구성, 길이 32 검증 함수 알고리즘 등)를 REA-native로 디컴파일할 수 없다. 이는 기존 분석이 Linux에서 별도 `xtensa-elf-objdump`를 쓴 이유와 일치한다. OS를 바꿔도 REA의 raw-binary architecture 목록에 Xtensa가 없어 동일하다.

## 2. 바이트 엔트로피 분석 (관찰)

- REA `inspect_byte_entropy`, block_size 4096, 전체 256블록 검사(`complete-within-view`, exhausted).
- Evidence ID: `ev_c41e7d48a610292d3c42eda66c8f83c28c6a96854f6bec2e30ceb033c55c0d7d` (뷰 `ev_bee1d875c99c2ac6a7eb3d51663bbbe9120dfa3930a1b1744a5b61b236d75eca`)
- **파일 전체 엔트로피: 6.0382** (임계값 high 7.2 / low 1.0)

### 고엔트로피(≥7.2) 구간 — 2곳, 각 8 KiB뿐

| 파일 오프셋 | 길이 | 소속 (추정) |
|---|---|---|
| `0x52000` | `0x2000` | 슬롯 A IROM(`0x1010–0x56080`) 끝부분 |
| `0xD3040` | `0x2000` | 슬롯 B IROM(`0x81010–0xd61a0`) 끝부분 |

### 저엔트로피(≤1.0) 대형 구간 — 모두 패딩/후미

| 파일 오프셋 | 길이 | 소속 (추정) |
|---|---|---|
| `0x61000` | `0x1B000` | 슬롯 A 끝(~`0x60b40`) 이후 FF 패딩 ~ 설정 섹터 전 |
| `0x7E000` | `0x3000` | 설정 섹터(`0x7c000`/`0x7d000`) 이후 ~ 슬롯 B 전 패딩 |
| `0xE1000` | `0x1A000` | 슬롯 B 끝(~`0xe0c90`) 이후 FF 패딩 |
| `0xFC000` | `0x4000` | 부트 제어/후미 영역(대부분 FF, fd/fe 부트 선택 레코드 포함) |

## 3. 해석

- **추정: 전면 저장 암호화는 없다.** 파일 엔트로피 6.04에 고엔트로피 구간이 각 8 KiB 두 곳(슬롯 IROM 끝)뿐이고 대형 고엔트로피 블록이 없다. 펌웨어 이미지는 대체로 평문 코드/데이터이며, 암호화된 펌웨어 블록으로 저장되어 있지 않다. 이는 기존 미해결 항목 "저장 암호화 사용 여부"를 **저장 레벨에서 음성**으로 좁힌다.
- **추정: 두 고엔트로피 8 KiB 구간**은 각 슬롯 IROM 끝의 압축/패킹 코드 또는 암호 상수표(기존에 확인된 SHA-256 K 상수·AES S-Box) 후보다. 엔트로피만으로 내용을 확정하지 않는다.
- **관찰: 설정 섹터 `0x7c000`/`0x7d000`은 고엔트로피로 분류되지 않았다** → 평문 설정 저장과 부합한다(MQTT 호스트·키·토픽 등은 평문). 민감 값은 기존 문서대로 마스킹한다.
- **관찰: 물리 레이아웃이 엔트로피로 재확인됐다** — 슬롯 A / 패딩 / 설정 2섹터 / 패딩 / 슬롯 B / 패딩 / 부트 제어 후미.

### 한계 (도구 명시)

- 엔트로피는 **압축과 암호화를 구분하지 못한다.** "암호화 없음"은 저장 레벨의 대형 암호화 블록 부재를 뜻하며, TLS 전송 암호화나 소규모 암호화 필드 존재를 배제하지 않는다.
- 4096바이트 블록은 소규모 영역의 엔트로피를 과소평가할 수 있다. 8 KiB 미만의 암호 필드는 이 측정에서 드러나지 않을 수 있다.

## 4. 깊은 코드 분석을 위한 현실적 경로 (미착수)

REA-native 디컴파일이 막혔으므로, 미확인 코드 로직(검증 알고리즘·GPIO·MQTT 구성)을 더 파려면 다음 중 하나가 필요하다.

1. **Xtensa 툴체인 경로** — 기존 작업대로 `xtensa-elf-objdump` 또는 Xtensa 플러그인을 넣은 Ghidra로 추출 IROM을 분석. REA 외부 도구다.
2. **IDA provider 경로** — IDA Pro(Xtensa 지원)를 `ida-pro-mcp`로 연결하면 REA `open_binary`에 `provider_id: "ida"`로 바인딩 가능. 현재 미구성.
3. **Linux 호스트에서 `inspect_firmware_regions`** — Binwalk 시그니처 재확인용. 단 이것도 코드 디컴파일은 아니다.

> 추출 산출물(추출한 슬롯 A IROM 구간, 추출한 슬롯 B IROM 구간)은 세션 스크래치패드에 생성했으며 저장소에 커밋하지 않았다. 슬롯 A IROM SHA-256 `640b8bd33effd2a42b97744ac19831c79c3f131093719cb825ddea18573e1cd4`, 슬롯 B IROM SHA-256 `475d2e9b1639eca93732c4567558f07cd83b84db0e15e3deba2a666e9600ae65`.
