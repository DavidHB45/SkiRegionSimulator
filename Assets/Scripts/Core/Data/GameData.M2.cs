using System.Collections.Generic;
using AlpineSim.Core.Fleet;
using AlpineSim.Core.Serialization;
using AlpineSim.Core.Tasks;
using AlpineSim.Core.Vehicles;

namespace AlpineSim.Core.Data
{
    public sealed partial class GameData
    {
        public List<VehicleDef> Vehicles { get; private set; } = new List<VehicleDef>();
        public List<AttachmentDef> Attachments { get; private set; } = new List<AttachmentDef>();
        public StationsData Stations { get; private set; } = new StationsData();
        public TasksData Tasks { get; private set; } = new TasksData();

        private readonly Dictionary<string, VehicleDef> _vehiclesById = new Dictionary<string, VehicleDef>();
        private readonly Dictionary<string, AttachmentDef> _attachmentsById = new Dictionary<string, AttachmentDef>();

        partial void LoadM2()
        {
            var v = ReadJsonOptional("vehicles.json");
            if (v != null) Vehicles = JsonMapper.FromJson<List<VehicleDef>>(v["vehicles"]);
            var a = ReadJsonOptional("attachments.json");
            if (a != null) Attachments = JsonMapper.FromJson<List<AttachmentDef>>(a["attachments"]);
            var s = ReadJsonOptional("stations.json");
            if (s != null) Stations = JsonMapper.FromJson<StationsData>(s);
            var t = ReadJsonOptional("tasks.json");
            if (t != null) Tasks = JsonMapper.FromJson<TasksData>(t);
            _vehiclesById.Clear();
            foreach (var d in Vehicles) _vehiclesById[d.Id] = d;
            _attachmentsById.Clear();
            foreach (var d in Attachments) _attachmentsById[d.Id] = d;
        }

        partial void ValidateM2()
        {
            var ids = new HashSet<string>();
            foreach (var d in Vehicles)
            {
                if (string.IsNullOrEmpty(d.Id)) throw new DataLoadException("vehicles.json: a vehicle has no id");
                if (!ids.Add(d.Id)) throw new DataLoadException("vehicles.json: duplicate id " + d.Id);
                if (d.Roles.Count == 0) Warnings.Add("vehicle " + d.Id + " declares no roles");
                foreach (var slot in d.AttachmentSlots)
                {
                    if (string.IsNullOrEmpty(slot.DefaultAttachmentId)) continue;
                    var att = Attachment(slot.DefaultAttachmentId);
                    if (att == null) throw new DataLoadException("vehicle " + d.Id + " default attachment " + slot.DefaultAttachmentId + " does not exist");
                    if (!att.FitsSlot(slot.Position)) throw new DataLoadException("vehicle " + d.Id + ": attachment " + att.Id + " does not fit slot " + slot.Position);
                }
            }
            var aids = new HashSet<string>();
            foreach (var a in Attachments)
            {
                if (string.IsNullOrEmpty(a.Id)) throw new DataLoadException("attachments.json: an attachment has no id");
                if (!aids.Add(a.Id)) throw new DataLoadException("attachments.json: duplicate id " + a.Id);
                if (a.CompatibleSlots.Count == 0) Warnings.Add("attachment " + a.Id + " has no compatible slots");
            }
        }

        public VehicleDef Vehicle(string id) => id != null && _vehiclesById.TryGetValue(id, out var d) ? d : null;
        public AttachmentDef Attachment(string id) => id != null && _attachmentsById.TryGetValue(id, out var d) ? d : null;
        public VehicleDef RequireVehicle(string id) => Vehicle(id) ?? throw new DataLoadException("Unknown vehicle id '" + id + "'");
        public AttachmentDef RequireAttachment(string id) => Attachment(id) ?? throw new DataLoadException("Unknown attachment id '" + id + "'");
    }
}
