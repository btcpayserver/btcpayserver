#!/usr/bin/env bash
set -u

ROOT_DIR="${1:-/tmp/btcpay-plugin-check}"
BTCPAY_ABS="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"

EXOLIX_REPO="https://github.com/Nisaba/btcpayserver-plugins.git"
SAMROCK_REPO="https://github.com/rockstardev/SamRockProtocol.git"
BOLTZ_REPO="https://github.com/BoltzExchange/boltz-btcpay-plugin.git"
PAYROLL_REPO="https://github.com/rockstardev/BTCPayServerPlugins.RockstarDev.git"
BOLTCARDS_REPO="https://github.com/NicolasDorier/boltcards-plugin.git"
SHOPIFY_REPO="https://github.com/btcpayserver/btcpayserver-shopify-plugin.git"
MONERO_REPO="https://github.com/btcpay-monero/btcpayserver-monero-plugin.git"
BLINK_REPO="https://github.com/Kukks/BTCPayServerPlugins.git"
CASHU_REPO="https://github.com/cashubtc/BTCNutServer.git"
TETHER_REPO="https://github.com/btcpayserver-tether/BTCPayServer.Plugins.USDt.git"

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

printf '==> Building BTCPay Server checkout once\n'
dotnet build "$BTCPAY_ABS/BTCPayServer/BTCPayServer.csproj" -c Release --nologo -v quiet /clp:ErrorsOnly || exit 1

printf '\n==> Cloning plugin source repositories\n'
git clone "$EXOLIX_REPO" exolix-plugin
git clone "$SAMROCK_REPO" samrock-protocol
git clone "$BOLTZ_REPO" boltz
git clone "$PAYROLL_REPO" payroll
git clone "$BOLTCARDS_REPO" boltcards-plugin
git clone "$SHOPIFY_REPO" shopify-plugin
git clone "$MONERO_REPO" monero-plugin
git clone "$BLINK_REPO" blink
git clone "$CASHU_REPO" cashu-plugin
git clone "$TETHER_REPO" tether-plugin

printf '\n==> Initializing non-BTCPay plugin submodules\n'
GIT_LFS_SKIP_SMUDGE=1 git -C blink submodule update --init --depth 1 --recommend-shallow --filter=blob:none submodules/walletwasabi
git -C cashu-plugin submodule update --init --depth 1 --recommend-shallow --filter=blob:none submodules/DotNut

printf '\n==> Wiring SamRock Boltz submodule from the cloned Boltz repo\n'
rm -rf samrock-protocol/submodules/boltz
git clone ./boltz samrock-protocol/submodules/boltz

printf '\n==> Applying compatibility patches in disposable plugin checkouts\n'
retarget_net10 samrock-protocol/Plugins/SamRockProtocol/SamRockProtocol.csproj
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
  replace_btcpay_copy cashu-plugin/submodules/btcpayserver
  replace_btcpay_copy tether-plugin/submodules/btcpayserver
  replace_btcpay_copy samrock-protocol/submodules/boltz/btcpayserver
}

declare -A RESULT

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
    cd cashu-plugin
    run_build "cashu-plugin" "Plugin/BTCPayServer.Plugins.Cashu/BTCPayServer.Plugins.Cashu.csproj"
  )
  _res["cashu-plugin"]=$?

  (
    cd tether-plugin
    run_build "tether-plugin" "BTCPayServer.Plugins.USDt/BTCPayServer.Plugins.USDt.csproj"
  )
  _res["tether-plugin"]=$?

  for project in blink/Plugins/*/*.csproj; do
    plugin_name="$(basename "$(dirname "$project")")"
    plugin_name="${plugin_name#BTCPayServer.Plugins.}"
    if [ "$plugin_name" = "Wabisabi" ]; then
      continue
    fi
    run_build "kukks-$plugin_name" "$project"
    _res["kukks-$plugin_name"]=$?
  done
}

printf '\n==> Environment\n'
printf 'BTCPay checkout: '
git -C "$BTCPAY_ABS" rev-parse HEAD
printf 'dotnet SDK: '
dotnet --version

printf '\n======= Building plugins =======\n'
splice_all "$BTCPAY_ABS"
build_all RESULT

printf '\n==> Summary\n'

plain_fail=()
plain_ok=()

for name in "${!RESULT[@]}"; do
  if [ "${RESULT[$name]}" -eq 0 ]; then
    plain_ok+=("$name")
  else
    plain_fail+=("$name")
  fi
done

printf 'OK: %s\n' "${plain_ok[*]:-(none)}"
printf 'FAILED: %s\n' "${plain_fail[*]:-(none)}"
if [ "${#plain_fail[@]}" -eq 0 ]; then
  printf 'All plugin builds passed.\n'
else
  printf 'One or more plugin builds failed. See output above.\n'
fi

if [ "${#plain_fail[@]}" -gt 0 ]; then
  exit 1
fi

exit 0
