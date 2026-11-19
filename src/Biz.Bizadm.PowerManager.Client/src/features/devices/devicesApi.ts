import { createApi, fetchBaseQuery } from "@reduxjs/toolkit/query/react";

/** 서버 `GET /api/admin/devices`의 원소. 시각은 오프셋이 붙은 ISO 8601 문자열이다. */
export interface Device {
  mac: string;
  userId: string;
  modelId: string;
  company: string;
  registeredAt: string;
  lastRequestedAt: string;
}

export const devicesApi = createApi({
  reducerPath: "devicesApi",
  // jsdom 테스트에서 상대 URL로는 Request를 만들 수 없어 현재 origin 기준 절대 주소로 둔다.
  baseQuery: fetchBaseQuery({ baseUrl: `${window.location.origin}/api/admin` }),
  endpoints: (build) => ({
    getDevices: build.query<Device[], void>({
      query: () => "devices",
    }),
  }),
});

export const { useGetDevicesQuery } = devicesApi;
