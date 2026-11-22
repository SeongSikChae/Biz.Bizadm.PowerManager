import {
  validatePowerCycle,
  validateReportCondition,
  validateSchedule,
  validateStandbyCutoff,
  type ScheduleInput,
} from "./validation";

const fields = (errors: object | null) => Object.keys(errors ?? {});

describe("validatePowerCycle", () => {
  it.each(["1", "10", "999", " 5 "])("%j 는 받는다", (text) => {
    expect(validatePowerCycle(text).value).toEqual({ seconds: Number(text) });
  });

  it.each(["", "0", "1000", "1.5", "-1", "abc", "1e2"])("%j 는 이유를 알린다", (text) => {
    const result = validatePowerCycle(text);
    expect(result.value).toBeNull();
    expect(fields(result.errors)).toEqual(["seconds"]);
  });
});

describe("validateReportCondition", () => {
  it.each([
    ["1", "11", "1"],
    ["3600", "3600", "100"],
    ["3600", "14400", "100"],
  ])("%s/%s/%s 는 받는다", (minInterval, maxInterval, changeThreshold) => {
    expect(validateReportCondition({ minInterval, maxInterval, changeThreshold }).value).toEqual({
      minInterval: Number(minInterval),
      maxInterval: Number(maxInterval),
      changeThreshold: Number(changeThreshold),
    });
  });

  it.each([
    ["0", "600", "5", ["minInterval"]],
    ["3601", "14400", "5", ["minInterval"]],
    ["1", "10", "5", ["maxInterval"]],
    ["1", "14401", "5", ["maxInterval"]],
    ["1", "600", "0", ["changeThreshold"]],
    ["1", "600", "101", ["changeThreshold"]],
    ["601", "600", "5", ["minInterval"]],
    ["601", "600", "0", ["minInterval", "changeThreshold"]],
    ["", "", "", ["minInterval", "maxInterval", "changeThreshold"]],
  ])("%s/%s/%s 는 이유를 알린다", (minInterval, maxInterval, changeThreshold, expected) => {
    const result = validateReportCondition({ minInterval, maxInterval, changeThreshold });
    expect(result.value).toBeNull();
    expect(fields(result.errors)).toEqual(expected);
  });
});

describe("validateStandbyCutoff", () => {
  it.each([
    ["0", "0"],
    ["5", "60"],
    ["0.01", "1"],
    ["9999.99", "9999"],
  ])("%s/%s 는 받는다", (thresholdWatts, delay) => {
    expect(validateStandbyCutoff({ enabled: true, thresholdWatts, delay }).value).toEqual({
      enabled: true,
      thresholdWatts: Number(thresholdWatts),
      delay: Number(delay),
    });
  });

  it.each([
    ["10000", "1", ["thresholdWatts"]],
    ["5.001", "1", ["thresholdWatts"]],
    ["-1", "1", ["thresholdWatts"]],
    ["", "1", ["thresholdWatts"]],
    ["5", "10000", ["delay"]],
    ["5", "-1", ["delay"]],
    ["5", "1.5", ["delay"]],
  ])("%s/%s 는 이유를 알린다", (thresholdWatts, delay, expected) => {
    const result = validateStandbyCutoff({ enabled: false, thresholdWatts, delay });
    expect(result.value).toBeNull();
    expect(fields(result.errors)).toEqual(expected);
  });
});

describe("validateSchedule", () => {
  const input = (overrides: Partial<ScheduleInput> = {}): ScheduleInput => ({
    enabled: true,
    days: [false, true, false, false, false, false, false],
    hour: "3",
    minute: "0",
    second: "0",
    seconds: "10",
    ...overrides,
  });

  it("시각을 HH:MM:SS로 만든다", () => {
    expect(validateSchedule(input({ hour: "3", minute: "05", second: "9" })).value).toEqual({
      enabled: true,
      days: [false, true, false, false, false, false, false],
      time: "03:05:09",
      seconds: 10,
    });
  });

  it.each([
    [{ second: "0", seconds: "59" }],
    [{ second: "58", seconds: "1" }],
    [{ hour: "23", minute: "59", second: "50", seconds: "9" }],
  ])("%j 는 받는다", (overrides) => {
    expect(validateSchedule(input(overrides)).errors).toBeNull();
  });

  it.each([
    [{ days: [false, false, false, false, false, false, false] }, ["days"]],
    [{ hour: "24" }, ["hour"]],
    [{ minute: "60" }, ["minute"]],
    [{ second: "60" }, ["second"]],
    [{ seconds: "0" }, ["seconds"]],
    [{ seconds: "60" }, ["seconds"]],
    [{ second: "50", seconds: "10" }, ["seconds"]],
    [{ hour: "", minute: "", second: "", seconds: "" }, ["hour", "minute", "second", "seconds"]],
  ])("%j 는 이유를 알린다", (overrides, expected) => {
    const result = validateSchedule(input(overrides));
    expect(result.value).toBeNull();
    expect(fields(result.errors)).toEqual(expected);
  });
});
