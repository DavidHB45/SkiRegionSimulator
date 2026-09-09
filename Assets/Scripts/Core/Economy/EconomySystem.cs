using System;
using System.Collections.Generic;
using AlpineSim.Core.Math;
using AlpineSim.Core.Sim;

namespace AlpineSim.Core.Economy
{
    /// <summary>
    /// The books. Every dollar moves through Post, which updates Cash and appends a ledger entry
    /// atomically, so the ledger always balances to the cash delta (the closed-book test). Daily
    /// close at 00:00: loan interest and payments, insurance and property tax prorated, the
    /// DailyReport, weekly P&amp;L every 7 days, the running season report, net worth, act
    /// progression by net worth and reputation (economy.json acts), bankruptcy check.
    /// Ticks last (after Fleet).
    /// </summary>
    public sealed class EconomySystem : ISimSystem
    {
        public string Name => "Economy";

        /// <summary>Optional valuation hooks supplied by other systems (fleet resale, lift book value).</summary>
        public Func<SimContext, double> FleetValuation;
        public Func<SimContext, double> LiftValuation;

        public void Initialize(SimContext ctx, bool newGame)
        {
            var e = ctx.World.Economy;
            if (newGame)
            {
                var scen = ctx.Sim.Scenario;
                e.Cash = scen.StartCash;
                e.CashAtDayStart = e.Cash;
                e.TicketPrice = scen.StartTicketPrice > 0f ? scen.StartTicketPrice : ctx.Data.Economy.Act(scen.StartingAct).TicketPriceBase;
                e.HalfDayPrice = e.TicketPrice * ctx.Data.Economy.HalfDayFactor;
                e.SeasonPassPrice = e.TicketPrice * ctx.Data.Economy.SeasonPassFactor;
                e.Act = System.Math.Max(1, scen.StartingAct);
                e.Season = new PeriodReport { Label = "Season", DayStart = ctx.Time.Day };
            }
        }

        public void Tick(SimContext ctx, float dt)
        {
            var time = ctx.Time;
            if (time.IsDayStart) CloseDay(ctx);
            if (time.IsHourStart && time.HourOfDay == 12) UpdateNetWorth(ctx);
        }

        // ------------------------------------------------------------------ posting
        public double Cash(SimContext ctx) => ctx.World.Economy.Cash;

        public void Post(SimContext ctx, LedgerCategory category, double amount, string memo)
        {
            if (System.Math.Abs(amount) < 1e-9) return;
            var e = ctx.World.Economy;
            var entry = new LedgerEntry { Day = ctx.Time.Day, Hour = ctx.Time.HourOfDay, Tick = ctx.Time.Tick, Category = category, Amount = amount, Memo = memo ?? "" };
            e.Ledger.Add(entry);
            e.Cash += amount;
            if (amount >= 0) { e.TodayRevenue += amount; e.TotalRevenue += amount; }
            else { e.TodayCosts -= amount; e.TotalCosts -= amount; }
            ctx.Events.Publish(new LedgerPostedEvent { Entry = entry, CashAfter = e.Cash });
        }

        public bool CanAfford(SimContext ctx, double amount) => ctx.World.Economy.Cash >= amount - 1e-6;

        public bool TrySpend(SimContext ctx, LedgerCategory category, double amount, string memo, out string reason)
        {
            reason = "";
            if (amount < 0) amount = -amount;
            if (!CanAfford(ctx, amount))
            {
                reason = "not enough cash: need " + Money(amount) + ", have " + Money(ctx.World.Economy.Cash);
                return false;
            }
            Post(ctx, category, -amount, memo);
            return true;
        }

        public static string Money(double v) => (v < 0 ? "-$" : "$") + System.Math.Abs(v).ToString("#,0", System.Globalization.CultureInfo.InvariantCulture);

        // ------------------------------------------------------------------ loans
        public double TotalDebt(SimContext ctx)
        {
            double d = 0;
            foreach (var l in ctx.World.Economy.Loans) if (!l.Closed) d += l.Balance;
            return d;
        }

        public float QuoteLoanRatePct(SimContext ctx, double principal)
        {
            var data = ctx.Data.Economy;
            double debt = TotalDebt(ctx) + principal;
            double equity = System.Math.Max(1.0, NetWorth(ctx) + TotalDebt(ctx));
            float debtRatio = (float)MathUtil.Clamp((float)(debt / equity), 0f, 2f);
            float rep = ctx.World.Guests != null ? ctx.World.Guests.Reputation : 50f;
            float rate = data.LoanBaseRatePct + data.LoanDebtRatioPenaltyPct * debtRatio - data.LoanReputationDiscountPct * MathUtil.Clamp((rep - 50f) / 50f, -1f, 1f);
            return MathF.Max(1f, rate);
        }

        public Loan TakeLoan(SimContext ctx, double principal, int termDays, string purpose, out string reason)
        {
            reason = "";
            var e = ctx.World.Economy;
            var act = ctx.Data.Economy.Act(e.Act);
            if (principal <= 0) { reason = "principal must be positive"; return null; }
            if (TotalDebt(ctx) + principal > act.MaxLoan) { reason = "the bank will not lend more than " + Money(act.MaxLoan) + " total in " + act.DisplayName; return null; }
            if (termDays <= 0) termDays = ctx.Data.Economy.LoanTermDaysDefault;
            float rate = QuoteLoanRatePct(ctx, principal);
            double r = rate / 100.0 / 365.0;
            double payment = r > 0 ? principal * r / (1.0 - System.Math.Pow(1.0 + r, -termDays)) : principal / termDays;
            var loan = new Loan { Id = e.NextLoanId++, Principal = principal, Balance = principal, AnnualRatePct = rate, TermDays = termDays, DailyPayment = payment, StartDay = ctx.Time.Day, Purpose = purpose ?? "" };
            e.Loans.Add(loan);
            Post(ctx, LedgerCategory.LoanPrincipal, principal, "Loan drawn: " + loan.Purpose + " at " + rate.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%");
            ctx.Sim.Log("Loan of " + Money(principal) + " at " + rate.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "% over " + termDays + " days.");
            return loan;
        }

        // ------------------------------------------------------------------ valuation / acts
        public double NetWorth(SimContext ctx)
        {
            var e = ctx.World.Economy;
            double fleet = FleetValuation != null ? FleetValuation(ctx) : DefaultFleetValue(ctx);
            double lifts = LiftValuation != null ? LiftValuation(ctx) : e.LiftBookValue;
            return e.Cash + fleet + lifts - TotalDebt(ctx);
        }

        private static double DefaultFleetValue(SimContext ctx)
        {
            double v = 0;
            if (ctx.World.Vehicles == null) return 0;
            foreach (var veh in ctx.World.Vehicles.List)
            {
                var def = ctx.Data.Vehicle(veh.DefId);
                if (def == null || veh.Leased || veh.Rented) continue;
                v += def.PurchasePrice * def.ResaleFraction(veh.HoursMeter) * (0.5 + 0.5 * veh.Condition.ConditionPct / 100.0);
            }
            return v;
        }

        private void UpdateNetWorth(SimContext ctx)
        {
            var e = ctx.World.Economy;
            e.FleetBookValue = FleetValuation != null ? FleetValuation(ctx) : DefaultFleetValue(ctx);
            if (LiftValuation != null) e.LiftBookValue = LiftValuation(ctx);
            e.NetWorth = NetWorth(ctx);
            // act progression: capital and reputation, never a quest flag
            var acts = ctx.Data.Economy.Acts;
            float rep = ctx.World.Guests != null ? ctx.World.Guests.Reputation : 50f;
            foreach (var a in acts)
            {
                if (a.Act <= e.Act) continue;
                if (a.Act != e.Act + 1) continue;
                if (e.NetWorth >= a.MinNetWorth && rep >= a.MinReputation)
                {
                    e.Act = a.Act;
                    ctx.Events.Publish(new ActChangedEvent { Act = a.Act, Name = a.DisplayName });
                    ctx.Sim.Log("Act " + a.Act + ": " + a.DisplayName + ". New lift and machine tiers are available.", LogLevel.Alert);
                }
            }
        }

        public int Act(SimContext ctx) => ctx.World.Economy.Act;
        public ActDef ActDef(SimContext ctx) => ctx.Data.Economy.Act(ctx.World.Economy.Act);
        public bool CanBuyLiftTier(SimContext ctx, int tier) => tier <= ActDef(ctx).MaxLiftTier;
        public bool CanBuyVehicleTier(SimContext ctx, int tier) => tier <= ActDef(ctx).MaxVehicleTier;

        public void SetTicketPrice(SimContext ctx, float price)
        {
            var e = ctx.World.Economy;
            var d = ctx.Data.Economy;
            e.TicketPrice = MathUtil.Clamp(price, d.TicketPriceMin, d.TicketPriceMax);
            e.HalfDayPrice = e.TicketPrice * d.HalfDayFactor;
            e.SeasonPassPrice = e.TicketPrice * d.SeasonPassFactor;
        }

        public DailyReport LastDay(SimContext ctx)
        {
            var d = ctx.World.Economy.Daily;
            return d.Count > 0 ? d[d.Count - 1] : null;
        }

        public double NetForDay(SimContext ctx, int day)
        {
            double sum = 0;
            foreach (var en in ctx.World.Economy.Ledger) if (en.Day == day) sum += en.Amount;
            return sum;
        }

        // ------------------------------------------------------------------ daily close
        private void CloseDay(SimContext ctx)
        {
            var e = ctx.World.Economy;
            var data = ctx.Data.Economy;
            int closedDay = ctx.Time.Day - 1;
            if (closedDay < 0) return;
            float seasonDays = System.Math.Max(30, ctx.Data.Climate.SeasonLengthDays);
            // fixed daily costs
            Post(ctx, LedgerCategory.Insurance, -data.InsuranceBaseAnnual / 365.0, "Resort liability insurance (daily)");
            Post(ctx, LedgerCategory.PropertyTax, -data.PropertyTaxAnnual / 365.0, "Property tax (daily)");
            // loans
            foreach (var l in e.Loans)
            {
                if (l.Closed) continue;
                double interest = l.Balance * l.AnnualRatePct / 100.0 / 365.0;
                double principal = System.Math.Min(l.Balance, l.DailyPayment - interest);
                if (principal < 0) principal = 0;
                Post(ctx, LedgerCategory.LoanInterest, -interest, "Interest on loan #" + l.Id);
                if (principal > 0) Post(ctx, LedgerCategory.LoanPrincipal, -principal, "Principal on loan #" + l.Id);
                l.Balance -= principal;
                if (l.Balance <= 0.5) { l.Balance = 0; l.Closed = true; ctx.Sim.Log("Loan #" + l.Id + " repaid."); }
            }
            // report
            var rep = new DailyReport { Day = closedDay, CashStart = e.CashAtDayStart, CashEnd = e.Cash };
            foreach (var en in e.Ledger)
            {
                if (en.Day != closedDay) continue;
                rep.Entries++;
                if (en.Amount >= 0) rep.Revenue += en.Amount; else rep.Costs -= en.Amount;
                string k = en.Category.ToString();
                rep.ByCategory[k] = (rep.ByCategory.TryGetValue(k, out var cur) ? cur : 0) + en.Amount;
            }
            rep.Net = rep.Revenue - rep.Costs;
            rep.Guests = ctx.World.Guests != null && ctx.World.Guests.History.Count > 0 ? ctx.World.Guests.History[ctx.World.Guests.History.Count - 1].Arrived : 0;
            rep.AvgPqi = ctx.World.Pistes != null ? ctx.World.Pistes.ResortPqi : 0f;
            rep.Reputation = ctx.World.Guests != null ? ctx.World.Guests.Reputation : 50f;
            e.Daily.Add(rep);
            e.DaysInBusiness++;
            e.TodayRevenue = 0; e.TodayCosts = 0;
            e.CashAtDayStart = e.Cash;
            // season aggregate
            Accumulate(e.Season, rep);
            e.Season.DayEnd = closedDay;
            // weekly
            if ((closedDay + 1) % 7 == 0)
            {
                var week = new PeriodReport { Label = "Week " + ((closedDay + 1) / 7), DayStart = closedDay - 6, DayEnd = closedDay };
                foreach (var d in e.Daily) if (d.Day >= week.DayStart && d.Day <= week.DayEnd) Accumulate(week, d);
                e.Weekly.Add(week);
                ctx.Events.Publish(new WeekClosedEvent { Report = week });
                ctx.Sim.Log(week.Label + ": revenue " + Money(week.Revenue) + ", costs " + Money(week.Costs) + ", net " + Money(week.Net) + ".");
            }
            // trim ledger
            int cutoff = closedDay - e.LedgerRetentionDays;
            if (cutoff > 0) e.Ledger.RemoveAll(en => en.Day < cutoff);
            UpdateNetWorth(ctx);
            ctx.Events.Publish(new DayClosedEvent { Report = rep });
            ctx.Sim.Log("Day " + (closedDay + 1) + " closed: revenue " + Money(rep.Revenue) + ", costs " + Money(rep.Costs) + ", cash " + Money(e.Cash) + ".");
            double floor = -ctx.Data.Economy.Act(e.Act).MaxLoan;
            if (e.Cash < floor && !e.Bankrupt)
            {
                e.Bankrupt = true;
                ctx.Events.Publish(new BankruptcyEvent { Cash = e.Cash });
                ctx.Sim.Log("The bank has called the overdraft. The resort is insolvent.", LogLevel.Alert);
            }
            else if (e.Cash >= floor) e.Bankrupt = false;
        }

        private static void Accumulate(PeriodReport p, DailyReport d)
        {
            p.Revenue += d.Revenue; p.Costs += d.Costs; p.Net += d.Net; p.Guests += d.Guests;
            p.AvgPqi = p.AvgPqi <= 0f ? d.AvgPqi : (p.AvgPqi * 0.8f + d.AvgPqi * 0.2f);
            foreach (var kv in d.ByCategory) p.ByCategory[kv.Key] = (p.ByCategory.TryGetValue(kv.Key, out var cur) ? cur : 0) + kv.Value;
        }
    }
}
