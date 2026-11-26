namespace iMobileDevice.Managed.Plist;

using System.Collections;

public enum PlistNodeKind
{
    Boolean,
    Integer,
    Real,
    String,
    Data,
    Date,
    Uid,
    Array,
    Dictionary,
}

public abstract class PlistNode
{
    public abstract PlistNodeKind Kind { get; }

    public PlistDictionary AsDictionary() => (PlistDictionary)this;
    public PlistArray AsArray() => (PlistArray)this;
    public string AsString() => ((PlistString)this).Value;
    public long AsInt64() => ((PlistInteger)this).Int64Value;
    public ulong AsUInt64() => ((PlistInteger)this).UInt64Value;
    public double AsReal() => ((PlistReal)this).Value;
    public bool AsBoolean() => ((PlistBoolean)this).Value;
    public byte[] AsData() => ((PlistData)this).Value;
    public DateTimeOffset AsDate() => ((PlistDate)this).Value;
    public ulong AsUid() => ((PlistUid)this).Value;

    public static implicit operator PlistNode(string value) => new PlistString(value);
    public static implicit operator PlistNode(bool value) => new PlistBoolean(value);
    public static implicit operator PlistNode(int value) => new PlistInteger(value);
    public static implicit operator PlistNode(long value) => new PlistInteger(value);
    public static implicit operator PlistNode(ulong value) => new PlistInteger(value);
    public static implicit operator PlistNode(double value) => new PlistReal(value);
    public static implicit operator PlistNode(byte[] value) => new PlistData(value);
    public static implicit operator PlistNode(DateTimeOffset value) => new PlistDate(value);
}

public sealed class PlistBoolean : PlistNode
{
    public static readonly PlistBoolean True = new(true);
    public static readonly PlistBoolean False = new(false);

    public PlistBoolean(bool value) => Value = value;

    public override PlistNodeKind Kind => PlistNodeKind.Boolean;
    public bool Value { get; }

    public override string ToString() => Value ? "true" : "false";
}

public sealed class PlistInteger : PlistNode
{
    public PlistInteger(long value)
    {
        Int64Value = value;
        UInt64Value = unchecked((ulong)value);
        IsUnsigned = false;
    }

    public PlistInteger(ulong value)
    {
        UInt64Value = value;
        Int64Value = unchecked((long)value);
        IsUnsigned = value > long.MaxValue;
    }

    public override PlistNodeKind Kind => PlistNodeKind.Integer;
    public long Int64Value { get; }
    public ulong UInt64Value { get; }
    public bool IsUnsigned { get; }

    public override string ToString() => IsUnsigned ? UInt64Value.ToString() : Int64Value.ToString();
}

public sealed class PlistReal : PlistNode
{
    public PlistReal(double value) => Value = value;

    public override PlistNodeKind Kind => PlistNodeKind.Real;
    public double Value { get; }

    public override string ToString() => Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class PlistString : PlistNode
{
    public PlistString(string value) => Value = value ?? throw new ArgumentNullException(nameof(value));

    public override PlistNodeKind Kind => PlistNodeKind.String;
    public string Value { get; }

    public override string ToString() => Value;
}

public sealed class PlistData : PlistNode
{
    public PlistData(byte[] value) => Value = value ?? throw new ArgumentNullException(nameof(value));

    public override PlistNodeKind Kind => PlistNodeKind.Data;
    public byte[] Value { get; }

    public override string ToString() => $"<data {Value.Length} bytes>";
}

public sealed class PlistDate : PlistNode
{
    /// <summary>
    /// Apple CoreFoundation absolute reference epoch: 2001-01-01T00:00:00Z.
    /// </summary>
    public static readonly DateTimeOffset AppleEpoch = new(2001, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public PlistDate(DateTimeOffset value) => Value = value.ToUniversalTime();

    public override PlistNodeKind Kind => PlistNodeKind.Date;
    public DateTimeOffset Value { get; }

    public override string ToString() => Value.ToString("yyyy-MM-ddTHH:mm:ssZ");
}

public sealed class PlistUid : PlistNode
{
    public PlistUid(ulong value) => Value = value;

    public override PlistNodeKind Kind => PlistNodeKind.Uid;
    public ulong Value { get; }

    public override string ToString() => $"UID({Value})";
}

public sealed class PlistArray : PlistNode, IList<PlistNode>
{
    private readonly List<PlistNode> items = new();

    public PlistArray()
    {
    }

    public PlistArray(IEnumerable<PlistNode> items)
    {
        this.items.AddRange(items);
    }

    public override PlistNodeKind Kind => PlistNodeKind.Array;

    public PlistNode this[int index]
    {
        get => items[index];
        set => items[index] = value;
    }

    public int Count => items.Count;
    public bool IsReadOnly => false;

    public void Add(PlistNode item) => items.Add(item);
    public void Clear() => items.Clear();
    public bool Contains(PlistNode item) => items.Contains(item);
    public void CopyTo(PlistNode[] array, int arrayIndex) => items.CopyTo(array, arrayIndex);
    public IEnumerator<PlistNode> GetEnumerator() => items.GetEnumerator();
    public int IndexOf(PlistNode item) => items.IndexOf(item);
    public void Insert(int index, PlistNode item) => items.Insert(index, item);
    public bool Remove(PlistNode item) => items.Remove(item);
    public void RemoveAt(int index) => items.RemoveAt(index);
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public sealed class PlistDictionary : PlistNode, IDictionary<string, PlistNode>
{
    private readonly Dictionary<string, PlistNode> entries = new(StringComparer.Ordinal);

    public override PlistNodeKind Kind => PlistNodeKind.Dictionary;

    public PlistNode this[string key]
    {
        get => entries[key];
        set => entries[key] = value;
    }

    public ICollection<string> Keys => entries.Keys;
    public ICollection<PlistNode> Values => entries.Values;
    public int Count => entries.Count;
    public bool IsReadOnly => false;

    public string? GetString(string key) =>
        entries.TryGetValue(key, out var node) && node is PlistString s ? s.Value : null;

    public long? GetInt64(string key) =>
        entries.TryGetValue(key, out var node) && node is PlistInteger i ? i.Int64Value : null;

    public ulong? GetUInt64(string key) =>
        entries.TryGetValue(key, out var node) && node is PlistInteger i ? i.UInt64Value : null;

    public bool? GetBoolean(string key) =>
        entries.TryGetValue(key, out var node) && node is PlistBoolean b ? b.Value : null;

    public byte[]? GetData(string key) =>
        entries.TryGetValue(key, out var node) && node is PlistData d ? d.Value : null;

    public PlistDictionary? GetDictionary(string key) =>
        entries.TryGetValue(key, out var node) && node is PlistDictionary dict ? dict : null;

    public PlistArray? GetArray(string key) =>
        entries.TryGetValue(key, out var node) && node is PlistArray arr ? arr : null;

    public void Add(string key, PlistNode value) => entries.Add(key, value);
    public void Add(KeyValuePair<string, PlistNode> item) => ((ICollection<KeyValuePair<string, PlistNode>>)entries).Add(item);
    public void Clear() => entries.Clear();
    public bool Contains(KeyValuePair<string, PlistNode> item) => ((ICollection<KeyValuePair<string, PlistNode>>)entries).Contains(item);
    public bool ContainsKey(string key) => entries.ContainsKey(key);
    public void CopyTo(KeyValuePair<string, PlistNode>[] array, int arrayIndex) => ((ICollection<KeyValuePair<string, PlistNode>>)entries).CopyTo(array, arrayIndex);
    public IEnumerator<KeyValuePair<string, PlistNode>> GetEnumerator() => entries.GetEnumerator();
    public bool Remove(string key) => entries.Remove(key);
    public bool Remove(KeyValuePair<string, PlistNode> item) => ((ICollection<KeyValuePair<string, PlistNode>>)entries).Remove(item);
    public bool TryGetValue(string key, out PlistNode value) => entries.TryGetValue(key, out value!);
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
