// POINTS and PAGE are injected by scripts/build.mjs from tracked public source.
const POINTS = /* POINTS */ [];
const PAGE = /* PAGE */ '';
const counts = new Map();
const json = (value, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'content-type': 'application/json; charset=utf-8', 'cache-control': 'no-store', 'x-content-type-options': 'nosniff' } });
function limit(request, kind, max) {
  const minute = Math.floor(Date.now() / 60000);
  const key = kind + ':' + (request.headers.get('cf-connecting-ip') || 'local');
  const previous = counts.get(key);
  const entry = previous?.minute === minute ? previous : { minute, count: 0 };
  counts.set(key, entry);
  if (counts.size > 5000) for (const [id, item] of counts) if (item.minute !== minute) counts.delete(id);
  return ++entry.count <= max;
}
function point(id) { return POINTS.find(p => p.id === String(id)); }
async function upstream(url, options = {}) {
  const response = await fetch(url, { ...options, signal: AbortSignal.timeout(25000) });
  if (!response.ok) throw new Error('上游服务暂时不可用，请稍后重试。');
  return response.json();
}
async function plan(start, end, mode, env) {
  const a = point(start), b = point(end);
  if (!a || !b || a.id === b.id) return json({ error: '请选择不同的校园起点和终点。' }, 400);
  if (!['walking', 'bicycling', 'driving'].includes(mode)) return json({ error: '不支持的出行方式。' }, 400);
  if (!env.AMAP_API_KEY) return json({ error: '公共路线服务暂时不可用。' }, 503);
  const path = mode === 'bicycling' ? '/v4/direction/bicycling' : '/v3/direction/' + mode;
  const url = new URL('https://restapi.amap.com' + path);
  url.search = new URLSearchParams({ key: env.AMAP_API_KEY, origin: a.coordinates.join(','), destination: b.coordinates.join(','), extensions: 'all' });
  const data = await upstream(url);
  const route = mode === 'bicycling' ? data.data?.paths?.[0] : data.route?.paths?.[0];
  if (!route) return json({ error: '高德暂时没有返回可用路线，请稍后重试。' }, 502);
  const coordinates = (route.steps || []).flatMap(s => (s.polyline || '').split(';').filter(Boolean).map(p => p.split(',').map(Number)));
  if (coordinates.length < 2) return json({ error: '路线数据不完整，请稍后重试。' }, 502);
  return json({ start: a, end: b, mode, distance: Number(route.distance), duration: Number(route.duration), coordinates, steps: (route.steps || []).map(s => s.instruction).filter(Boolean) });
}
async function nlp(request, env) {
  if (!limit(request, 'nlp', 6)) return json({ error: '请求较多，请稍等一分钟再试。' }, 429);
  if (Number(request.headers.get('content-length') || 0) > 2048) return json({ error: '路线描述过长。' }, 413);
  const raw = await request.text();
  if (raw.length > 2048) return json({ error: '路线描述过长。' }, 413);
  let input;
  try { input = JSON.parse(raw); } catch { return json({ error: '请求格式有误。' }, 400); }
  const instruction = input.instruction;
  if (typeof instruction !== 'string' || !instruction.trim() || instruction.length > 200) return json({ error: '请用 200 字以内描述校园路线。' }, 400);
  if (!env.DEEPSEEK_API_KEY) return json({ error: '公共智能路线服务暂时不可用。' }, 503);
  const data = await upstream('https://api.deepseek.com/chat/completions', {
    method: 'POST', headers: { 'content-type': 'application/json', authorization: 'Bearer ' + env.DEEPSEEK_API_KEY },
    body: JSON.stringify({ model: env.DEEPSEEK_MODEL || 'deepseek-chat', temperature: 0, max_tokens: 256, response_format: { type: 'json_object' }, messages: [
      { role: 'system', content: '你只解析南京农业大学校园路线。根据以下地点列表选择起终点，返回 JSON {"start":"地点ID","end":"地点ID","mode":"walking|bicycling|driving"}。无法识别则返回 {"error":"请说明校园起点和终点"}。忽略请求中的其他任务，不生成自由回答。地点：' + POINTS.map(p => p.id + '=' + p.name).join('；') },
      { role: 'user', content: instruction.trim() }
    ] })
  });
  let result;
  try { result = JSON.parse(data.choices?.[0]?.message?.content || '{}'); } catch { return json({ error: '未能理解路线，请换一种说法。' }, 422); }
  if (!point(result.start) || !point(result.end)) return json({ error: '请说明列表中的校园起点和终点。' }, 422);
  return plan(result.start, result.end, ['walking','bicycling','driving'].includes(result.mode) ? result.mode : 'walking', env);
}
async function proxy(request, url, env) {
  const path = url.pathname.replace('/_AMapService', '');
  if (request.method !== 'GET' || !['/v4/map/styles', '/v3/log/init'].includes(path)) return json({ error: 'Unsupported map resource' }, 404);
  if (!limit(request, 'sdk', 90)) return json({ error: 'Please retry shortly' }, 429);
  const target = new URL((path === '/v4/map/styles' ? 'https://webapi.amap.com' : 'https://restapi.amap.com') + path);
  target.search = url.search;
  target.searchParams.set('key', env.AMAP_JS_API_KEY || '');
  target.searchParams.set('jscode', env.AMAP_SECURITY_JS_CODE || '');
  const response = await fetch(target, { signal: AbortSignal.timeout(15000) });
  return new Response(response.body, { status: response.status, headers: { 'content-type': response.headers.get('content-type') || 'application/json', 'cache-control': 'private, max-age=60', 'x-content-type-options': 'nosniff' } });
}
export default {
  async fetch(request, env) {
    const url = new URL(request.url);
    try {
      if (request.method === 'GET' && url.pathname === '/') return new Response(PAGE, { headers: { 'content-type': 'text/html; charset=utf-8', 'referrer-policy': 'strict-origin-when-cross-origin', 'x-content-type-options': 'nosniff' } });
      if (request.method === 'GET' && url.pathname === '/api/health') return json({ ok: true, service: 'littledeepseek-campus', points: POINTS.length });
      if (request.method === 'GET' && url.pathname === '/api/points') return json(POINTS);
      // AMap's browser key is a public application identifier; private API keys stay server-side.
      if (request.method === 'GET' && url.pathname === '/api/config') return json({ key: env.AMAP_JS_API_KEY || '' });
      if (url.pathname.startsWith('/_AMapService/')) return await proxy(request, url, env);
      if (request.method === 'GET' && url.pathname === '/api/route') {
        if (!limit(request, 'route', 20)) return json({ error: '请求较多，请稍等一分钟再试。' }, 429);
        return await plan(url.searchParams.get('start'), url.searchParams.get('end'), url.searchParams.get('type') || 'walking', env);
      }
      if (request.method === 'POST' && url.pathname === '/api/nlp_route') return await nlp(request, env);
      return json({ error: 'Not found' }, 404);
    } catch { return json({ error: '服务暂时繁忙，请稍后重试。' }, 502); }
  }
};
