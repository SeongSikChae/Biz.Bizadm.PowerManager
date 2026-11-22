import {
  describeApiError,
  displayName,
  formatCurrent,
  formatLastReport,
  formatPower,
  formatPowerFactor,
  formatSummary,
  formatVoltage,
  sortByName,
  uncontrollableReason,
} from "./format";
import type { DeviceStatus } from "./types";

function status(overrides: Partial<DeviceStatus> = {}): DeviceStatus {
  return {
    mac: "3a0000000001",
    name: "",
    modelId: "B540_W",
    online: true,
    relay: "on",
    voltage: 221.4,
    current: 0.5,
    powerFactor: 0.987,
    power: 111.9,
    lastReportAt: null,
    controllable: true,
    ...overrides,
  };
}

describe("계량 값 표시", () => {
  it("자릿수와 단위를 맞춘다", () => {
    expect(formatPower(111.9)).toBe("111.90 W");
    expect(formatVoltage(221.4)).toBe("221.40 V");
    expect(formatCurrent(0.5)).toBe("0.500 A");
    expect(formatPowerFactor(0.98)).toBe("0.980");
    expect(formatPower(0)).toBe("0.00 W");
  });

  it("값이 없으면 —다", () => {
    for (const format of [formatPower, formatVoltage, formatCurrent, formatPowerFactor]) expect(format(null)).toBe("—");
  });
});

describe("formatSummary", () => {
  it("온라인·오프라인 수와 온라인 장비의 전력 합을 소수 첫째 자리로 보인다", () => {
    const devices = [
      status({ power: 10.24 }),
      status({ mac: "3a0000000002", power: 20.01 }),
      status({ mac: "3a0000000003", power: null }),
      status({ mac: "3a0000000004", online: false, power: 500 }),
    ];
    expect(formatSummary(devices)).toBe("온라인 3대 · 오프라인 1대 · 합계 30.3 W");
    expect(formatSummary([])).toBe("온라인 0대 · 오프라인 0대 · 합계 0.0 W");
  });
});

describe("formatLastReport", () => {
  it("보고가 없으면 '보고 없음', 있으면 상대 시간이다", () => {
    const now = Date.parse("2026-11-22T06:00:00Z");
    expect(formatLastReport(null, now)).toBe("보고 없음");
    expect(formatLastReport("2026-11-22T05:59:30Z", now)).toBe("방금");
    expect(formatLastReport("2026-11-22T05:55:00Z", now)).toBe("5분 전");
    expect(formatLastReport("2026-11-22T03:00:00Z", now)).toBe("3시간 전");
  });
});

describe("그 밖의 표시", () => {
  it("장비명 순서로, 장비명이 없으면 뒤에 mac 순서로 늘어놓는다", () => {
    const devices = [
      ["3a0000000002", ""],
      ["0a0000000009", ""],
      ["3a0000000005", "거실 10"],
      ["3a0000000006", "거실 2"],
      ["3a0000000008", "서재"],
      ["3a0000000007", "서재"],
      ["3a0000000009", "나무"],
    ].map(([mac, name]) => status({ mac, name }));
    expect(sortByName(Object.fromEntries(devices.map((d) => [d.mac, d]))).map((d) => d.mac)).toEqual([
      "3a0000000006",
      "3a0000000005",
      "3a0000000009",
      "3a0000000007",
      "3a0000000008",
      "0a0000000009",
      "3a0000000002",
    ]);
  });

  it("장비명이 없으면 모델을 제목으로 쓴다", () => {
    expect(displayName(status({ name: "거실" }))).toBe("거실");
    expect(displayName(status())).toBe("B540_W");
    expect(displayName(status({ modelId: "" }))).toBe("—");
  });

  it("제어할 수 없는 까닭을 알린다", () => {
    expect(uncontrollableReason(status())).toBeNull();
    expect(uncontrollableReason(status({ online: false, controllable: false }))).toBe("장비가 오프라인입니다");
    expect(uncontrollableReason(status({ controllable: false }))).toMatch("아직 메시지를 보내지 않아");
  });

  it("API 실패의 원인을 밝힌다", () => {
    expect(describeApiError({ status: 409, data: null })).toMatch("오프라인");
    expect(describeApiError({ status: 404, data: null })).toMatch("등록되지 않은");
    expect(describeApiError({ status: 400, data: null })).toMatch("입력 값");
    expect(describeApiError({ status: 500, data: null })).toBe("서버 응답: 500");
    expect(describeApiError({ status: "FETCH_ERROR", error: "x" })).toBe("서버에 연결할 수 없습니다.");
    expect(describeApiError({ status: "PARSING_ERROR", originalStatus: 200, data: "", error: "x" })).toMatch("해석하지 못했습니다");
  });
});
