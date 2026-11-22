import { fieldText, isB540, type FieldKey } from "./fields";

// spec(device-name-and-units) C5·C6 표: [입력, B540 이름, 다른 모델 이름, B540 설명, 다른 모델 설명(같으면 생략)]
const TABLE: [FieldKey, string, string, string, string?][] = [
  ["powerCycle", "전원 재투입 (초)", "전원 재투입 (초, 추정)", "전원을 끄고 이 시간 뒤에 다시 켭니다. 1~999의 정수"],
  [
    "minInterval",
    "최소 간격 (초)",
    "최소 간격 (초, 추정)",
    "계량 값이 바뀌어도 이 시간 안에는 다시 보고하지 않습니다. 1~3600의 정수, 최대 간격 이하",
    "계량 값이 바뀌어도 이 시간 안에는 다시 보고하지 않는 것으로 추정합니다. 1~3600의 정수, 최대 간격 이하",
  ],
  [
    "maxInterval",
    "최대 간격 (초)",
    "최대 간격 (초, 추정)",
    "계량 값이 바뀌지 않아도 이 시간이 지나면 보고합니다. 11~14400의 정수",
    "계량 값이 바뀌지 않아도 이 시간이 지나면 보고하는 것으로 추정합니다. 11~14400의 정수",
  ],
  ["changeThreshold", "변화 기준 (%, 추정)", "변화 기준 (%, 추정)", "계량 값이 이만큼 바뀌면 보고하는 것으로 추정합니다. 1~100의 정수"],
  [
    "thresholdWatts",
    "기준 전력 (W, 추정)",
    "기준 전력 (W, 추정)",
    "전력이 이 값보다 낮으면 차단하는 것으로 추정합니다. 0~9999.99, 소수 둘째 자리까지",
  ],
  [
    "delay",
    "지연 (초, 추정)",
    "지연 (초, 추정)",
    "기준 전력보다 낮은 상태가 이만큼 이어지면 차단하는 것으로 추정합니다. 0~9999의 정수",
  ],
  ["hour", "시", "시", "0~23의 정수"],
  ["minute", "분", "분", "0~59의 정수"],
  ["second", "초", "초", "0~59의 정수"],
  ["seconds", "다시 켤 때까지의 초 (추정)", "다시 켤 때까지의 초 (추정)", "1~59의 정수, 시각의 초와 더해 60보다 작게"],
];

describe("fieldText", () => {
  it.each(TABLE)("%s 의 이름과 설명은 모델에 맞춘다", (key, b540Label, otherLabel, b540Hint, otherHint = b540Hint) => {
    expect(fieldText(key, "B540_W")).toEqual({ label: b540Label, hint: b540Hint });
    expect(fieldText(key, "M130-W")).toEqual({ label: otherLabel, hint: otherHint });
    expect(fieldText(key, "")).toEqual({ label: otherLabel, hint: otherHint });
  });
});

describe("isB540", () => {
  it("B540으로 시작하는 모델만 참이다", () => {
    expect(["B540", "B540_W", "B530", "M130-W", "", "b540"].map(isB540)).toEqual([true, true, false, false, false, false]);
  });
});
