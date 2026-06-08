#!/usr/bin/env bash
set -u

ROOT_DIR="${1:-btcpay-plugin-check}"
BTCPAY_REPO="https://github.com/btcpayserver/btcpayserver.git"
EXOLIX_REPO="https://github.com/Nisaba/btcpayserver-plugins.git"
SAMROCK_REPO="https://github.com/rockstardev/SamRockProtocol.git"
BOLTZ_REPO="https://github.com/BoltzExchange/boltz-btcpay-plugin.git"
PAYROLL_REPO="https://github.com/rockstardev/BTCPayServerPlugins.RockstarDev.git"
BOLTCARDS_REPO="https://github.com/NicolasDorier/boltcards-plugin.git"
SHOPIFY_REPO="https://github.com/btcpayserver/btcpayserver-shopify-plugin.git"
MONERO_REPO="https://github.com/btcpay-monero/btcpayserver-monero-plugin.git"
BLINK_REPO="https://github.com/Kukks/BTCPayServerPlugins.git"

replace_btcpay_copy() {
  local target="$1"

  rm -rf "$target"
  mkdir -p "$(dirname "$target")"
  cp -a "$BTCPAY_MASTER_ABS" "$target"
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

printf '==> Cloning BTCPay Server master\n'
git clone --depth 1 --branch master "$BTCPAY_REPO" btcpayserver-master

printf '\n==> Building BTCPay Server master once\n'
dotnet build "btcpayserver-master/BTCPayServer/BTCPayServer.csproj" -c Release --nologo -v quiet /clp:ErrorsOnly || exit 1

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

printf '\n==> Replacing BTCPay submodules with copies of built BTCPay master\n'
replace_btcpay_copy exolix-plugin/btcpayserver
replace_btcpay_copy boltz/btcpayserver
replace_btcpay_copy samrock-protocol/submodules/btcpayserver
replace_btcpay_copy payroll/submodules/btcpayserver
replace_btcpay_copy boltcards-plugin/btcpayserver
replace_btcpay_copy shopify-plugin/btcpayserver
replace_btcpay_copy shopify-plugin/submodules/btcpayserver
replace_btcpay_copy monero-plugin/submodules/btcpayserver
replace_btcpay_copy blink/submodules/btcpayserver

printf '\n==> Replacing SamRock Boltz submodule with the cloned Boltz repo\n'
rm -rf samrock-protocol/submodules/boltz
git clone ./boltz samrock-protocol/submodules/boltz
replace_btcpay_copy samrock-protocol/submodules/boltz/btcpayserver

printf '\n==> Applying compatibility patches in disposable plugin checkouts\n'
retarget_net10 samrock-protocol/Plugins/SamRockProtocol/SamRockProtocol.csproj
retarget_net10 shopify-plugin/Plugins/BTCPayServer.Plugins.ShopifyPlugin/BTCPayServer.Plugins.ShopifyPlugin.csproj
rm -f monero-plugin/global.json

printf '\n==> Environment\n'
printf 'BTCPay master: '
git -C btcpayserver-master rev-parse HEAD
printf 'dotnet SDK: '
dotnet --version

status=0

(
  cd exolix-plugin
  run_build "exolix-plugin" "BTCPayServer.Plugins.Exolix/BTCPayServer.Plugins.Exolix.csproj"
) || status=1

(
  cd boltz
  run_build "boltz" "BTCPayServer.Plugins.Boltz/BTCPayServer.Plugins.Boltz.csproj"
) || status=1

(
  cd samrock-protocol
  run_build "samrock-protocol" "Plugins/SamRockProtocol/SamRockProtocol.csproj"
) || status=1

(
  cd payroll
  run_build "payroll" "Plugins/BTCPayServer.RockstarDev.Plugins.Payroll/BTCPayServer.RockstarDev.Plugins.Payroll.csproj"
) || status=1

(
  cd boltcards-plugin
  run_build "boltcards-plugin" "BTCPayServer.Plugins.Boltcards/BTCPayServer.Plugins.Boltcards.csproj"
) || status=1

(
  cd shopify-plugin
  run_build "shopify-plugin" "Plugins/BTCPayServer.Plugins.ShopifyPlugin/BTCPayServer.Plugins.ShopifyPlugin.csproj"
) || status=1

(
  cd monero-plugin
  run_build "monero-plugin" "Plugins/Monero/BTCPayServer.Plugins.Monero.csproj"
) || status=1

(
  cd blink
  for project in Plugins/*/*.csproj; do
    plugin_name="$(basename "$(dirname "$project")")"
    plugin_name="${plugin_name#BTCPayServer.Plugins.}"
    run_build "kukks-$plugin_name" "$project" || status=1
  done
  exit "$status"
) || status=1

printf '\n==> Summary\n'
if [ "$status" -eq 0 ]; then
  printf 'All plugin builds passed.\n'
else
  printf 'One or more plugin builds failed. See output above.\n'
fi

exit "$status"
