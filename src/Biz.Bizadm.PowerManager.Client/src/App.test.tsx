import { render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import App from "@/App";
import { makeStore } from "@/app/store";

beforeEach(() => {
  vi.stubGlobal("fetch", vi.fn(() => Promise.resolve(new Response("[]", { headers: { "Content-Type": "application/json" } }))));
});

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("App", () => {
  it("사이드바와 본문으로 된 앱 틀 안에 장비 목록을 보인다", async () => {
    render(
      <Provider store={makeStore()}>
        <App />
      </Provider>,
    );

    const nav = screen.getByRole("navigation", { name: "주 메뉴" });
    expect(nav).toHaveTextContent("PowerManager");
    expect(screen.getByRole("link", { name: "장비" })).toHaveAttribute("aria-current", "page");
    expect(screen.getByRole("main")).toContainElement(screen.getByRole("heading", { level: 1, name: "장비" }));
    expect(screen.getByRole("button", { name: "새로고침" })).toBeInTheDocument();
    expect(await screen.findByText("서버에 등록된 장비 0대")).toBeInTheDocument();
  });
});
