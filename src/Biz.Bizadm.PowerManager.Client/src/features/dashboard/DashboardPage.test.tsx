import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { MemoryRouter, Route, Routes } from "react-router";
import type { Mock } from "vitest";
import { makeStore } from "@/app/store";
import type { DeviceStatus } from "@/features/device-status/types";
import { useDeviceStatusStream } from "@/features/device-status/useDeviceStatusStream";
import { FakeEventSource } from "@/test/setup";
import { DashboardPage } from "./DashboardPage";

const NOW = Date.parse("2026-11-22T06:00:00Z");
const A = "3a0000000001";
const B = "3a0000000002";

function status(mac: string, overrides: Partial<DeviceStatus> = {}): DeviceStatus {
  return {
    mac,
    name: "",
    modelId: "B540_W",
    online: true,
    relay: "on",
    voltage: 221.4,
    current: 0.512,
    powerFactor: 0.987,
    power: 111.9,
    lastReportAt: new Date(NOW - 5 * 60_000).toISOString(),
    controllable: true,
    ...overrides,
  };
}

function json(body: unknown, code = 200): Response {
  return new Response(JSON.stringify(body), { status: code, headers: { "Content-Type": "application/json" } });
}

let fetchMock: Mock;

beforeEach(() => {
  // 상태 조회는 응답하지 않게 두어 스트림만 본다(필요한 테스트가 바꾼다).
  fetchMock = vi.fn(() => new Promise<Response>(() => {}));
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

function Screen() {
  useDeviceStatusStream();
  return <DashboardPage getNow={() => NOW} />;
}

function renderPage() {
  render(
    <Provider store={makeStore()}>
      <MemoryRouter>
        <Routes>
          <Route path="/" element={<Screen />} />
          <Route path="/devices/:mac" element={<p>장비 제어 화면</p>} />
        </Routes>
      </MemoryRouter>
    </Provider>,
  );
}

const stream = () => FakeEventSource.latest();

function openWith(...devices: DeviceStatus[]) {
  act(() => {
    stream().open();
    stream().send("snapshot", devices);
  });
}

const card = (mac: string) => screen.getByRole("article", { name: mac });
const cardMacs = () => screen.getAllByRole("article").map((c) => c.getAttribute("aria-label"));

describe("DashboardPage", () => {
  it("처음에는 스켈레톤을 보이고 요약과 연결 상태를 숨긴다", () => {
    renderPage();
    expect(screen.getByRole("heading", { level: 1, name: "대시보드" })).toBeInTheDocument();
    expect(screen.getByRole("status", { name: "장비 상태를 불러오는 중" })).toBeInTheDocument();
    expect(screen.queryByText(/^온라인 \d/)).not.toBeInTheDocument();
  });

  it("요약과 연결 상태, mac 오름차순 카드를 보인다", () => {
    renderPage();
    openWith(status(B, { online: false, controllable: false, power: 50 }), status(A));
    expect(screen.getByText("온라인 1대 · 오프라인 1대 · 합계 111.9 W")).toBeInTheDocument();
    expect(screen.getByText("실시간 연결됨")).toBeInTheDocument();
    expect(cardMacs()).toEqual([A, B]);
  });

  it("장비명이 있으면 제목에 장비명, 아래에 모델과 mac을 보이고 확인 창에도 밝힌다", async () => {
    renderPage();
    openWith(status(A, { name: "거실 TV" }), status(B));
    const c = card(A);
    expect(within(c).getByText("거실 TV")).toHaveAttribute("data-slot", "card-title");
    expect(c.querySelector("[data-slot=card-description]")).toHaveTextContent(`B540_W · ${A}`);
    expect(within(c).getByText(A)).toHaveClass("font-mono");
    expect(card(B).querySelector("[data-slot=card-title]")).toHaveTextContent("B540_W");
    expect(card(B).querySelector("[data-slot=card-description]")).toHaveTextContent(new RegExp(`^${B}$`));

    await userEvent.click(within(c).getByRole("switch", { name: "릴레이" }));
    const dialog = screen.getByRole("alertdialog");
    expect(dialog).toHaveTextContent(`거실 TV — B540_W (${A})`);
    expect(within(dialog).getByText(A)).toHaveClass("font-mono");
  });

  it("장비명 순서로 먼저, 장비명이 없는 장비는 뒤에 보이고 바뀌면 바로 반영한다", () => {
    const C = "3a0000000003";
    renderPage();
    openWith(status(A), status(B, { name: "서재" }), status(C, { name: "거실" }));
    expect(cardMacs()).toEqual([C, B, A]);
    act(() => stream().send("device", status(A, { name: "가게" })));
    expect(cardMacs()).toEqual([A, C, B]);
    expect(within(card(A)).getByText("가게")).toBeInTheDocument();
    act(() => stream().send("device", status(C)));
    expect(cardMacs()).toEqual([A, B, C]);
  });

  it("카드에 모델·mac·온라인 글자·계량 값·릴레이·마지막 보고를 보인다", () => {
    renderPage();
    openWith(status(A));
    const c = card(A);
    expect(c).toHaveTextContent("B540_W");
    expect(within(c).getByText(A)).toHaveClass("font-mono");
    expect(within(c).getByText("온라인").querySelector("[data-slot=online-dot]")).toHaveClass("bg-success");
    for (const text of ["111.90 W", "221.40 V", "0.512 A", "0.987", "켜짐", "5분 전"]) {
      expect(within(c).getByText(text)).toBeInTheDocument();
    }
    expect(within(c).getByRole("switch", { name: "릴레이" })).toBeChecked();
    expect(within(c).getByTestId("meters")).not.toHaveClass("opacity-50");
  });

  it("값이 없으면 —와 '보고 없음'이고, 오프라인 장비의 계량 값은 흐리며 릴레이를 누를 수 없다", () => {
    renderPage();
    openWith(
      status(A, {
        modelId: "",
        online: false,
        controllable: false,
        relay: "unknown",
        voltage: null,
        current: null,
        powerFactor: null,
        power: null,
        lastReportAt: null,
      }),
    );
    const c = card(A);
    expect(within(c).getByText("오프라인").querySelector("[data-slot=online-dot]")).not.toHaveClass("bg-success");
    expect(within(c).getAllByText("—")).toHaveLength(5);
    expect(within(c).getByText("보고 없음")).toBeInTheDocument();
    expect(within(c).getByTestId("meters")).toHaveClass("opacity-50");
    expect(within(c).getByText("상태 모름")).toBeInTheDocument();
    expect(within(c).getByRole("button", { name: "켜기" })).toBeDisabled();
  });

  it("'제어' 버튼은 장비 제어 화면으로 간다", async () => {
    renderPage();
    openWith(status(A));
    const link = within(card(A)).getByRole("link", { name: "제어" });
    expect(link).toHaveAttribute("href", `/devices/${A}`);
    await userEvent.click(link);
    expect(screen.getByText("장비 제어 화면")).toBeInTheDocument();
  });

  it("스트림의 변경을 카드와 요약에 반영하고 새 장비 카드를 더한다", () => {
    renderPage();
    openWith(status(B));
    act(() => stream().send("device", status(B, { power: 20, relay: "off" })));
    expect(within(card(B)).getByText("20.00 W")).toBeInTheDocument();
    expect(within(card(B)).getByText("꺼짐")).toBeInTheDocument();
    expect(screen.getByText("온라인 1대 · 오프라인 0대 · 합계 20.0 W")).toBeInTheDocument();
    act(() => stream().send("device", status(A, { power: 1.5 })));
    expect(cardMacs()).toEqual([A, B]);
    expect(screen.getByText("온라인 2대 · 오프라인 0대 · 합계 21.5 W")).toBeInTheDocument();
  });

  it("릴레이 켜기는 스트림으로 바뀐 것을 받으면 끝난다", async () => {
    renderPage();
    openWith(status(A, { relay: "off" }));
    fetchMock.mockImplementation(() => Promise.resolve(new Response(null, { status: 204 })));
    await userEvent.click(within(card(A)).getByRole("switch"));
    expect(within(card(A)).getByRole("switch")).toBeDisabled();
    act(() => stream().send("device", status(A, { relay: "on" })));
    expect(within(card(A)).getByRole("switch")).toBeEnabled();
    expect(within(card(A)).getByRole("switch")).toBeChecked();
  });

  it("한 카드의 오류는 그 카드에만 보인다", async () => {
    renderPage();
    openWith(status(A, { relay: "off" }), status(B, { relay: "off" }));
    fetchMock.mockImplementation(() => Promise.resolve(json({}, 409)));
    await userEvent.click(within(card(A)).getByRole("switch"));
    expect(await within(card(A)).findByRole("alert")).toHaveTextContent("(409)");
    expect(within(card(B)).queryByRole("alert")).not.toBeInTheDocument();
  });

  it("끊기면 마지막 값을 그대로 보이고, 브라우저가 다시 연결하면 전체 상태로 맞춘다", () => {
    renderPage();
    openWith(status(A), status(B));
    act(() => stream().fail(FakeEventSource.CONNECTING));
    expect(screen.getByText("연결 끊김 — 다시 연결 중")).toBeInTheDocument();
    expect(within(card(A)).getByText("111.90 W")).toBeInTheDocument();

    openWith(status(A, { power: 5 }));
    expect(FakeEventSource.instances).toHaveLength(1);
    expect(screen.getByText("실시간 연결됨")).toBeInTheDocument();
    expect(within(card(A)).getByText("5.00 W")).toBeInTheDocument();
    expect(screen.queryByRole("article", { name: B })).not.toBeInTheDocument();
  });

  it("스트림이 닫히면 3초 뒤 새로 열고 전체 상태로 맞춘다", async () => {
    vi.useFakeTimers();
    renderPage();
    openWith(status(A));
    act(() => stream().fail(FakeEventSource.CLOSED));
    expect(screen.getByText("연결 끊김 — 다시 연결 중")).toBeInTheDocument();
    await act(() => vi.advanceTimersByTimeAsync(2_900));
    expect(FakeEventSource.instances).toHaveLength(1);
    await act(() => vi.advanceTimersByTimeAsync(200));
    expect(FakeEventSource.instances).toHaveLength(2);

    openWith(status(A, { power: 7 }));
    expect(screen.getByText("실시간 연결됨")).toBeInTheDocument();
    expect(within(card(A)).getByText("7.00 W")).toBeInTheDocument();
  });

  it("새로 연 스트림도 닫히면 다시 3초 뒤에 연다", async () => {
    vi.useFakeTimers();
    renderPage();
    for (let opened = 1; opened <= 3; opened++) {
      expect(FakeEventSource.instances).toHaveLength(opened);
      act(() => stream().fail(FakeEventSource.CLOSED));
      await act(() => vi.advanceTimersByTimeAsync(3_000));
    }
    expect(FakeEventSource.instances).toHaveLength(4);
  });

  it("스트림보다 먼저 온 상태 조회 결과로 첫 화면을 보이고, 스트림 전체 상태가 오면 그것으로 맞춘다", async () => {
    fetchMock.mockImplementation(() => Promise.resolve(json([status(A, { power: 1 })])));
    renderPage();
    expect(await screen.findByText("1.00 W")).toBeInTheDocument();
    openWith(status(A, { power: 2 }));
    expect(within(card(A)).getByText("2.00 W")).toBeInTheDocument();
  });

  it("스트림보다 늦게 온 상태 조회 결과는 버린다", async () => {
    let respond!: (response: Response) => void;
    fetchMock.mockImplementation(() => new Promise<Response>((resolve) => (respond = resolve)));
    renderPage();
    await waitFor(() => expect(fetchMock).toHaveBeenCalled());
    openWith(status(A, { power: 2 }));
    await act(async () => {
      respond(json([status(A, { power: 1 })]));
      await new Promise((resolve) => setTimeout(resolve, 50));
    });
    expect(within(card(A)).getByText("2.00 W")).toBeInTheDocument();
  });

  it("처음 불러오기에 실패하면 원인과 '다시 시도'를 보이고, 다시 시도해 성공하면 카드를 보인다", async () => {
    fetchMock.mockImplementationOnce(() => Promise.resolve(json({}, 500)));
    renderPage();
    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("서버 응답: 500");
    fetchMock.mockImplementationOnce(() => Promise.resolve(json([status(A)])));
    await userEvent.click(within(alert).getByRole("button", { name: "다시 시도" }));
    expect(await screen.findByRole("article", { name: A })).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("등록된 장비가 없으면 빈 목록 안내를 보인다", () => {
    renderPage();
    openWith();
    expect(screen.getByText("등록된 장비가 없습니다")).toBeInTheDocument();
    expect(screen.getByText("온라인 0대 · 오프라인 0대 · 합계 0.0 W")).toBeInTheDocument();
  });
});
