namespace iMobileDevice.Managed.Dvt;

using iMobileDevice.Managed.Plist;

/// <summary>
/// Pure managed Apple <c>NSKeyedArchiver</c> / <c>NSKeyedUnarchiver</c> built on top of <see cref="PlistSerializer"/>.
/// </summary>
public static class NsKeyedArchiver
{
    public static byte[] Archive(object? root)
    {
        var objects = new PlistArray { "$null" };
        var classUids = new Dictionary<string, PlistUid>(StringComparer.Ordinal);

        PlistUid GetOrAddClass(string className, params string[] hierarchy)
        {
            if (classUids.TryGetValue(className, out PlistUid? existing))
            {
                return existing;
            }

            var classesArr = new PlistArray();
            foreach (string c in hierarchy)
            {
                classesArr.Add(c);
            }

            var classDict = new PlistDictionary
            {
                ["$classname"] = className,
                ["$classes"] = classesArr,
            };

            var uid = new PlistUid((ulong)objects.Count);
            objects.Add(classDict);
            classUids[className] = uid;
            return uid;
        }

        PlistUid AddObject(object? value)
        {
            if (value == null)
            {
                return new PlistUid(0);
            }

            switch (value)
            {
                case string s:
                {
                    var uid = new PlistUid((ulong)objects.Count);
                    objects.Add(s);
                    return uid;
                }

                case bool b:
                {
                    var uid = new PlistUid((ulong)objects.Count);
                    objects.Add(b);
                    return uid;
                }

                case int i:
                {
                    var uid = new PlistUid((ulong)objects.Count);
                    objects.Add(i);
                    return uid;
                }

                case long l:
                {
                    var uid = new PlistUid((ulong)objects.Count);
                    objects.Add(l);
                    return uid;
                }

                case ulong ul:
                {
                    var uid = new PlistUid((ulong)objects.Count);
                    objects.Add(ul);
                    return uid;
                }

                case double d:
                {
                    var uid = new PlistUid((ulong)objects.Count);
                    objects.Add(d);
                    return uid;
                }

                case byte[] bytes:
                {
                    var uid = new PlistUid((ulong)objects.Count);
                    objects.Add(bytes);
                    return uid;
                }

                case IDictionary<string, object?> dict:
                {
                    int slot = objects.Count;
                    var placeholder = new PlistDictionary();
                    objects.Add(placeholder);

                    var keys = new PlistArray();
                    var vals = new PlistArray();
                    foreach (var kv in dict)
                    {
                        keys.Add(AddObject(kv.Key));
                        vals.Add(AddObject(kv.Value));
                    }

                    placeholder["NS.keys"] = keys;
                    placeholder["NS.objects"] = vals;
                    placeholder["$class"] = GetOrAddClass("NSDictionary", "NSDictionary", "NSObject");
                    return new PlistUid((ulong)slot);
                }

                case IList<object?> list:
                {
                    int slot = objects.Count;
                    var placeholder = new PlistDictionary();
                    objects.Add(placeholder);

                    var items = new PlistArray();
                    foreach (var item in list)
                    {
                        items.Add(AddObject(item));
                    }

                    placeholder["NS.objects"] = items;
                    placeholder["$class"] = GetOrAddClass("NSArray", "NSArray", "NSObject");
                    return new PlistUid((ulong)slot);
                }

                default:
                    throw new NotSupportedException($"NSKeyedArchiver cannot archive type {value.GetType().Name}.");
            }
        }

        PlistUid rootUid = AddObject(root);
        var top = new PlistDictionary
        {
            ["root"] = rootUid,
        };

        var archive = new PlistDictionary
        {
            ["$version"] = 100000L,
            ["$archiver"] = "NSKeyedArchiver",
            ["$top"] = top,
            ["$objects"] = objects,
        };

        return PlistSerializer.WriteBinary(archive);
    }

    public static object? Unarchive(ReadOnlySpan<byte> data)
    {
        PlistNode parsed = PlistSerializer.Parse(data);
        if (parsed is not PlistDictionary archive || !archive.ContainsKey("$objects"))
        {
            return UnwrapPlainPlist(parsed);
        }

        PlistArray objects = archive.GetArray("$objects") ?? throw new FormatException("NSKeyedArchiver missing $objects.");
        PlistDictionary top = archive.GetDictionary("$top") ?? throw new FormatException("NSKeyedArchiver missing $top.");

        PlistNode? rootRef = top.TryGetValue("root", out var r) ? r : top.Values.FirstOrDefault();
        if (rootRef is PlistUid uid)
        {
            return ResolveObject(objects, (int)uid.Value, 0);
        }

        return rootRef != null ? UnwrapPlainPlist(rootRef) : null;
    }

    private static object? ResolveObject(PlistArray objects, int index, int depth)
    {
        if (depth > 64 || index <= 0 || index >= objects.Count)
        {
            return null;
        }

        PlistNode node = objects[index];
        if (node is PlistString s && s.Value == "$null")
        {
            return null;
        }

        if (node is not PlistDictionary dict)
        {
            return UnwrapPlainPlist(node);
        }

        string? className = null;
        if (dict.TryGetValue("$class", out var clsNode) && clsNode is PlistUid clsUid && (int)clsUid.Value < objects.Count)
        {
            if (objects[(int)clsUid.Value] is PlistDictionary clsDict)
            {
                className = clsDict.GetString("$classname");
            }
        }

        if (className == "NSNull")
        {
            return null;
        }

        if (dict.TryGetValue("NS.data", out var nsData) && nsData is PlistData d)
        {
            return d.Value;
        }

        if (dict.TryGetValue("NS.string", out var nsStr) && nsStr is PlistString st)
        {
            return st.Value;
        }

        if (dict.TryGetValue("NS.keys", out var keysNode) && keysNode is PlistArray keysArr &&
            dict.TryGetValue("NS.objects", out var valsNode) && valsNode is PlistArray valsArr)
        {
            var result = new Dictionary<string, object?>(keysArr.Count, StringComparer.Ordinal);
            int count = Math.Min(keysArr.Count, valsArr.Count);
            for (int i = 0; i < count; i++)
            {
                object? k = ResolveNode(objects, keysArr[i], depth + 1);
                object? v = ResolveNode(objects, valsArr[i], depth + 1);
                if (k != null)
                {
                    result[k.ToString()!] = v;
                }
            }

            return result;
        }

        if (dict.TryGetValue("NS.objects", out var arrNode) && arrNode is PlistArray arr)
        {
            var list = new List<object?>(arr.Count);
            foreach (var item in arr)
            {
                list.Add(ResolveNode(objects, item, depth + 1));
            }

            return list;
        }

        var generic = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (!string.IsNullOrEmpty(className))
        {
            generic["$classname"] = className;
        }

        foreach (var kv in dict)
        {
            if (kv.Key == "$class")
            {
                continue;
            }

            generic[kv.Key] = ResolveNode(objects, kv.Value, depth + 1);
        }

        return generic;
    }

    private static object? ResolveNode(PlistArray objects, PlistNode node, int depth)
    {
        if (node is PlistUid uid)
        {
            return ResolveObject(objects, (int)uid.Value, depth);
        }

        return UnwrapPlainPlist(node);
    }

    private static object? UnwrapPlainPlist(PlistNode node) => node switch
    {
        PlistBoolean b => b.Value,
        PlistInteger i => i.IsUnsigned ? i.UInt64Value : i.Int64Value,
        PlistReal r => r.Value,
        PlistString s => s.Value,
        PlistData d => d.Value,
        PlistDate dt => dt.Value,
        PlistUid u => u.Value,
        PlistArray a => a.Select(UnwrapPlainPlist).ToList(),
        PlistDictionary d => d.ToDictionary(kv => kv.Key, kv => UnwrapPlainPlist(kv.Value), StringComparer.Ordinal),
        _ => null,
    };
}
