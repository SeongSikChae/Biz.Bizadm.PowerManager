import { createApi, fetchBaseQuery } from "@reduxjs/toolkit/query/react";
import type { DeviceSettings, DeviceStatus, ReportCondition, Schedule, StandbyCutoff } from "./types";

export const deviceStatusApi = createApi({
  reducerPath: "deviceStatusApi",
  // jsdom 테스트에서 상대 URL로는 Request를 만들 수 없어 현재 origin 기준 절대 주소로 둔다.
  baseQuery: fetchBaseQuery({ baseUrl: `${window.location.origin}/api/admin/devices` }),
  endpoints: (build) => ({
    /** 상태 스트림이 전체 상태를 보내기 전의 첫 화면에 쓴다. 그 뒤 상태는 slice에 있다. */
    getStatuses: build.query<DeviceStatus[], void>({ query: () => "/status" }),
    /** 화면을 열 때마다 새로 읽는다(다른 화면에서 보낸 값이 있을 수 있다). */
    getSettings: build.query<DeviceSettings, string>({ query: (mac) => `/${mac}/settings`, keepUnusedDataFor: 0 }),
    setRelay: build.mutation<void, { mac: string; on: boolean }>({
      query: ({ mac, on }) => ({ url: `/${mac}/relay`, method: "POST", body: { on } }),
    }),
    powerCycle: build.mutation<void, { mac: string; seconds: number }>({
      query: ({ mac, seconds }) => ({ url: `/${mac}/power-cycle`, method: "POST", body: { seconds } }),
    }),
    reboot: build.mutation<void, string>({
      query: (mac) => ({ url: `/${mac}/reboot`, method: "POST" }),
    }),
    saveReportCondition: build.mutation<void, { mac: string; value: ReportCondition }>({
      query: ({ mac, value }) => ({ url: `/${mac}/report-condition`, method: "PUT", body: value }),
    }),
    saveStandbyCutoff: build.mutation<void, { mac: string; value: StandbyCutoff }>({
      query: ({ mac, value }) => ({ url: `/${mac}/standby-cutoff`, method: "PUT", body: value }),
    }),
    saveSchedule: build.mutation<void, { mac: string; value: Schedule }>({
      query: ({ mac, value }) => ({ url: `/${mac}/schedule`, method: "PUT", body: value }),
    }),
    deleteSchedule: build.mutation<void, string>({
      query: (mac) => ({ url: `/${mac}/schedule`, method: "DELETE" }),
    }),
  }),
});

export const {
  useGetStatusesQuery,
  useGetSettingsQuery,
  useSetRelayMutation,
  usePowerCycleMutation,
  useRebootMutation,
  useSaveReportConditionMutation,
  useSaveStandbyCutoffMutation,
  useSaveScheduleMutation,
  useDeleteScheduleMutation,
} = deviceStatusApi;
