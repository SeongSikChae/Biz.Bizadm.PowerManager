/** 장비명의 최대 길이(UTF-16 코드 단위). 서버와 같다. */
export const MAX_NAME_LENGTH = 30;

const LINE_SEPARATOR = 0x2028;
const PARAGRAPH_SEPARATOR = 0x2029;
/** .NET `char.IsWhiteSpace`가 참인 문자 가운데 공백 구분 문자(Zs)가 아닌 것. */
const OTHER_WHITE_SPACE = [0x09, 0x0a, 0x0b, 0x0c, 0x0d, 0x85, LINE_SEPARATOR, PARAGRAPH_SEPARATOR];

const isWhiteSpace = (char: string) => /\p{Zs}/u.test(char) || OTHER_WHITE_SPACE.includes(char.charCodeAt(0));
const isBreakOrControl = (char: string) => /\p{Cc}/u.test(char) || [LINE_SEPARATOR, PARAGRAPH_SEPARATOR].includes(char.charCodeAt(0));

/** 서버가 없애는 앞뒤 공백을 똑같이 없앤다. JS `trim`은 없애는 문자가 달라(U+FEFF, U+0085) 쓰지 않는다. */
export function normalizeName(text: string): string {
  let start = 0;
  let end = text.length;
  while (start < end && isWhiteSpace(text[start])) start++;
  while (end > start && isWhiteSpace(text[end - 1])) end--;
  return text.slice(start, end);
}

/** 앞뒤 공백을 없앤 장비명의 오류. 없으면 null. 빈 값은 장비명을 지운다는 뜻이라 오류가 아니다. */
export function validateName(name: string): string | null {
  if (name.length > MAX_NAME_LENGTH) return `${MAX_NAME_LENGTH}자 이하로 입력하세요.`;
  if ([...name].some(isBreakOrControl)) return "줄바꿈이나 제어 문자는 쓸 수 없습니다.";
  return null;
}
