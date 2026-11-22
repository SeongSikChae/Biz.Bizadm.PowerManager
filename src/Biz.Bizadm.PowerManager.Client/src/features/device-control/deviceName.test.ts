import { MAX_NAME_LENGTH, normalizeName, validateName } from "./deviceName";

const char = String.fromCodePoint;
const LONGEST = "가".repeat(MAX_NAME_LENGTH);

describe("normalizeName", () => {
  it.each([0x09, 0x0a, 0x0d, 0x20, 0x85, 0xa0, 0x1680, 0x2003, 0x2028, 0x2029, 0x202f, 0x3000])("앞뒤의 U+%i 를 없앤다", (code) => {
    expect(normalizeName(`${char(code)} 거실 TV ${char(code)}`)).toBe("거실 TV");
    expect(normalizeName(char(code))).toBe("");
  });

  it("서버가 남기는 U+FEFF와 가운데 공백은 남긴다", () => {
    expect(normalizeName(`${char(0xfeff)}거실`)).toBe(`${char(0xfeff)}거실`);
    expect(normalizeName(`거${char(0x0a)}실`)).toBe(`거${char(0x0a)}실`);
    expect(normalizeName("")).toBe("");
  });
});

describe("validateName", () => {
  it("30자까지와 빈 값을 받는다", () => {
    expect(validateName("")).toBeNull();
    expect(validateName(LONGEST)).toBeNull();
    expect(validateName(char(0x1f600).repeat(15))).toBeNull();
  });

  it("30자를 넘으면(이모지는 2로 셈) 이유를 알린다", () => {
    expect(validateName(`${LONGEST}가`)).toBe("30자 이하로 입력하세요.");
    expect(validateName(`${char(0x1f600).repeat(15)}가`)).toBe("30자 이하로 입력하세요.");
  });

  it.each([0x00, 0x09, 0x0a, 0x7f, 0x85, 0x2028, 0x2029])("가운데의 U+%i 는 받지 않는다", (code) => {
    expect(validateName(`거${char(code)}실`)).toBe("줄바꿈이나 제어 문자는 쓸 수 없습니다.");
  });
});
