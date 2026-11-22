import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import { BrowserRouter } from "react-router";
import App from "@/App";
import { makeStore } from "@/app/store";
import { FakeEventSource } from "@/test/setup";

const MAC = "3a0000000001";

beforeEach(() => {
  vi.stubGlobal("fetch", vi.fn(() => Promise.resolve(new Response("[]", { headers: { "Content-Type": "application/json" } }))));
});

afterEach(() => {
  vi.unstubAllGlobals();
  window.history.replaceState(null, "", "/");
});

/** 주소를 직접 연 것과 같다. */
function openAt(path: string) {
  window.history.replaceState(null, "", path);
  return render(
    <Provider store={makeStore()}>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </Provider>,
  );
}

const menu = (name: string) => screen.getByRole("link", { name });

function expectCurrent(name: "대시보드" | "장비") {
  expect(menu(name)).toHaveAttribute("aria-current", "page");
  expect(menu(name === "장비" ? "대시보드" : "장비")).not.toHaveAttribute("aria-current");
}

describe("App", () => {
  it("메뉴는 대시보드, 장비 순서다", async () => {
    openAt("/");
    const nav = screen.getByRole("navigation", { name: "주 메뉴" });
    expect(nav).toHaveTextContent("PowerManager");
    expect([...nav.querySelectorAll("a")].map((a) => a.textContent)).toEqual(["대시보드", "장비"]);
    expect(await screen.findByRole("heading", { level: 1, name: "대시보드" })).toBeInTheDocument();
  });

  it("/ 는 대시보드이고 대시보드 메뉴가 현재다", async () => {
    openAt("/");
    expect(screen.getByRole("main")).toContainElement(await screen.findByRole("heading", { level: 1, name: "대시보드" }));
    expectCurrent("대시보드");
  });

  it("/devices 는 장비 목록이고 장비 메뉴가 현재다", async () => {
    openAt("/devices");
    expect(screen.getByRole("main")).toContainElement(screen.getByRole("heading", { level: 1, name: "장비" }));
    expect(screen.getByRole("button", { name: "새로고침" })).toBeInTheDocument();
    expect(await screen.findByText("서버에 등록된 장비 0대")).toBeInTheDocument();
    expectCurrent("장비");
  });

  it("/devices/{mac} 은 장비 제어 화면이고 대시보드 메뉴가 현재다", async () => {
    openAt(`/devices/${MAC}`);
    expectCurrent("대시보드");
    expect(screen.queryByRole("button", { name: "새로고침" })).not.toBeInTheDocument();
    expect(await screen.findByText("장비를 찾을 수 없습니다")).toBeInTheDocument();
  });

  it("메뉴로 옮기고 뒤로 가기로 돌아온다", async () => {
    openAt("/");
    await userEvent.click(menu("장비"));
    expect(window.location.pathname).toBe("/devices");
    expect(screen.getByRole("heading", { level: 1, name: "장비" })).toBeInTheDocument();

    await act(async () => {
      window.history.back();
      await new Promise((resolve) => window.addEventListener("popstate", resolve, { once: true }));
    });
    expect(window.location.pathname).toBe("/");
    expect(await screen.findByRole("heading", { level: 1, name: "대시보드" })).toBeInTheDocument();
    expectCurrent("대시보드");
  });

  it("화면을 옮겨도 상태 스트림은 하나다", async () => {
    openAt("/");
    await userEvent.click(menu("장비"));
    await userEvent.click(menu("대시보드"));
    expect(FakeEventSource.instances).toHaveLength(1);
    expect(FakeEventSource.latest().url).toBe("/api/admin/devices/status/stream");
  });
});
