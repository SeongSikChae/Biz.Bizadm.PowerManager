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
    name: "",
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

let settings: () => Response | Promise<Response>;
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

/** 설정 응답을 기다리지 않고 연다. */
function openLoading(device: DeviceStatus = status()) {
  renderPage();
  act(() => {
    stream().open();
    stream().send("snapshot", [device]);
  });
}

async function open(device: DeviceStatus = status()) {
  openLoading(device);
  await waitFor(() => expect(screen.queryByText("설정을 불러오는 중")).not.toBeInTheDocument());
}

function deferred() {
  let resolve!: (response: Response) => void;
  const promise = new Promise<Response>((done) => (resolve = done));
  return { promise, resolve };
}

const SECTIONS = ["전원", "보고 조건", "대기전력 차단", "예약 전원 재투입"];
const SETTING_SECTIONS = SECTIONS.slice(1);

const group = (name: string) => within(screen.getByRole("group", { name }));
const dialog = () => within(screen.getByRole("alertdialog"));
const nameDialog = () => within(screen.getByRole("dialog", { name: "이름 변경" }));
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
});

describe("DeviceControlPage: 설정을 불러오는 동안", () => {
  const fields = (name: string) => [
    ...group(name).getAllByRole("textbox"),
    ...group(name).queryAllByRole("switch"),
    ...group(name).queryAllByRole("checkbox"),
  ];
  const removeSchedule = async () => {
    await userEvent.click(group("예약 전원 재투입").getByRole("button", { name: "삭제" }));
    await userEvent.click(dialog().getByRole("button", { name: "삭제" }));
  };

  it("네 묶음을 바로 보이고 설정 묶음의 입력과 저장만 비활성이다", async () => {
    settings = () => new Promise<Response>(() => {});
    openLoading();
    for (const name of SECTIONS) expect(screen.getByRole("group", { name })).toBeInTheDocument();
    for (const name of SETTING_SECTIONS) {
      for (const field of fields(name)) expect(field).toBeDisabled();
      for (const box of group(name).getAllByRole("textbox")) expect(box).toHaveValue("");
      const save = group(name).getByRole("button", { name: "저장" });
      expect(save).toBeDisabled();
      expect(group(name).getByRole("status")).toHaveTextContent("설정을 불러오는 중");
      expect(group(name).getByRole("status").parentElement).toBe(save.parentElement);
      expect(screen.getByRole("group", { name })).toHaveAttribute("aria-busy", "true");
    }
    expect(document.querySelector("[data-slot=skeleton]")).toBeNull();
    expect(group("전원").queryByRole("status")).not.toBeInTheDocument();
    expect(group("전원").getByRole("textbox")).toBeEnabled();
    expect(group("전원").getByRole("button", { name: "실행" })).toBeEnabled();
    expect(group("전원").getByRole("switch", { name: "릴레이" })).toBeEnabled();
    expect(group("예약 전원 재투입").getByRole("button", { name: "삭제" })).toBeEnabled();
  });

  it("응답이 오면 같은 묶음에 값을 채우고 활성으로 바꾼다", async () => {
    const response = deferred();
    settings = () => response.promise;
    openLoading();
    const before = SECTIONS.map((name) => screen.getByRole("group", { name }));
    await act(async () => response.resolve(json(SETTINGS)));
    await waitFor(() => expect(screen.queryByText("설정을 불러오는 중")).not.toBeInTheDocument());
    expect(SECTIONS.map((name) => screen.getByRole("group", { name }))).toEqual(before);
    for (const name of SETTING_SECTIONS) {
      for (const field of fields(name)) expect(field).toBeEnabled();
      expect(group(name).getByRole("button", { name: "저장" })).toBeEnabled();
      expect(screen.getByRole("group", { name })).toHaveAttribute("aria-busy", "false");
    }
    expect(group("보고 조건").getByRole("textbox", { name: "최소 간격 (초)" })).toHaveValue("60");
    expect(group("대기전력 차단").getByRole("switch", { name: "사용" })).toBeChecked();
    expect(group("예약 전원 재투입").getByRole("textbox", { name: "시" })).toHaveValue("03");
  });

  it("제어할 수 없는 장비면 응답이 와도 저장은 비활성이고 입력은 활성이다", async () => {
    await open(status({ online: false, controllable: false }));
    expect(group("보고 조건").getByRole("button", { name: "저장" })).toBeDisabled();
    expect(group("보고 조건").getByRole("textbox", { name: "최소 간격 (초)" })).toBeEnabled();
  });

  it("실패하면 묶음을 둔 채 위에 원인을 보이고, 다시 시도하는 동안 안내를 두었다가 성공하면 채운다", async () => {
    settings = () => json({}, 500);
    await open();
    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("설정을 불러오지 못했습니다");
    expect(alert).toHaveTextContent("서버 응답: 500");
    expect(alert.compareDocumentPosition(screen.getByRole("group", { name: "전원" })) & Node.DOCUMENT_POSITION_FOLLOWING).not.toBe(0);
    for (const name of SETTING_SECTIONS) {
      for (const field of fields(name)) expect(field).toBeDisabled();
      expect(group(name).getByRole("button", { name: "저장" })).toBeDisabled();
      expect(group(name).queryByRole("status")).not.toBeInTheDocument();
    }
    expect(group("예약 전원 재투입").getByRole("button", { name: "삭제" })).toBeEnabled();

    const retry = deferred();
    settings = () => retry.promise;
    await userEvent.click(within(alert).getByRole("button", { name: "다시 시도" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "다시 시도" })).toBeDisabled());
    expect(screen.getByRole("alert")).toHaveTextContent("서버 응답: 500");
    expect(screen.getAllByText("설정을 불러오는 중")).toHaveLength(3);

    await act(async () => retry.resolve(json(SETTINGS)));
    await waitFor(() => expect(screen.queryByRole("alert")).not.toBeInTheDocument());
    expect(group("보고 조건").getByRole("textbox", { name: "최소 간격 (초)" })).toHaveValue("60");
    expect(group("보고 조건").getByRole("button", { name: "저장" })).toBeEnabled();
  });

  it("다시 시도도 실패하면 새 원인을 보이고 비활성으로 남는다", async () => {
    settings = () => json({}, 500);
    await open();
    settings = () => json({}, 404);
    await userEvent.click(screen.getByRole("button", { name: "다시 시도" }));
    await waitFor(() => expect(screen.getByRole("alert")).toHaveTextContent("등록되지 않은 장비입니다"));
    expect(screen.getByRole("button", { name: "다시 시도" })).toBeEnabled();
    expect(group("보고 조건").getByRole("button", { name: "저장" })).toBeDisabled();
  });

  it("응답 전에 예약을 지우면 늦게 온 옛 예약으로 채우지 않는다", async () => {
    const response = deferred();
    settings = () => response.promise;
    openLoading();
    await removeSchedule();
    const form = group("예약 전원 재투입");
    expect(await form.findByText("장비에 보냈습니다")).toBeInTheDocument();
    expect(sent).toEqual([{ method: "DELETE", path: `/${MAC}/schedule`, body: null }]);
    expect(form.getByText("설정을 불러오는 중")).toHaveClass("sr-only");
    expect(group("보고 조건").getByText("설정을 불러오는 중")).not.toHaveClass("sr-only");

    await act(async () => response.resolve(json(SETTINGS)));
    await waitFor(() => expect(form.queryByText("설정을 불러오는 중")).not.toBeInTheDocument());
    expect(form.getByText("장비에 보냈습니다")).toBeInTheDocument();
    for (const box of form.getAllByRole("textbox")) expect(box).toHaveValue("");
    for (const box of form.getAllByRole("checkbox")) expect(box).not.toBeChecked();
    expect(form.getByRole("switch", { name: "사용" })).not.toBeChecked();
    expect(group("보고 조건").getByRole("textbox", { name: "최소 간격 (초)" })).toHaveValue("60");
  });

  it("예약 삭제보다 설정 응답이 먼저 오면 채웠다가 삭제가 성공하면 비운다", async () => {
    const response = deferred();
    const removal = deferred();
    settings = () => response.promise;
    respond = () => removal.promise;
    openLoading();
    await removeSchedule();
    const form = group("예약 전원 재투입");
    await act(async () => response.resolve(json(SETTINGS)));
    await waitFor(() => expect(form.getByRole("textbox", { name: "시" })).toHaveValue("03"));
    await act(async () => removal.resolve(new Response(null, { status: 204 })));
    await waitFor(() => expect(form.getByRole("textbox", { name: "시" })).toHaveValue(""));
  });

  it("응답 전의 예약 삭제가 실패하면 원인을 보이고 설정은 채운다", async () => {
    const response = deferred();
    settings = () => response.promise;
    respond = () => json({}, 409);
    openLoading();
    await removeSchedule();
    const form = group("예약 전원 재투입");
    expect(await form.findByRole("alert")).toHaveTextContent("(409)");
    expect(form.getByText("설정을 불러오는 중")).not.toHaveClass("sr-only");
    await act(async () => response.resolve(json(SETTINGS)));
    await waitFor(() => expect(form.getByRole("textbox", { name: "시" })).toHaveValue("03"));
  });
});

describe("DeviceControlPage: 장비명", () => {
  const rename = () => userEvent.click(screen.getByRole("button", { name: "이름 변경" }));
  const box = () => nameDialog().getByRole("textbox", { name: "장비 이름" });

  it("장비명이 있으면 제목에 장비명, 아래에 모델과 mac을 보이고 확인 창에도 밝힌다", async () => {
    await open(status({ name: "거실 TV" }));
    const heading = screen.getByRole("heading", { level: 1, name: "거실 TV" });
    expect(heading.closest("header")).toHaveTextContent(`B540_W · ${MAC}`);
    expect(screen.getByText(MAC)).toHaveClass("font-mono");
    await userEvent.click(group("전원").getByRole("button", { name: "재부팅" }));
    expect(screen.getByRole("alertdialog")).toHaveTextContent(`거실 TV — B540_W (${MAC})`);
  });

  it("장비를 밝힌 창에서 이름을 저장하면 닫히고 머리가 바뀐다(스트림이 끊겨 있어도)", async () => {
    await open();
    act(() => stream().fail(FakeEventSource.CONNECTING));
    await rename();
    expect(nameDialog().getByText(MAC)).toHaveClass("font-mono");
    expect(screen.getByRole("dialog")).toHaveTextContent(`B540_W (${MAC})`);
    expect(box()).toHaveValue("");
    expect(box()).toHaveAccessibleDescription("30자까지. 비워서 저장하면 이름을 지웁니다");
    await type(box(), "  거실 TV ");
    await userEvent.click(nameDialog().getByRole("button", { name: "저장" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(sent).toEqual([{ method: "PUT", path: `/${MAC}/name`, body: { name: "거실 TV" } }]);
    expect(screen.getByRole("heading", { level: 1, name: "거실 TV" })).toBeInTheDocument();
  });

  it("비워서 저장하면 이름을 지운다(Enter로도 저장한다)", async () => {
    await open(status({ name: "거실 TV" }));
    await rename();
    expect(box()).toHaveValue("거실 TV");
    await userEvent.clear(box());
    await userEvent.type(box(), "{Enter}");
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(sent).toEqual([{ method: "PUT", path: `/${MAC}/name`, body: { name: "" } }]);
    expect(screen.getByRole("heading", { level: 1, name: "B540_W" })).toBeInTheDocument();
  });

  it.each(["취소", "Esc"])("%s 는 아무것도 보내지 않고 닫고, 다시 열면 지금 이름으로 시작한다", async (how) => {
    await open(status({ name: "거실 TV" }));
    await rename();
    await type(box(), "서재");
    if (how === "Esc") await userEvent.keyboard("{Escape}");
    else await userEvent.click(nameDialog().getByRole("button", { name: "취소" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(sent).toEqual([]);
    await rename();
    expect(box()).toHaveValue("거실 TV");
  });

  it("쓸 수 없는 이름이면 보내지 않고 입력 옆에 이유를 보인다", async () => {
    await open();
    await rename();
    fireEvent.change(box(), { target: { value: "가".repeat(31) } });
    await userEvent.click(nameDialog().getByRole("button", { name: "저장" }));
    expect(box()).toBeInvalid();
    expect(box()).toHaveAccessibleDescription(/30자 이하로 입력하세요/);
    expect(box()).toHaveAccessibleDescription(/30자까지/);
    fireEvent.change(box(), { target: { value: `거${String.fromCodePoint(0x2028)}실` } });
    await userEvent.click(nameDialog().getByRole("button", { name: "저장" }));
    expect(box()).toHaveAccessibleDescription(/제어 문자는 쓸 수 없습니다/);
    expect(sent).toEqual([]);
  });

  it("저장하는 동안 저장이 비활성이고 다시 보내지 않으며, 실패하면 창을 둔 채 원인을 보인다", async () => {
    const response = deferred();
    respond = () => response.promise;
    await open();
    await rename();
    await type(box(), "거실");
    await userEvent.click(nameDialog().getByRole("button", { name: "저장" }));
    expect(nameDialog().getByRole("button", { name: "저장" })).toBeDisabled();
    fireEvent.submit(box().closest("form")!);
    await waitFor(() => expect(sent).toHaveLength(1));
    await act(async () => response.resolve(json({}, 404)));
    expect(await nameDialog().findByRole("alert")).toHaveTextContent("등록되지 않은 장비입니다");
    expect(box()).toHaveValue("거실");
    expect(nameDialog().getByRole("button", { name: "저장" })).toBeEnabled();
    expect(sent).toHaveLength(1);
  });

  it("저장하는 동안 닫고 다시 연 창은 앞 저장이 성공해도 남는다", async () => {
    const response = deferred();
    respond = () => response.promise;
    await open();
    await rename();
    await type(box(), "거실");
    await userEvent.click(nameDialog().getByRole("button", { name: "저장" }));
    await userEvent.click(nameDialog().getByRole("button", { name: "취소" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    await rename();
    await type(box(), "서재");
    await act(async () => response.resolve(new Response(null, { status: 204 })));
    await waitFor(() => expect(screen.getByText("거실")).toBeInTheDocument());
    expect(box()).toHaveValue("서재");
    expect(nameDialog().getByRole("button", { name: "저장" })).toBeEnabled();
    expect(sent).toHaveLength(1);
  });

  it("오프라인이어도 이름을 바꿀 수 있다", async () => {
    await open(status({ online: false, controllable: false }));
    await rename();
    await type(box(), "거실");
    await userEvent.click(nameDialog().getByRole("button", { name: "저장" }));
    expect(await screen.findByRole("heading", { level: 1, name: "거실" })).toBeInTheDocument();
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

  it("전원 재투입은 기본 10이고 확인 창을 거쳐 보낸다", async () => {
    await open();
    const power = group("전원");
    expect(power.getByRole("textbox", { name: "전원 재투입 (초)" })).toHaveValue("10");
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
    const box = group("전원").getByRole("textbox", { name: "전원 재투입 (초)" });
    await type(box, text);
    await userEvent.click(group("전원").getByRole("button", { name: "실행" }));
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(box).toBeInvalid();
    expect(box).toHaveAccessibleDescription(/^전원을 끄고 .*정수 .*입력하세요/);
    expect(sent).toEqual([]);
  });

  it("성공한 뒤 범위 밖 값으로 실행하면 앞의 성공 안내가 남지 않는다", async () => {
    await open();
    const power = group("전원");
    await userEvent.click(power.getByRole("button", { name: "실행" }));
    await userEvent.click(dialog().getByRole("button", { name: "실행" }));
    await power.findByText("장비에 보냈습니다");

    const box = power.getByRole("textbox", { name: "전원 재투입 (초)" });
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
    expect(screen.queryByText(/값의 의미와 단위는 추정입니다|전원 재투입 단위는/)).not.toBeInTheDocument();
  });

  it.each([
    ["B540_W", ""],
    ["M130-W", ", 추정"],
    ["", ", 추정"],
  ])("%j 장비의 입력마다 이름에 단위를, 아래에 설명을 보인다", async (modelId, estimated) => {
    await open(status({ modelId }));
    const labels = [
      `전원 재투입 (초${estimated})`,
      `최소 간격 (초${estimated})`,
      `최대 간격 (초${estimated})`,
      "변화 기준 (%, 추정)",
      "기준 전력 (W, 추정)",
      "지연 (초, 추정)",
      "시",
      "분",
      "초",
      "다시 켤 때까지의 초 (추정)",
    ];
    const boxes = screen.getAllByRole("textbox");
    expect(boxes).toHaveLength(labels.length);
    labels.forEach((name, i) => {
      expect(boxes[i]).toHaveAccessibleName(name);
      const hint = boxes[i].nextElementSibling;
      expect(hint).toHaveAttribute("id", boxes[i].getAttribute("aria-describedby"));
      expect(boxes[i]).toHaveAccessibleDescription(hint?.textContent ?? "없음");
      expect(hint).toHaveTextContent(/\d~\d/);
    });
    expect(boxes[1]).toHaveAccessibleDescription(
      modelId === "B540_W" ? /다시 보고하지 않습니다/ : /다시 보고하지 않는 것으로 추정합니다/,
    );
  });

  it("오류는 설명 아래에 보인다", async () => {
    await open();
    const min = group("보고 조건").getByRole("textbox", { name: "최소 간격 (초)" });
    await userEvent.click(group("보고 조건").getByRole("button", { name: "저장" }));
    const [hintId, errorId] = min.getAttribute("aria-describedby")!.split(" ");
    const hint = document.getElementById(hintId)!;
    expect(hint).toHaveTextContent(/1~3600의 정수, 최대 간격 이하/);
    expect(hint.nextElementSibling).toBe(document.getElementById(errorId));
    expect(document.getElementById(errorId)).toHaveTextContent("값을 입력하세요.");
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
    await type(form.getByRole("textbox", { name: "최소 간격 (초)" }), "30");
    await type(form.getByRole("textbox", { name: "최대 간격 (초)" }), "300");
    await type(form.getByRole("textbox", { name: "변화 기준 (%, 추정)" }), "7");
    await userEvent.click(form.getByRole("button", { name: "저장" }));
    expect(sent).toEqual([
      { method: "PUT", path: `/${MAC}/report-condition`, body: { minInterval: 30, maxInterval: 300, changeThreshold: 7 } },
    ]);
    expect(await form.findByText("장비에 보냈습니다")).toBeInTheDocument();
    expect(group("대기전력 차단").queryByText("장비에 보냈습니다")).not.toBeInTheDocument();
    act(() => stream().send("device", status({ power: 1 })));
    expect(form.getByText("장비에 보냈습니다")).toBeInTheDocument();
    expect(form.getByRole("textbox", { name: "최소 간격 (초)" })).toHaveValue("30");
  });

  it("보고 조건이 범위를 벗어나면 보내지 않고 그 입력 옆에 이유를 보인다", async () => {
    settings = () => json(SETTINGS);
    await open();
    const form = group("보고 조건");
    const min = form.getByRole("textbox", { name: "최소 간격 (초)" });
    await type(min, "601");
    await userEvent.click(form.getByRole("button", { name: "저장" }));
    expect(min).toHaveAccessibleDescription(/최대 간격보다/);
    expect(form.getByRole("textbox", { name: "최대 간격 (초)" })).toBeValid();
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
    await type(form.getByRole("textbox", { name: "기준 전력 (W, 추정)" }), "5.5");
    await type(form.getByRole("textbox", { name: "지연 (초, 추정)" }), "60");
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
    await type(form.getByRole("textbox", { name: "다시 켤 때까지의 초 (추정)" }), "10");
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
    await type(form.getByRole("textbox", { name: "다시 켤 때까지의 초 (추정)" }), "10");
    await userEvent.click(form.getByRole("button", { name: "저장" }));
    expect(form.getByText("요일을 하나 이상 고르세요.")).toBeInTheDocument();
    expect(form.getByRole("textbox", { name: "다시 켤 때까지의 초 (추정)" })).toHaveAccessibleDescription(/60보다/);
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
    ["전원", [], "실행", "재부팅"],
    ["보고 조건", ["서버가 마지막으로 보낸 값이며 장비의 현재 값과 다를 수 있습니다"], "저장", null],
    ["대기전력 차단", ["서버가 마지막으로 보낸 값이며 장비의 현재 값과 다를 수 있습니다"], "저장", null],
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
    for (const name of SECTIONS) for (const button of group(name).getAllByRole("button")) expect(button).toBeDisabled();
    expect(screen.getByRole("button", { name: "이름 변경" })).toBeEnabled();
    expect(group("전원").getByRole("switch", { name: "릴레이" })).toBeDisabled();
  });

  it("제어 가능해지면 까닭이 사라지고 버튼이 켜진다", async () => {
    await open(status({ online: false, controllable: false }));
    act(() => stream().send("device", status()));
    expect(screen.queryByText("장비가 오프라인입니다")).not.toBeInTheDocument();
    expect(group("보고 조건").getByRole("button", { name: "저장" })).toBeEnabled();
  });
});
