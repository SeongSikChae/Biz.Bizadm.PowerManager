import type { Device } from "./devicesApi";
import {
  DEFAULT_SORT,
  PAGE_SIZE,
  clampPage,
  displayValue,
  filterDevices,
  formatDateTime,
  formatRelative,
  nextSort,
  pageCount,
  pageSlice,
  sortDevices,
} from "./deviceList";

function device(mac: string, lastRequestedAt: string, overrides: Partial<Device> = {}): Device {
  return {
    mac,
    userId: "user-01",
    modelId: "M130-W",
    company: "DAWONDNS",
    registeredAt: "2026-11-12T00:14:00+00:00",
    lastRequestedAt,
    ...overrides,
  };
}

describe("displayValue", () => {
  it("빈 값은 —, 나머지는 그대로", () => {
    expect(displayValue("")).toBe("—");
    expect(displayValue("3a0000000101")).toBe("3a0000000101");
  });
});

describe("formatDateTime", () => {
  it("브라우저 시간대의 YYYY-MM-DD HH:mm", () => {
    const local = new Date(2026, 0, 5, 7, 3, 59);
    expect(formatDateTime(local.toISOString())).toBe("2026-01-05 07:03");
  });
});

describe("formatRelative", () => {
  const now = Date.parse("2026-11-19T06:00:00Z");
  const before = (ms: number) => new Date(now - ms).toISOString();
  const second = 1000;
  const minute = 60 * second;
  const hour = 60 * minute;
  const day = 24 * hour;

  it.each([
    [0, "방금"],
    [59 * second, "방금"],
    [minute, "1분 전"],
    [59 * minute + 59 * second, "59분 전"],
    [hour, "1시간 전"],
    [23 * hour + 59 * minute, "23시간 전"],
    [day, "1일 전"],
    [45 * day + 3 * hour, "45일 전"],
  ])("%d ms 전은 %s", (ms, expected) => {
    expect(formatRelative(before(ms), now)).toBe(expected);
  });

  it("미래 시각은 방금", () => {
    expect(formatRelative(new Date(now + 5 * minute).toISOString(), now)).toBe("방금");
  });
});

describe("정렬", () => {
  const devices = [
    device("bbbbbbbbbbbb", "2026-11-19T05:00:00+00:00", { registeredAt: "2026-11-10T00:00:00+00:00" }),
    device("cccccccccccc", "2026-11-19T06:00:00+00:00", { registeredAt: "2026-11-12T00:00:00+00:00" }),
    device("aaaaaaaaaaaa", "2026-11-19T05:00:00+00:00", { registeredAt: "2026-11-11T00:00:00+00:00" }),
  ];

  it("기본은 마지막 요청 최근 순, 같으면 MAC 오름차순", () => {
    expect(DEFAULT_SORT).toEqual({ key: "lastRequestedAt", dir: "desc" });
    expect(sortDevices(devices, DEFAULT_SORT).map((d) => d.mac)).toEqual(["cccccccccccc", "aaaaaaaaaaaa", "bbbbbbbbbbbb"]);
  });

  it("오름차순에서도 같은 값은 MAC 오름차순", () => {
    expect(sortDevices(devices, { key: "lastRequestedAt", dir: "asc" }).map((d) => d.mac)).toEqual([
      "aaaaaaaaaaaa",
      "bbbbbbbbbbbb",
      "cccccccccccc",
    ]);
  });

  it("등록 열로 정렬", () => {
    expect(sortDevices(devices, { key: "registeredAt", dir: "desc" }).map((d) => d.mac)).toEqual([
      "cccccccccccc",
      "aaaaaaaaaaaa",
      "bbbbbbbbbbbb",
    ]);
  });

  it("다른 열을 처음 누르면 내림차순, 같은 열을 다시 누르면 방향이 바뀐다", () => {
    const registered = nextSort(DEFAULT_SORT, "registeredAt");
    expect(registered).toEqual({ key: "registeredAt", dir: "desc" });
    expect(nextSort(registered, "registeredAt")).toEqual({ key: "registeredAt", dir: "asc" });
    expect(nextSort({ key: "registeredAt", dir: "asc" }, "registeredAt")).toEqual({ key: "registeredAt", dir: "desc" });
    expect(nextSort({ key: "registeredAt", dir: "asc" }, "lastRequestedAt")).toEqual({ key: "lastRequestedAt", dir: "desc" });
  });

  it("원래 배열을 바꾸지 않는다", () => {
    const copy = [...devices];
    sortDevices(devices, DEFAULT_SORT);
    expect(devices).toEqual(copy);
  });
});

describe("filterDevices", () => {
  const devices = [
    device("3a0000000101", "2026-11-19T05:00:00+00:00", { modelId: "M130-W", userId: "Alice" }),
    device("5e0000000201", "2026-11-19T05:00:00+00:00", { modelId: "B540", userId: "bob" }),
  ];
  const macs = (q: string) => filterDevices(devices, q).map((d) => d.mac);

  it("빈 검색어나 공백은 전체", () => {
    expect(macs("")).toHaveLength(2);
    expect(macs("   ")).toHaveLength(2);
  });

  it("MAC 부분 일치, 대소문자 무시, 콜론·하이픈 무시", () => {
    expect(macs("A00")).toEqual(["3a0000000101"]);
    expect(macs("3A:00:00")).toEqual(["3a0000000101"]);
    expect(macs("5e-00-00")).toEqual(["5e0000000201"]);
  });

  it("모델·사용자 부분 일치, 대소문자 무시", () => {
    expect(macs("b540")).toEqual(["5e0000000201"]);
    expect(macs("m130")).toEqual(["3a0000000101"]);
    expect(macs("ALI")).toEqual(["3a0000000101"]);
  });

  it("구분자만 있는 검색어는 MAC으로 맞추지 않는다", () => {
    expect(macs(":")).toEqual([]);
  });

  it("맞는 것이 없으면 빈 배열", () => {
    expect(macs("zzz")).toEqual([]);
  });
});

describe("페이지", () => {
  const items = Array.from({ length: 101 }, (_, i) => i);

  it("50대 단위, 최소 1페이지", () => {
    expect(PAGE_SIZE).toBe(50);
    expect(pageCount(0)).toBe(1);
    expect(pageCount(50)).toBe(1);
    expect(pageCount(51)).toBe(2);
    expect(pageCount(101)).toBe(3);
  });

  it("페이지 자르기", () => {
    expect(pageSlice(items, 1)).toEqual(items.slice(0, 50));
    expect(pageSlice(items, 3)).toEqual([100]);
  });

  it("전체 페이지 수를 넘으면 마지막 페이지, 1보다 작으면 1", () => {
    expect(clampPage(3, 60)).toBe(2);
    expect(clampPage(3, 0)).toBe(1);
    expect(clampPage(0, 60)).toBe(1);
    expect(pageSlice(items.slice(0, 60), 3)).toEqual(items.slice(50, 60));
  });
});
