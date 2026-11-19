import type { ReactNode } from "react";
import { Plug, Zap } from "lucide-react";

/**
 * 관리 화면 앱 틀: 왼쪽 사이드바 + 오른쪽 본문.
 * 바깥이 flex-wrap이고 본문이 큰 grow와 basis를 가져, 둘이 한 줄에 안 들어가는 좁은 폭에서는 사이드바가 본문 위로 쌓인다.
 * 본문의 min-w-0은 넓은 표가 페이지 가로 스크롤을 만들지 않게 한다(표는 자기 상자 안에서 스크롤).
 */
export function AppShell({ children }: { children: ReactNode }) {
  return (
    <div className="flex min-h-svh flex-wrap bg-background text-foreground">
      <nav
        aria-label="주 메뉴"
        className="flex shrink grow basis-56 flex-col gap-1 border-r border-b border-sidebar-border bg-sidebar px-3 py-4 text-sidebar-foreground"
      >
        <div className="flex items-center gap-2.5 px-2.5 pt-2 pb-5">
          <span className="flex size-8 items-center justify-center rounded-lg bg-sidebar-primary text-sidebar-primary-foreground">
            <Zap aria-hidden="true" className="size-4" />
          </span>
          <span className="flex flex-col leading-tight">
            <span className="font-semibold">PowerManager</span>
            <span className="text-xs text-muted-foreground">관리 콘솔</span>
          </span>
        </div>
        <a
          href="/"
          aria-current="page"
          className="flex min-h-10 items-center gap-2.5 rounded-lg bg-sidebar-accent px-2.5 font-medium text-sidebar-accent-foreground outline-none focus-visible:ring-3 focus-visible:ring-sidebar-ring/50"
        >
          <Plug aria-hidden="true" className="size-4" />
          장비
        </a>
      </nav>
      <main className="flex min-w-0 shrink grow-[999] basis-[35rem] flex-col gap-5 p-6 md:px-10 md:py-8">{children}</main>
    </div>
  );
}
