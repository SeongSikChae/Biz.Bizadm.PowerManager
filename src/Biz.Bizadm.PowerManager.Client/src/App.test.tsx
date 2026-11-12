import { render, screen } from "@testing-library/react";
import { Provider } from "react-redux";
import App from "@/App";
import { store } from "@/app/store";

describe("App", () => {
  it("renders the title", () => {
    render(
      <Provider store={store}>
        <App />
      </Provider>,
    );

    expect(screen.getByRole("heading", { name: "Biz.Bizadm.PowerManager" })).toBeInTheDocument();
  });
});
