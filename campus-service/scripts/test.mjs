import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
const source = await readFile('dist/server/index.js', 'utf8');
const { default: app } = await import('data:text/javascript;base64,' + Buffer.from(source).toString('base64'));
const env = { AMAP_API_KEY: 'private-map-fixture', AMAP_JS_API_KEY: 'public-sdk-fixture', AMAP_SECURITY_JS_CODE: 'private-sdk-fixture', DEEPSEEK_API_KEY: 'private-ai-fixture' };
const req = (path, options) => new Request('https://campus.test' + path, options);
assert.equal((await app.fetch(req('/'), env)).status, 200);
assert.equal((await (await app.fetch(req('/api/points'), env)).json()).length, 30);
const page = await (await app.fetch(req('/'), env)).text();
for (const secret of [env.AMAP_API_KEY, env.DEEPSEEK_API_KEY, env.AMAP_SECURITY_JS_CODE]) assert(!page.includes(secret));
const config = await (await app.fetch(req('/api/config'), env)).json();
assert.deepEqual(config, { key: env.AMAP_JS_API_KEY });
assert.equal((await app.fetch(req('/api/route?start=1&end=1'), env)).status, 400);
assert.equal((await app.fetch(req('/api/route?start=1&end=2&type=invalid'), env)).status, 400);
assert.equal((await app.fetch(req('/_AMapService/v3/direction/walking'), env)).status, 404);
assert.equal((await app.fetch(req('/api/nlp_route', { method:'POST', body:'bad' }), env)).status, 400);
assert.equal((await app.fetch(req('/api/nlp_route', { method:'POST', body:JSON.stringify({instruction:'a'.repeat(201)}) }), env)).status, 400);
let calls = 0;
globalThis.fetch = async (url, options) => {
  calls++;
  if (String(url).includes('deepseek.com')) {
    assert.equal(options.headers.authorization, 'Bearer ' + env.DEEPSEEK_API_KEY);
    return Response.json({ choices:[{message:{content:'{"start":"1","end":"2","mode":"walking"}'}}] });
  }
  assert.equal(new URL(url).searchParams.get('key'), env.AMAP_API_KEY);
  return Response.json({ route:{paths:[{distance:'1200',duration:'900',steps:[{instruction:'沿道路前行',polyline:'118.638654,32.004734;118.636393,32.012066'}]}]} });
};
const route = await app.fetch(req('/api/route?start=1&end=2'), env);
assert.equal(route.status, 200);
assert.equal((await route.json()).coordinates.length, 2);
const ai = await app.fetch(req('/api/nlp_route', { method:'POST', body:JSON.stringify({instruction:'信息管理学院到农学院'}) }), env);
assert.equal(ai.status, 200);
assert.equal((await ai.json()).end.id, '2');
assert.equal(calls, 3);
globalThis.fetch = async () => { throw Error('upstream secret must never escape'); };
const failed = await app.fetch(req('/api/route?start=1&end=2'), env);
assert.equal(failed.status, 502);
assert(!(await failed.text()).includes('secret'));
console.log('PASS: campus routes, AI parsing, malformed requests, proxy boundaries and secret isolation.');
