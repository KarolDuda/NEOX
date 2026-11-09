import { call } from './api.js';
export const S = { info: null, catalog: null, nav: null };
export async function refreshInfo() { S.info = await call('app_info'); S.nav && S.nav.updateBars(); return S.info; }
export async function getCatalog() { return S.catalog || (S.catalog = await call('catalog_list')); }
