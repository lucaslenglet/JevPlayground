#!/usr/bin/env bash
# Packs the tool, installs it from a local feed and drives its real endpoints.
#
# This is the only check that proves the tool still works once it has been through a
# package. Everything else exercises build output, which is not what a user installs:
# it would not catch a missing embedded asset, a broken tool manifest or a bad
# FrameworkReference.
#
#   ./tests/smoke/run.sh              # packs the default version
#   ./tests/smoke/run.sh 1.2.3        # packs that version, as the release workflow does

set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
version="${1:-}"

artifacts="$root/tests/smoke/.artifacts"
feed="$artifacts/feed"
tools="$artifacts/tools"

# Wiped on every run, with an isolated package cache: a repacked package at an
# unchanged version would otherwise be served from the cache, and this would pass
# without testing the package just built.
rm -rf "$artifacts"
mkdir -p "$feed" "$tools"
export NUGET_PACKAGES="$artifacts/packages"

pack=(dotnet pack "$root/src/JevPlay/JevPlay.csproj" -c Release --output "$feed")
install=(dotnet tool install lucaslgt.JevPlay --tool-path "$tools" --add-source "$feed")
if [ -n "$version" ]; then
  pack+=("-p:JevPlayVersion=$version")
  install+=(--version "$version")
fi

"${pack[@]}"
"${install[@]}"

jevplay="$tools/jevplay"
log="$artifacts/serve.log"

# No --port on purpose: this also covers picking a free port and reporting it.
"$jevplay" serve --provider mock >"$log" 2>&1 &
server=$!
trap 'kill "$server" 2>/dev/null || true' EXIT

for _ in $(seq 1 100); do
  grep -q 'Ctrl+C' "$log" 2>/dev/null && break
  sleep 0.1
done

url="$(grep -om1 'http://127\.0\.0\.1:[0-9]\+' "$log" || true)"
if [ -z "$url" ]; then
  echo "FAIL  the tool never reported a URL"
  cat "$log"
  exit 1
fi

failures=0

check() { # check <label> <actual> <expected substring>
  if printf '%s' "$2" | grep -qF -- "$3"; then
    printf '  ok    %s\n' "$1"
  else
    printf '  FAIL  %s\n        expected to find: %s\n        got: %.300s\n' "$1" "$3" "$2"
    failures=$((failures + 1))
  fi
}

same() { # same <label> <actual> <expected, in full>
  if [ "$2" = "$3" ]; then
    printf '  ok    %s\n' "$1"
  else
    printf '  FAIL  %s\n        expected: %.300s\n        got:      %.300s\n' "$1" "$3" "$2"
    failures=$((failures + 1))
  fi
}

# Latency is the one field that legitimately varies between two identical calls.
without_latency() { printf '%s' "$1" | sed 's/"latencyMs":[0-9]*,//'; }

ask='{"state":"Hi, my order arrived broken. I want a refund.","questions":{
  "intent":{"type":"choice","instructions":"Main intent?","criteria":{"refund":null,"exchange":null}},
  "urgency":{"type":"score","instructions":"How urgent?","criteria":["low","medium","high"]},
  "unhappy":{"type":"noul","instructions":"Is the customer unhappy?"}}}'

post() { curl -sS -X POST "$url/api/classify" -H 'Content-Type: application/json' --data-binary "$1"; }

echo "Serving on $url"

check "the front end is served"   "$(curl -sS "$url/")"           '<title>Jev Playground</title>'
check "app.js is embedded"        "$(curl -sS "$url/app.js")"     'api/classify'
check "style.css is embedded"     "$(curl -sS "$url/style.css")"  '.bar-fill'
check "/api/config reports mock"  "$(curl -sS "$url/api/config")" '"provider":"mock"'

answer="$(post "$ask")"
check "choice answer"   "$answer" '"type":"choice"'
check "score legend"    "$answer" '"legend"'
check "noul answer"     "$answer" '"type":"noul"'
check "usage reported"  "$answer" '"input_tokens"'
check "request echoed"  "$answer" '"state":"Hi, my order arrived broken. I want a refund."'

# The mock draw is deterministic by design, so the interface can be worked on against
# a reproducible answer.
same "same request, same answer" \
  "$(without_latency "$(post "$ask")")" \
  "$(without_latency "$answer")"

status="$(curl -sS -o /dev/null -w '%{http_code}' -X POST "$url/api/classify" \
  -H 'Content-Type: application/json' --data-binary '{"state":"x","questions":{}}')"
check "an empty question set is refused" "$status" '400'

status="$(curl -sS -o /dev/null -w '%{http_code}' "$url/nope.txt")"
check "an unknown file is a 404" "$status" '404'

echo
if [ "$failures" -eq 0 ]; then
  echo "Smoke test passed."
else
  echo "Smoke test failed: $failures check(s)."
  exit 1
fi
