using System.Collections.Generic;
using AlpineSim.Core.Economy;
using AlpineSim.Core.Fleet;
using AlpineSim.Core.Vehicles;
using UnityEngine;
using UnityEngine.UI;

namespace AlpineSim.Unity.UI
{
    /// <summary>
    /// Fleet, garage and market in one window with tabs: machines (condition, service, sell),
    /// market (new, used, lease, rent), attachments (buy, mount, dismount), workshop (tier, bays,
    /// jobs, parts) and fuel (depot, orders, contract).
    /// </summary>
    public sealed class FleetPanel : UiPanel
    {
        private enum Tab { Machines, Market, Attachments, Workshop, Fuel }
        private Tab _tab = Tab.Machines;
        private RectTransform _content;
        private ScrollRect _scroll;
        private Text _summary;
        private float _nextRefresh;
        private int _expandedVehicle = -1;
        private int _mountVehicle = -1;
        private string _filterCategory = "";

        public FleetPanel() : base("Fleet & Garage") { }

        protected override void BuildBody(RectTransform body)
        {
            _summary = UiFactory.Label("Summary", body, "", 13);
            var tabs = UiFactory.Row("Tabs", body, 26f);
            foreach (Tab t in System.Enum.GetValues(typeof(Tab)))
            {
                var tt = t;
                UiFactory.Button("tab" + t, tabs, t.ToString(), () => { _tab = tt; _nextRefresh = 0f; }, 12);
            }
            _content = UiFactory.ScrollView("Content", body, out _scroll);
        }

        public override void Refresh()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + 1f;
            var ctx = Boot.Sim.Ctx;
            if (!Boot.Sim.TryGetSystem<FleetSystem>(out var fs)) return;
            var f = Boot.Sim.World.Fleet;
            var e = Boot.Sim.World.Economy;
            int machines = Boot.Sim.World.Vehicles.List.Count, down = 0;
            foreach (var v in Boot.Sim.World.Vehicles.List) if (!v.IsUsable) down++;
            _summary.text = "<b>" + _tab + "</b>   cash " + UiFactory.Money(e.Cash) + "   " + machines + " machines (" + down + " down)   fleet value " + UiFactory.Money(f.FleetValue) + "   workshop tier " + f.Workshop.Tier + ", " + f.Workshop.GarageBays + " bays" + (f.Workshop.WashBay ? ", wash bay" : "") + "   diesel " + UiFactory.F(f.Fuel.DieselL, 0) + " L";
            ClearChildren(_content);
            switch (_tab)
            {
                case Tab.Machines: Machines(ctx, fs); break;
                case Tab.Market: Market(ctx, fs); break;
                case Tab.Attachments: Attachments(ctx, fs); break;
                case Tab.Workshop: Workshop(ctx, fs); break;
                case Tab.Fuel: Fuel(ctx, fs); break;
            }
        }

        private void Log(string s) => Boot.Sim.Log(s, Core.Sim.LogLevel.Warning);

        // ------------------------------------------------------------------ machines
        private void Machines(Core.Sim.SimContext ctx, FleetSystem fs)
        {
            var vs = Boot.Sim.GetSystem<VehicleSystem>();
            var header = UiFactory.Row("h", _content, 18f);
            UiFactory.RowLabel(header, "Machine", 200f, TextAnchor.MiddleLeft, 11, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Condition", 70f, TextAnchor.MiddleRight, 11, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Hours", 60f, TextAnchor.MiddleRight, 11, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Fuel", 60f, TextAnchor.MiddleRight, 11, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Service in", 70f, TextAnchor.MiddleRight, 11, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Operator / where", 220f, TextAnchor.MiddleLeft, 11, UiFactory.TextDim);
            int n = 0;
            foreach (var v in Boot.Sim.World.Vehicles.List)
            {
                n++;
                var def = Boot.Data.Vehicle(v.DefId);
                if (def == null) continue;
                var row = UiFactory.Row("v" + v.Id, _content, 22f);
                row.gameObject.AddComponent<Image>().color = n % 2 == 0 ? UiFactory.RowBg : UiFactory.RowBgAlt;
                string tag = v.Leased ? " (lease)" : (v.Rented ? " (rental)" : "");
                UiFactory.RowLabel(row, "<b>" + v.Name + "</b> " + def.DisplayName + tag, 200f, TextAnchor.MiddleLeft, 11);
                float cond = v.Condition.ConditionPct;
                UiFactory.RowLabel(row, UiFactory.ColorTag(cond > 60f ? UiFactory.Good : (cond > 35f ? UiFactory.Warn : UiFactory.Bad), UiFactory.F(cond, 0) + "%"), 70f, TextAnchor.MiddleRight, 11);
                UiFactory.RowLabel(row, UiFactory.F(v.HoursMeter, 0), 60f, TextAnchor.MiddleRight, 11);
                float fuelFrac = def.EnergyCapacity > 0f ? v.Fuel / def.EnergyCapacity : 0f;
                UiFactory.RowLabel(row, def.FuelType == FuelType.None ? "-" : UiFactory.ColorTag(fuelFrac < 0.2f ? UiFactory.Bad : UiFactory.TextColor, UiFactory.F(fuelFrac * 100f, 0) + "%"), 60f, TextAnchor.MiddleRight, 11);
                float toService = fs.HoursToService(ctx, v);
                UiFactory.RowLabel(row, UiFactory.ColorTag(toService <= 0f ? UiFactory.Bad : (toService < 25f ? UiFactory.Warn : UiFactory.TextColor), UiFactory.F(toService, 0) + " h"), 70f, TextAnchor.MiddleRight, 11);
                var op = v.OperatorId >= 0 ? Boot.Sim.World.Fleet.Operator(v.OperatorId) : null;
                string where = (v.PlayerControlled ? "you" : (op != null ? op.Name : "nobody")) + " / " + v.LocationLabel + (v.Stranded ? UiFactory.ColorTag(UiFactory.Bad, " STRANDED: " + v.StrandedReason) : "") + (v.Condition.ActiveFailures.Count > 0 ? UiFactory.ColorTag(UiFactory.Bad, " " + v.Condition.ActiveFailures[0].Description) : "");
                UiFactory.RowLabel(row, where, 220f, TextAnchor.MiddleLeft, 11);
                int id = v.Id;
                var more = UiFactory.Button("More", row, _expandedVehicle == id ? "Less" : "More", () => { _expandedVehicle = _expandedVehicle == id ? -1 : id; _nextRefresh = 0f; }, 11);
                more.GetComponent<LayoutElement>().preferredWidth = 50f;
                if (_expandedVehicle == v.Id)
                {
                    var c = v.Condition;
                    UiFactory.Label("wear" + v.Id, _content, "    wear: engine " + UiFactory.F(c.WearEngine * 100f, 0) + "%  hydraulics " + UiFactory.F(c.WearHydraulics * 100f, 0) + "%  drivetrain " + UiFactory.F(c.WearDrivetrain * 100f, 0) + "%  tracks/tyres " + UiFactory.F(c.WearTracks * 100f, 0) + "%  attachment " + UiFactory.F(c.WearAttachment * 100f, 0) + "%   since service " + UiFactory.F(c.HoursSinceService, 0) + " h   " + (v.Sheltered ? "sheltered" : UiFactory.ColorTag(UiFactory.Warn, "outside overnight")) + "   resale " + UiFactory.Money(fs.ResaleValue(ctx, v)), 11);
                    foreach (var m in v.Mounted)
                    {
                        var att = Boot.Data.Attachment(m.DefId);
                        var arow = UiFactory.Row("att" + m.Slot, _content, 20f);
                        UiFactory.RowLabel(arow, "    " + m.Slot + ": " + (att != null ? att.DisplayName : m.DefId), 300f, TextAnchor.MiddleLeft, 11);
                        var slot = m.Slot;
                        var d = UiFactory.Button("Dis", arow, "Dismount (shop)", () => { if (fs.RequestDismount(ctx, id, slot, out string r) == null) Log(r); _nextRefresh = 0f; }, 11);
                        d.GetComponent<LayoutElement>().preferredWidth = 120f;
                    }
                    var act = UiFactory.Row("act" + v.Id, _content, 24f);
                    UiFactory.RowLabel(act, "    ", 20f);
                    UiFactory.Button("PM", act, "Schedule service", () => { if (fs.SchedulePm(ctx, id, out string r) == null) Log(r); _nextRefresh = 0f; }, 11);
                    UiFactory.Button("Rep", act, "Repair", () => { if (fs.RequestRepair(ctx, id, out string r) == null) Log(r); _nextRefresh = 0f; }, 11);
                    UiFactory.Button("Reb", act, "Rebuild", () => { if (fs.RequestRebuild(ctx, id, out string r) == null) Log(r); _nextRefresh = 0f; }, 11);
                    UiFactory.Button("Fuel", act, "Refuel at depot", () => { fs.RefuelAtDepot(ctx, id, out string r); if (!string.IsNullOrEmpty(r)) Log(r); _nextRefresh = 0f; }, 11);
                    UiFactory.Button("Call", act, "Service call", () => { fs.RequestServiceCall(ctx, id, "requested from the fleet screen"); _nextRefresh = 0f; }, 11);
                    UiFactory.Button("Home", act, "Return to base", () => { vs.ReturnToBase(ctx, id); _nextRefresh = 0f; }, 11);
                    UiFactory.Button("Mount", act, "Mount attachment...", () => { _mountVehicle = id; _tab = Tab.Attachments; _nextRefresh = 0f; }, 11);
                    UiFactory.Button("Sell", act, "Sell " + UiFactory.Money(fs.ResaleValue(ctx, v)), () => { if (!fs.Sell(ctx, id, out string r)) Log(r); _expandedVehicle = -1; _nextRefresh = 0f; }, 11, new Color(0.45f, 0.2f, 0.2f));
                }
            }
            if (n == 0) UiFactory.Label("Empty", _content, "No machines. Visit the market.", 12, TextAnchor.UpperLeft, UiFactory.TextDim);
            if (Boot.Sim.World.Fleet.ServiceCalls.Count > 0)
            {
                UiFactory.Spacer(_content, 2f);
                UiFactory.Label("Calls", _content, "<b>Service calls</b>", 12);
                foreach (var call in Boot.Sim.World.Fleet.ServiceCalls)
                {
                    if (call.Resolved) continue;
                    var v = Boot.Sim.World.Vehicles.Get(call.VehicleId);
                    UiFactory.Label("call" + call.Id, _content, "  " + (v != null ? v.Name : "?") + ": " + call.Reason + (call.TaskId >= 0 ? "  (service truck dispatched, job #" + call.TaskId + ")" : UiFactory.ColorTag(UiFactory.Warn, "  waiting for a service truck")), 11);
                }
            }
        }

        // ------------------------------------------------------------------ market
        private void Market(Core.Sim.SimContext ctx, FleetSystem fs)
        {
            var es = Boot.Sim.GetSystem<EconomySystem>();
            var catRow = UiFactory.Row("cats", _content, 24f);
            UiFactory.Button("all", catRow, "All", () => { _filterCategory = ""; _nextRefresh = 0f; }, 11);
            foreach (VehicleCategory c in System.Enum.GetValues(typeof(VehicleCategory)))
            {
                string cc = c.ToString();
                UiFactory.Button("c" + c, catRow, cc, () => { _filterCategory = cc; _nextRefresh = 0f; }, 11);
            }
            var header = UiFactory.Row("h", _content, 18f);
            UiFactory.RowLabel(header, "Listing", 260f, TextAnchor.MiddleLeft, 11, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Kind", 60f, TextAnchor.MiddleLeft, 11, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Price", 90f, TextAnchor.MiddleRight, 11, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Hours / condition", 120f, TextAnchor.MiddleLeft, 11, UiFactory.TextDim);
            UiFactory.RowLabel(header, "Note", 200f, TextAnchor.MiddleLeft, 11, UiFactory.TextDim);
            int n = 0;
            foreach (var l in fs.Market(ctx))
            {
                var def = Boot.Data.Vehicle(l.DefId);
                if (def == null) continue;
                if (!string.IsNullOrEmpty(_filterCategory) && def.Category.ToString() != _filterCategory) continue;
                bool allowed = es == null || es.CanBuyVehicleTier(ctx, def.Tier);
                n++;
                var row = UiFactory.Row("l" + l.Id, _content, 22f);
                row.gameObject.AddComponent<Image>().color = n % 2 == 0 ? UiFactory.RowBg : UiFactory.RowBgAlt;
                UiFactory.RowLabel(row, "T" + def.Tier + " " + def.DisplayName + "  <i>" + def.Category + "</i>", 260f, TextAnchor.MiddleLeft, 11, allowed ? UiFactory.TextColor : UiFactory.TextDim);
                UiFactory.RowLabel(row, l.Kind.ToString(), 60f, TextAnchor.MiddleLeft, 11);
                string price = l.Kind == ListingKind.Lease ? UiFactory.Money(l.MonthlyOrDaily) + "/mo" : (l.Kind == ListingKind.Rent ? UiFactory.Money(l.MonthlyOrDaily) + "/day" : UiFactory.Money(l.Price));
                UiFactory.RowLabel(row, price, 90f, TextAnchor.MiddleRight, 11);
                UiFactory.RowLabel(row, l.Kind == ListingKind.Used ? UiFactory.F(l.Hours, 0) + " h / " + UiFactory.F(l.ConditionPct, 0) + "%" + (l.Inspected ? (l.Defects.Count > 0 ? UiFactory.ColorTag(UiFactory.Bad, " " + l.Defects.Count + " defect(s)") : UiFactory.ColorTag(UiFactory.Good, " clean")) : "") : "new", 120f, TextAnchor.MiddleLeft, 11);
                UiFactory.RowLabel(row, l.SellerNote, 200f, TextAnchor.MiddleLeft, 11, UiFactory.TextDim);
                int id = l.Id; string defId = l.DefId;
                if (!allowed) { UiFactory.RowLabel(row, "locked by act", 90f, TextAnchor.MiddleLeft, 11, UiFactory.TextDim); continue; }
                switch (l.Kind)
                {
                    case ListingKind.New:
                        UiFactory.Button("Buy", row, "Buy", () => { if (fs.BuyNew(ctx, defId, out string r) == null) Log(r); _nextRefresh = 0f; }, 11).GetComponent<LayoutElement>().preferredWidth = 60f;
                        break;
                    case ListingKind.Used:
                        UiFactory.Button("Insp", row, l.Inspected ? "Inspected" : "Inspect", () => { if (!fs.Inspect(ctx, id, out string r)) Log(r); _nextRefresh = 0f; }, 11).GetComponent<LayoutElement>().preferredWidth = 70f;
                        UiFactory.Button("Buy", row, "Buy", () => { if (fs.BuyListing(ctx, id, out string r) == null) Log(r); _nextRefresh = 0f; }, 11).GetComponent<LayoutElement>().preferredWidth = 60f;
                        break;
                    case ListingKind.Lease:
                        UiFactory.Button("Lease", row, "Lease 36 mo", () => { if (fs.Lease(ctx, defId, 36, out string r) == null) Log(r); _nextRefresh = 0f; }, 11).GetComponent<LayoutElement>().preferredWidth = 90f;
                        break;
                    case ListingKind.Rent:
                        UiFactory.Button("Rent", row, "Rent 7 days", () => { if (fs.Rent(ctx, defId, 7, out string r) == null) Log(r); _nextRefresh = 0f; }, 11).GetComponent<LayoutElement>().preferredWidth = 90f;
                        break;
                }
            }
            if (n == 0) UiFactory.Label("Empty", _content, "Nothing listed in this category. Listings refresh every few days.", 12, TextAnchor.UpperLeft, UiFactory.TextDim);
        }

        // ------------------------------------------------------------------ attachments
        private void Attachments(Core.Sim.SimContext ctx, FleetSystem fs)
        {
            var target = _mountVehicle >= 0 ? Boot.Sim.World.Vehicles.Get(_mountVehicle) : null;
            var targetDef = target != null ? Boot.Data.Vehicle(target.DefId) : null;
            UiFactory.Label("Owned", _content, "<b>Owned attachments</b>" + (target != null ? "   mounting onto <b>" + target.Name + "</b> (shop job; the machine must be at the garage)" : "   pick a machine (Machines tab, More, Mount attachment...) to mount"), 12);
            var owned = new List<string>(fs.OwnedAttachments(ctx));
            var mountedIds = new HashSet<string>();
            foreach (var v in Boot.Sim.World.Vehicles.List) foreach (var m in v.Mounted) mountedIds.Add(m.DefId + "#" + v.Id);
            int n = 0;
            foreach (var aid in owned)
            {
                var att = Boot.Data.Attachment(aid);
                if (att == null) continue;
                n++;
                var row = UiFactory.Row("o" + n, _content, 22f);
                row.gameObject.AddComponent<Image>().color = n % 2 == 0 ? UiFactory.RowBg : UiFactory.RowBgAlt;
                UiFactory.RowLabel(row, att.DisplayName + "  <i>" + att.Kind + "</i>  " + UiFactory.F(att.WorkingWidthM, 1) + " m, " + UiFactory.F(att.MassKg, 0) + " kg", 340f, TextAnchor.MiddleLeft, 11);
                if (target != null && targetDef != null)
                {
                    foreach (var slot in targetDef.AttachmentSlots)
                    {
                        if (!att.FitsSlot(slot.Position)) continue;
                        var sp = slot.Position; string id = aid;
                        UiFactory.Button("m", row, "Mount " + sp, () => { if (fs.RequestMount(ctx, target.Id, id, sp, out string r) == null) Log(r); _nextRefresh = 0f; }, 11).GetComponent<LayoutElement>().preferredWidth = 90f;
                    }
                }
            }
            if (n == 0) UiFactory.Label("None", _content, "None in stock.", 11, TextAnchor.UpperLeft, UiFactory.TextDim);
            UiFactory.Spacer(_content, 2f);
            UiFactory.Label("Buy", _content, "<b>Dealer catalogue</b>", 12);
            n = 0;
            foreach (var att in Boot.Data.Attachments)
            {
                n++;
                var row = UiFactory.Row("b" + att.Id, _content, 22f);
                row.gameObject.AddComponent<Image>().color = n % 2 == 0 ? UiFactory.RowBg : UiFactory.RowBgAlt;
                UiFactory.RowLabel(row, att.DisplayName + "  <i>" + att.Kind + "</i>", 240f, TextAnchor.MiddleLeft, 11);
                UiFactory.RowLabel(row, UiFactory.F(att.WorkingWidthM, 1) + " m  " + UiFactory.F(att.MassKg, 0) + " kg  min " + UiFactory.F(att.MinHostPowerKw, 0) + " kW  slots " + string.Join("/", att.CompatibleSlots.ConvertAll(s => s.ToString()).ToArray()), 300f, TextAnchor.MiddleLeft, 11, UiFactory.TextDim);
                string id = att.Id;
                UiFactory.Button("buy", row, "Buy " + UiFactory.Money(att.PurchasePrice), () => { if (!fs.BuyAttachment(ctx, id, out string r)) Log(r); _nextRefresh = 0f; }, 11).GetComponent<LayoutElement>().preferredWidth = 110f;
            }
        }

        // ------------------------------------------------------------------ workshop
        private void Workshop(Core.Sim.SimContext ctx, FleetSystem fs)
        {
            var f = Boot.Sim.World.Fleet;
            var tier = Boot.Data.Stations.Workshop(f.Workshop.Tier);
            UiFactory.Label("Tier", _content, "<b>Workshop tier " + f.Workshop.Tier + " - " + tier.DisplayName + "</b>  " + tier.Bays + " bay(s), repairs " + string.Join(", ", tier.CanRepair.ToArray()) + (tier.CanRebuild ? ", rebuilds" : "") + ".  Garage " + f.Workshop.GarageBays + " sheltered bays" + (f.Workshop.WashBay ? ", wash bay" : ""), 12);
            var up = UiFactory.Row("up", _content, 24f);
            UiFactory.Button("wt", up, "Upgrade workshop", () => { if (!fs.UpgradeWorkshop(ctx, out string r)) Log(r); _nextRefresh = 0f; }, 11);
            UiFactory.Button("gb", up, "Add garage bays " + UiFactory.Money(Boot.Data.Stations.Garage.CostPerBay * Boot.Data.Stations.Garage.BaysPerUpgrade), () => { if (!fs.BuyGarageBays(ctx, out string r)) Log(r); _nextRefresh = 0f; }, 11);
            if (!f.Workshop.WashBay) UiFactory.Button("wb", up, "Build wash bay " + UiFactory.Money(Boot.Data.Stations.WashBay.Cost), () => { if (!fs.BuildWashBay(ctx, out string r)) Log(r); _nextRefresh = 0f; }, 11);
            UiFactory.Spacer(_content, 2f);
            UiFactory.Label("Jobs", _content, "<b>Service jobs</b>", 12);
            int n = 0;
            foreach (var j in fs.Jobs(ctx))
            {
                if (j.Status == ServiceJobStatus.Done || j.Status == ServiceJobStatus.Cancelled) continue;
                n++;
                var v = Boot.Sim.World.Vehicles.Get(j.VehicleId);
                var row = UiFactory.Row("j" + j.Id, _content, 22f);
                row.gameObject.AddComponent<Image>().color = n % 2 == 0 ? UiFactory.RowBg : UiFactory.RowBgAlt;
                UiFactory.RowLabel(row, (v != null ? v.Name : "?") + ": " + j.Kind + (j.Kind == ServiceJobKind.Repair ? " " + j.Subsystem : "") + (string.IsNullOrEmpty(j.AttachmentId) ? "" : " " + j.AttachmentId), 260f, TextAnchor.MiddleLeft, 11);
                Color sc = j.Status == ServiceJobStatus.InProgress ? UiFactory.Accent : (j.Status == ServiceJobStatus.WaitingParts ? UiFactory.Bad : (j.Status == ServiceJobStatus.SentOut ? UiFactory.Warn : UiFactory.TextColor));
                UiFactory.RowLabel(row, UiFactory.ColorTag(sc, j.Status.ToString()) + (j.Status == ServiceJobStatus.SentOut ? " back day " + (j.ReturnDay + 1) : ""), 140f, TextAnchor.MiddleLeft, 11);
                UiFactory.RowLabel(row, UiFactory.F(j.HoursDone, 1) + " / " + UiFactory.F(j.HoursRequired, 1) + " h", 90f, TextAnchor.MiddleLeft, 11);
                UiFactory.RowLabel(row, UiFactory.Money(j.Cost) + (string.IsNullOrEmpty(j.PartsKind) ? "" : "  parts: " + j.PartsKind + " x" + j.PartsQty), 180f, TextAnchor.MiddleLeft, 11, UiFactory.TextDim);
                int id = j.Id;
                UiFactory.Button("x", row, "Cancel", () => { fs.CancelJob(ctx, id); _nextRefresh = 0f; }, 11, new Color(0.45f, 0.2f, 0.2f)).GetComponent<LayoutElement>().preferredWidth = 60f;
            }
            if (n == 0) UiFactory.Label("nj", _content, "No jobs queued.", 11, TextAnchor.UpperLeft, UiFactory.TextDim);
            UiFactory.Spacer(_content, 2f);
            UiFactory.Label("Parts", _content, "<b>Parts stock</b>   auto-order " + (f.AutoOrderParts ? "on" : "off"), 12);
            var autoRow = UiFactory.Row("auto", _content, 22f);
            UiFactory.Button("ao", autoRow, f.AutoOrderParts ? "Disable auto-order" : "Enable auto-order", () => { f.AutoOrderParts = !f.AutoOrderParts; _nextRefresh = 0f; }, 11);
            foreach (var pk in Boot.Data.Stations.PartsKinds)
            {
                var row = UiFactory.Row("pk" + pk.Id, _content, 22f);
                int onOrder = 0; foreach (var o in f.Parts.Orders) if (o.Kind == pk.Id) onOrder += o.Qty;
                UiFactory.RowLabel(row, pk.DisplayName, 160f, TextAnchor.MiddleLeft, 11);
                UiFactory.RowLabel(row, "stock " + f.Parts.Get(pk.Id) + (onOrder > 0 ? "  (+" + onOrder + " on order)" : ""), 160f, TextAnchor.MiddleLeft, 11);
                UiFactory.RowLabel(row, UiFactory.Money(pk.UnitCost) + ", " + UiFactory.F(pk.LeadDays, 0) + " days", 120f, TextAnchor.MiddleLeft, 11, UiFactory.TextDim);
                string id = pk.Id;
                UiFactory.Button("o1", row, "Order 1", () => { if (!fs.OrderParts(ctx, id, 1, out string r)) Log(r); _nextRefresh = 0f; }, 11).GetComponent<LayoutElement>().preferredWidth = 70f;
                UiFactory.Button("o3", row, "Order 3", () => { if (!fs.OrderParts(ctx, id, 3, out string r)) Log(r); _nextRefresh = 0f; }, 11).GetComponent<LayoutElement>().preferredWidth = 70f;
            }
        }

        // ------------------------------------------------------------------ fuel
        private void Fuel(Core.Sim.SimContext ctx, FleetSystem fs)
        {
            var d = Boot.Sim.World.Fleet.Fuel;
            UiFactory.Label("Depot", _content, "<b>Depot</b>  diesel " + UiFactory.F(d.DieselL, 0) + " / " + UiFactory.F(d.DieselCapacityL, 0) + " L   gasoline " + UiFactory.F(d.GasolineL, 0) + " / " + UiFactory.F(d.GasolineCapacityL, 0) + " L\nspot price " + UiFactory.F(d.DieselPricePerL, 2) + "/L" + (d.ContractRemainingL > 0f ? "   contract " + UiFactory.F(d.ContractRemainingL, 0) + " L left at " + UiFactory.F(d.ContractPricePerL, 2) + "/L" : "   no contract") + "\nseason: " + UiFactory.F(d.DieselUsedSeasonL, 0) + " L burned, " + UiFactory.Money(d.FuelSpendSeason) + " spent", 12);
            var row = UiFactory.Row("orders", _content, 24f);
            foreach (float l in new[] { 5000f, 10000f, 20000f })
            {
                float liters = l;
                UiFactory.Button("o" + l, row, "Order " + UiFactory.F(liters, 0) + " L spot", () => { if (!fs.OrderFuel(ctx, liters, false, out string r)) Log(r); _nextRefresh = 0f; }, 11);
            }
            UiFactory.Button("contract", row, d.ContractRemainingL > 0f ? "Order 10k L on contract" : "Sign supply contract", () => { if (d.ContractRemainingL > 0f) { if (!fs.OrderFuel(ctx, 10000f, true, out string r)) Log(r); } else if (!fs.SignFuelContract(ctx, out string r2)) Log(r2); _nextRefresh = 0f; }, 11);
            UiFactory.Button("expand", row, "Expand tank " + UiFactory.Money(Boot.Data.Stations.FuelDepot.UpgradeCostPer10000L), () => { if (!fs.ExpandFuelDepot(ctx, out string r)) Log(r); _nextRefresh = 0f; }, 11);
            UiFactory.Label("Pending", _content, "<b>Deliveries</b>", 12);
            if (d.Pending.Count == 0) UiFactory.Label("np", _content, "None pending. Lead time " + UiFactory.F(Boot.Data.Stations.FuelDepot.DeliveryLeadHours, 0) + " h; a dry depot stops the night shift.", 11, TextAnchor.UpperLeft, UiFactory.TextDim);
            foreach (var p in d.Pending) UiFactory.Label("p" + p.Id, _content, "  " + UiFactory.F(p.Liters, 0) + " L " + p.Fuel + " arriving day " + (p.ArriveDay + 1) + " " + p.ArriveHour.ToString("00") + ":00  " + UiFactory.Money(p.Cost) + (p.Contract ? " (contract)" : " (spot)"), 11);
        }
    }
}
