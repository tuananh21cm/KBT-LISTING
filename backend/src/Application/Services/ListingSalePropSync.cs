using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Application.Services;

/// <summary>
/// Resolved TikTok schema for one sale-prop axis, used to declare axes / values that the
/// previous RawPayload never carried (user added a brand new Color or enabled a new axis).
/// </summary>
public sealed record SalePropAxisSchema(
    string AttrId,
    IReadOnlyDictionary<string, string> ValueIdByLabel);

/// <summary>Axes / labels that could not be resolved from RawPayload alone.</summary>
public sealed class SalePropGaps
{
    /// <summary>Axes in use that have no TikTok attribute id anywhere in RawPayload.</summary>
    public List<string> Axes { get; } = [];

    /// <summary>Labels in use that have no value id in the matching <c>option{N}Value</c> map.</summary>
    public List<(string Axis, string Label)> Labels { get; } = [];

    /// <summary>True when a category-schema lookup is needed to complete the maps.</summary>
    public bool Any => Axes.Count > 0 || Labels.Count > 0;
}

/// <summary>
/// Owns the 4Seller sale-prop block: root <c>option{N}</c> (attrId→axis name) and
/// <c>option{N}Value</c> (valueId→label) maps plus the per-SKU <c>option{N}</c> labels and
/// <c>attrs</c> array. Both maps travel as JSON strings but not in the same container:
/// <c>option{N}</c> is an object, <c>option{N}Value</c> an ordered array of single-pair objects.
/// <para>
/// Push bodies are cloned from the previous 4Seller detail, so anything the edit form removed
/// (a Color, a whole Size axis) survives unless it is rebuilt. Everything here is derived from
/// the live variation set: existing ids are reused so 4Seller keeps recognising known values,
/// unused axes/labels are dropped, and new labels get an id from the category schema.
/// </para>
/// </summary>
public static class ListingSalePropSync
{
    /// <summary>Color axis name as TikTok / the edit form spell it.</summary>
    public const string ColorAxis = "Color";

    /// <summary>Size axis name as TikTok / the edit form spell it.</summary>
    public const string SizeAxis = "Size";

    /// <summary>Number of Items axis name as TikTok / the edit form spell it.</summary>
    public const string ItemsAxis = "Number of Items";

    /// <summary>Golden save-draft slot order: Color→option1, Size→option2, Items→option3.</summary>
    public static readonly string[] AxisOrder = [ColorAxis, SizeAxis, ItemsAxis];

    private const int MaxSlots = 3;

    /// <summary>
    /// Container 4Seller uses for a root sale-prop map. <c>option{N}</c> arrives as an object
    /// (<c>{"100000":"Color"}</c>) while <c>option{N}Value</c> arrives as an ordered array of
    /// single-pair objects (<c>[{"1001073":"Orange"},…]</c>); sending the wrong one still returns
    /// success but the 4Seller edit page can never parse the listing back.
    /// </summary>
    public enum MapShape
    {
        Object,
        Array
    }

    private static string OptionKey(int slot) =>
        slot == 1 ? "option1" : slot == 2 ? "option2" : "option3";

    private static string OptionValueKey(int slot) => $"{OptionKey(slot)}Value";

    /// <summary>
    /// Maps each axis in use to an <c>option{N}</c> slot, keeping the slot the payload already
    /// declares so an existing listing never has its Color and Size labels swapped.
    /// </summary>
    public static Dictionary<string, int> ResolveAxisSlots(
        JsonObject obj,
        IReadOnlyList<IReadOnlyDictionary<string, string>> variationAttrs)
    {
        var used = AxisOrder
            .Where(axis => variationAttrs.Any(attrs => HasValue(attrs, axis)))
            .ToList();

        var slots = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var takenSlots = new HashSet<int>();

        for (var slot = 1; slot <= MaxSlots; slot++)
        {
            var declared = DeclaredAxisName(obj, slot);
            if (declared is null)
            {
                continue;
            }

            var axis = used.FirstOrDefault(a => a.Equals(declared, StringComparison.OrdinalIgnoreCase));
            if (axis is null || slots.ContainsKey(axis))
            {
                continue;
            }

            slots[axis] = slot;
            takenSlots.Add(slot);
        }

        foreach (var axis in used.Where(a => !slots.ContainsKey(a)))
        {
            for (var slot = 1; slot <= MaxSlots; slot++)
            {
                if (takenSlots.Add(slot))
                {
                    slots[axis] = slot;
                    break;
                }
            }
        }

        return slots;
    }

    /// <summary>
    /// Writes the per-SKU option labels for the slots in use and <b>removes</b> the ones whose
    /// axis is gone — a cloned row otherwise keeps e.g. <c>option2 = "1XL"</c> after the Size
    /// axis was deleted on the form.
    /// </summary>
    public static void ApplyVariationOptions(
        JsonObject vObj,
        IReadOnlyDictionary<string, string> attrs,
        IReadOnlyDictionary<string, int> axisSlots)
    {
        for (var slot = 1; slot <= MaxSlots; slot++)
        {
            var axis = AxisForSlot(axisSlots, slot);
            var value = axis is null ? null : Value(attrs, axis);
            if (string.IsNullOrWhiteSpace(value))
            {
                vObj.Remove(OptionKey(slot));
            }
            else
            {
                vObj[OptionKey(slot)] = value;
            }
        }

        RebuildVariationAttrsArray(vObj, axisSlots.Count > 0);
    }

    /// <summary>
    /// <c>attrs</c> must always equal the option labels of the same row. It is rebuilt (not just
    /// filled when missing) because cloned rows arrive with the previous combo's labels.
    /// </summary>
    public static void RebuildVariationAttrsArray(JsonObject vObj, bool hasAxes)
    {
        var labels = new JsonArray();
        for (var slot = 1; slot <= MaxSlots; slot++)
        {
            var label = ReadString(vObj, OptionKey(slot));
            if (!string.IsNullOrWhiteSpace(label))
            {
                labels.Add(label);
            }
        }

        if (labels.Count > 0)
        {
            vObj["attrs"] = labels;
        }
        else if (hasAxes)
        {
            vObj.Remove("attrs");
        }
    }

    /// <summary>
    /// Reports axes / labels that RawPayload cannot resolve on its own, so the caller only pays
    /// for a category-schema round trip when something genuinely new was added.
    /// </summary>
    public static SalePropGaps CollectGaps(
        JsonObject obj,
        IReadOnlyList<IReadOnlyDictionary<string, string>> variationAttrs)
    {
        var gaps = new SalePropGaps();
        var axisSlots = ResolveAxisSlots(obj, variationAttrs);

        foreach (var (axis, slot) in axisSlots)
        {
            // A non-numeric key (4Seller detail echoes `{"Color":"Color"}`) is a display artifact,
            // not a TikTok attribute id — treat it as missing so the schema is consulted.
            if (FindAttrId(obj, slot, axis, numericOnly: true) is null)
            {
                gaps.Axes.Add(axis);
            }

            var known = InvertValueMap(ReadJsonEntries(obj, OptionValueKey(slot)));
            foreach (var label in LabelsForAxis(variationAttrs, axis))
            {
                if (!known.TryGetValue(label, out var existingId))
                {
                    gaps.Labels.Add((axis, label));
                    continue;
                }

                // Size tùy biến (0XL, 1XL…) giữ nhãn làm key — không gọi schema. Color/Items thì
                // label-as-key (`{"White":"White"}`) cần nâng lên id TikTok trước push.
                if (!IsNumericId(existingId) &&
                    !axis.Equals(SizeAxis, StringComparison.OrdinalIgnoreCase))
                {
                    gaps.Labels.Add((axis, label));
                }
            }
        }

        return gaps;
    }

    /// <summary>
    /// Captures each <c>option{N}Value</c> container shape from RawPayload before any rebuild
    /// pass mutates it. Push runs PatchRawPayload then schema completion — both must honour the
    /// shape 4Seller originally sent.
    /// </summary>
    public static Dictionary<int, MapShape> CaptureValueShapes(JsonObject obj)
    {
        var shapes = new Dictionary<int, MapShape>();
        for (var slot = 1; slot <= MaxSlots; slot++)
        {
            shapes[slot] = DetectShape(obj, OptionValueKey(slot), MapShape.Array);
        }

        return shapes;
    }

    /// <summary>
    /// Rebuilds the root sale-prop maps from the live variation set, writing each one back in the
    /// container 4Seller sent (<c>option{N}</c> object, <c>option{N}Value</c> array). Returns axes
    /// that still have no TikTok attribute id (push must be blocked — 4Seller would drop the
    /// dimension).
    /// </summary>
    public static List<string> RebuildRootMaps(
        JsonObject obj,
        IReadOnlyList<IReadOnlyDictionary<string, string>> variationAttrs,
        IReadOnlyDictionary<string, int> axisSlots,
        Func<string, SalePropAxisSchema?>? resolveSchema = null,
        bool allowMintedValueIds = false,
        IReadOnlyDictionary<int, MapShape>? capturedValueShapes = null)
    {
        var unresolvedAxes = new List<string>();

        for (var slot = 1; slot <= MaxSlots; slot++)
        {
            var axis = AxisForSlot(axisSlots, slot);
            if (axis is null)
            {
                // Axis dropped on the form — remove the whole dimension, not just its labels.
                obj.Remove(OptionKey(slot));
                obj.Remove(OptionValueKey(slot));
                continue;
            }

            var schema = resolveSchema?.Invoke(axis);
            var attrId = FindAttrId(obj, slot, axis, numericOnly: true)
                         ?? schema?.AttrId
                         ?? FindAttrId(obj, slot, axis, numericOnly: false);
            if (string.IsNullOrWhiteSpace(attrId))
            {
                unresolvedAxes.Add(axis);
                continue;
            }

            // Shapes must be read before anything is written back over them. When the push pipeline
            // captured shapes from the pre-patch payload, always honour those — a schema pass must
            // not convert an array the edit page sent back into an object.
            var optionShape = DetectShape(obj, OptionKey(slot), MapShape.Object);
            var valueShape = capturedValueShapes?.GetValueOrDefault(slot)
                             ?? DetectShape(obj, OptionValueKey(slot), MapShape.Array);

            var existingEntries = ReadJsonEntries(obj, OptionValueKey(slot));
            var existingValueId = InvertValueMap(existingEntries);
            var nextValues = new List<KeyValuePair<string, string>>();
            var takenIds = new HashSet<string>(StringComparer.Ordinal);
            var mintSeed = MaxNumericKey(existingEntries);
            var skippedLabels = 0;

            foreach (var label in LabelsForAxis(variationAttrs, axis))
            {
                var existing = existingValueId.GetValueOrDefault(label);
                var schemaId = schema?.ValueIdByLabel.GetValueOrDefault(label);

                // A non-numeric key is 4Seller's placeholder for a value it never resolved (same
                // trick as `option{N}` = {"Color":"Color"}), so a real TikTok id from the schema
                // wins over it — that is what the 4Seller edit page itself writes back.
                var valueId = IsNumericId(existing)
                    ? existing
                    : string.IsNullOrWhiteSpace(schemaId) ? existing : schemaId;

                if (string.IsNullOrWhiteSpace(valueId))
                {
                    if (!allowMintedValueIds)
                    {
                        skippedLabels++;
                        continue;
                    }

                    // 4Seller keys a custom sale-prop value by its own label (`{"0XL":"0XL"}`).
                    // A minted number would land in TikTok's id space and point at nothing.
                    valueId = label;
                }

                // Two labels can never share a value id — keep the first and mint for the rest.
                while (takenIds.Contains(valueId))
                {
                    if (!allowMintedValueIds)
                    {
                        valueId = string.Empty;
                        break;
                    }

                    mintSeed++;
                    valueId = mintSeed.ToString(CultureInfo.InvariantCulture);
                }

                if (string.IsNullOrWhiteSpace(valueId))
                {
                    skippedLabels++;
                    continue;
                }

                nextValues.Add(new KeyValuePair<string, string>(valueId, label));
                takenIds.Add(valueId);
            }

            WriteJsonMap(obj, OptionKey(slot), [new KeyValuePair<string, string>(attrId, axis)], optionShape);

            // A label we could not resolve means this is the local-only save (no category schema).
            // Writing the pruned map would drop ids that only 4Seller can hand back, so leave the
            // map alone and let PushAsync rebuild it with the schema.
            if (skippedLabels > 0 && existingEntries.Count > 0)
            {
                continue;
            }

            WriteJsonMap(obj, OptionValueKey(slot), nextValues, valueShape);
        }

        return unresolvedAxes;
    }

    /// <summary>Labels of one axis, ordered by first appearance across the variation rows.</summary>
    public static List<string> LabelsForAxis(
        IReadOnlyList<IReadOnlyDictionary<string, string>> variationAttrs,
        string axis)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var labels = new List<string>();
        foreach (var attrs in variationAttrs)
        {
            var value = Value(attrs, axis);
            if (!string.IsNullOrWhiteSpace(value) && seen.Add(value))
            {
                labels.Add(value);
            }
        }

        return labels;
    }

    /// <summary>Label set declared by <c>option{slot}Value</c> after rebuild, for validation.</summary>
    public static HashSet<string> DeclaredLabels(JsonObject obj, int slot)
    {
        var map = ReadJsonMap(obj, OptionValueKey(slot));
        return new HashSet<string>(
            map.Values.Where(v => !string.IsNullOrWhiteSpace(v)),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Axis assigned to an <c>option{slot}</c> slot, or null when the slot is unused.</summary>
    public static string? AxisForSlot(IReadOnlyDictionary<string, int> axisSlots, int slot)
    {
        foreach (var (axis, assigned) in axisSlots)
        {
            if (assigned == slot)
            {
                return axis;
            }
        }

        return null;
    }

    /// <summary>Per-SKU / root option key for a slot (<c>option1</c>…<c>option3</c>).</summary>
    public static string OptionKeyForSlot(int slot) => OptionKey(slot);

    /// <summary>Number of sale-prop slots 4Seller accepts.</summary>
    public static int SlotCount => MaxSlots;

    private static string? DeclaredAxisName(JsonObject obj, int slot)
    {
        foreach (var value in ReadJsonMap(obj, OptionKey(slot)).Values)
        {
            var match = AxisOrder.FirstOrDefault(a => a.Equals(value, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    /// <summary>Existing attribute id for the axis, from the slot map or any other slot map.</summary>
    private static string? FindAttrId(JsonObject obj, int slot, string axis, bool numericOnly)
    {
        foreach (var candidate in Enumerable.Repeat(slot, 1).Concat(Enumerable.Range(1, MaxSlots)))
        {
            foreach (var (key, value) in ReadJsonMap(obj, OptionKey(candidate)))
            {
                if (!axis.Equals(value, StringComparison.OrdinalIgnoreCase) ||
                    string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                if (numericOnly &&
                    !long.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    continue;
                }

                return key;
            }
        }

        return null;
    }

    private static Dictionary<string, string> InvertValueMap(
        IEnumerable<KeyValuePair<string, string>> entries)
    {
        var byLabel = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, label) in entries)
        {
            if (!string.IsNullOrWhiteSpace(label) && !byLabel.ContainsKey(label))
            {
                byLabel[label] = id;
            }
        }

        return byLabel;
    }

    private static long MaxNumericKey(IEnumerable<KeyValuePair<string, string>> entries)
    {
        var max = 0L;
        foreach (var (key, _) in entries)
        {
            if (long.TryParse(key, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) &&
                parsed > max)
            {
                max = parsed;
            }
        }

        return max;
    }

    /// <summary>Reads a map that RawPayload may hold either as an object or as a JSON string.</summary>
    private static Dictionary<string, string> ReadJsonMap(JsonObject obj, string key)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (entryKey, value) in ReadJsonEntries(obj, key))
        {
            map[entryKey] = value;
        }

        return map;
    }

    /// <summary>
    /// Reads a root sale-prop map in every shape 4Seller uses — an object, an ordered array of
    /// single-pair objects, or a JSON string of either — keeping the payload's own order.
    /// </summary>
    private static List<KeyValuePair<string, string>> ReadJsonEntries(JsonObject obj, string key)
    {
        var entries = new List<KeyValuePair<string, string>>();
        var node = obj[key];

        if (node is JsonValue jv && jv.TryGetValue(out string? raw))
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return entries;
            }

            try
            {
                node = JsonNode.Parse(raw);
            }
            catch (JsonException)
            {
                return entries;
            }
        }

        switch (node)
        {
            case JsonObject mapNode:
                AddEntries(entries, mapNode);
                break;
            case JsonArray arrayNode:
                foreach (var item in arrayNode)
                {
                    if (item is JsonObject pair)
                    {
                        AddEntries(entries, pair);
                    }
                }

                break;
        }

        return entries;
    }

    private static void AddEntries(List<KeyValuePair<string, string>> entries, JsonObject node)
    {
        foreach (var (entryKey, entryValue) in node)
        {
            var value = entryValue?.GetValueKind() == JsonValueKind.String
                ? entryValue.GetValue<string>()
                : entryValue?.ToString();
            if (!string.IsNullOrWhiteSpace(entryKey) && !string.IsNullOrWhiteSpace(value))
            {
                entries.Add(new KeyValuePair<string, string>(entryKey.Trim(), value.Trim()));
            }
        }
    }

    private static bool IsNumericId(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);

    private static MapShape DetectShape(JsonObject obj, string key, MapShape fallback)
    {
        var node = obj[key];
        if (node is JsonValue jv && jv.TryGetValue(out string? raw))
        {
            var trimmed = raw?.TrimStart();
            return string.IsNullOrEmpty(trimmed)
                ? fallback
                : trimmed[0] switch
                {
                    '[' => MapShape.Array,
                    '{' => MapShape.Object,
                    _ => fallback
                };
        }

        return node switch
        {
            JsonArray => MapShape.Array,
            JsonObject => MapShape.Object,
            _ => fallback
        };
    }

    /// <summary>
    /// Golden save-draft body sends these maps as JSON strings, in the same container 4Seller sent
    /// them. An unchanged map is left byte-for-byte alone so a no-op save never touches it.
    /// </summary>
    private static void WriteJsonMap(
        JsonObject obj,
        string key,
        IReadOnlyList<KeyValuePair<string, string>> entries,
        MapShape shape)
    {
        if (obj[key] is JsonValue jv &&
            jv.GetValueKind() == JsonValueKind.String &&
            DetectShape(obj, key, shape) == shape &&
            EntriesEqual(ReadJsonEntries(obj, key), entries))
        {
            return;
        }

        if (shape == MapShape.Array)
        {
            var array = new JsonArray();
            foreach (var (id, label) in entries)
            {
                array.Add(new JsonObject { [id] = label });
            }

            obj[key] = array.ToJsonString();
            return;
        }

        var map = new JsonObject();
        foreach (var (id, label) in entries)
        {
            map[id] = label;
        }

        obj[key] = map.ToJsonString();
    }

    private static bool EntriesEqual(
        IReadOnlyList<KeyValuePair<string, string>> left,
        IReadOnlyList<KeyValuePair<string, string>> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i].Key, right[i].Key, StringComparison.Ordinal) ||
                !string.Equals(left[i].Value, right[i].Value, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static string? ReadString(JsonObject obj, string key)
    {
        if (!obj.TryGetPropertyValue(key, out var node) || node is null or JsonObject or JsonArray)
        {
            return null;
        }

        var s = node.GetValueKind() == JsonValueKind.String ? node.GetValue<string>() : node.ToString();
        s = s?.Trim();
        return string.IsNullOrWhiteSpace(s) || string.Equals(s, "null", StringComparison.OrdinalIgnoreCase)
            ? null
            : s;
    }

    private static bool HasValue(IReadOnlyDictionary<string, string> attrs, string axis) =>
        !string.IsNullOrWhiteSpace(Value(attrs, axis));

    private static string? Value(IReadOnlyDictionary<string, string> attrs, string axis)
    {
        foreach (var (key, value) in attrs)
        {
            if (axis.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
        }

        return null;
    }
}
