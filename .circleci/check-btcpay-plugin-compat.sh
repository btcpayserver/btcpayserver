#!/usr/bin/env bash
set -u

ROOT_DIR="${1:-btcpay-plugin-check}"
BTCPAY_BASELINE_REF="${BTCPAY_BASELINE_REF:-}"

BTCPAY_REPO="https://github.com/btcpayserver/btcpayserver.git"
EXOLIX_REPO="https://github.com/Nisaba/btcpayserver-plugins.git"
SAMROCK_REPO="https://github.com/rockstardev/SamRockProtocol.git"
BOLTZ_REPO="https://github.com/BoltzExchange/boltz-btcpay-plugin.git"
PAYROLL_REPO="https://github.com/rockstardev/BTCPayServerPlugins.RockstarDev.git"
BOLTCARDS_REPO="https://github.com/NicolasDorier/boltcards-plugin.git"
SHOPIFY_REPO="https://github.com/btcpayserver/btcpayserver-shopify-plugin.git"
MONERO_REPO="https://github.com/btcpay-monero/btcpayserver-monero-plugin.git"
BLINK_REPO="https://github.com/Kukks/BTCPayServerPlugins.git"

BTCPAY_SPLICE_ABS=""

replace_btcpay_copy() {
  local target="$1"

  rm -rf "$target"
  mkdir -p "$(dirname "$target")"
  cp -a "$BTCPAY_SPLICE_ABS" "$target"
}

run_build() {
  local name="$1"
  local project="$2"

  printf '\n==> Building %s\n' "$name"
  if dotnet build "$project" -c Release --nologo -v quiet /clp:ErrorsOnly; then
    printf '==> %s: PASS\n' "$name"
    return 0
  else
    printf '==> %s: FAIL\n' "$name"
    return 1
  fi
}

retarget_net10() {
  local project="$1"

  perl -0pi -e 's#<TargetFramework>net8\.0</TargetFramework>#<TargetFramework>net10.0</TargetFramework>#' "$project"
}

if [ -e "$ROOT_DIR" ]; then
  printf 'Refusing to overwrite existing path: %s\n' "$ROOT_DIR" >&2
  printf 'Pass a new directory name as the first argument, or remove it manually.\n' >&2
  exit 1
fi

mkdir "$ROOT_DIR"
cd "$ROOT_DIR"
ROOT_ABS="$(pwd)"
BTCPAY_MASTER_ABS="$ROOT_ABS/btcpayserver-master"
BTCPAY_BASELINE_ABS="$ROOT_ABS/btcpayserver-baseline"

printf '==> Cloning BTCPay Server master\n'
git clone --depth 1 --branch master "$BTCPAY_REPO" btcpayserver-master

printf '\n==> Building BTCPay Server master once\n'
dotnet build "btcpayserver-master/BTCPayServer/BTCPayServer.csproj" -c Release --nologo -v quiet /clp:ErrorsOnly || exit 1

if [ -n "$BTCPAY_BASELINE_REF" ]; then
  printf '\n==> Cloning BTCPay Server baseline (%s)\n' "$BTCPAY_BASELINE_REF"
  git clone --depth 1 --branch "$BTCPAY_BASELINE_REF" "$BTCPAY_REPO" btcpayserver-baseline

  printf '\n==> Building BTCPay Server baseline once\n'
  if ! dotnet build "btcpayserver-baseline/BTCPayServer/BTCPayServer.csproj" -c Release --nologo -v quiet /clp:ErrorsOnly; then
    printf '!! WARNING: baseline BTCPay (%s) failed to build.\n' "$BTCPAY_BASELINE_REF"
    BTCPAY_BASELINE_REF=""
  fi
fi

printf '\n==> Cloning plugin source repositories\n'
git clone "$EXOLIX_REPO" exolix-plugin
git clone "$SAMROCK_REPO" samrock-protocol
git clone "$BOLTZ_REPO" boltz
git clone "$PAYROLL_REPO" payroll
git clone "$BOLTCARDS_REPO" boltcards-plugin
git clone "$SHOPIFY_REPO" shopify-plugin
git clone "$MONERO_REPO" monero-plugin
git clone "$BLINK_REPO" blink

printf '\n==> Initializing non-BTCPay plugin submodules\n'
GIT_LFS_SKIP_SMUDGE=1 git -C blink submodule update --init --depth 1 --recommend-shallow --filter=blob:none submodules/walletwasabi

printf '\n==> Wiring SamRock Boltz submodule from the cloned Boltz repo\n'
rm -rf samrock-protocol/submodules/boltz
git clone ./boltz samrock-protocol/submodules/boltz

printf '\n==> Applying compatibility patches in disposable plugin checkouts\n'
retarget_net10 samrock-protocol/Plugins/SamRockProtocol/SamRockProtocol.csproj
retarget_net10 shopify-plugin/Plugins/BTCPayServer.Plugins.ShopifyPlugin/BTCPayServer.Plugins.ShopifyPlugin.csproj
rm -f monero-plugin/global.json

splice_all() {
  BTCPAY_SPLICE_ABS="$1"
  replace_btcpay_copy exolix-plugin/btcpayserver
  replace_btcpay_copy boltz/btcpayserver
  replace_btcpay_copy samrock-protocol/submodules/btcpayserver
  replace_btcpay_copy payroll/submodules/btcpayserver
  replace_btcpay_copy boltcards-plugin/btcpayserver
  replace_btcpay_copy shopify-plugin/btcpayserver
  replace_btcpay_copy shopify-plugin/submodules/btcpayserver
  replace_btcpay_copy monero-plugin/submodules/btcpayserver
  replace_btcpay_copy blink/submodules/btcpayserver
  replace_btcpay_copy samrock-protocol/submodules/boltz/btcpayserver
}

declare -A MASTER_RESULT
declare -A BASE_RESULT

build_all() {
  local -n _res="$1"

  (
    cd exolix-plugin
    run_build "exolix-plugin" "BTCPayServer.Plugins.Exolix/BTCPayServer.Plugins.Exolix.csproj"
  )
  _res["exolix-plugin"]=$?

  (
    cd boltz
    run_build "boltz" "BTCPayServer.Plugins.Boltz/BTCPayServer.Plugins.Boltz.csproj"
  )
  _res["boltz"]=$?

  (
    cd samrock-protocol
    run_build "samrock-protocol" "Plugins/SamRockProtocol/SamRockProtocol.csproj"
  )
  _res["samrock-protocol"]=$?

  (
    cd payroll
    run_build "payroll" "Plugins/BTCPayServer.RockstarDev.Plugins.Payroll/BTCPayServer.RockstarDev.Plugins.Payroll.csproj"
  )
  _res["payroll"]=$?

  (
    cd boltcards-plugin
    run_build "boltcards-plugin" "BTCPayServer.Plugins.Boltcards/BTCPayServer.Plugins.Boltcards.csproj"
  )
  _res["boltcards-plugin"]=$?

  (
    cd shopify-plugin
    run_build "shopify-plugin" "Plugins/BTCPayServer.Plugins.ShopifyPlugin/BTCPayServer.Plugins.ShopifyPlugin.csproj"
  )
  _res["shopify-plugin"]=$?

  (
    cd monero-plugin
    run_build "monero-plugin" "Plugins/Monero/BTCPayServer.Plugins.Monero.csproj"
  )
  _res["monero-plugin"]=$?

  (
    cd blink
    blink_status=0
    for project in Plugins/*/*.csproj; do
      plugin_name="$(basename "$(dirname "$project")")"
      plugin_name="${plugin_name#BTCPayServer.Plugins.}"
      run_build "kukks-$plugin_name" "$project" || blink_status=1
    done
    exit "$blink_status"
  )
  _res["blink"]=$?
}

printf '\n==> Environment\n'
printf 'BTCPay master: '
git -C btcpayserver-master rev-parse HEAD
if [ -n "$BTCPAY_BASELINE_REF" ]; then
  printf 'BTCPay baseline (%s): ' "$BTCPAY_BASELINE_REF"
  git -C btcpayserver-baseline rev-parse HEAD
fi
printf 'dotnet SDK: '
dotnet --version

printf '\n======= PASS: master =======\n'
splice_all "$BTCPAY_MASTER_ABS"
build_all MASTER_RESULT

if [ -n "$BTCPAY_BASELINE_REF" ]; then
  printf '\n======= PASS: baseline (%s) ========\n' "$BTCPAY_BASELINE_REF"
  splice_all "$BTCPAY_BASELINE_ABS"
  build_all BASE_RESULT
fi

printf '\n==> Summary\n'

regressions=()
still_broken=()
still_ok=()
newly_fixed=()
plain_fail=()
plain_ok=()

for name in "${!MASTER_RESULT[@]}"; do
  m="${MASTER_RESULT[$name]}"
  if [ -z "$BTCPAY_BASELINE_REF" ]; then
    if [ "$m" -eq 0 ]; then
      plain_ok+=("$name")
    else
      plain_fail+=("$name")
    fi
    continue
  fi
  b="${BASE_RESULT[$name]:-1}"
  if [ "$b" -eq 0 ] && [ "$m" -ne 0 ]; then
    regressions+=("$name")
  elif [ "$b" -ne 0 ] && [ "$m" -ne 0 ]; then
    still_broken+=("$name")
  elif [ "$b" -ne 0 ] && [ "$m" -eq 0 ]; then
    newly_fixed+=("$name")
  else
    still_ok+=("$name")
  fi
done

if [ -z "$BTCPAY_BASELINE_REF" ]; then
  printf 'OK: %s\n' "${plain_ok[*]:-(none)}"
  printf 'FAILED: %s\n' "${plain_fail[*]:-(none)}"
  if [ "${#plain_fail[@]}" -eq 0 ]; then
    printf 'All plugin builds passed.\n'
  else
    printf 'One or more plugin builds failed. See output above.\n'
  fi
else
  printf 'still-ok: %s\n' "${still_ok[*]:-(none)}"
  printf 'still-broken: %s\n' "${still_broken[*]:-(none)}"
  printf 'newly-fixed: %s\n' "${newly_fixed[*]:-(none)}"
  printf 'REGRESSION: %s\n' "${regressions[*]:-(none)}"
  printf '\n'
  if [ "${#regressions[@]}" -gt 0 ]; then
    printf '!! %d plugin(s) built against %s but FAIL against master.\n' "${#regressions[@]}" "$BTCPAY_BASELINE_REF"
    printf '!! These are user-facing regressions: consider a Core fix (revert + [Obsolete]),\n'
    printf '!! or ping the author only if Core cannot absorb it.\n'
  else
    printf 'No new regressions versus %s.\n' "$BTCPAY_BASELINE_REF"
  fi
  if [ "${#still_broken[@]}" -gt 0 ]; then
    printf '(Note: still-broken plugins were already broken before this release.)\n'
  fi
fi

exit 0