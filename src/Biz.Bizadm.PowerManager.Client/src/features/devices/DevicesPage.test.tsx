import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Provider } from "react-redux";
import type { Mock } from "vitest";
import { makeStore } from "@/app/store";
import { DevicesPage } from "./DevicesPage";
import type { Device } from "./devicesApi";
import { formatDateTime } from "./deviceList";

const NOW = Date.parse("2026-11-19T06:00:00Z");

function device(i: number, overrides: Partial<Device> = {}): Device {
  return {
    mac: "3a00" + i.toString(16).padStart(8, "0"),
    name: "",
    userId: "user-01",
    modelId: "M130-W",
    company: "DAWONDNS",
    registeredAt: new Date(NOW - 10 * 86_400_000 + i * 60_000).toISOString(),
    lastRequestedAt: new Date(NOW - (i + 1) * 60_000).toISOString(),
    ...overrides,
  };
}

function devices(count: number): Device[] {
  return Array.from({ length: count }, (_, i) => device(i));
}

function json(body: unknown, status = 200): Response {
  return new Response(JSON.stringify(body), { status, headers: { "Content-Type": "application/json" } });
}

function deferred() {
  let resolve!: (response: Response) => void;
  const promise = new Promise<Response>((r) => (resolve = r));
  return { promise, resolve };
}

let fetchMock: Mock;

beforeEach(() => {
  fetchMock = vi.fn();
  vi.stubGlobal("fetch", fetchMock);
});

afterEach(() => {
  vi.unstubAllGlobals();
});

function respond(...responses: (Response | Promise<Response>)[]) {
  for (const r of responses) fetchMock.mockImplementationOnce(() => Promise.resolve(r));
}

function renderPage() {
  return render(
    <Provider store={makeStore()}>
      <DevicesPage getNow={() => NOW} />
    </Provider>,
  );
}

const refreshButton = () => screen.getByRole("button", { name: "새로고침" });
const pageText = () => screen.getByRole("navigation", { name: "페이지" }).textContent;
const sortHeader = (name: string) => screen.getByRole("columnheader", { name });
const macsOnScreen = () =>
  screen
    .getAllByRole("row")
    .slice(1)
    .map((row) => within(row).getAllByRole("cell")[1].textContent);

describe("DevicesPage", () => {
  it("처음 불러오는 동안 스켈레톤을 보이고 등록 대수를 숨긴다", async () => {
    const first = deferred();
    respond(first.promise);
    renderPage();

    expect(await screen.findByRole("status", { name: "장비 목록을 불러오는 중" })).toBeInTheDocument();
    expect(screen.queryByText(/서버에 등록된 장비/)).not.toBeInTheDocument();
    expect(refreshButton()).toBeDisabled();

    first.resolve(json(devices(2)));
    expect(await screen.findByText("서버에 등록된 장비 2대")).toBeInTheDocument();
    expect(screen.queryByRole("status", { name: "장비 목록을 불러오는 중" })).not.toBeInTheDocument();
  });

  it("열 순서, MAC 원본, 빈 값 —, 등록·마지막 요청 시각을 표시한다", async () => {
    const withBlank = device(0, { mac: "5e0000000201", modelId: "", company: "", userId: "", lastRequestedAt: new Date(NOW - 5 * 60_000).toISOString() });
    respond(json([withBlank, device(10, { name: "거실 TV" })]));
    renderPage();

    await screen.findByRole("table", { name: "장비 목록" });
    expect(screen.getAllByRole("columnheader").map((h) => h.textContent)).toEqual(["이름", "MAC", "모델", "제조사", "사용자", "등록", "마지막 요청"]);
    const cells = within(screen.getAllByRole("row")[1]).getAllByRole("cell");
    expect(cells[0]).toHaveTextContent(/^—$/);
    expect(cells[1]).toHaveTextContent(/^5e0000000201$/);
    expect(cells[1]).toHaveClass("font-mono");
    expect(cells[2]).toHaveTextContent(/^—$/);
    expect(cells[3]).toHaveTextContent(/^—$/);
    expect(cells[4]).toHaveTextContent(/^—$/);
    expect(cells[5]).toHaveTextContent(formatDateTime(withBlank.registeredAt));
    expect(cells[6]).toHaveTextContent("5분 전");
    expect(cells[6]).toHaveTextContent(formatDateTime(withBlank.lastRequestedAt));
  });

  it("장비명을 보이고 장비명으로 검색한다", async () => {
    const user = userEvent.setup();
    respond(json([device(0), device(1, { name: "거실 TV" })]));
    renderPage();
    await screen.findByRole("table", { name: "장비 목록" });
    expect(screen.getByRole("cell", { name: "거실 TV" })).toBeInTheDocument();

    const search = screen.getByPlaceholderText("이름, MAC, 모델, 사용자로 검색");
    await user.type(search, "거실 tv");
    expect(macsOnScreen()).toEqual([device(1).mac]);
  });

  it("다시 열면 목록을 다시 읽어 바뀐 장비명을 보인다", async () => {
    const store = makeStore();
    const ui = (
      <Provider store={store}>
        <DevicesPage getNow={() => NOW} />
      </Provider>
    );
    respond(json([device(0)]), json([device(0, { name: "거실 TV" })]));
    const first = render(ui);
    await screen.findByRole("table", { name: "장비 목록" });
    first.unmount();

    render(ui);
    expect(await screen.findByRole("cell", { name: "거실 TV" })).toBeInTheDocument();
    expect(fetchMock).toHaveBeenCalledTimes(2);
  });

  it("등록된 장비가 없으면 빈 목록 안내와 0대를 보인다", async () => {
    respond(json([]));
    renderPage();

    expect(await screen.findByText("등록된 장비가 없습니다")).toBeInTheDocument();
    expect(screen.getByText("서버에 등록된 장비 0대")).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
  });

  it("처음 조회에 실패하면 목록 자리에 오류를, 다시 시도 중에는 스켈레톤을 보인다", async () => {
    const user = userEvent.setup();
    const retry = deferred();
    respond(json({ title: "error" }, 503), retry.promise);
    renderPage();

    expect(await screen.findByText("장비 목록을 불러오지 못했습니다")).toBeInTheDocument();
    expect(screen.getByText("서버 응답: 503")).toBeInTheDocument();
    expect(screen.queryByText(/서버에 등록된 장비/)).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "다시 시도" }));
    expect(await screen.findByRole("status", { name: "장비 목록을 불러오는 중" })).toBeInTheDocument();
    expect(screen.queryByText("장비 목록을 불러오지 못했습니다")).not.toBeInTheDocument();

    retry.resolve(json(devices(1)));
    expect(await screen.findByText("서버에 등록된 장비 1대")).toBeInTheDocument();
  });

  it("목록이 있을 때 새로고침하는 동안 기존 표와 대수를 두고 버튼을 비활성화한다", async () => {
    const user = userEvent.setup();
    const refresh = deferred();
    respond(json(devices(2)), refresh.promise);
    renderPage();
    await screen.findByText("서버에 등록된 장비 2대");

    await user.click(refreshButton());
    await waitFor(() => expect(refreshButton()).toBeDisabled());
    expect(screen.getByRole("table", { name: "장비 목록" })).toBeInTheDocument();
    expect(screen.getByText("서버에 등록된 장비 2대")).toBeInTheDocument();
    expect(screen.queryByRole("status", { name: "장비 목록을 불러오는 중" })).not.toBeInTheDocument();

    refresh.resolve(json(devices(3)));
    expect(await screen.findByText("서버에 등록된 장비 3대")).toBeInTheDocument();
    expect(refreshButton()).toBeEnabled();
  });

  it("새로고침 실패는 표 위 오류로 보이고, 다시 불러오는 동안 남았다가 성공하면 사라진다", async () => {
    const user = userEvent.setup();
    const retry = deferred();
    respond(json(devices(2)), json({}, 500), retry.promise);
    renderPage();
    await screen.findByText("서버에 등록된 장비 2대");

    await user.click(refreshButton());
    expect(await screen.findByText("장비 목록을 다시 불러오지 못했습니다")).toBeInTheDocument();
    expect(screen.getByRole("table", { name: "장비 목록" })).toBeInTheDocument();
    expect(screen.getByText("서버에 등록된 장비 2대")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "다시 시도" }));
    await waitFor(() => expect(screen.getByRole("button", { name: "다시 시도" })).toBeDisabled());
    expect(screen.getByText("장비 목록을 다시 불러오지 못했습니다")).toBeInTheDocument();
    expect(refreshButton()).toBeDisabled();

    retry.resolve(json(devices(2)));
    await waitFor(() => expect(screen.queryByText("장비 목록을 다시 불러오지 못했습니다")).not.toBeInTheDocument());
    expect(refreshButton()).toBeEnabled();
  });

  it("다시 불러온 뒤에도 검색어·정렬·페이지를 유지하고, 페이지가 넘치면 마지막 페이지로 간다", async () => {
    const user = userEvent.setup();
    respond(json(devices(120)), json({}, 500), json(devices(60)), json(devices(120)));
    renderPage();
    await screen.findByText("서버에 등록된 장비 120대");

    await user.type(screen.getByLabelText("장비 검색"), "3a00");
    await user.click(within(sortHeader("등록")).getByRole("button"));
    await user.click(screen.getByRole("button", { name: "다음 페이지" }));
    await user.click(screen.getByRole("button", { name: "다음 페이지" }));
    expect(pageText()).toContain("3 / 3 페이지");

    await user.click(refreshButton());
    await screen.findByText("장비 목록을 다시 불러오지 못했습니다");
    expect(pageText()).toContain("3 / 3 페이지");
    expect(screen.getByLabelText("장비 검색")).toHaveValue("3a00");
    expect(sortHeader("등록")).toHaveAttribute("aria-sort", "descending");

    await user.click(refreshButton());
    await screen.findByText("서버에 등록된 장비 60대");
    expect(pageText()).toContain("2 / 2 페이지");
    expect(screen.getByLabelText("장비 검색")).toHaveValue("3a00");
    expect(sortHeader("등록")).toHaveAttribute("aria-sort", "descending");

    // 보정한 페이지(2)가 현재 페이지다. 목록이 다시 늘어도 예전 페이지(3)로 돌아가지 않는다.
    await user.click(refreshButton());
    await screen.findByText("서버에 등록된 장비 120대");
    expect(pageText()).toContain("2 / 3 페이지");
  });

  it("열 머리로 정렬하고 aria-sort로 표시한다", async () => {
    const user = userEvent.setup();
    respond(json(devices(3)));
    renderPage();
    await screen.findByRole("table", { name: "장비 목록" });

    expect(sortHeader("마지막 요청")).toHaveAttribute("aria-sort", "descending");
    expect(sortHeader("등록")).toHaveAttribute("aria-sort", "none");
    expect(macsOnScreen()).toEqual(["3a0000000000", "3a0000000001", "3a0000000002"]);

    await user.click(within(sortHeader("등록")).getByRole("button"));
    expect(sortHeader("등록")).toHaveAttribute("aria-sort", "descending");
    expect(sortHeader("마지막 요청")).toHaveAttribute("aria-sort", "none");
    expect(macsOnScreen()).toEqual(["3a0000000002", "3a0000000001", "3a0000000000"]);

    await user.click(within(sortHeader("등록")).getByRole("button"));
    expect(sortHeader("등록")).toHaveAttribute("aria-sort", "ascending");
    expect(macsOnScreen()).toEqual(["3a0000000000", "3a0000000001", "3a0000000002"]);
  });

  it("검색으로 거르고 대수는 전체를 유지하며, 결과가 없으면 지우기 버튼을 보인다", async () => {
    const user = userEvent.setup();
    respond(json([device(0), device(1, { mac: "5e0000000201", modelId: "B540" })]));
    renderPage();
    await screen.findByRole("table", { name: "장비 목록" });

    const search = screen.getByLabelText("장비 검색");
    await user.type(search, "b540");
    expect(macsOnScreen()).toEqual(["5e0000000201"]);
    expect(screen.getByText("서버에 등록된 장비 2대")).toBeInTheDocument();

    await user.clear(search);
    await user.type(search, "3A:00:00");
    expect(macsOnScreen()).toEqual(["3a0000000000"]);

    await user.clear(search);
    await user.type(search, "zzz");
    expect(screen.getByText("검색 결과 없음")).toBeInTheDocument();
    expect(screen.queryByRole("table")).not.toBeInTheDocument();
    expect(screen.getByText("서버에 등록된 장비 2대")).toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: "검색어 지우기" }));
    expect(search).toHaveValue("");
    expect(macsOnScreen()).toHaveLength(2);
  });

  it("50대씩 페이지를 나누고, 검색어나 정렬이 바뀌면 첫 페이지로 간다", async () => {
    const user = userEvent.setup();
    respond(json(devices(51)));
    renderPage();
    await screen.findByRole("table", { name: "장비 목록" });

    expect(macsOnScreen()).toHaveLength(50);
    expect(pageText()).toContain("1 / 2 페이지");
    expect(screen.getByRole("button", { name: "이전 페이지" })).toBeDisabled();

    await user.click(screen.getByRole("button", { name: "다음 페이지" }));
    expect(pageText()).toContain("2 / 2 페이지");
    expect(macsOnScreen()).toHaveLength(1);
    expect(screen.getByRole("button", { name: "다음 페이지" })).toBeDisabled();

    await user.click(within(sortHeader("등록")).getByRole("button"));
    expect(pageText()).toContain("1 / 2 페이지");

    await user.click(screen.getByRole("button", { name: "다음 페이지" }));
    await user.type(screen.getByLabelText("장비 검색"), "3a00");
    expect(pageText()).toContain("1 / 2 페이지");
  });

  it("50대 이하면 페이지 이동을 보이지 않는다", async () => {
    respond(json(devices(50)));
    renderPage();
    await screen.findByRole("table", { name: "장비 목록" });

    expect(screen.queryByRole("navigation", { name: "페이지" })).not.toBeInTheDocument();
  });

  it("키보드로 새로고침과 정렬을 조작할 수 있다", async () => {
    const user = userEvent.setup();
    respond(json(devices(2)), json(devices(2)));
    renderPage();
    await screen.findByRole("table", { name: "장비 목록" });

    await user.tab();
    expect(refreshButton()).toHaveFocus();
    await user.keyboard("{Enter}");
    await waitFor(() => expect(fetchMock).toHaveBeenCalledTimes(2));

    await user.tab();
    expect(screen.getByLabelText("장비 검색")).toHaveFocus();
    await user.tab();
    expect(within(sortHeader("등록")).getByRole("button")).toHaveFocus();
    await user.keyboard("{Enter}");
    expect(sortHeader("등록")).toHaveAttribute("aria-sort", "descending");
    await user.tab();
    expect(within(sortHeader("마지막 요청")).getByRole("button")).toHaveFocus();
    await user.keyboard(" ");
    expect(sortHeader("마지막 요청")).toHaveAttribute("aria-sort", "descending");
  });
});
