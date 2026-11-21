// CS2 prefab catalogue: every building and static-object prefab with what CS1 → CS2 matching needs.
//
// Source: the managed prefabs (PrefabSystem.m_Prefabs). A prefab's authoring components (ComponentBase) carry
// everything matching needs: UIObject (menu group), ServiceObject (service), SpawnableBuilding (zone, level),
// ThemeObject, ContentPrerequisite (DLC), AssetPackItem, and the service components with capacities and ranges
// (Hospital.m_PatientCapacity, School.m_StudentCapacity, ServiceCoverage.m_Range, …). Those are written generically
// (every primitive / enum / string field, prefab references by name), so new CS2 components need no code here.
// The ECS side adds the measured size (ObjectGeometryData.m_Size, from the meshes).
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Game.Prefabs;
using Unity.Entities;
using Unity.Mathematics;

namespace Cs2Buildings
{
    internal static class Catalogue
    {
        const BindingFlags Inst = BindingFlags.Public | BindingFlags.Instance;

        public static List<PrefabBase> AllPrefabs(PrefabSystem ps)
        {
            var f = typeof(PrefabSystem).GetField("m_Prefabs", BindingFlags.NonPublic | BindingFlags.Instance);
            var list = f != null ? f.GetValue(ps) as List<PrefabBase> : null;
            if (list == null) throw new InvalidOperationException("PrefabSystem.m_Prefabs not found");
            return new List<PrefabBase>(list);
        }

        /// <summary>Which prefabs go in: buildings (and their extensions) always; static objects when includeObjects.</summary>
        static string Kind(PrefabBase p)
        {
            if (p is BuildingPrefab) return "building";
            if (p is BuildingExtensionPrefab) return "buildingExtension";
            if (p is StaticObjectPrefab) return "object";
            return null;
        }

        public static Dictionary<string, object> Build(EntityManager em, PrefabSystem ps, bool includeObjects, out int buildings, out int objects)
        {
            buildings = 0;
            objects = 0;
            var items = new List<object>();
            var errors = new List<object>();
            foreach (var p in AllPrefabs(ps))
            {
                if (p == null) continue;
                var kind = Kind(p);
                if (kind == null || (kind == "object" && !includeObjects)) continue;
                try
                {
                    items.Add(Item(em, ps, p, kind));
                    if (kind == "object") objects++; else buildings++;
                }
                catch (Exception e)
                {
                    if (errors.Count < 50) errors.Add(p.name + ": " + e.GetType().Name + ": " + e.Message);
                }
            }
            items.Sort((a, b) => string.CompareOrdinal((string)((Dictionary<string, object>)a)["name"], (string)((Dictionary<string, object>)b)["name"]));
            return new Dictionary<string, object>
            {
                { "format", "cs2-catalogue" },
                { "formatVersion", 1 },
                { "generatedAt", DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture) },
                { "game", new Dictionary<string, object> { { "version", GameVersion() } } },
                { "counts", new Dictionary<string, object> { { "buildings", buildings }, { "objects", objects } } },
                { "errors", errors },
                { "prefabs", items },
            };
        }

        static string GameVersion()
        {
            try { return Game.Version.current.fullVersion; } catch { }
            try { return UnityEngine.Application.version; } catch { return null; }
        }

        static Dictionary<string, object> Item(EntityManager em, PrefabSystem ps, PrefabBase p, string kind)
        {
            var d = new Dictionary<string, object>();
            d["name"] = p.name;
            d["kind"] = kind;
            d["type"] = p.GetType().Name;

            var bp = p as BuildingPrefab;
            if (bp != null)
            {
                d["lot"] = new List<object> { bp.m_LotWidth, bp.m_LotDepth };
                d["accessType"] = bp.m_AccessType.ToString();
            }

            // Measured size from the ECS prefab entity (x = width, y = height, z = depth, metres).
            Entity e;
            if (ps.TryGetEntity(p, out e) && em.HasComponent<ObjectGeometryData>(e))
            {
                var g = em.GetComponentData<ObjectGeometryData>(e);
                d["size"] = new List<object> { R(g.m_Size.x), R(g.m_Size.y), R(g.m_Size.z) };
                d["height"] = R(g.m_Size.y);
            }

            var comps = new Dictionary<string, object>();
            var flags = new List<object>();
            if (p.components != null)
            {
                foreach (var c in p.components)
                {
                    if (c == null) continue;
                    var cname = c.GetType().Name;
                    var fields = Fields(c);
                    if (fields.Count == 0) flags.Add(cname); else comps[cname] = fields;

                    var ui = c as UIObject;
                    if (ui != null)
                    {
                        d["uiGroup"] = ui.m_Group != null ? ui.m_Group.name : null;
                        object menu = ui.m_Group != null ? FieldValue(ui.m_Group, "m_Menu") : null;
                        d["uiMenu"] = menu is PrefabBase ? ((PrefabBase)menu).name : null;
                        d["uiPriority"] = ui.m_Priority;
                    }
                    var so = c as ServiceObject;
                    if (so != null) d["service"] = so.m_Service != null ? so.m_Service.name : null;
                    var sb = c as SpawnableBuilding;
                    if (sb != null)
                    {
                        d["zone"] = sb.m_ZoneType != null ? sb.m_ZoneType.name : null;
                        d["level"] = (int)sb.m_Level;
                    }
                    var th = c as ThemeObject;
                    if (th != null) d["theme"] = th.m_Theme != null ? th.m_Theme.name : null;
                    var cp = c as ContentPrerequisite;
                    if (cp != null) d["content"] = cp.m_ContentPrerequisite != null ? cp.m_ContentPrerequisite.name : null;
                    var ap = c as AssetPackItem;
                    if (ap != null && ap.m_Packs != null)
                    {
                        var packs = new List<object>();
                        foreach (var x in ap.m_Packs) if (x != null) packs.Add(x.name);
                        d["assetPacks"] = packs;
                    }
                }
            }
            d["components"] = comps;
            d["flags"] = flags;
            // Placeable = the player can build it from a menu: a PlaceableObject with a UI group, not a zoned (spawnable)
            // building and not a service upgrade (extensions like "MedicalClinic01 Extension Wing" attach to a parent).
            bool upgrade = comps.ContainsKey("ServiceUpgrade") || flags.Contains("ServiceUpgrade");
            d["upgrade"] = upgrade;
            // Signature buildings (SignatureBuilding) are built from the menu too, without a PlaceableObject component.
            bool signature = comps.ContainsKey("SignatureBuilding") || flags.Contains("SignatureBuilding");
            d["signature"] = signature;
            d["placeable"] = d.ContainsKey("uiGroup") && d["uiGroup"] != null && (comps.ContainsKey("PlaceableObject") || flags.Contains("PlaceableObject") || signature)
                && !upgrade && !comps.ContainsKey("SpawnableBuilding") && !flags.Contains("SpawnableBuilding");
            d["capacity"] = Capacity(comps);
            return d;
        }

        /// <summary>One comparable number per service family, from the service components.</summary>
        static Dictionary<string, object> Capacity(Dictionary<string, object> comps)
        {
            var c = new Dictionary<string, object>();
            Take(c, comps, "Hospital", "m_PatientCapacity", "patients");
            Take(c, comps, "School", "m_StudentCapacity", "students");
            Take(c, comps, "PoliceStation", "m_JailCapacity", "jail");
            Take(c, comps, "PoliceStation", "m_PatrolCarCapacity", "patrolCars");
            Take(c, comps, "Prison", "m_PrisonerCapacity", "prisoners");
            Take(c, comps, "FireStation", "m_FireEngineCapacity", "fireEngines");
            Take(c, comps, "DeathcareFacility", "m_StorageCapacity", "bodies");
            Take(c, comps, "DeathcareFacility", "m_HearseCapacity", "hearses");
            Take(c, comps, "GarbageFacility", "m_GarbageCapacity", "garbage");
            Take(c, comps, "GarbageFacility", "m_VehicleCapacity", "garbageTrucks");
            Take(c, comps, "PowerPlant", "m_ElectricityProduction", "electricity");
            Take(c, comps, "WaterPumpingStation", "m_Capacity", "water");
            Take(c, comps, "SewageOutlet", "m_Capacity", "sewage");
            Take(c, comps, "TransportDepot", "m_VehicleCapacity", "depotVehicles");
            Take(c, comps, "PostFacility", "m_MailStorageCapacity", "mail");
            Take(c, comps, "EmergencyShelter", "m_ShelterCapacity", "shelter");
            Take(c, comps, "ParkingFacility", "m_GarageMarkerCapacity", "parking");
            Take(c, comps, "ServiceCoverage", "m_Range", "coverageRange");
            Take(c, comps, "TelecomFacility", "m_Range", "telecomRange");
            return c;
        }

        static void Take(Dictionary<string, object> dst, Dictionary<string, object> comps, string comp, string field, string key)
        {
            object o;
            if (!comps.TryGetValue(comp, out o)) return;
            var f = o as Dictionary<string, object>;
            object v;
            if (f != null && f.TryGetValue(field, out v) && v != null) dst[key] = v;
        }

        /// <summary>Primitive, enum and string fields; prefab references (and arrays of them) by name.</summary>
        static Dictionary<string, object> Fields(object c)
        {
            var d = new Dictionary<string, object>();
            foreach (var f in c.GetType().GetFields(Inst))
            {
                if (f.DeclaringType == typeof(ComponentBase) || f.DeclaringType == typeof(UnityEngine.Object) || f.DeclaringType == typeof(UnityEngine.ScriptableObject)) continue;
                object v;
                try { v = f.GetValue(c); } catch { continue; }
                var t = f.FieldType;
                if (t == typeof(bool) || t == typeof(string)) d[f.Name] = v;
                else if (t == typeof(float) || t == typeof(double)) d[f.Name] = R(Convert.ToDouble(v));
                else if (t.IsPrimitive) d[f.Name] = Convert.ToInt64(v);
                else if (t.IsEnum) d[f.Name] = v.ToString();
                else if (t == typeof(float2)) { var x = (float2)v; d[f.Name] = new List<object> { R(x.x), R(x.y) }; }
                else if (t == typeof(float3)) { var x = (float3)v; d[f.Name] = new List<object> { R(x.x), R(x.y), R(x.z) }; }
                else if (typeof(PrefabBase).IsAssignableFrom(t)) d[f.Name] = v != null ? ((PrefabBase)v).name : null;
                else if (t.IsArray && typeof(PrefabBase).IsAssignableFrom(t.GetElementType()))
                {
                    var arr = new List<object>();
                    var a = v as Array;
                    if (a != null) foreach (var x in a) arr.Add(x != null ? ((PrefabBase)x).name : null);
                    d[f.Name] = arr;
                }
            }
            return d;
        }

        static object FieldValue(object o, string name)
        {
            for (var t = o.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                if (f != null) return f.GetValue(o);
            }
            return null;
        }

        static double R(double v) { return Math.Round(v, 3); }
    }
}
