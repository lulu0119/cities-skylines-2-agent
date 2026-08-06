# Agent runtime Web API requirements vs Gameface

**Date:** 2026-08-06  
**Packages audited:** `@apeira/core@0.0.7`, `@xsai/stream-text@0.5.0-beta.8`, `@xsai/generate-text@0.5.0-beta.8`, `@xsai/shared@0.5.0-beta.8`, `@xsai/shared-chat@0.5.0-beta.8`, `@xsai/shared-stream@0.5.0-beta.8`, `@xsai/tool@0.5.0-beta.8` (npm / jsDelivr sources).  
**Gameface baseline:** Coherent public docs (DOM API module, Window, XMLHttpRequest, React support).  
**CS2 empirical:** `ReadableStream` undefined (smoke 2026-08-06).

> Agent loop **conceptually** does not need streams.  
> **Stock apeira + stock xsai** do — including non-stream `generateText`.

---

## Call graph (what actually hits the network)

```text
@apeira/core  chat()  ──►  @xsai/stream-text  streamText()
                              │
@apeira/core  run()   ──►  new ReadableStream({...})   (event bus to caller)

@xsai/generate-text  generateText()
        │
        ▼
@xsai/shared-chat  chat()
        │
        ▼
@xsai/shared  postJSON()  ──►  (options.fetch ?? globalThis.fetch)(...)
        │
        ▼
@xsai/shared  responseCatch(res)
        │  requires res.ok
        │  requires res.body
        │  requires (res.body instanceof ReadableStream)   ← HARD GATE
        ▼
  stream path: body.pipeThrough(TextDecoderStream)…
  unary path:  responseJSON(res) → res.text() → JSON.parse
```

Source: `@xsai/shared` `responseCatch` / `postJSON`; `@xsai/shared-chat` `chat`; `@apeira/core` `chat.js` / `run()`.

---

## Requirement matrix

Legend for **Gameface docs:**  
`DOC-YES` = listed in Coherent IDL/docs · `DOC-NO` = not in DOM API / Window surface · `POLY` = official samples ship a polyfill · `EMPIRICAL` = CS2 in-game probe

| # | API / capability | Who needs it | Gameface docs | CS2 status | Blocker? |
| --- | --- | --- | --- | --- | --- |
| 1 | **`ReadableStream`** (ctor + `instanceof`) | **apeira** `run()`; **xsai shared-stream**; **xsai shared `responseCatch`** (all `postJSON`, incl. generate-text) | **DOC-NO** (not in [DOM API](https://docs.coherent-labs.com/cpp-gameface/api_reference/modules/group___d_o_m___a_p_i/) list) | **EMPIRICAL: undefined** | **Yes — stock apeira & stock xsai** |
| 2 | **`WritableStream`** | stream-text `pipeTo(new WritableStream…)` | DOC-NO | Likely absent with #1 | Yes for stream-text |
| 3 | **`TransformStream`** | shared-stream `EventSourceParserStream`, `JsonMessageTransformStream` | DOC-NO | Likely absent | Yes for stream-text |
| 4 | **`TextDecoderStream`** | stream-text SSE decode pipeline | DOC-NO | Likely absent | Yes for stream-text |
| 5 | **`fetch` + `Response` + `Headers`** | xsai `postJSON` (`globalThis.fetch` or injected `fetch`) | **DOC-NO** native; React/Preact/Redux docs use **`whatwg-fetch` POLY** ([React](https://docs.coherent-labs.com/cpp-gameface/content_development/reactsupport/), [Redux DevTools](https://docs.coherent-labs.com/cpp-gameface/content_development/reduxdevtools/)) | CS2 has some `fetch` (smoke uses it); body/`instanceof` unknown | **Ambiguous** — must probe `res.body instanceof ReadableStream` |
| 6 | **`Response.body` as stream** | responseCatch | DOC-NO | Fail if body null / not RS | Yes for stock xsai |
| 7 | **`XMLHttpRequest`** | Not used by apeira/xsai; **documented Gameface network path** | **DOC-YES** ([XHR](https://docs.coherent-labs.com/cpp-gameface/api_reference/classes/interface_x_m_l_http_request/), [News Feed](https://docs.coherent-labs.com/cpp-gameface/content_development/pages_guides/newsfeed_native/)) | Available | Alt transport |
| 8 | **`AbortController` / `AbortSignal`** | apeira queue; xsai options | DOC-NO on Window IDL | Unknown | Soft — polyfill or drop cancel |
| 9 | **`crypto.randomUUID`** | apeira turn/entry ids | DOC-NO | Unknown | Soft — replace with id helper |
| 10 | **`structuredClone`** | xsai clone messages/steps; apeira state | DOC-NO | Unknown | Soft — `JSON.parse(JSON.stringify)` |
| 11 | **`Promise.withResolvers`** | stream-text internal | DOC-NO (ES2024) | Unknown | Soft — polyfill |
| 12 | **`for await…of` on streams** | apeira `chat` over `eventStream` | Needs async iter + RS | Blocked by #1 | With stream path |
| 13 | **ES6 `Promise`** | everywhere | **DOC-YES** ([javascript_native](https://docs.coherent-labs.com/cpp-gameface/integration/ui_scripting/javascript_native/)) | OK | No |
| 14 | **`URL`** | xsai `requestURL` | Not highlighted; usually in V8 | Probe | Soft |
| 15 | **`localStorage`** | not required by apeira/xsai core | **DOC-YES** on Window | Likely OK | No for runtime |
| 16 | **`queueMicrotask`** | not required by these pkgs | **DOC-YES** on Window | OK | No |
| 17 | **WebSocket** | not required by apeira/xsai | Optional host feature | N/A | No |
| 18 | **`yocto-queue`** | apeira dependency (npm, not Web API) | N/A | Bundle it | No |

### Critical quote (`@xsai/shared`)

Even **non-streaming** completions go through:

```js
if (!(res.body instanceof ReadableStream)) {
  throw new InvalidResponseError(
    `Expected Response body to be a ReadableStream, but got …`
  );
}
```

So: **turning off apeira streaming / using `generateText` does not remove the ReadableStream gate** unless you change transport (`fetch` option) or patch `@xsai/shared`.

---

## What Gameface docs actually say (networking)

| Doc claim | Source |
| --- | --- |
| JS DOM networking surface centers on **XMLHttpRequest** | [DOM API](https://docs.coherent-labs.com/cpp-gameface/api_reference/modules/group___d_o_m___a_p_i/), [XHR](https://docs.coherent-labs.com/cpp-gameface/api_reference/classes/interface_x_m_l_http_request/) |
| Production sample fetches HTTP via **XHR** | [News Feed](https://docs.coherent-labs.com/cpp-gameface/content_development/pages_guides/newsfeed_native/) |
| Official React toolchain expects **`whatwg-fetch` polyfill** (implies native fetch not assumed) | [React Support](https://docs.coherent-labs.com/cpp-gameface/content_development/reactsupport/) |
| No `ReadableStream` / Fetch Streams in published DOM API index | [DOM API](https://docs.coherent-labs.com/cpp-gameface/api_reference/modules/group___d_o_m___a_p_i/) |
| V8 + ES6 Promises | [Communicating with JS](https://docs.coherent-labs.com/cpp-gameface/integration/ui_scripting/javascript_native/) |

**Conclusion from docs alone:** stock Gameface is **XHR-first**, not Streams-first. Matching our CS2 smoke.

---

## Fit verdict

| Stack | Fit on stock Gameface (docs + CS2 RS miss) |
| --- | --- |
| **`@apeira/core` as-is** | **No** — `run()` + `streamText` + shared `responseCatch` |
| **`@xsai/generate-text` as-is** | **No** — same `responseCatch` RS gate |
| **`@xsai/*` + custom `fetch` that returns `Response` whose `body` is a polyfilled `ReadableStream`** (e.g. web-streams-polyfill + XHR-backed fetch) | **Plausible** — needs one integration smoke |
| **Thin OpenAI client on XHR only** (no xsai/apeira) | **Yes per docs** |
| **Gameface TS orchestrates; C# does HTTPS** (bindings) | **Yes** (3.2/3.3 already green) |

---

## Still worth probing in CS2 (once each)

Do **not** re-smoke “is ReadableStream missing” — docs + empirical already agree.

Probe these if you insist on **in-Gameface xsai/apeira**:

1. After a real `fetch`, log `response.body`, `response.body && response.body.constructor?.name`, `response.body instanceof ReadableStream`.
2. Inject `web-streams-polyfill` (+ ensure fetch body is RS); retry `generateText` against mock.
3. Soft APIs: `AbortController`, `crypto.randomUUID`, `structuredClone`, `Promise.withResolvers`, `URL`.

If you **do not** use stock xsai/apeira, skip 1–2; use XHR or C# HTTP.

---

## Sources

| Item | URL / path |
| --- | --- |
| apeira README (stream-first, `run` → ReadableStream) | https://github.com/moeru-ai/apeira |
| `@apeira/core@0.0.7` dist | npm / jsDelivr `dist/index.js`, `dist/chat.js` |
| `@xsai/shared` `postJSON` / `responseCatch` | npm `0.5.0-beta.8` |
| `@xsai/stream-text` pipeThrough pipeline | npm `0.5.0-beta.8` |
| `@xsai/generate-text` → `chat({stream:false})` | npm `0.5.0-beta.8` |
| Gameface DOM API / Window / XHR / React / News Feed | docs.coherent-labs.com (linked above) |
| CS2 `ReadableStream` | smoke panel / `Logs/UI.log` 2026-08-06 |
