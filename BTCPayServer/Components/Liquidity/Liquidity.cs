#nullable enable
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.CLightning;
using BTCPayServer.Lightning.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Components.Liquidity
{
    /// <summary>
    /// Overall inbound-liquidity health.
    /// </summary>
    public enum LiquidityStatus
    {
        Good,
        Pending,
        Bad
    }

    /// <summary>
    /// DTO returned when liquidity could be analysed.
    /// </summary>
    public class LiquidityReport
    {
        public LiquidityStatus LiquidityStatus { get; set; }

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney ActiveInboundSatoshis { get; set; } = LightMoney.Zero;

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney PendingInboundSatoshis { get; set; } = LightMoney.Zero;
    }

    /// <summary>
    /// Helper for checking Core-Lightning inbound liquidity.
    /// </summary>
    public static class Liquidity
    {
        private static readonly LightMoney DefaultThreshold = LightMoney.Satoshis(250_000);

        /// <summary>
        /// Returns <c>null</c> when the supplied node is not Core-Lightning or
        /// if an error occurs; otherwise returns a populated <see cref="LiquidityReport"/>.
        /// </summary>
        public static async Task<LiquidityReport?> CheckAsync(
            ILightningClient client,
            LightMoney? threshold = null,
            CancellationToken token = default)
        {
            // This check is specific to CLN because it's the only implementation
            // where ListChannels returns pending channels.
            if (client is not CLightningClient)
            {
                return null;
            }

            var min = threshold ?? DefaultThreshold;

            try
            {
                var channels = await client.ListChannels(token);

                if (channels is null)
                {
                    return null;
                }

                // inbound capacity = total – local
                LightMoney activeInbound = channels.Where(c => c.IsActive)
                                                    .Aggregate(LightMoney.Zero,
                                                               (s, ch) => s + (ch.Capacity - ch.LocalBalance));

                LightMoney pendingInbound = channels.Where(c => !c.IsActive)
                                                     .Aggregate(LightMoney.Zero,
                                                                (s, ch) => s + (ch.Capacity - ch.LocalBalance));

                var status = LiquidityStatus.Bad;
                if (activeInbound >= min)
                    status = LiquidityStatus.Good;
                else if (pendingInbound >= min)
                    status = LiquidityStatus.Pending;

                var report = new LiquidityReport
                {
                    LiquidityStatus = status,
                    ActiveInboundSatoshis = activeInbound,
                    PendingInboundSatoshis = pendingInbound
                };
                return report;
            }
            catch (Exception)
            {
                // Return null instead of re-throwing to allow the UI to handle it gracefully.
                return null;
            }
        }
    }
}