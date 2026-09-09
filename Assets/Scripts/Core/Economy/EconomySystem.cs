using System;
using System.Collections.Generic;
using AlpineSim.Core.Sim;

namespace AlpineSim.Core.Economy
{
    /// <summary>
    /// The books. Every dollar moves through Post, which updates Cash and appends a ledger entry
    /// atomically, so the ledger always balances to the cash delta (the closed-book test). Daily
    /// close at 00:00: loan interest and payments, insurance and property tax prorated, wages
    /// owed, the DailyReport, weekly P&amp;L every 7 days, the running season report, net worth,
    /// act progression by net worth and reputation (economy.json acts), bankruptcy check.
    /// Ticks last (after Fleet).
    /// </summary>
    public sealed class EconomySystem : ISimSystem
    {
        public string Name => "Economy";

        public void Initialize(SimContext ctx, bool newGame) => throw new NotImplementedException("M3: EconomySystem.Initialize");
        public void Tick(SimContext ctx, float dt) => throw new NotImplementedException("M3: EconomySystem.Tick");

        public double Cash(SimContext ctx) => ctx.World.Economy.Cash;
        /// <summary>Posts revenue (+) or cost (-) and updates cash. Never bypass this.</summary>
        public void Post(SimContext ctx, LedgerCategory category, double amount, string memo) => throw new NotImplementedException("M3");
        public bool CanAfford(SimContext ctx, double amount) => throw new NotImplementedException("M3");
        /// <summary>Spends if affordable (or within overdraft policy), else returns false with a reason.</summary>
        public bool TrySpend(SimContext ctx, LedgerCategory category, double amount, string memo, out string reason) => throw new NotImplementedException("M3");
        /// <summary>Annual rate offered now: base + debt-ratio penalty - reputation discount.</summary>
        public float QuoteLoanRatePct(SimContext ctx, double principal) => throw new NotImplementedException("M3");
        public Loan TakeLoan(SimContext ctx, double principal, int termDays, string purpose, out string reason) => throw new NotImplementedException("M3");
        public double TotalDebt(SimContext ctx) => throw new NotImplementedException("M3");
        public double NetWorth(SimContext ctx) => throw new NotImplementedException("M3");
        public int Act(SimContext ctx) => ctx.World.Economy.Act;
        public ActDef ActDef(SimContext ctx) => ctx.Data.Economy.Act(ctx.World.Economy.Act);
        public bool CanBuyLiftTier(SimContext ctx, int tier) => throw new NotImplementedException("M3");
        public bool CanBuyVehicleTier(SimContext ctx, int tier) => throw new NotImplementedException("M3");
        public void SetTicketPrice(SimContext ctx, float price) => throw new NotImplementedException("M3");
        public DailyReport LastDay(SimContext ctx) => throw new NotImplementedException("M3");
        /// <summary>Sum of ledger entries for a day (0 = today so far).</summary>
        public double NetForDay(SimContext ctx, int day) => throw new NotImplementedException("M3");
    }
}
