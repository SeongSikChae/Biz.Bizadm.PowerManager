import type { SerializedError } from "@reduxjs/toolkit";
import type { FetchBaseQueryError } from "@reduxjs/toolkit/query";
import { displayValue, formatRelative } from "@/features/devices/deviceList";
import type { DeviceStatus } from "./types";

const NO_VALUE = "—";

function fixed(value: number | null, digits: number, unit: string): string {
  return value === null ? NO_VALUE : `${value.toFixed(digits)}${unit}`;
}

export const formatPower = (value: number | null) => fixed(value, 2, " W");
export const formatVoltage = (value: number | null) => fixed(value, 2, " V");
export const formatCurrent = (value: number | null) => fixed(value, 3, " A");
export const formatPowerFactor = (value: number | null) => fixed(value, 3, "");

/** 마지막 보고의 상대 시간. 보고가 없으면 "보고 없음". */
export function formatLastReport(lastReportAt: string | null, now: number): string {
  return lastReportAt === null ? "보고 없음" : formatRelative(lastReportAt, now);
}

/** "온라인 N대 · 오프라인 N대 · 합계 X W"(합계는 온라인 장비의 전력 합). */
export function formatSummary(devices: readonly DeviceStatus[]): string {
  const online = devices.filter((d) => d.online);
  const total = online.reduce((sum, d) => sum + (d.power ?? 0), 0);
  return `온라인 ${online.length}대 · 오프라인 ${devices.length - online.length}대 · 합계 ${total.toFixed(1)} W`;
}

export function sortByMac(devices: Record<string, DeviceStatus>): DeviceStatus[] {
  return Object.values(devices).sort((a, b) => (a.mac < b.mac ? -1 : a.mac > b.mac ? 1 : 0));
}

/** 확인 창 등에서 장비를 밝히는 "모델 (mac)". */
export function deviceLabel(device: Pick<DeviceStatus, "modelId" | "mac">): string {
  return `${displayValue(device.modelId)} (${device.mac})`;
}

/** 제어할 수 없는 까닭. 제어 가능하면 null. */
export function uncontrollableReason(device: DeviceStatus): string | null {
  if (device.controllable) return null;
  return device.online ? "장비가 아직 메시지를 보내지 않아 제어할 수 없습니다" : "장비가 오프라인입니다";
}

/** API 실패의 원인. */
export function describeApiError(error: FetchBaseQueryError | SerializedError): string {
  if (!("status" in error)) return error.message ?? "알 수 없는 오류";
  switch (error.status) {
    case 400:
      return "서버가 입력 값을 받지 않았습니다. (400)";
    case 404:
      return "등록되지 않은 장비입니다. (404)";
    case 409:
      return "장비가 오프라인이거나 메시지를 받을 수 없는 상태입니다. (409)";
    case "FETCH_ERROR":
      return "서버에 연결할 수 없습니다.";
    default:
      return typeof error.status === "number" ? `서버 응답: ${error.status}` : "서버 응답을 해석하지 못했습니다.";
  }
}

/** 제어 요청의 결과. 성공은 장비 큐에 넣었다는 뜻이다. */
export type SendOutcome = { ok: true } | { ok: false; message: string };

export function toOutcome(result: { error?: FetchBaseQueryError | SerializedError }): SendOutcome {
  return result.error === undefined ? { ok: true } : { ok: false, message: describeApiError(result.error) };
}

/** 같은 장비의 릴레이 요청 진행 여부를 다른 부품과 나누는 열쇠. */
export function relayCacheKey(mac: string): string {
  return `relay-${mac}`;
}
