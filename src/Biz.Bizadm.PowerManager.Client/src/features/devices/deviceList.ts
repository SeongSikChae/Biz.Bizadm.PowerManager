import type { Device } from "./devicesApi";

/** 한 페이지에 보이는 장비 수. */
export const PAGE_SIZE = 50;

export type SortKey = "lastRequestedAt" | "registeredAt";
export type SortDir = "asc" | "desc";
export interface Sort {
  key: SortKey;
  dir: SortDir;
}

export const DEFAULT_SORT: Sort = { key: "lastRequestedAt", dir: "desc" };

/** 빈 값은 "—"로 표시한다. */
export function displayValue(value: string): string {
  return value === "" ? "—" : value;
}

function pad(n: number): string {
  return n.toString().padStart(2, "0");
}

/** 브라우저 시간대의 `YYYY-MM-DD HH:mm`. */
export function formatDateTime(iso: string): string {
  const d = new Date(iso);
  return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())} ${pad(d.getHours())}:${pad(d.getMinutes())}`;
}

const MINUTE = 60_000;
const HOUR = 60 * MINUTE;
const DAY = 24 * HOUR;

/**
 * 현재 시각(`now`, ms)과의 차이로 정한 상대 시간. 1분 미만이거나 미래면 "방금",
 * 60분 미만 "N분 전", 24시간 미만 "N시간 전", 그 이상 "N일 전"(N은 내림).
 */
export function formatRelative(iso: string, now: number): string {
  const diff = now - new Date(iso).getTime();
  if (diff < MINUTE) return "방금";
  if (diff < HOUR) return `${Math.floor(diff / MINUTE)}분 전`;
  if (diff < DAY) return `${Math.floor(diff / HOUR)}시간 전`;
  return `${Math.floor(diff / DAY)}일 전`;
}

/** 열 머리를 눌렀을 때의 다음 정렬. 같은 열이면 방향을 바꾸고, 다른 열이면 최근 순(내림차순)부터. */
export function nextSort(current: Sort, key: SortKey): Sort {
  if (current.key === key) return { key, dir: current.dir === "desc" ? "asc" : "desc" };
  return { key, dir: "desc" };
}

/** 정렬한 새 배열. 값이 같으면 방향과 관계없이 MAC 오름차순. */
export function sortDevices(devices: readonly Device[], sort: Sort): Device[] {
  const sign = sort.dir === "desc" ? -1 : 1;
  return [...devices].sort((a, b) => {
    const diff = new Date(a[sort.key]).getTime() - new Date(b[sort.key]).getTime();
    if (diff !== 0) return sign * diff;
    return a.mac < b.mac ? -1 : a.mac > b.mac ? 1 : 0;
  });
}

/**
 * MAC·모델·사용자 부분 일치(대소문자 무시). MAC은 검색어와 값 모두에서 `:`·`-`를 지우고 비교한다.
 * 공백뿐인 검색어는 전체를 돌려준다.
 */
export function filterDevices(devices: readonly Device[], query: string): Device[] {
  const q = query.trim().toLowerCase();
  if (q === "") return [...devices];
  const macQuery = q.replace(/[:-]/g, "");
  return devices.filter(
    (d) =>
      (macQuery !== "" && d.mac.toLowerCase().replace(/[:-]/g, "").includes(macQuery)) ||
      d.modelId.toLowerCase().includes(q) ||
      d.userId.toLowerCase().includes(q),
  );
}

/** 전체 페이지 수(최소 1). */
export function pageCount(total: number): number {
  return Math.max(1, Math.ceil(total / PAGE_SIZE));
}

/** 페이지(1부터)를 1..전체 페이지 수 안으로 맞춘다. 넘으면 마지막 페이지. */
export function clampPage(page: number, total: number): number {
  return Math.min(Math.max(1, page), pageCount(total));
}

/** 그 페이지(1부터)의 장비. */
export function pageSlice<T>(items: readonly T[], page: number): T[] {
  const start = (clampPage(page, items.length) - 1) * PAGE_SIZE;
  return items.slice(start, start + PAGE_SIZE);
}
