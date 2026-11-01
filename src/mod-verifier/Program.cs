// Verifies a Motorsport Manager mod DLL against the game's Assembly-CSharp.
//  (a) Hook targets: every [HarmonyPatch(typeof(T), "Method")] class names a method that exists
//      exactly once on T, and every patch parameter (other than __instance/__result/etc.) matches
//      a parameter name on the target. Extra field names can be checked with --field Type.field.
//  (b) References: every TypeRef/MemberRef in the mod that points into Assembly-CSharp exists.
// Usage: Verify <mod.dll> <Managed dir> <extra dir>... [--field Type.field]...
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

class Program
{
    const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    static int Main(string[] args)
    {
        string modPath = args[0];
        var dirs = args.Skip(1).TakeWhile(a => !a.StartsWith("--")).ToList();
        var fieldChecks = new List<string>();
        for (int i = 0; i < args.Length - 1; i++) if (args[i] == "--field") fieldChecks.Add(args[i + 1]);

        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        byName[Path.GetFileNameWithoutExtension(modPath)] = modPath;
        foreach (var d in dirs) foreach (var p in Directory.GetFiles(d, "*.dll")) { var n = Path.GetFileNameWithoutExtension(p); if (!byName.ContainsKey(n)) byName[n] = p; }
        using var mlc = new MetadataLoadContext(new NameResolver(byName), "mscorlib");
        var mod = mlc.LoadFromAssemblyPath(modPath);
        var game = mlc.LoadFromAssemblyPath(byName["Assembly-CSharp"]);
        int fails = 0, hooks = 0;

        Console.WriteLine("== (a) Hook targets");
        foreach (var t in mod.GetTypes())
        {
            foreach (var ca in t.GetCustomAttributesData().Where(c => c.AttributeType.Name == "HarmonyPatch"))
            {
                var a = ca.ConstructorArguments;
                if (a.Count < 2 || !(a[0].Value is Type target) || !(a[1].Value is string method)) continue;
                hooks++;
                var found = target.GetMethods(All).Where(m => m.Name == method).ToList();
                if (a.Count >= 3 && a[2].Value is System.Collections.Generic.IReadOnlyCollection<CustomAttributeTypedArgument> argTypes)
                {
                    var names = argTypes.Select(x => ((Type)x.Value).FullName).ToList();
                    found = found.Where(m => m.GetParameters().Select(p => p.ParameterType.FullName).SequenceEqual(names)).ToList();
                }
                if (found.Count != 1) { Console.WriteLine($"FAIL {t.Name}: {target.Name}.{method} found {found.Count}x"); fails++; continue; }
                var targetParams = new HashSet<string>(found[0].GetParameters().Select(p => p.Name));
                foreach (var pm in t.GetMethods(All).Where(m => m.Name == "Prefix" || m.Name == "Postfix"))
                    foreach (var p in pm.GetParameters())
                        if (!p.Name.StartsWith("__") && !targetParams.Contains(p.Name))
                        { Console.WriteLine($"FAIL {t.Name}.{pm.Name}: parameter '{p.Name}' not on {target.Name}.{method}"); fails++; }
                Console.WriteLine($"ok   {target.Name}.{method}({string.Join(", ", found[0].GetParameters().Select(p => p.ParameterType.Name + " " + p.Name))}) -> {found[0].ReturnType.Name}");
            }
        }
        foreach (var fc in fieldChecks)
        {
            var dot = fc.LastIndexOf('.');
            var f = game.GetType(fc.Substring(0, dot))?.GetField(fc.Substring(dot + 1), All);
            Console.WriteLine(f != null ? $"ok   field {fc} : {f.FieldType.Name}" : $"FAIL field {fc} missing");
            if (f == null) fails++;
        }
        Console.WriteLine(fails == 0 ? $"ALL TARGETS OK ({hooks} hooks, {fieldChecks.Count} fields)" : $"{fails} TARGET PROBLEM(S)");

        Console.WriteLine("== (b) References into Assembly-CSharp, -firstpass, UnityEngine and UnityEngine.UI");
        var asms = new Dictionary<string, System.Reflection.Assembly>();
        System.Reflection.Assembly Asm(string n) { if (!asms.TryGetValue(n, out var a)) { a = byName.ContainsKey(n) ? mlc.LoadFromAssemblyPath(byName[n]) : null; asms[n] = a; } return a; }
        using var fs = File.OpenRead(modPath);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();
        int refs = 0, missing = 0;
        foreach (var h in md.TypeReferences)
        {
            var an = GameAsm(md, h); if (an == null || Asm(an) == null) continue;
            refs++;
            if (Asm(an).GetType(FullName(md, h)) == null) { Console.WriteLine($"MISSING type {FullName(md, h)}"); missing++; }
        }
        foreach (var h in md.MemberReferences)
        {
            var mr = md.GetMemberReference(h);
            if (mr.Parent.Kind != HandleKind.TypeReference) continue;
            var parent = (TypeReferenceHandle)mr.Parent;
            var pan = GameAsm(md, parent); if (pan == null || Asm(pan) == null) continue;
            refs++;
            var type = Asm(pan).GetType(FullName(md, parent));
            var name = md.GetString(mr.Name);
            bool ok;
            if (mr.GetKind() == MemberReferenceKind.Field) ok = FindField(type, name);
            else
            {
                var blob = md.GetBlobReader(mr.Signature);
                var header = blob.ReadSignatureHeader();
                if (header.IsGeneric) blob.ReadCompressedInteger();
                int pc = blob.ReadCompressedInteger();
                ok = name == ".ctor" ? type?.GetConstructors(All).Any(c => c.GetParameters().Length == pc) == true : FindMethod(type, name, pc);
            }
            if (!ok) { Console.WriteLine($"MISSING member {FullName(md, parent)}::{name}"); missing++; }
        }
        Console.WriteLine($"{refs - missing}/{refs} references resolve, {missing} missing");
        return fails + missing == 0 ? 0 : 1;
    }

    static readonly HashSet<string> Checked = new HashSet<string> { "Assembly-CSharp", "Assembly-CSharp-firstpass", "UnityEngine", "UnityEngine.UI" };

    static string GameAsm(MetadataReader md, TypeReferenceHandle h)
    {
        var scope = md.GetTypeReference(h).ResolutionScope;
        if (scope.Kind == HandleKind.TypeReference) return GameAsm(md, (TypeReferenceHandle)scope);
        if (scope.Kind != HandleKind.AssemblyReference) return null;
        var n = md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)scope).Name);
        return Checked.Contains(n) ? n : null;
    }

    static string FullName(MetadataReader md, TypeReferenceHandle h)
    {
        var tr = md.GetTypeReference(h);
        var name = md.GetString(tr.Name); var ns = md.GetString(tr.Namespace);
        if (tr.ResolutionScope.Kind == HandleKind.TypeReference) return FullName(md, (TypeReferenceHandle)tr.ResolutionScope) + "+" + name;
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    static bool FindField(Type t, string name)
    {
        for (; t != null; t = t.BaseType) if (t.GetField(name, All) != null) return true;
        return false;
    }

    static bool FindMethod(Type t, string name, int pc)
    {
        for (; t != null; t = t.BaseType) if (t.GetMethods(All).Any(m => m.Name == name && m.GetParameters().Length == pc)) return true;
        return false;
    }
}

// Unity's mscorlib/System are 2.0.5.0 with a different public key than the net35 reference
// assemblies the mod compiles against, so resolve by simple name only.
class NameResolver : MetadataAssemblyResolver
{
    readonly Dictionary<string, string> mPaths;
    public NameResolver(Dictionary<string, string> paths) { mPaths = paths; }
    public override Assembly Resolve(MetadataLoadContext context, AssemblyName name)
    {
        return mPaths.TryGetValue(name.Name, out var p) ? context.LoadFromAssemblyPath(p) : null;
    }
}
