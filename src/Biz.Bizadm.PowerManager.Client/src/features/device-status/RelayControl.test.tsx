import { act, fireEvent, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import type { Mock } from "vitest";
import { makeStore } from "@/app/store";
import { RelayControl } from "./RelayControl";
import type { DeviceStatus } from "./types";

const MAC = "3a0000000001";

function status(overrides: Partial<DeviceStatus> = {}): DeviceStatus {
  return {
    mac: MAC,
    modelId: "B540_W",
    online: true,
    relay: "on",
    voltage: null,
    current: null,
    powerFactor: null,
    power: null,
    lastReportAt: null,
    controllable: true,
    ...overrides,
  };
}

let fetchMock: Mock;

beforeEach(() => {
  fetchMock = vi.fn(() => Promise.resolve(new Response(null, { status: 204 })));
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

function setup(device: DeviceStatus) {
  const store = makeStore();
  const ui = (d: DeviceStatus) => (
    <Provider store={store}>
      <RelayControl device={d} />
    </Provider>
  );
  const view = render(ui(device));
  return { update: (d: DeviceStatus) => view.rerender(ui(d)) };
}

async function sentBodies(): Promise<unknown[]> {
  return Promise.all(
    fetchMock.mock.calls.map(async (call) => {
      const request = call[0] as Request;
      expect(request.method).toBe("POST");
      expect(new URL(request.url).pathname).toBe(`/api/admin/devices/${MAC}/relay`);
      return request.clone().json();
    }),
  );
}

const relaySwitch = () => screen.getByRole("switch", { name: "릴레이" });
const button = (name: string) => screen.getByRole("button", { name });

describe("RelayControl: 상태를 알 때", () => {
  it("스위치와 상태 글자를 보인다", () => {
    const { update } = setup(status({ relay: "on" }));
    expect(relaySwitch()).toBeChecked();
    expect(screen.getByText("켜짐")).toBeInTheDocument();
    update(status({ relay: "off" }));
    expect(relaySwitch()).not.toBeChecked();
    expect(screen.getByText("꺼짐")).toBeInTheDocument();
  });

  it("꺼진 스위치를 누르면 확인 없이 켜기를 보낸다", async () => {
    setup(status({ relay: "off" }));
    await userEvent.click(relaySwitch());
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(await sentBodies()).toEqual([{ on: true }]);
  });

  it("켜진 스위치를 누르면 장비를 밝힌 확인 창을 거쳐 끄기를 보낸다", async () => {
    setup(status({ relay: "on" }));
    await userEvent.click(relaySwitch());
    const dialog = screen.getByRole("alertdialog");
    expect(dialog).toHaveTextContent("B540_W");
    expect(dialog).toHaveTextContent(MAC);
    expect(fetchMock).not.toHaveBeenCalled();
    await userEvent.click(button("끄기"));
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(await sentBodies()).toEqual([{ on: false }]);
  });

  it("취소와 Esc는 아무것도 보내지 않는다", async () => {
    setup(status({ relay: "on" }));
    await userEvent.click(relaySwitch());
    await userEvent.click(button("취소"));
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    await userEvent.click(relaySwitch());
    await userEvent.keyboard("{Escape}");
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
    expect(relaySwitch()).toBeEnabled();
  });

  it("제어 가능하지 않으면 누를 수 없다", () => {
    setup(status({ online: false, controllable: false }));
    expect(relaySwitch()).toBeDisabled();
  });
});

describe("RelayControl: 상태를 모를 때", () => {
  it("켜기는 바로, 끄기는 확인 창을 거쳐 보내고, 알게 되면 스위치로 바뀐다", async () => {
    const { update } = setup(status({ relay: "unknown" }));
    expect(screen.getByText("상태 모름")).toBeInTheDocument();
    expect(screen.queryByRole("switch")).not.toBeInTheDocument();
    await userEvent.click(button("켜기"));
    expect(await sentBodies()).toEqual([{ on: true }]);
    expect(button("켜기")).toBeDisabled();
    expect(button("끄기")).toBeDisabled();
    update(status({ relay: "on" }));
    expect(relaySwitch()).toBeEnabled();
    expect(screen.queryByText("상태 모름")).not.toBeInTheDocument();
  });

  it("끄기는 확인 창을 거친다", async () => {
    setup(status({ relay: "unknown" }));
    await userEvent.click(button("끄기"));
    expect(screen.getByRole("alertdialog")).toHaveTextContent(MAC);
    expect(fetchMock).not.toHaveBeenCalled();
    await userEvent.click(button("끄기"));
    expect(await sentBodies()).toEqual([{ on: false }]);
  });

  it("제어 가능하지 않으면 두 버튼을 누를 수 없다", () => {
    setup(status({ relay: "unknown", controllable: false }));
    expect(button("켜기")).toBeDisabled();
    expect(button("끄기")).toBeDisabled();
  });
});

describe("RelayControl: 보낸 뒤", () => {
  it("보낸 값이 될 때까지 진행 중 표시와 함께 비활성이고, 다른 변경에는 풀리지 않는다", async () => {
    const { update } = setup(status({ relay: "off" }));
    await userEvent.click(relaySwitch());
    expect(screen.getByRole("status")).toHaveTextContent("켜는 중");
    expect(relaySwitch()).toBeDisabled();
    update(status({ relay: "off", power: 1 }));
    expect(relaySwitch()).toBeDisabled();
    update(status({ relay: "on" }));
    expect(relaySwitch()).toBeEnabled();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("API가 실패하면 마지막 상태로 되돌리고 원인을 알린다", async () => {
    fetchMock.mockResolvedValue(new Response("{}", { status: 409, headers: { "Content-Type": "application/problem+json" } }));
    setup(status({ relay: "off" }));
    await userEvent.click(relaySwitch());
    expect(await screen.findByRole("alert")).toHaveTextContent("(409)");
    expect(relaySwitch()).toBeEnabled();
    expect(relaySwitch()).not.toBeChecked();
  });

  it("확인 창을 연 사이 이미 꺼졌어도 API가 실패하면 원인을 알린다", async () => {
    fetchMock.mockResolvedValue(new Response("{}", { status: 409, headers: { "Content-Type": "application/problem+json" } }));
    const { update } = setup(status({ relay: "on" }));
    await userEvent.click(relaySwitch());
    update(status({ relay: "off" }));
    await userEvent.click(button("끄기"));
    expect(await screen.findByRole("alert")).toHaveTextContent("(409)");
    expect(await sentBodies()).toEqual([{ on: false }]);
    expect(relaySwitch()).toBeEnabled();
    expect(relaySwitch()).not.toBeChecked();
  });

  it("10초 안에 바뀌지 않으면 되돌리고 알린다", async () => {
    vi.useFakeTimers();
    setup(status({ relay: "off" }));
    fireEvent.click(relaySwitch());
    await act(() => vi.advanceTimersByTimeAsync(9_900));
    expect(relaySwitch()).toBeDisabled();
    await act(() => vi.advanceTimersByTimeAsync(200));
    expect(screen.getByRole("alert")).toHaveTextContent("10초 안에");
    expect(relaySwitch()).toBeEnabled();
    expect(relaySwitch()).not.toBeChecked();
  });

  it("바뀐 뒤에는 10초가 지나도 오류가 나지 않고, 다시 보내면 앞 오류를 지운다", async () => {
    vi.useFakeTimers();
    const { update } = setup(status({ relay: "off" }));
    fireEvent.click(relaySwitch());
    update(status({ relay: "on" }));
    await act(() => vi.advanceTimersByTimeAsync(11_000));
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();

    update(status({ relay: "off" }));
    fireEvent.click(relaySwitch());
    await act(() => vi.advanceTimersByTimeAsync(11_000));
    expect(screen.getByRole("alert")).toBeInTheDocument();
    fireEvent.click(relaySwitch());
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
