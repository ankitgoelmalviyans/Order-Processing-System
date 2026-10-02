#!/usr/bin/env bash
# End-to-end smoke test against the running docker compose stack (through the gateway).
# Usage: ./scripts/smoke-test.sh [base-url]        (default http://localhost:8080)
# Needs only bash + curl. To also verify the background job quickly, start the stack with
#   WORKER_INTERVAL_SECONDS=30 docker compose up --build -d
# and run with WAIT_FOR_WORKER=1.
set -euo pipefail

BASE_URL="${1:-http://localhost:8080}"
WAIT_FOR_WORKER="${WAIT_FOR_WORKER:-0}"
WORKER_TIMEOUT_SECONDS="${WORKER_TIMEOUT_SECONDS:-90}"
PASS=0
FAIL=0

# request METHOD PATH [JSON_BODY] -> sets STATUS and BODY
request() {
  local method="$1" path="$2" body="${3:-}" out
  if [[ -n "$body" ]]; then
    out=$(curl -s -w $'\n%{http_code}' -X "$method" -H 'Content-Type: application/json' -d "$body" "$BASE_URL$path")
  else
    out=$(curl -s -w $'\n%{http_code}' -X "$method" "$BASE_URL$path")
  fi
  STATUS="${out##*$'\n'}"
  BODY="${out%$'\n'*}"
}

json_field() { # json_field NAME  -> FIRST occurrence in $BODY (top-level fields come before nested items)
  grep -o "\"$1\":\"\{0,1\}[^,\"}]*" <<<"$BODY" | head -n1 | sed "s/^\"$1\":\"\{0,1\}//"
}

body_contains() { grep -qF -- "$1" <<<"$BODY" && echo yes || echo no; }

if [[ -t 1 ]]; then GREEN=$'\e[32m' RED=$'\e[31m' RESET=$'\e[0m'; else GREEN='' RED='' RESET=''; fi

check() { # check DESCRIPTION EXPECTED ACTUAL
  if [[ "$2" == "$3" ]]; then
    printf '  %sPASS%s %s\n' "$GREEN" "$RESET" "$1"; PASS=$((PASS + 1))
  else
    printf '  %sFAIL%s %s (expected %s, got %s)\n' "$RED" "$RESET" "$1" "$2" "$3"; FAIL=$((FAIL + 1))
    [[ -n "${BODY:-}" ]] && printf '       body: %s\n' "$BODY"
  fi
}

CUSTOMER="smoke-$(date +%s)-$RANDOM"
echo "Smoke testing $BASE_URL (customer $CUSTOMER)"

echo "Health and routing"
request GET /health;                         check "gateway health is 200" 200 "$STATUS"
request GET /swagger/v1/swagger.json;        check "swagger document is served through the gateway" 200 "$STATUS"
request POST /internal/jobs/promote-pending; check "internal endpoint is NOT exposed by the gateway" 404 "$STATUS"

echo "Create and get"
request POST /api/orders "{\"customerId\":\"$CUSTOMER\",\"items\":[{\"productId\":\"SKU-1\",\"productName\":\"Keyboard\",\"quantity\":2,\"unitPrice\":49.99},{\"productId\":\"SKU-2\",\"productName\":\"Mouse\",\"quantity\":1,\"unitPrice\":19.50}]}"
check "create order returns 201" 201 "$STATUS"
ORDER_ID=$(json_field id)
check "new order is PENDING" PENDING "$(json_field status)"
check "total is computed on the server" 119.48 "$(json_field totalAmount)"

request GET "/api/orders/$ORDER_ID";         check "get order by id returns 200" 200 "$STATUS"
check "fetched order has the same id" "$ORDER_ID" "$(json_field id)"
request GET "/api/orders/00000000-0000-0000-0000-000000000000"; check "unknown order returns 404" 404 "$STATUS"

echo "Validation"
request POST /api/orders "{\"customerId\":\"$CUSTOMER\",\"items\":[]}";  check "order without items returns 400" 400 "$STATUS"
request POST /api/orders "{\"customerId\":\"$CUSTOMER\",\"items\":[{\"productId\":\"A\",\"productName\":\"A\",\"quantity\":0,\"unitPrice\":1}]}"
check "quantity 0 returns 400" 400 "$STATUS"
request GET "/api/orders?status=BOGUS";      check "unknown status filter returns 400" 400 "$STATUS"

echo "List and filter"
request GET "/api/orders?customerId=$CUSTOMER&status=PENDING"
check "list filtered by status returns 200" 200 "$STATUS"
check "filtered list contains the order" yes "$(body_contains "\"id\":\"$ORDER_ID\"")"
request GET "/api/orders?customerId=$CUSTOMER&status=SHIPPED"
check "filter by another status excludes it" no "$(body_contains "\"id\":\"$ORDER_ID\"")"

echo "Status transitions"
request PATCH "/api/orders/$ORDER_ID/status" '{"status":"DELIVERED"}'; check "skipping PROCESSING/SHIPPED returns 409" 409 "$STATUS"
request PATCH "/api/orders/$ORDER_ID/status" '{"status":"PROCESSING"}'; check "PENDING -> PROCESSING returns 200" 200 "$STATUS"
request POST "/api/orders/$ORDER_ID/cancel";                          check "cancel a PROCESSING order returns 409" 409 "$STATUS"
request PATCH "/api/orders/$ORDER_ID/status" '{"status":"SHIPPED"}';    check "PROCESSING -> SHIPPED returns 200" 200 "$STATUS"
request PATCH "/api/orders/$ORDER_ID/status" '{"status":"DELIVERED"}';  check "SHIPPED -> DELIVERED returns 200" 200 "$STATUS"

echo "Cancel"
request POST /api/orders "{\"customerId\":\"$CUSTOMER\",\"items\":[{\"productId\":\"SKU-3\",\"productName\":\"Cable\",\"quantity\":1,\"unitPrice\":5}]}"
CANCEL_ID=$(json_field id)
request POST "/api/orders/$CANCEL_ID/cancel"; check "cancel a PENDING order returns 200" 200 "$STATUS"
check "order is CANCELLED" CANCELLED "$(json_field status)"
request POST "/api/orders/$CANCEL_ID/cancel"; check "cancelling twice returns 409" 409 "$STATUS"

if [[ "$WAIT_FOR_WORKER" == "1" ]]; then
  echo "Background job (waiting up to ${WORKER_TIMEOUT_SECONDS}s for PENDING -> PROCESSING)"
  request POST /api/orders "{\"customerId\":\"$CUSTOMER\",\"items\":[{\"productId\":\"SKU-4\",\"productName\":\"Monitor\",\"quantity\":1,\"unitPrice\":199}]}"
  JOB_ID=$(json_field id)
  deadline=$((SECONDS + WORKER_TIMEOUT_SECONDS))
  status=PENDING
  while [[ "$status" == "PENDING" && $SECONDS -lt $deadline ]]; do
    sleep 5
    request GET "/api/orders/$JOB_ID"
    status=$(json_field status)
  done
  check "worker promoted the order to PROCESSING" PROCESSING "$status"
fi

echo
echo "Result: $PASS passed, $FAIL failed"
[[ $FAIL -eq 0 ]]
