import "@testing-library/jest-dom/vitest";

/** jsdom에는 EventSource가 없다. 테스트는 `FakeEventSource.latest()`로 연 스트림에 이벤트를 넣는다. */
export class FakeEventSource extends EventTarget {
  static readonly CONNECTING = 0;
  static readonly OPEN = 1;
  static readonly CLOSED = 2;
  static instances: FakeEventSource[] = [];

  static latest(): FakeEventSource {
    const last = FakeEventSource.instances.at(-1);
    if (last === undefined) throw new Error("열린 EventSource가 없다.");
    return last;
  }

  readonly url: string;
  readyState: number = FakeEventSource.CONNECTING;

  constructor(url: string) {
    super();
    this.url = url;
    FakeEventSource.instances.push(this);
  }

  open() {
    this.readyState = FakeEventSource.OPEN;
    this.dispatchEvent(new Event("open"));
  }

  send(event: string, data: unknown) {
    this.dispatchEvent(new MessageEvent(event, { data: JSON.stringify(data) }));
  }

  /** 끊김. 브라우저가 다시 연결하면 `CONNECTING`, 다시 연결하지 않으면 `CLOSED`다. */
  fail(readyState: number) {
    this.readyState = readyState;
    this.dispatchEvent(new Event("error"));
  }

  close() {
    this.readyState = FakeEventSource.CLOSED;
  }
}

// 테스트가 vi.unstubAllGlobals()를 불러도 남도록 stubGlobal을 쓰지 않는다.
globalThis.EventSource = FakeEventSource as unknown as typeof EventSource;

beforeEach(() => {
  FakeEventSource.instances = [];
});
