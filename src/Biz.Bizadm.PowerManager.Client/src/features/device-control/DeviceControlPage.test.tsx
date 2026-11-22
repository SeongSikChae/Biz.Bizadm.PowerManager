import { act, fireEvent, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router";
import { makeStore } from "@/app/store";
import type { DeviceSettings, DeviceStatus } from "@/features/device-status/types";
import { useDeviceStatusStream } from "@/features/device-status/useDeviceStatusStream";
import { FakeEventSource } from "@/test/setup";
import { DeviceControlPage } from "./DeviceControlPage";

const NOW = Date.parse("2026-11-22T06:00:00Z");
const MAC = "3a0000000001";
const NO_SETTINGS: DeviceSettings = { reportCondition: null, standbyCutoff: null, schedule: null };
const SETTINGS: DeviceSettings = {
  reportCondition: { minInterval: 60, maxInterval: 600, changeThreshold: 5 },
  standbyCutoff: { enabled: true, thresholdWatts: 5, delay: 60 },
  schedule: { enabled: true, days: [false, true, true, true, true, true, false], time: "03:04:05", seconds: 10 },
};

function status(overrides: Partial<DeviceStatus> = {}): DeviceStatus {
  return {
    mac: MAC,
    modelId: "B540_W",
    online: true,
    relay: "on",
    voltage: 221.4,
    current: 0.5,
    powerFactor: 0.98,
    power: 111.9,
    lastReportAt: new Date(NOW - 5 * 60_000).toISOString(),
    controllable: true,
    ...overrides,
  };
}

function json(body: unknown, code = 200): Response {
  return new Response(JSON.stringify(body), { status: code, headers: { "Content-Type": "application/json" } });
}

interface Sent {
  method: string;
  path: string;
  body: unknown;
}

let settings: () => Response;
/** 제어 요청의 응답. */
let respond: () => Response | Promise<Response>;
let sent: Sent[];

beforeEach(() => {
  settings = () => json(NO_SETTINGS);
  respond = () => new Response(null, { status: 204 });
  sent = [];
  vi.stubGlobal(
    "fetch",
    vi.fn(async (request: Request) => {
      const path = new URL(request.url).pathname.replace("/api/admin/devices", "");
      if (request.method === "GET") return path === "/status" ? new Promise<Response>(() => {}) : settings();
      const text = await request.text();
      sent.push({ method: request.method, path, body: text === "" ? null : JSON.parse(text) });
      return respond();
    }),
  );
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function Screen() {
  useDeviceStatusStream();
  return (
    <Routes>
      <Route path="/" element={<p>대시보드 화면</p>} />
      <Route path="/devices/:mac" element={<DeviceControlPage getNow={() => NOW} />} />
    </Routes>
  );
}

const stream = () => FakeEventSource.latest();

function renderPage(mac = MAC) {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter initialEntries={[`/devices/${mac}`]}>
        <Screen />
      </MemoryRouter>
    </Provider>,
  );
}

async function open(device: DeviceStatus = status()) {
  renderPage();
  act(() => {
    stream().open();
    stream().send("snapshot", [device]);
  });
  await screen.findByRole("group", { name: "보고 조건" });
}

const group = (name: string) => within(screen.getByRole("group", { name }));
const dialog = () => within(screen.getByRole("alertdialog"));
const type = async (box: HTMLElement, text: string) => {
  await userEvent.clear(box);
  if (text !== "") await userEvent.type(box, text);
};

describe("DeviceControlPage: 머리", () => {
  it("모델·mac·온라인·전력·마지막 보고를 보이고 스트림으로 반영한다", async () => {
    await open();
    expect(screen.getByRole("heading", { level: 1, name: "B540_W" })).toBeInTheDocument();
    expect(screen.getByText(MAC)).toHaveClass("font-mono");
    expect(screen.getByText("온라인")).toBeInTheDocument();
    expect(screen.getByText("111.90 W")).toBeInTheDocument();
    expect(screen.getByText("5분 전")).toBeInTheDocument();

    act(() => stream().send("device", status({ power: 3, online: false, controllable: false, lastReportAt: null })));
    expect(screen.getByText("3.00 W")).toBeInTheDocument();
    expect(screen.getByText("오프라인")).toBeInTheDocument();
    expect(screen.getByText("보고 없음")).toBeInTheDocument();
  });

  it("'← 대시보드'는 대시보드로 간다", async () => {
    await open();
    await userEvent.click(screen.getByRole("link", { name: "← 대시보드" }));
    expect(screen.getByText("대시보드 화면")).toBeInTheDocument();
  });

  it("등록되지 않은 mac이면 안내와 대시보드 링크를 보인다", async () => {
    renderPage("3a00000000ff");
    act(() => stream().send("snapshot", [status()]));
    expect(screen.getByText("장비를 찾을 수 없습니다")).toBeInTheDocument();
    await userEvent.click(screen.getByRole("link", { name: "대시보드로 가기" }));
    expect(screen.getByText("대시보드 화면")).toBeInTheDocument();
  });

  it("상태를 받기 전에는 스켈레톤을 보인다", () => {
    renderPage();
    expect(screen.getByRole("status", { name: "장비 상태를 불러오는 중" })).toBeInTheDocument();
    expect(screen.queryByText("장비를 찾을 수 없습니다")).not.toBeInTheDocument();
  });

  it("설정을 불러오지 못하면 원인과 '다시 시도'를 보인다", async () => {
    settings = () => json({}, 500);
    renderPage();
    act(() => stream().send("snapshot", [status()]));
    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("서버 응답: 500");
    settings = () => json(NO_SETTINGS);
    await userEvent.click(within(alert).getByRole("button", { name: "다시 시도" }));
    expect(await screen.findByRole("group", { name: "보고 조건" })).toBeInTheDocument();
  });
});

describe("DeviceControlPage: 전원", () => {
  it("릴레이를 대시보드와 같이 제어한다", async () => {
    await open(status({ relay: "off" }));
    await userEvent.click(group("전원").getByRole("switch", { name: "릴레이" }));
    expect(sent).toEqual([{ method: "POST", path: `/${MAC}/relay`, body: { on: true } }]);
    act(() => stream().send("device", status({ relay: "on" })));
    await userEvent.click(group("전원").getByRole("switch", { name: "릴레이" }));
    await userEvent.click(dialog().getByRole("button", { name: "끄기" }));
    expect(sent[1]).toEqual({ method: "POST", path: `/${MAC}/relay`, body: { on: false } });
  });

  it("전원 재투입은 기본 10이고 단위 안내를 보이며 확인 창을 거쳐 보낸다", async () => {
    await open();
    const power = group("전원");
    expect(power.getByRole("textbox", { name: "전원 재투입" })).toHaveValue("10");
    expect(power.getByText("전원 재투입 단위는 약 1초입니다(B540에서 확인)")).toBeInTheDocument();
    await userEvent.click(power.getByRole("button", { name: "실행" }));
    expect(screen.getByRole("alertdialog")).toHaveTextContent(`B540_W (${MAC})`);
    await userEvent.click(dialog().getByRole("button", { name: "취소" }));
    expect(sent).toEqual([]);

    await userEvent.click(power.getByRole("button", { name: "실행" }));
    await userEvent.click(dialog().getByRole("button", { name: "실행" }));
    expect(sent).toEqual([{ method: "POST", path: `/${MAC}/power-cycle`, body: { seconds: 10 } }]);
    expect(await power.findByText("장비에 보냈습니다")).toBeInTheDocument();
  });

  it.each(["", "0", "1000", "1.5"])("전원 재투입 %j 는 보내지 않고 입력 옆에 이유를 보인다", async (text) => {
    await open();
    const box = group("전원").getByRole("textbox", { name: "전원 재투입" });
    await type(box, text);
    await userEvent.click(group("전원").getByRole("button", { name: "실행" }));
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(box).toBeInvalid();
    expect(box).toHaveAccessibleDescription(/입력하세요/);
    expect(sent).toEqual([]);
  });

  it("성공한 뒤 범위 밖 값으로 실행하면 앞의 성공 안내가 남지 않는다", async () => {
    await open();
    const power = group("전원");
    await userEvent.click(power.getByRole("button", { name: "실행" }));
    await userEvent.click(dialog().getByRole("button", { name: "실행" }));
    await power.findByText("장비에 보냈습니다");

    const box = power.getByRole("textbox", { name: "전원 재투입" });
    await type(box, "1000");
    await userEvent.click(power.getByRole("button", { name: "실행" }));
    expect(box).toBeInvalid();
    expect(power.queryByText("장비에 보냈습니다")).not.toBeInTheDocument();
    expect(sent).toHaveLength(1);
  });

  it("재부팅은 확인 창을 거쳐 보낸다", async () => {
    await open();
    await userEvent.click(group("전원").getByRole("button", { name: "재부팅" }));
    expect(screen.getByRole("alertdialog")).toHaveTextContent(MAC);
    await userEvent.click(dialog().getByRole("button", { name: "재부팅" }));
    expect(sent).toEqual([{ method: "POST", path: `/${MAC}/reboot`, body: null }]);
  });

  it("확인 창이 닫히는 동안 다시 눌러도 더 보내지 않는다", async () => {
    // jsdom은 애니메이션이 없어 바로 사라지므로, 닫힘 애니메이션이 있는 것처럼 만든다.
    const original = window.getComputedStyle.bind(window);
    const computed = vi.spyOn(window, "getComputedStyle").mockImplementation((element, pseudo) => {
      const style = original(element, pseudo);
      if (element.getAttribute("role") === "alertdialog") {
        Object.defineProperty(style, "animationName", { get: () => `${element.getAttribute("data-state")}-animation` });
      }
      return style;
    });
    try {
      await open();
      const power = group("전원");
      await userEvent.click(power.getByRole("button", { name: "재부팅" }));
      const confirm = dialog().getByRole("button", { name: "재부팅" });
      fireEvent.click(confirm);
      expect(confirm).toBeInTheDocument();
      fireEvent.click(confirm);
      await power.findByText("장비에 보냈습니다");
      expect(sent).toEqual([{ method: "POST", path: `/${MAC}/reboot`, body: null }]);
    } finally {
      computed.mockRestore();
    }
  });

  it("B540이 아니면 재부팅을 보이지 않는다", async () => {
    await open(status({ modelId: "M130-W" }));
    expect(group("전원").queryByRole("button", { name: "재부팅" })).not.toBeInTheDocument();
    expect(group("전원").getByRole("button", { name: "실행" })).toBeEnabled();
  });

  it("보내는 동안 전원 묶음의 버튼과 릴레이가 비활성이다", async () => {
    let finish!: (response: Response) => void;
    respond = () => new Promise<Response>((resolve) => (finish = resolve));
    await open();
    const power = group("전원");
    await userEvent.click(power.getByRole("button", { name: "재부팅" }));
    await userEvent.click(dialog().getByRole("button", { name: "재부팅" }));
    expect(power.getByRole("button", { name: "재부팅" })).toBeDisabled();
    expect(power.getByRole("button", { name: "실행" })).toBeDisabled();
    expect(power.getByRole("switch", { name: "릴레이" })).toBeDisabled();
    await waitFor(() => expect(sent).toHaveLength(1));
    await act(async () => finish(new Response(null, { status: 204 })));
    await waitFor(() => expect(power.getByRole("button", { name: "실행" })).toBeEnabled());
  });

  it("릴레이를 보내는 동안 전원 재투입·재부팅이 비활성이다", async () => {
    respond = () => new Promise<Response>(() => {});
    await open(status({ relay: "off" }));
    await userEvent.click(group("전원").getByRole("switch", { name: "릴레이" }));
    await waitFor(() => expect(group("전원").getByRole("button", { name: "실행" })).toBeDisabled());
    expect(group("전원").getByRole("button", { name: "재부팅" })).toBeDisabled();
  });

  it("실패하면 원인을 보인다", async () => {
    respond = () => json({}, 409);
    await open();
    await userEvent.click(group("전원").getByRole("button", { name: "실행" }));
    await userEvent.click(dialog().getByRole("button", { name: "실행" }));
    expect(await group("전원").findByRole("alert")).toHaveTextContent("(409)");
    expect(group("전원").queryByText("장비에 보냈습니다")).not.toBeInTheDocument();
  });
});

describe("DeviceControlPage: 설정", () => {
  it("보낸 적이 없으면 비어 있고 안내를 보인다", async () => {
    await open();
    for (const [name, boxes] of [
      ["보고 조건", 3],
      ["대기전력 차단", 2],
      ["예약 전원 재투입", 4],
    ] as const) {
      const inputs = group(name).getAllByRole("textbox");
      expect(inputs).toHaveLength(boxes);
      for (const input of inputs) expect(input).toHaveValue("");
      expect(group(name).getByText("서버가 마지막으로 보낸 값이며 장비의 현재 값과 다를 수 있습니다")).toBeInTheDocument();
    }
    for (const box of group("예약 전원 재투입").getAllByRole("checkbox")) expect(box).not.toBeChecked();
    expect(group("대기전력 차단").getByText("값의 의미와 단위는 추정입니다")).toBeInTheDocument();
    expect(group("대기전력 차단").getByRole("textbox", { name: "지연" })).toBeInTheDocument();
  });

  it("서버가 마지막으로 보낸 값으로 채운다", async () => {
    settings = () => json(SETTINGS);
    await open();
    const values = (name: string) => group(name).getAllByRole<HTMLInputElement>("textbox").map((b) => b.value);
    expect(values("보고 조건")).toEqual(["60", "600", "5"]);
    expect(values("대기전력 차단")).toEqual(["5.00", "60"]);
    expect(group("대기전력 차단").getByRole("switch", { name: "사용" })).toBeChecked();
    expect(values("예약 전원 재투입")).toEqual(["03", "04", "05", "10"]);
    expect(group("예약 전원 재투입").getByRole("switch", { name: "사용" })).toBeChecked();
    expect(group("예약 전원 재투입").getAllByRole("checkbox").map((b) => b.getAttribute("aria-checked"))).toEqual([
      "false", "true", "true", "true", "true", "true", "false",
    ]);
  });

  it("보고 조건을 저장하고, 스트림이 와도 입력과 안내가 남는다", async () => {
    await open();
    const form = group("보고 조건");
    await type(form.getByRole("textbox", { name: "최소 간격" }), "30");
    await type(form.getByRole("textbox", { name: "최대 간격" }), "300");
    await type(form.getByRole("textbox", { name: "변화 기준" }), "7");
    await userEvent.click(form.getByRole("button", { name: "저장" }));
    expect(sent).toEqual([
      { method: "PUT", path: `/${MAC}/report-condition`, body: { minInterval: 30, maxInterval: 300, changeThreshold: 7 } },
    ]);
    expect(await form.findByText("장비에 보냈습니다")).toBeInTheDocument();
    expect(group("대기전력 차단").queryByText("장비에 보냈습니다")).not.toBeInTheDocument();
    act(() => stream().send("device", status({ power: 1 })));
    expect(form.getByText("장비에 보냈습니다")).toBeInTheDocument();
    expect(form.getByRole("textbox", { name: "최소 간격" })).toHaveValue("30");
  });

  it("보고 조건이 범위를 벗어나면 보내지 않고 그 입력 옆에 이유를 보인다", async () => {
    settings = () => json(SETTINGS);
    await open();
    const form = group("보고 조건");
    const min = form.getByRole("textbox", { name: "최소 간격" });
    await type(min, "601");
    await userEvent.click(form.getByRole("button", { name: "저장" }));
    expect(min).toHaveAccessibleDescription(/최대 간격보다/);
    expect(form.getByRole("textbox", { name: "최대 간격" })).toBeValid();
    expect(sent).toEqual([]);

    await type(min, "60");
    await userEvent.click(form.getByRole("button", { name: "저장" }));
    expect(min).toBeValid();
    expect(sent).toHaveLength(1);
  });

  it("대기전력 차단을 저장한다", async () => {
    await open();
    const form = group("대기전력 차단");
    await userEvent.click(form.getByRole("switch", { name: "사용" }));
    await type(form.getByRole("textbox", { name: "기준 전력 (W)" }), "5.5");
    await type(form.getByRole("textbox", { name: "지연" }), "60");
    await userEvent.click(form.getByRole("button", { name: "저장" }));
    expect(sent).toEqual([{ method: "PUT", path: `/${MAC}/standby-cutoff`, body: { enabled: true, thresholdWatts: 5.5, delay: 60 } }]);
  });

  it("예약을 저장한다", async () => {
    await open();
    const form = group("예약 전원 재투입");
    await userEvent.click(form.getByRole("switch", { name: "사용" }));
    await userEvent.click(form.getByRole("checkbox", { name: "일" }));
    await userEvent.click(form.getByRole("checkbox", { name: "토" }));
    await type(form.getByRole("textbox", { name: "시" }), "3");
    await type(form.getByRole("textbox", { name: "분" }), "5");
    await type(form.getByRole("textbox", { name: "초" }), "0");
    await type(form.getByRole("textbox", { name: "다시 켤 때까지의 초" }), "10");
    await userEvent.click(form.getByRole("button", { name: "저장" }));
    expect(sent).toEqual([
      {
        method: "PUT",
        path: `/${MAC}/schedule`,
        body: { enabled: true, days: [true, false, false, false, false, false, true], time: "03:05:00", seconds: 10 },
      },
    ]);
  });

  it("예약이 범위를 벗어나면 보내지 않고 이유를 보인다", async () => {
    await open();
    const form = group("예약 전원 재투입");
    await type(form.getByRole("textbox", { name: "시" }), "3");
    await type(form.getByRole("textbox", { name: "분" }), "0");
    await type(form.getByRole("textbox", { name: "초" }), "55");
    await type(form.getByRole("textbox", { name: "다시 켤 때까지의 초" }), "10");
    await userEvent.click(form.getByRole("button", { name: "저장" }));
    expect(form.getByText("요일을 하나 이상 고르세요.")).toBeInTheDocument();
    expect(form.getByRole("textbox", { name: "다시 켤 때까지의 초" })).toHaveAccessibleDescription(/60보다/);
    expect(sent).toEqual([]);
  });

  it("예약 삭제는 보낸 예약이 없어도 확인 창을 거쳐 보낸다", async () => {
    await open();
    const form = group("예약 전원 재투입");
    await userEvent.click(form.getByRole("button", { name: "삭제" }));
    expect(screen.getByRole("alertdialog")).toHaveTextContent(MAC);
    await userEvent.click(dialog().getByRole("button", { name: "취소" }));
    expect(sent).toEqual([]);
    await userEvent.click(form.getByRole("button", { name: "삭제" }));
    await userEvent.click(dialog().getByRole("button", { name: "삭제" }));
    expect(sent).toEqual([{ method: "DELETE", path: `/${MAC}/schedule`, body: null }]);
    expect(await form.findByText("장비에 보냈습니다")).toBeInTheDocument();
  });

  it("예약 삭제가 성공하면 입력을 비우고, 실패하면 그대로 둔다", async () => {
    settings = () => json(SETTINGS);
    respond = () => json({}, 409);
    await open();
    const form = group("예약 전원 재투입");
    const remove = async () => {
      await userEvent.click(form.getByRole("button", { name: "삭제" }));
      await userEvent.click(dialog().getByRole("button", { name: "삭제" }));
    };
    await remove();
    expect(await form.findByRole("alert")).toHaveTextContent("(409)");
    expect(form.getByRole("textbox", { name: "시" })).toHaveValue("03");

    respond = () => new Response(null, { status: 204 });
    await remove();
    expect(await form.findByText("장비에 보냈습니다")).toBeInTheDocument();
    expect(form.getByRole("textbox", { name: "시" })).toHaveValue("");
    expect(form.getByRole("checkbox", { name: "월" })).not.toBeChecked();
  });

  it("보내는 동안 그 묶음의 버튼만 비활성이고, 실패하면 원인을 보인다", async () => {
    let finish!: (response: Response) => void;
    respond = () => new Promise<Response>((resolve) => (finish = resolve));
    settings = () => json(SETTINGS);
    await open();
    const form = group("예약 전원 재투입");
    await userEvent.click(form.getByRole("button", { name: "저장" }));
    expect(form.getByRole("button", { name: "저장" })).toBeDisabled();
    expect(form.getByRole("button", { name: "삭제" })).toBeDisabled();
    expect(group("보고 조건").getByRole("button", { name: "저장" })).toBeEnabled();
    await waitFor(() => expect(sent).toHaveLength(1));
    await act(async () => finish(json({}, 404)));
    expect(await form.findByRole("alert")).toHaveTextContent("등록되지 않은 장비입니다");
    expect(form.getByRole("button", { name: "저장" })).toBeEnabled();
  });
});

describe("DeviceControlPage: 묶음 규격", () => {
  const follows = (a: Element, b: Element) => (a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING) !== 0;

  it.each([
    ["전원", ["전원 재투입 단위는 약 1초입니다(B540에서 확인)"], "실행", "재부팅"],
    ["보고 조건", ["서버가 마지막으로 보낸 값이며 장비의 현재 값과 다를 수 있습니다"], "저장", null],
    ["대기전력 차단", ["서버가 마지막으로 보낸 값이며 장비의 현재 값과 다를 수 있습니다", "값의 의미와 단위는 추정입니다"], "저장", null],
    ["예약 전원 재투입", ["서버가 마지막으로 보낸 값이며 장비의 현재 값과 다를 수 있습니다"], "저장", "삭제"],
  ])("%s 묶음은 제목·안내·입력·버튼 순서이고 주 버튼만 채운 버튼이다", async (name, notes, main, other) => {
    await open();
    const section = group(name);
    const order: Element[] = [section.getByRole("heading", { level: 2, name }), ...notes.map((note) => section.getByText(note))];
    const inputs = [...section.queryAllByRole("switch"), ...section.queryAllByRole("checkbox"), ...section.getAllByRole("textbox")];
    const mainButton = section.getByRole("button", { name: main });
    for (let i = 1; i < order.length; i++) expect(follows(order[i - 1], order[i])).toBe(true);
    for (const input of inputs) {
      expect(follows(order[order.length - 1], input)).toBe(true);
      expect(follows(input, mainButton)).toBe(true);
    }
    expect(mainButton).toHaveAttribute("data-variant", "default");
    if (other !== null) {
      const otherButton = section.getByRole("button", { name: other });
      expect(otherButton).toHaveAttribute("data-variant", "outline");
      expect(otherButton.parentElement).toBe(mainButton.parentElement);
    }
  });

  it("성공 안내는 버튼 줄에, 실패 안내는 버튼 줄 위에 보인다", async () => {
    settings = () => json(SETTINGS);
    await open();
    const form = group("보고 조건");
    const button = form.getByRole("button", { name: "저장" });
    await userEvent.click(button);
    expect((await form.findByText("장비에 보냈습니다")).parentElement).toBe(button.parentElement);

    respond = () => json({}, 409);
    await userEvent.click(button);
    const alert = await form.findByRole("alert");
    expect(button.parentElement).not.toContainElement(alert);
    expect(follows(alert, button)).toBe(true);
    expect(form.queryByText("장비에 보냈습니다")).not.toBeInTheDocument();
  });
});

describe("DeviceControlPage: 제어할 수 없는 장비", () => {
  it.each([
    [{ online: false, controllable: false }, "장비가 오프라인입니다"],
    [{ online: true, controllable: false }, "장비가 아직 메시지를 보내지 않아 제어할 수 없습니다"],
  ])("%j 이면 모든 제어 버튼이 비활성이고 까닭을 보인다", async (overrides, reason) => {
    await open(status(overrides));
    expect(screen.getByText(reason)).toBeInTheDocument();
    for (const button of screen.getAllByRole("button")) expect(button).toBeDisabled();
    expect(group("전원").getByRole("switch", { name: "릴레이" })).toBeDisabled();
  });

  it("제어 가능해지면 까닭이 사라지고 버튼이 켜진다", async () => {
    await open(status({ online: false, controllable: false }));
    act(() => stream().send("device", status()));
    expect(screen.queryByText("장비가 오프라인입니다")).not.toBeInTheDocument();
    expect(group("보고 조건").getByRole("button", { name: "저장" })).toBeEnabled();
  });
});
