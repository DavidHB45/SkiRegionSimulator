using AlpineSim.Core.Vehicles;
using UnityEngine;

namespace AlpineSim.Unity.Vehicles
{
    /// <summary>
    /// Turns a MeshRecipe (vehicles.json "visual") into chassis meshes, and attachment defs into
    /// implement meshes. Silhouettes are simple, readable primitives: the fleet must be tellable
    /// apart at a glance, not photoreal. Local frame: +Z forward, +Y up, origin at ground under the
    /// chassis centre.
    /// </summary>
    public static class VehicleMeshBuilder
    {
        private static readonly Color32 Glass = new Color32(120, 170, 210, 255);
        private static readonly Color32 Track = new Color32(40, 40, 42, 255);
        private static readonly Color32 Steel = new Color32(150, 150, 155, 255);
        private static readonly Color32 Tire = new Color32(30, 30, 32, 255);

        public static Mesh BuildChassis(VehicleDef def)
        {
            var r = def.Visual ?? new MeshRecipe();
            var pm = new ProceduralMesh();
            var body = ProceduralMesh.Hex(r.ColorHex, new Color32(200, 60, 40, 255));
            var accent = ProceduralMesh.Hex(r.AccentHex, new Color32(45, 55, 70, 255));
            float L = Mathf.Max(0.8f, r.BodyL), W = Mathf.Max(0.5f, r.BodyW), H = Mathf.Max(0.3f, r.BodyH);
            float trackH = Mathf.Max(0.2f, r.TrackH);
            float wheelR = Mathf.Max(0.15f, r.WheelRadiusM);
            switch ((r.Silhouette ?? "truck").ToLowerInvariant())
            {
                case "groomer":
                    Tracks(pm, L * 0.85f, W, trackH, Track);
                    pm.Box(new Vector3(0f, trackH + H * 0.5f, 0f), new Vector3(W * 0.8f, H, L * 0.9f), body);
                    Cab(pm, r, trackH + H, accent, true);
                    pm.Box(new Vector3(0f, trackH + H + 0.15f, -L * 0.25f), new Vector3(W * 0.7f, 0.3f, L * 0.35f), accent); // engine deck
                    break;
                case "carrier":
                case "utv":
                    Tracks(pm, L * 0.8f, W, trackH, Track);
                    pm.Box(new Vector3(0f, trackH + H * 0.5f, 0f), new Vector3(W * 0.85f, H, L * 0.9f), body);
                    Cab(pm, r, trackH + H, accent, false);
                    break;
                case "loader":
                case "telehandler":
                    Wheels(pm, L, W, wheelR, 2, Tire);
                    pm.Box(new Vector3(0f, wheelR + H * 0.5f, -L * 0.1f), new Vector3(W * 0.8f, H, L * 0.75f), body);
                    Cab(pm, r, wheelR + H, accent, true);
                    if (r.Silhouette == "telehandler") pm.Box(new Vector3(-W * 0.2f, wheelR + H + 0.4f, L * 0.1f), new Vector3(0.45f, 0.45f, Mathf.Max(2f, r.BoomLengthM)), accent);
                    else { pm.Beam(new Vector3(W * 0.35f, wheelR + H * 0.8f, L * 0.2f), new Vector3(W * 0.35f, wheelR + H * 1.1f, L * 0.55f), 0.25f, accent); pm.Beam(new Vector3(-W * 0.35f, wheelR + H * 0.8f, L * 0.2f), new Vector3(-W * 0.35f, wheelR + H * 1.1f, L * 0.55f), 0.25f, accent); }
                    break;
                case "truck":
                case "tanker":
                case "mixer":
                case "semi":
                case "lowboy":
                    Wheels(pm, L, W, wheelR, Mathf.Max(2, r.Axles), Tire);
                    pm.Box(new Vector3(0f, wheelR + 0.4f, 0f), new Vector3(W * 0.9f, 0.4f, L), Steel); // frame
                    Cab(pm, r, wheelR + 0.4f, accent, true);
                    if (r.Silhouette == "tanker") pm.Cylinder(new Vector3(0f, wheelR + 0.6f + H * 0.5f, -L * 0.2f), H * 0.5f, L * 0.55f, 2, 12, body);
                    else if (r.Silhouette == "mixer") pm.Cylinder(new Vector3(0f, wheelR + 0.8f + H * 0.5f, -L * 0.2f), H * 0.55f, L * 0.5f, 2, 12, body);
                    else if (r.Silhouette == "lowboy") pm.Box(new Vector3(0f, wheelR + 0.5f, -L * 0.3f), new Vector3(W, 0.2f, L * 0.55f), body);
                    else if (r.Silhouette != "semi") pm.Box(new Vector3(0f, wheelR + 0.6f + H * 0.5f, -L * 0.2f), new Vector3(W * 0.95f, H, L * 0.55f), body); // bed
                    break;
                case "pickup":
                    Wheels(pm, L, W, wheelR, 2, Tire);
                    pm.Box(new Vector3(0f, wheelR + 0.35f + H * 0.5f, 0f), new Vector3(W * 0.95f, H, L), body);
                    Cab(pm, r, wheelR + 0.35f + H, accent, true);
                    break;
                case "tractor":
                    pm.Cylinder(new Vector3(W * 0.45f, wheelR, -L * 0.25f), wheelR, 0.5f, 0, 12, Tire);
                    pm.Cylinder(new Vector3(-W * 0.45f, wheelR, -L * 0.25f), wheelR, 0.5f, 0, 12, Tire);
                    pm.Cylinder(new Vector3(W * 0.4f, wheelR * 0.6f, L * 0.3f), wheelR * 0.6f, 0.3f, 0, 10, Tire);
                    pm.Cylinder(new Vector3(-W * 0.4f, wheelR * 0.6f, L * 0.3f), wheelR * 0.6f, 0.3f, 0, 10, Tire);
                    pm.Box(new Vector3(0f, wheelR * 0.7f + H * 0.5f, L * 0.1f), new Vector3(W * 0.5f, H, L * 0.8f), body);
                    Cab(pm, r, wheelR * 0.7f + H, accent, true);
                    break;
                case "snowmobile":
                    pm.Box(new Vector3(0f, 0.25f, 0f), new Vector3(W, 0.35f, L * 0.75f), Track);
                    pm.Box(new Vector3(0f, 0.55f, 0.1f), new Vector3(W * 0.7f, 0.35f, L * 0.7f), body);
                    pm.Box(new Vector3(W * 0.45f, 0.1f, L * 0.4f), new Vector3(0.15f, 0.08f, 0.9f), Steel);
                    pm.Box(new Vector3(-W * 0.45f, 0.1f, L * 0.4f), new Vector3(0.15f, 0.08f, 0.9f), Steel);
                    pm.Box(new Vector3(0f, 0.9f, L * 0.25f), new Vector3(0.5f, 0.3f, 0.1f), Glass);
                    break;
                case "atv":
                    Wheels(pm, L, W, wheelR, 2, Tire);
                    pm.Box(new Vector3(0f, wheelR + 0.2f, 0f), new Vector3(W * 0.6f, 0.4f, L * 0.8f), body);
                    break;
                case "walkbehind":
                    pm.Box(new Vector3(0f, 0.3f, 0f), new Vector3(W, 0.5f, L * 0.7f), body);
                    pm.Beam(new Vector3(0.25f, 0.5f, -L * 0.3f), new Vector3(0.25f, 1.0f, -L * 0.8f), 0.04f, Steel);
                    pm.Beam(new Vector3(-0.25f, 0.5f, -L * 0.3f), new Vector3(-0.25f, 1.0f, -L * 0.8f), 0.04f, Steel);
                    pm.Box(new Vector3(0f, 0.75f, L * 0.4f), new Vector3(W, 0.35f, 0.3f), accent); // chute
                    break;
                case "excavator":
                case "drill":
                    Tracks(pm, L * 0.8f, W, trackH, Track);
                    pm.Box(new Vector3(0f, trackH + H * 0.5f, -L * 0.1f), new Vector3(W * 0.9f, H, L * 0.7f), body);
                    Cab(pm, r, trackH + H, accent, false);
                    if (r.Silhouette == "drill") pm.Box(new Vector3(0f, trackH + H + Mathf.Max(3f, r.BoomLengthM) * 0.5f, L * 0.2f), new Vector3(0.5f, Mathf.Max(3f, r.BoomLengthM), 0.5f), accent);
                    else
                    {
                        float boom = Mathf.Max(3f, r.BoomLengthM);
                        pm.Box(new Vector3(0f, trackH + H + boom * 0.3f, L * 0.2f + boom * 0.35f), new Vector3(0.4f, 0.5f, boom * 0.8f), Quaternion.Euler(-35f, 0f, 0f), accent);
                        pm.Box(new Vector3(0f, trackH + H * 0.5f, L * 0.2f + boom * 0.8f), new Vector3(0.9f, 0.6f, 0.8f), Steel);
                    }
                    break;
                case "crane":
                    Wheels(pm, L, W, wheelR, Mathf.Max(2, r.Axles), Tire);
                    pm.Box(new Vector3(0f, wheelR + 0.4f, 0f), new Vector3(W * 0.9f, 0.5f, L), Steel);
                    Cab(pm, r, wheelR + 0.4f, accent, true);
                    pm.Box(new Vector3(0f, wheelR + 0.8f + Mathf.Max(6f, r.BoomLengthM) * 0.35f, -L * 0.2f), new Vector3(0.6f, Mathf.Max(6f, r.BoomLengthM), 0.6f), Quaternion.Euler(-30f, 0f, 0f), body);
                    break;
                case "grader":
                    Wheels(pm, L, W, wheelR, 3, Tire);
                    pm.Box(new Vector3(0f, wheelR + 0.6f, 0f), new Vector3(W * 0.5f, 0.4f, L), body);
                    Cab(pm, r, wheelR + 0.8f, accent, true);
                    pm.Box(new Vector3(0f, wheelR * 0.5f, 0f), new Vector3(W * 1.3f, 0.5f, 0.15f), Quaternion.Euler(0f, 25f, 0f), Steel); // mid blade
                    break;
                case "blower":
                    Wheels(pm, L, W, wheelR, 2, Tire);
                    pm.Box(new Vector3(0f, wheelR + H * 0.5f, -L * 0.1f), new Vector3(W * 0.9f, H, L * 0.7f), body);
                    Cab(pm, r, wheelR + H, accent, true);
                    pm.Box(new Vector3(0f, wheelR * 0.8f, L * 0.45f), new Vector3(W, wheelR * 1.4f, 0.9f), accent); // intake drum
                    pm.Box(new Vector3(W * 0.3f, wheelR + H + 0.5f, L * 0.3f), new Vector3(0.4f, 1.2f, 0.4f), Steel); // chute
                    break;
                case "gun":
                    pm.Box(new Vector3(0f, 0.2f, 0f), new Vector3(W, 0.3f, L), Steel); // sled/carriage
                    pm.Box(new Vector3(0f, 0.9f, 0f), new Vector3(0.3f, 1.2f, 0.3f), Steel);
                    pm.Cylinder(new Vector3(0f, 1.6f, 0.2f), 0.45f, 1.4f, 2, 14, body);
                    break;
                case "tower":
                    pm.Cylinder(new Vector3(0f, 4f, 0f), 0.2f, 8f, 1, 8, Steel);
                    pm.Cylinder(new Vector3(0f, 8.3f, 0.3f), 0.45f, 1.4f, 2, 14, body);
                    break;
                case "station":
                    pm.Box(new Vector3(0f, H * 0.5f, 0f), new Vector3(W, H, L), body);
                    pm.Wedge(new Vector3(0f, H + 0.4f, 0f), new Vector3(W * 1.05f, 0.8f, L * 1.05f), accent);
                    break;
                case "sled":
                case "trailer":
                    pm.Box(new Vector3(0f, 0.25f, 0f), new Vector3(W, 0.2f, L), body);
                    pm.Box(new Vector3(W * 0.45f, 0.08f, 0f), new Vector3(0.1f, 0.1f, L), Steel);
                    pm.Box(new Vector3(-W * 0.45f, 0.08f, 0f), new Vector3(0.1f, 0.1f, L), Steel);
                    if (r.Silhouette == "trailer") Wheels(pm, L, W, wheelR, 1, Tire);
                    break;
                default:
                    Wheels(pm, L, W, wheelR, 2, Tire);
                    pm.Box(new Vector3(0f, wheelR + H * 0.5f, 0f), new Vector3(W, H, L), body);
                    Cab(pm, r, wheelR + H, accent, true);
                    break;
            }
            return pm.Build("chassis_" + def.Id);
        }

        private static void Tracks(ProceduralMesh pm, float len, float width, float trackH, Color32 col)
        {
            float tw = Mathf.Max(0.3f, width * 0.32f);
            pm.Box(new Vector3(width * 0.5f - tw * 0.5f, trackH * 0.5f, 0f), new Vector3(tw, trackH, len), col);
            pm.Box(new Vector3(-width * 0.5f + tw * 0.5f, trackH * 0.5f, 0f), new Vector3(tw, trackH, len), col);
            // sprocket hint
            pm.Box(new Vector3(width * 0.5f - tw * 0.5f, trackH * 0.55f, len * 0.5f), new Vector3(tw * 0.6f, trackH * 0.5f, 0.3f), ProceduralMesh.Shade(col, 1.6f));
            pm.Box(new Vector3(-width * 0.5f + tw * 0.5f, trackH * 0.55f, len * 0.5f), new Vector3(tw * 0.6f, trackH * 0.5f, 0.3f), ProceduralMesh.Shade(col, 1.6f));
        }

        private static void Wheels(ProceduralMesh pm, float len, float width, float r, int axles, Color32 col)
        {
            float tw = Mathf.Max(0.2f, r * 0.55f);
            for (int a = 0; a < axles; a++)
            {
                float z = axles == 1 ? 0f : -len * 0.35f + a * (len * 0.7f / (axles - 1));
                pm.Cylinder(new Vector3(width * 0.5f - tw * 0.5f, r, z), r, tw, 0, 12, col);
                pm.Cylinder(new Vector3(-width * 0.5f + tw * 0.5f, r, z), r, tw, 0, 12, col);
            }
        }

        private static void Cab(ProceduralMesh pm, MeshRecipe r, float baseY, Color32 accent, bool windows)
        {
            float cl = Mathf.Max(0.5f, r.CabL), cw = Mathf.Max(0.5f, r.CabW), ch = Mathf.Max(0.5f, r.CabH);
            pm.Box(new Vector3(0f, baseY + ch * 0.5f, r.CabOffset), new Vector3(cw, ch, cl), accent);
            if (windows)
            {
                pm.Box(new Vector3(0f, baseY + ch * 0.62f, r.CabOffset + cl * 0.5f + 0.02f), new Vector3(cw * 0.85f, ch * 0.5f, 0.04f), Glass);
                pm.Box(new Vector3(cw * 0.5f + 0.02f, baseY + ch * 0.62f, r.CabOffset), new Vector3(0.04f, ch * 0.5f, cl * 0.8f), Glass);
                pm.Box(new Vector3(-cw * 0.5f - 0.02f, baseY + ch * 0.62f, r.CabOffset), new Vector3(0.04f, ch * 0.5f, cl * 0.8f), Glass);
            }
        }

        /// <summary>Implement mesh with its origin at the mount point (attachment root); extends forward (+Z) for front slots, backward for rear.</summary>
        public static Mesh BuildAttachment(AttachmentDef att, SlotPosition slot)
        {
            var pm = new ProceduralMesh();
            var col = ProceduralMesh.Hex(att.Visual != null ? att.Visual.ColorHex : null, new Color32(230, 180, 40, 255));
            float w = Mathf.Max(0.5f, att.WorkingWidthM);
            float dir = slot == SlotPosition.Rear ? -1f : 1f;
            switch (att.Kind)
            {
                case AttachmentKind.Tiller:
                case AttachmentKind.TrackSetter:
                case AttachmentKind.PipeCutter:
                    pm.Box(new Vector3(0f, 0.35f, dir * 0.8f), new Vector3(w, 0.35f, 0.5f), col);
                    pm.Box(new Vector3(0f, 0.15f, dir * 1.15f), new Vector3(w, 0.06f, 0.35f), Steel); // finisher flap
                    pm.Beam(new Vector3(0f, 0.6f, 0f), new Vector3(0f, 0.6f, dir * 0.8f), 0.18f, Steel);
                    if (att.Kind == AttachmentKind.TrackSetter) { pm.Box(new Vector3(0.6f, 0.1f, dir * 1.3f), new Vector3(0.25f, 0.2f, 0.4f), Steel); pm.Box(new Vector3(-0.6f, 0.1f, dir * 1.3f), new Vector3(0.25f, 0.2f, 0.4f), Steel); }
                    break;
                case AttachmentKind.BlowerHead:
                    pm.Box(new Vector3(0f, 0.45f, dir * 0.6f), new Vector3(w, 0.9f, 0.7f), col);
                    pm.Box(new Vector3(w * 0.3f, 1.2f, dir * 0.4f), new Vector3(0.3f, 0.8f, 0.3f), Steel);
                    break;
                case AttachmentKind.SnowBucket:
                case AttachmentKind.LightBucket:
                    pm.Box(new Vector3(0f, 0.3f, dir * 0.55f), new Vector3(w, 0.08f, 1.0f), col);
                    pm.Box(new Vector3(0f, 0.6f, dir * 0.1f), new Vector3(w, 0.7f, 0.08f), col);
                    pm.Box(new Vector3(w * 0.5f, 0.5f, dir * 0.5f), new Vector3(0.06f, 0.6f, 1.0f), col);
                    pm.Box(new Vector3(-w * 0.5f, 0.5f, dir * 0.5f), new Vector3(0.06f, 0.6f, 1.0f), col);
                    break;
                case AttachmentKind.Forks:
                    pm.Box(new Vector3(0f, 0.5f, 0f), new Vector3(w, 1.0f, 0.1f), col);
                    pm.Box(new Vector3(w * 0.25f, 0.06f, dir * 0.6f), new Vector3(0.12f, 0.06f, 1.2f), Steel);
                    pm.Box(new Vector3(-w * 0.25f, 0.06f, dir * 0.6f), new Vector3(0.12f, 0.06f, 1.2f), Steel);
                    break;
                case AttachmentKind.Spreader:
                    pm.Box(new Vector3(0f, 0.9f, dir * 0.9f), new Vector3(w, 1.2f, 1.6f), col);
                    pm.Cylinder(new Vector3(0f, 0.3f, dir * 1.7f), 0.3f, 0.1f, 1, 10, Steel);
                    break;
                case AttachmentKind.BrineTank:
                case AttachmentKind.PumpSkid:
                    pm.Cylinder(new Vector3(0f, 0.8f, dir * 1.0f), 0.6f, 1.8f, 2, 12, col);
                    break;
                case AttachmentKind.Winch:
                    pm.Box(new Vector3(0f, 0.4f, 0f), new Vector3(1.2f, 0.8f, 1.2f), col);
                    pm.Beam(new Vector3(0f, 0.8f, 0f), new Vector3(0f, 2.6f, 0f), 0.15f, Steel);
                    break;
                case AttachmentKind.Broom:
                    pm.Cylinder(new Vector3(0f, 0.4f, dir * 0.6f), 0.4f, w, 0, 10, col);
                    break;
                case AttachmentKind.Auger:
                    pm.Cylinder(new Vector3(0f, 1.2f, dir * 0.6f), 0.25f, 2.4f, 1, 8, col);
                    break;
                case AttachmentKind.LightTower:
                    pm.Beam(new Vector3(0f, 0f, dir * 0.3f), new Vector3(0f, 4.5f, dir * 0.3f), 0.12f, Steel);
                    pm.Box(new Vector3(0f, 4.6f, dir * 0.3f), new Vector3(1.2f, 0.3f, 0.3f), col);
                    break;
                case AttachmentKind.TowerJib:
                    pm.Box(new Vector3(0f, 0.6f, dir * 1.8f), new Vector3(0.3f, 0.3f, Mathf.Max(3f, att.Effects.ReachM)), Quaternion.Euler(-25f, 0f, 0f), col);
                    break;
                case AttachmentKind.Hitch:
                case AttachmentKind.SledHitch:
                    pm.Beam(new Vector3(0f, 0.4f, 0f), new Vector3(0f, 0.4f, dir * 0.6f), 0.1f, Steel);
                    break;
                case AttachmentKind.CableReel:
                    pm.Cylinder(new Vector3(0f, 0.9f, dir * 1.0f), 0.8f, 1.2f, 0, 12, col);
                    break;
                case AttachmentKind.Grapple:
                    pm.Box(new Vector3(0f, 0.5f, dir * 0.5f), new Vector3(w, 0.15f, 0.9f), col);
                    pm.Box(new Vector3(0f, 0.9f, dir * 0.3f), new Vector3(w * 0.8f, 0.15f, 0.6f), Quaternion.Euler(30f * dir, 0f, 0f), col);
                    break;
                case AttachmentKind.Mulcher:
                    pm.Box(new Vector3(0f, 0.45f, dir * 0.6f), new Vector3(w, 0.8f, 0.9f), col);
                    break;
                default: // blades, plows, wings, box pusher, park blade
                    {
                        float h = att.Kind == AttachmentKind.VPlow ? 1.1f : 0.9f;
                        if (att.Kind == AttachmentKind.VPlow)
                        {
                            pm.Box(new Vector3(w * 0.25f, h * 0.5f, dir * 0.45f), new Vector3(w * 0.55f, h, 0.1f), Quaternion.Euler(0f, 35f * dir, 0f), col);
                            pm.Box(new Vector3(-w * 0.25f, h * 0.5f, dir * 0.45f), new Vector3(w * 0.55f, h, 0.1f), Quaternion.Euler(0f, -35f * dir, 0f), col);
                        }
                        else if (att.Kind == AttachmentKind.UBlade || att.Kind == AttachmentKind.BoxPusher)
                        {
                            pm.Box(new Vector3(0f, h * 0.5f, dir * 0.6f), new Vector3(w, h, 0.1f), col);
                            pm.Box(new Vector3(w * 0.5f, h * 0.5f, dir * 0.2f), new Vector3(0.1f, h, 0.9f), col);
                            pm.Box(new Vector3(-w * 0.5f, h * 0.5f, dir * 0.2f), new Vector3(0.1f, h, 0.9f), col);
                        }
                        else pm.Box(new Vector3(0f, h * 0.5f, dir * 0.6f), new Vector3(w, h, 0.12f), col);
                        pm.Beam(new Vector3(0.4f, 0.5f, 0f), new Vector3(0.4f, 0.5f, dir * 0.6f), 0.12f, Steel);
                        pm.Beam(new Vector3(-0.4f, 0.5f, 0f), new Vector3(-0.4f, 0.5f, dir * 0.6f), 0.12f, Steel);
                        break;
                    }
            }
            return pm.Build("att_" + att.Id);
        }
    }
}
