import { configureStore } from "@reduxjs/toolkit";
import { devicesApi } from "@/features/devices/devicesApi";

/** 테스트는 매번 새 store를 만들어 RTK Query 캐시가 테스트 사이에 섞이지 않게 한다. */
export function makeStore() {
  return configureStore({
    reducer: {
      [devicesApi.reducerPath]: devicesApi.reducer,
    },
    middleware: (getDefaultMiddleware) => getDefaultMiddleware().concat(devicesApi.middleware),
  });
}

export const store = makeStore();

export type AppStore = ReturnType<typeof makeStore>;
export type RootState = ReturnType<AppStore["getState"]>;
export type AppDispatch = AppStore["dispatch"];
