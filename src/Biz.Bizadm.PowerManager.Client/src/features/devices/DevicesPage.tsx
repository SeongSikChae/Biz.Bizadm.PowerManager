import { useMemo, useState, type ReactNode } from "react";
import type { FetchBaseQueryError } from "@reduxjs/toolkit/query";
import type { SerializedError } from "@reduxjs/toolkit";
import { ArrowDown, ArrowUp, ArrowUpDown, ChevronLeft, ChevronRight, CircleAlert, Plug, RefreshCw, Search } from "lucide-react";
import { Alert, AlertDescription, AlertTitle } from "@/components/ui/alert";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { useGetDevicesQuery } from "./devicesApi";
import {
  DEFAULT_SORT,
  clampPage,
  displayValue,
  filterDevices,
  formatDateTime,
  formatRelative,
  nextSort,
  pageCount,
  pageSlice,
  sortDevices,
  type Sort,
  type SortKey,
} from "./deviceList";

function describeError(error: FetchBaseQueryError | SerializedError): string {
  if ("status" in error) {
    if (typeof error.status === "number") return `서버 응답: ${error.status}`;
    if (error.status === "FETCH_ERROR") return "서버에 연결할 수 없습니다.";
    return "서버 응답을 해석하지 못했습니다.";
  }
  return error.message ?? "알 수 없는 오류";
}

function SortableHead({ label, sortKey, sort, onSort }: { label: string; sortKey: SortKey; sort: Sort; onSort: (key: SortKey) => void }) {
  const active = sort.key === sortKey;
  const Icon = !active ? ArrowUpDown : sort.dir === "desc" ? ArrowDown : ArrowUp;
  return (
    <TableHead aria-sort={active ? (sort.dir === "desc" ? "descending" : "ascending") : "none"}>
      <button
        type="button"
        onClick={() => onSort(sortKey)}
        className={`-mx-1 inline-flex min-h-8 items-center gap-1 rounded-md px-1 outline-none hover:text-foreground focus-visible:ring-3 focus-visible:ring-ring/50 ${active ? "text-foreground" : "text-muted-foreground"}`}
      >
        {label}
        <Icon aria-hidden="true" className="size-3.5" />
      </button>
    </TableHead>
  );
}

function ListSkeleton() {
  return (
    <div role="status" aria-label="장비 목록을 불러오는 중" className="rounded-lg border">
      <div className="h-10 border-b" />
      {[0, 1, 2, 3, 4].map((i) => (
        <div key={i} className="flex h-14 items-center gap-4 border-b px-3 last:border-0">
          <Skeleton className="h-3 w-32" />
          <Skeleton className="h-3 w-20" />
          <Skeleton className="h-3 w-24" />
          <Skeleton className="h-3 w-20" />
          <Skeleton className="ml-auto h-3 w-28" />
        </div>
      ))}
    </div>
  );
}

function Notice({ icon, title, children }: { icon: ReactNode; title: string; children?: ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-3 rounded-lg border border-dashed px-6 py-16 text-center">
      <span className="flex size-12 items-center justify-center rounded-xl bg-muted text-muted-foreground">{icon}</span>
      <p className="text-base font-semibold">{title}</p>
      {children}
    </div>
  );
}

/**
 * 장비 목록. 열 때마다 다시 읽는다(다른 화면에서 장비명을 바꿨을 수 있다). 화면은 서버에서 받은 목록이 있는지(빈 목록 포함)로 정한다.
 * 받은 목록이 없으면 스켈레톤 또는 목록 자리 오류, 있으면 기존 화면을 유지하고 오류는 그 위에 보인다.
 * 오류 판정은 `isError`가 아니라 `error`로 한다: `isError`는 다시 불러오기를 시작하면 false가 되지만 `error`는 성공할 때까지 남는다.
 */
export function DevicesPage({ getNow = Date.now }: { getNow?: () => number }) {
  const { data, isFetching, error, refetch } = useGetDevicesQuery(undefined, { refetchOnMountOrArgChange: true });
  const [query, setQuery] = useState("");
  const [sort, setSort] = useState<Sort>(DEFAULT_SORT);
  const [page, setPage] = useState(1);

  const visible = useMemo(() => (data ? sortDevices(filterDevices(data, query), sort) : []), [data, query, sort]);
  const totalPages = pageCount(visible.length);
  const currentPage = clampPage(page, visible.length);
  // 목록이 줄어 마지막 페이지로 보정했으면 상태도 맞춘다. 그러지 않으면 목록이 다시 늘 때 보던 페이지가 바뀐다.
  if (data !== undefined && page !== currentPage) setPage(currentPage);
  const rows = pageSlice(visible, currentPage);
  const now = getNow();

  const changeQuery = (value: string) => {
    setQuery(value);
    setPage(1);
  };
  const changeSort = (key: SortKey) => {
    setSort((current) => nextSort(current, key));
    setPage(1);
  };
  const retryButton = (
    <Button variant="outline" onClick={() => refetch()} disabled={isFetching}>
      다시 시도
    </Button>
  );

  return (
    <>
      <header className="flex flex-wrap items-end justify-between gap-4">
        <div className="flex flex-col gap-1">
          <h1 className="text-2xl font-semibold tracking-tight">장비</h1>
          {data !== undefined && <p className="text-muted-foreground">서버에 등록된 장비 {data.length}대</p>}
        </div>
        <Button variant="outline" size="lg" onClick={() => refetch()} disabled={isFetching}>
          <RefreshCw aria-hidden="true" />
          새로고침
        </Button>
      </header>

      {data === undefined ? (
        error !== undefined && !isFetching ? (
          <Alert variant="destructive" className="py-6">
            <CircleAlert aria-hidden="true" />
            <AlertTitle>장비 목록을 불러오지 못했습니다</AlertTitle>
            <AlertDescription className="flex flex-col items-start gap-3">
              <span>{describeError(error)}</span>
              {retryButton}
            </AlertDescription>
          </Alert>
        ) : (
          <ListSkeleton />
        )
      ) : (
        <>
          {error !== undefined && (
            <Alert variant="destructive">
              <CircleAlert aria-hidden="true" />
              <AlertTitle>장비 목록을 다시 불러오지 못했습니다</AlertTitle>
              <AlertDescription className="flex flex-wrap items-center gap-3">
                <span>{describeError(error)} — 이전에 받은 목록을 보여 주고 있습니다.</span>
                {retryButton}
              </AlertDescription>
            </Alert>
          )}

          {data.length === 0 ? (
            <Notice icon={<Plug aria-hidden="true" className="size-5" />} title="등록된 장비가 없습니다">
              <p className="max-w-sm text-muted-foreground">장비 설정 도구로 장비를 등록하면 이 목록에 나타납니다.</p>
            </Notice>
          ) : (
            <>
              <div className="relative flex w-full max-w-sm items-center">
                <label htmlFor="device-search" className="sr-only">
                  장비 검색
                </label>
                <Search aria-hidden="true" className="pointer-events-none absolute left-2.5 size-4 text-muted-foreground" />
                <Input
                  id="device-search"
                  type="search"
                  value={query}
                  onChange={(e) => changeQuery(e.target.value)}
                  placeholder="이름, MAC, 모델, 사용자로 검색"
                  className="h-9 pl-8"
                />
              </div>

              {visible.length === 0 ? (
                <Notice icon={<Search aria-hidden="true" className="size-5" />} title="검색 결과 없음">
                  <p className="text-muted-foreground">“{query}”에 맞는 장비가 없습니다.</p>
                  <Button variant="outline" onClick={() => changeQuery("")}>
                    검색어 지우기
                  </Button>
                </Notice>
              ) : (
                <div className="rounded-lg border">
                  <Table aria-label="장비 목록" className="min-w-[840px]">
                    <TableHeader>
                      <TableRow className="hover:bg-transparent">
                        <TableHead className="text-muted-foreground">이름</TableHead>
                        <TableHead className="text-muted-foreground">MAC</TableHead>
                        <TableHead className="text-muted-foreground">모델</TableHead>
                        <TableHead className="text-muted-foreground">제조사</TableHead>
                        <TableHead className="text-muted-foreground">사용자</TableHead>
                        <SortableHead label="등록" sortKey="registeredAt" sort={sort} onSort={changeSort} />
                        <SortableHead label="마지막 요청" sortKey="lastRequestedAt" sort={sort} onSort={changeSort} />
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {rows.map((d) => (
                        <TableRow key={d.mac}>
                          <TableCell className="font-medium">{displayValue(d.name)}</TableCell>
                          <TableCell className="font-mono font-medium">{d.mac}</TableCell>
                          <TableCell>{displayValue(d.modelId)}</TableCell>
                          <TableCell className="text-muted-foreground">{displayValue(d.company)}</TableCell>
                          <TableCell>{displayValue(d.userId)}</TableCell>
                          <TableCell className="text-muted-foreground tabular-nums">{formatDateTime(d.registeredAt)}</TableCell>
                          <TableCell>
                            <div className="flex flex-col leading-snug">
                              <span>{formatRelative(d.lastRequestedAt, now)}</span>
                              <span className="text-xs text-muted-foreground tabular-nums">{formatDateTime(d.lastRequestedAt)}</span>
                            </div>
                          </TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                  {totalPages > 1 && (
                    <nav aria-label="페이지" className="flex items-center justify-end gap-2 border-t px-3 py-2 text-sm text-muted-foreground">
                      <span>
                        {currentPage} / {totalPages} 페이지
                      </span>
                      <Button
                        variant="outline"
                        size="icon"
                        aria-label="이전 페이지"
                        disabled={currentPage <= 1}
                        onClick={() => setPage(currentPage - 1)}
                      >
                        <ChevronLeft aria-hidden="true" />
                      </Button>
                      <Button
                        variant="outline"
                        size="icon"
                        aria-label="다음 페이지"
                        disabled={currentPage >= totalPages}
                        onClick={() => setPage(currentPage + 1)}
                      >
                        <ChevronRight aria-hidden="true" />
                      </Button>
                    </nav>
                  )}
                </div>
              )}
            </>
          )}
        </>
      )}
    </>
  );
}
