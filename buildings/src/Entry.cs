// exec entry point of the CS2 buildings library (tools/PROTOCOL.md "exec"). One op per call, on args.op:
//
//   catalogue {path, objects=true}           [phase main]  dump every building / static-object prefab to {path} (JSON)
//   place     {plan, offsetX=0, offsetZ=0, …} [phase tool, pump tool]  place the plan's buildings, then verify;
//                                                           returns the report (and writes it to {report} if given)
//   inspect   {x, z, r=60}                    [phase main]  real buildings near a point (prefab, lot, road edge)
//
// Statics never survive between calls (the bridge loads a fresh copy of the DLL every time).
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Game.Prefabs;
using GameBridge;
using Unity.Entities;

namespace Cs2Buildings
{
    public static class Entry
    {
        public static object Run(Dictionary<string, object> args)
        {
            string op = P.Str(args, "op", "catalogue");
            switch (op)
            {
                case "catalogue": return CatalogueOp(args);
                case "place": return Placement.Place(args);
                case "inspect": return Placement.Inspect(args);
                default: throw new ArgumentException("unknown op '" + op + "' (catalogue, place, inspect)");
            }
        }

        static object CatalogueOp(Dictionary<string, object> a)
        {
            var path = Environment.ExpandEnvironmentVariables(P.Required(a, "path"));
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) throw new InvalidOperationException("no ECS world");
            var ps = world.GetOrCreateSystemManaged<PrefabSystem>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int b, o;
            var cat = Catalogue.Build(world.EntityManager, ps, P.Bool(a, "objects", true), out b, out o);
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, MiniJson.Write(cat), new UTF8Encoding(false));
            return new Dictionary<string, object>
            {
                { "path", Path.GetFullPath(path) },
                { "buildings", b },
                { "objects", o },
                { "errors", ((List<object>)cat["errors"]).Count },
                { "bytes", new FileInfo(path).Length },
                { "ms", sw.ElapsedMilliseconds },
            };
        }
    }
}
