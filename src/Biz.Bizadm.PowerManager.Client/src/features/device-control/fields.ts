/** B540 장비인지. 재부팅과 실장비에서 확인한 단위의 기준이다. */
export function isB540(modelId: string): boolean {
  return modelId.startsWith("B540");
}

export type FieldKey =
  | "powerCycle"
  | "minInterval"
  | "maxInterval"
  | "changeThreshold"
  | "thresholdWatts"
  | "delay"
  | "hour"
  | "minute"
  | "second"
  | "seconds";

/** 입력의 이름(단위 포함)과 입력 아래에 늘 보이는 설명(뜻과 범위). */
export interface FieldText {
  label: string;
  hint: string;
}

const COMMON: Record<FieldKey, FieldText> = {
  powerCycle: { label: "전원 재투입 (초)", hint: "전원을 끄고 이 시간 뒤에 다시 켭니다. 1~999의 정수" },
  minInterval: {
    label: "최소 간격 (초)",
    hint: "계량 값이 바뀌어도 이 시간 안에는 다시 보고하지 않습니다. 1~3600의 정수, 최대 간격 이하",
  },
  maxInterval: { label: "최대 간격 (초)", hint: "계량 값이 바뀌지 않아도 이 시간이 지나면 보고합니다. 11~14400의 정수" },
  changeThreshold: { label: "변화 기준 (%, 추정)", hint: "계량 값이 이만큼 바뀌면 보고하는 것으로 추정합니다. 1~100의 정수" },
  thresholdWatts: {
    label: "기준 전력 (W, 추정)",
    hint: "전력이 이 값보다 낮으면 차단하는 것으로 추정합니다. 0~9999.99, 소수 둘째 자리까지",
  },
  delay: {
    label: "지연 (초, 추정)",
    hint: "기준 전력보다 낮은 상태가 이만큼 이어지면 차단하는 것으로 추정합니다. 0~9999의 정수",
  },
  hour: { label: "시", hint: "0~23의 정수" },
  minute: { label: "분", hint: "0~59의 정수" },
  second: { label: "초", hint: "0~59의 정수" },
  seconds: { label: "다시 켤 때까지의 초 (추정)", hint: "1~59의 정수, 시각의 초와 더해 60보다 작게" },
};

/** B540에서 확인한 것을 다른 모델에서는 추정으로 보인다. */
const OTHER_MODELS: Partial<Record<FieldKey, Partial<FieldText>>> = {
  powerCycle: { label: "전원 재투입 (초, 추정)" },
  minInterval: {
    label: "최소 간격 (초, 추정)",
    hint: "계량 값이 바뀌어도 이 시간 안에는 다시 보고하지 않는 것으로 추정합니다. 1~3600의 정수, 최대 간격 이하",
  },
  maxInterval: {
    label: "최대 간격 (초, 추정)",
    hint: "계량 값이 바뀌지 않아도 이 시간이 지나면 보고하는 것으로 추정합니다. 11~14400의 정수",
  },
};

export function fieldText(key: FieldKey, modelId: string): FieldText {
  return isB540(modelId) ? COMMON[key] : { ...COMMON[key], ...OTHER_MODELS[key] };
}
