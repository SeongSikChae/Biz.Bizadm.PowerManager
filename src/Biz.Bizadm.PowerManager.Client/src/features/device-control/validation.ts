import type { ReportCondition, Schedule, StandbyCutoff } from "@/features/device-status/types";

/** 검사 결과. 오류가 하나라도 있으면 값이 없고 보내지 않는다. 오류의 키는 입력 이름이다. */
export type Validated<T, K extends string> = { value: T; errors: null } | { value: null; errors: Partial<Record<K, string>> };

const REQUIRED = "값을 입력하세요.";

function integer(text: string, min: number, max: number): { value: number; error: null } | { value: null; error: string } {
  const trimmed = text.trim();
  if (trimmed === "") return { value: null, error: REQUIRED };
  const value = Number(trimmed);
  if (!/^\d{1,9}$/.test(trimmed) || value < min || value > max) return { value: null, error: `${min}~${max}의 정수를 입력하세요.` };
  return { value, error: null };
}

/** 오류만 남긴다. */
function collect<K extends string>(errors: Record<K, string | null>): Partial<Record<K, string>> {
  return Object.fromEntries(Object.entries(errors).filter(([, message]) => message !== null)) as Partial<Record<K, string>>;
}

export function validatePowerCycle(seconds: string): Validated<{ seconds: number }, "seconds"> {
  const result = integer(seconds, 1, 999);
  return result.error === null ? { value: { seconds: result.value }, errors: null } : { value: null, errors: { seconds: result.error } };
}

export type ReportConditionInput = Record<keyof ReportCondition, string>;

export function validateReportCondition(input: ReportConditionInput): Validated<ReportCondition, keyof ReportCondition> {
  const min = integer(input.minInterval, 1, 3600);
  const max = integer(input.maxInterval, 11, 14400);
  const change = integer(input.changeThreshold, 1, 100);
  if (min.error === null && max.error === null && min.value > max.value) {
    const errors = collect({ minInterval: "최소 간격은 최대 간격보다 클 수 없습니다.", changeThreshold: change.error });
    return { value: null, errors };
  }
  if (min.error !== null || max.error !== null || change.error !== null) {
    return { value: null, errors: collect({ minInterval: min.error, maxInterval: max.error, changeThreshold: change.error }) };
  }
  return { value: { minInterval: min.value, maxInterval: max.value, changeThreshold: change.value }, errors: null };
}

export interface StandbyCutoffInput {
  enabled: boolean;
  thresholdWatts: string;
  delay: string;
}

function watts(text: string): { value: number; error: null } | { value: null; error: string } {
  const trimmed = text.trim();
  if (trimmed === "") return { value: null, error: REQUIRED };
  if (!/^\d{1,4}(\.\d{1,2})?$/.test(trimmed)) return { value: null, error: "0~9999.99를 소수 둘째 자리까지 입력하세요." };
  return { value: Number(trimmed), error: null };
}

export function validateStandbyCutoff(input: StandbyCutoffInput): Validated<StandbyCutoff, "thresholdWatts" | "delay"> {
  const threshold = watts(input.thresholdWatts);
  const delay = integer(input.delay, 0, 9999);
  if (threshold.error !== null || delay.error !== null) {
    return { value: null, errors: collect({ thresholdWatts: threshold.error, delay: delay.error }) };
  }
  return { value: { enabled: input.enabled, thresholdWatts: threshold.value, delay: delay.value }, errors: null };
}

export interface ScheduleInput {
  enabled: boolean;
  days: boolean[];
  hour: string;
  minute: string;
  second: string;
  seconds: string;
}

export type ScheduleField = "days" | "hour" | "minute" | "second" | "seconds";

const pad = (value: number) => value.toString().padStart(2, "0");

/** 시각의 초와 다시 켤 때까지의 초의 합이 60 이상이면 장치가 다시 켜지 않으므로 받지 않는다. */
export function validateSchedule(input: ScheduleInput): Validated<Schedule, ScheduleField> {
  const hour = integer(input.hour, 0, 23);
  const minute = integer(input.minute, 0, 59);
  const second = integer(input.second, 0, 59);
  let seconds = integer(input.seconds, 1, 59);
  if (second.error === null && seconds.error === null && second.value + seconds.value >= 60) {
    seconds = { value: null, error: "시각의 초와 더해 60보다 작아야 합니다." };
  }
  const days = input.days.includes(true) ? null : "요일을 하나 이상 고르세요.";
  if (days !== null || hour.error !== null || minute.error !== null || second.error !== null || seconds.error !== null) {
    return {
      value: null,
      errors: collect({ days, hour: hour.error, minute: minute.error, second: second.error, seconds: seconds.error }),
    };
  }
  return {
    value: {
      enabled: input.enabled,
      days: input.days,
      time: `${pad(hour.value)}:${pad(minute.value)}:${pad(second.value)}`,
      seconds: seconds.value,
    },
    errors: null,
  };
}
