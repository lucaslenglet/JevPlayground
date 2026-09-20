# JevPlay

A local playground for a **Jev** model (a "System One" model: it does not
generate prose, it returns a typed decision with its probabilities).

You give it a context, declare typed questions, send, and see the value the
model settled on for each question along with its distribution.

## Install

```bash
dotnet tool install -g lucaslgt.JevPlay
```

Then:

```bash
jevplay serve --key YOUR_KEY
```

## Usage

```
jevplay serve [options]

  --key <value>       API key. Falls back to $JEV_API_KEY.
  --model, -m <name>  Model to send. Defaults to the provider's own.
  --port, -p <n>      Port to listen on. Defaults to a free port picked by the OS.
  --provider <name>   typesafe | simple-jev | mock   (default: typesafe)
  -h, --help          Show the help.
  --version           Show the version.
```

At startup the tool prints the URL, the provider, the model and the endpoint,
then waits. It does not open a browser: open the URL yourself.

```
Jev Playground: http://127.0.0.1:39421
  provider: mock
  model:    jev-mock
  endpoint: none (answers are simulated locally)
  key:      none (this provider does not need one)

Open the URL above in a browser. Press Ctrl+C to stop.
```

### The API key

In order of priority:

1. `--key YOUR_KEY`
2. the `JEV_API_KEY` environment variable

If the provider needs one and neither is supplied, the tool refuses to start
and repeats those two ways of supplying it. `simple-jev` and `mock` do not
require a key.

The key never leaves the process: the browser does not call the remote API, it
goes through `/api/classify` on the local server. That also sidesteps CORS.

### Choosing the model

Each provider has its own default model. `--model` overrides it, which is what
you want when the demo API swaps its loaded model, or to try a different one:

```bash
jevplay serve --provider simple-jev -m featherless-ai/Qwen3.8-27B-classifier
```

See [`simple-jev`](#simple-jev) below for how to list the models that demo
currently serves.

## The three providers

### `typesafe` (default)

`POST https://api.typesafe.ai/v1/systemone`, `Authorization: Bearer <key>`,
model `jev-latest`.

> **⚠️ The shape of the body being sent is not verified.** The official
> TypeSafe documentation sits behind a waitlist and could not be read. What the
> tool sends mirrors Simple Jev, whose repository says it takes after the
> TypeSafe interface: that is an **inference**, not a source.
>
> One clue points the same way without proving anything: the Simple Jev
> reference describes `POST /v1/systemone` as an *exact alias* of
> `POST /v1/classifier`. So the TypeSafe path is already served, identically,
> by the open implementation. It stays a clue — until the official docs are
> read, treat the `typesafe` provider as untested.
>
> If the real API differs, everything to fix lives in
> [`src/JevPlay/Providers/TypeSafeProvider.cs`](src/JevPlay/Providers/TypeSafeProvider.cs):
> the DTOs at the bottom of the file, and the `BuildBody` and `ReadReply`
> methods. No other file in the project knows the HTTP body shape.

### `simple-jev`

`POST https://simple-jev-demo-api.featherless.ai/v1/classifier`, no
authentication, model `featherless-ai/Qwen3.6-35B-A3B-classifier`.

This is the only one of the three body shapes that has been **verified**
against a real server. Public demo limits: 2k context tokens and 2 requests per
second.

The demo serves several models. List them with:

```bash
curl https://simple-jev-demo-api.featherless.ai/v1/models
```

```json
{"object":"list","data":[
  {"id":"featherless-ai/Qwen3.6-35B-A3B-classifier","object":"model","owned_by":"Featherless Classifier Demo"},
  {"id":"featherless-ai/Qwen3.8-27B-classifier","object":"model","owned_by":"Featherless Classifier Demo"},
  {"id":"featherless-ai/RWKV-small-classifier","object":"model","owned_by":"Featherless Classifier Demo"},
  {"id":"featherless-ai/RWKV-mid-classifier","object":"model","owned_by":"Featherless Classifier Demo"},
  {"id":"featherless-ai/RWKV-std-classifier","object":"model","owned_by":"Featherless Classifier Demo"}
]}
```

Any `id` from that list can be passed to `--model`. Note that the
standalone-server reference documents `GET /health`, `GET /docs` and
`GET /openapi.json` but no `/v1/models`; the public demo exposes it anyway.
`jevplay` itself calls none of these: the model comes from `--model` or from
the built-in default.

### `mock`

Deterministic simulated answers, no network and no quota: the same request
always returns the same answer. This is what you want to work on the interface
offline.

```bash
jevplay serve --provider mock
```

## The three question types

| Type | What you declare | What comes back |
| --- | --- | --- |
| `choice` | 2 to 50 possible values (+ optional description) | the chosen value, its confidence, the full distribution |
| `score` | 2 to 50 levels, lowest first | a fractional mean index (0 to N-1) and the distribution |
| `noul` | nothing beyond the instructions | a value from 0.01 to 0.99 (likelihood of "true"), with no confidence |

The form is saved to `localStorage`, so a reload loses nothing.

## The API contract

Verified against the Simple Jev reference, inferred for TypeSafe. Full
reference:
<https://github.com/featherless-ai/simple-jev/blob/main/hf-server/API_REFERENCE.md>

`criteria` takes 2 to 50 entries for `choice` and for `score`; `jevplay`
rejects the request before the network call when it does not. The number of
questions (1 to 256 at schema level, 100 by default server side) is not checked
locally: the server decides.

Request:

```json
{
  "model": "...",
  "state": "the text to analyse",
  "questions": {
    "intent": {
      "type": "choice",
      "instructions": "What is the main intent of this message?",
      "criteria": { "refund": "the customer wants their money back", "exchange": null }
    },
    "urgency": {
      "type": "score",
      "instructions": "How urgent is this request?",
      "criteria": ["low", "medium", "high"]
    },
    "unhappy": { "type": "noul", "instructions": "Is the customer unhappy?" }
  }
}
```

Response:

```json
{
  "model": "...",
  "answers": {
    "intent":  {"type":"choice","choice":"refund","confidence":0.97,
                "probabilities":{"refund":0.97,"exchange":0.03}},
    "urgency": {"type":"score","score":1.9,"confidence":0.92,
                "probabilities":{"0":0.02,"1":0.06,"2":0.92},
                "legend":{"0":"low","1":"medium","2":"high"}},
    "unhappy": {"type":"noul","noul":0.96}
  },
  "usage": {"input_tokens": 600, "output_tokens": 0}
}
```

## Layout

```
src/JevPlay/
  Program.cs              entry point
  Cli.cs                  argument parsing, key resolution, help
  Jev/JevModel.cs         the neutral model (question, answer) everything shares
  Jev/JevJson.cs          browser JSON <-> neutral model
  Providers/
    IJevProvider.cs       the interface: one provider = one class + its DTOs
    ProviderCatalog.cs    the only place that knows the list of providers
    HttpJevProvider.cs    shared transport, knows no body shape
    TypeSafeProvider.cs   inferred shape, to be confirmed
    SimpleJevProvider.cs  verified shape
    MockProvider.cs       deterministic draw, no network
  Server/Playground.cs    Kestrel: /api/config, /api/classify, the front end
  Server/Assets.cs        the front end, read from embedded resources
  wwwroot/                index.html, app.js, style.css (EmbeddedResource)
```

No NuGet dependency. The front end is embedded in the assembly: once the tool
is installed there is no file to deploy beside it.

## Development

```bash
dotnet build                                    # whole solution, 0 warnings expected
dotnet run --project src/JevPlay -- serve --provider mock

./tests/smoke/run.sh                            # packs, installs from a local feed, drives the endpoints
./tests/smoke/run.sh 1.2.3                      # same, at the version a release would pack
```

`TreatWarningsAsErrors` is on repo-wide. There is no unit test project: the smoke test is the
safety net, and it runs against the packed tool rather than build output, so it also covers the
embedded assets and the tool manifest.

To release, push a `v*` tag. The tag is the only source of the published version, so nothing in
the repository needs bumping.
