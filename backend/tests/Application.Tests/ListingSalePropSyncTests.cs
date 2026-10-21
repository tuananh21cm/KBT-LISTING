using System.Text.Json.Nodes;
using Application.Services;
using Xunit;

namespace Application.Tests;

/// <summary>
/// Khoá 4 kịch bản đã làm publish 4Seller lỗi: payload push là bản clone detail cũ, nên bỏ một màu,
/// bỏ hẳn một trục, hay thêm màu mới đều phải thấy trong khối sale-prop — không được chỉ prune.
/// </summary>
public class ListingSalePropSyncTests
{
    private static Dictionary<string, string> Attrs(string? color = null, string? size = null, string? items = null)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (color is not null) dict["Color"] = color;
        if (size is not null) dict["Size"] = size;
        if (items is not null) dict["Number of Items"] = items;
        return dict;
    }

    /// <summary>Payload sát golden shape: root maps là JSON string, per-SKU option là label.</summary>
    private static JsonObject BasePayload() => new()
    {
        ["option1"] = """{"100000":"Color"}""",
        ["option1Value"] = """{"1001":"Black","1002":"Blue","1003":"Grey"}""",
        ["option2"] = """{"100007":"Size"}""",
        ["option2Value"] = """{"2001":"1XL","2002":"3XL"}""",
        ["variation"] = new JsonArray(
            Row("SKU-BLACK-1XL", "Black", "1XL", 1),
            Row("SKU-BLACK-3XL", "Black", "3XL", 2),
            Row("SKU-BLUE-1XL", "Blue", "1XL", 3),
            Row("SKU-BLUE-3XL", "Blue", "3XL", 4),
            Row("SKU-GREY-1XL", "Grey", "1XL", 5),
            Row("SKU-GREY-3XL", "Grey", "3XL", 6))
    };

    private static JsonObject Row(string sku, string color, string size, long id) => new()
    {
        ["id"] = id,
        ["keyId"] = id,
        ["sellerSku"] = sku,
        ["option1"] = color,
        ["option2"] = size,
        ["attrs"] = new JsonArray(color, size)
    };

    /// <summary>
    /// Shape 4Seller thật gửi (capture 2026-08-25): <c>option{N}</c> là object, còn
    /// <c>option{N}Value</c> là mảng cặp có thứ tự, giá trị tùy biến lấy chính nhãn làm key.
    /// </summary>
    private static JsonObject ArrayShapePayload() => new()
    {
        ["option1"] = """{"100000":"Color"}""",
        ["option1Value"] = """[{"1001073":"Orange"},{"1001063":"Black"},{"1001068":"Green"}]""",
        ["option2"] = """{"100007":"Size"}""",
        ["option2Value"] = """[{"1003584":"4XL"},{"2XL":"2XL"},{"1003583":"3XL"}]""",
        ["variation"] = new JsonArray(
            Row("SKU-BLACK-4XL", "Black", "4XL", 1),
            Row("SKU-BLACK-2XL", "Black", "2XL", 2),
            Row("SKU-GREEN-4XL", "Green", "4XL", 3),
            Row("SKU-GREEN-2XL", "Green", "2XL", 4))
    };

    private static Dictionary<string, string> ReadMap(JsonObject obj, string key)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (k, v) in ReadEntries(obj, key))
        {
            map[k] = v;
        }

        return map;
    }

    /// <summary>Đọc map ở cả hai shape, giữ thứ tự để kiểm tra mảng.</summary>
    private static List<KeyValuePair<string, string>> ReadEntries(JsonObject obj, string key)
    {
        var entries = new List<KeyValuePair<string, string>>();
        var raw = obj[key]?.GetValue<string>();
        if (raw is null)
        {
            return entries;
        }

        var pairs = JsonNode.Parse(raw) switch
        {
            JsonArray array => array.OfType<JsonObject>().ToList(),
            JsonObject map => new List<JsonObject> { map },
            _ => new List<JsonObject>()
        };

        foreach (var pair in pairs)
        {
            foreach (var (k, v) in pair)
            {
                entries.Add(new KeyValuePair<string, string>(k, v!.GetValue<string>()));
            }
        }

        return entries;
    }

    [Fact]
    public void BoMotMau_XoaNhanKhoiOptionValue_GiuIdCuCacMauConLai()
    {
        var obj = BasePayload();
        var variations = new List<IReadOnlyDictionary<string, string>>
        {
            Attrs("Black", "1XL"), Attrs("Black", "3XL"),
            Attrs("Grey", "1XL"), Attrs("Grey", "3XL")
        };

        var slots = ListingSalePropSync.ResolveAxisSlots(obj, variations);
        var unresolved = ListingSalePropSync.RebuildRootMaps(obj, variations, slots);

        Assert.Empty(unresolved);
        var colors = ReadMap(obj, "option1Value");
        Assert.Equal(new[] { "Black", "Grey" }, colors.Values.OrderBy(v => v).ToArray());
        // Nhãn còn lại phải giữ đúng valueId 4Seller đã biết, không được sinh id mới.
        Assert.Equal("Black", colors["1001"]);
        Assert.Equal("Grey", colors["1003"]);
        Assert.DoesNotContain("1002", colors.Keys);
    }

    [Fact]
    public void BoHanTrucSize_XoaCaOption2VaOption2Value()
    {
        var obj = BasePayload();
        var variations = new List<IReadOnlyDictionary<string, string>>
        {
            Attrs("Black"), Attrs("Blue"), Attrs("Grey")
        };

        var slots = ListingSalePropSync.ResolveAxisSlots(obj, variations);
        ListingSalePropSync.RebuildRootMaps(obj, variations, slots);

        Assert.False(obj.ContainsKey("option2"));
        Assert.False(obj.ContainsKey("option2Value"));
        Assert.Equal(3, ReadMap(obj, "option1Value").Count);
    }

    [Fact]
    public void BoHanTrucSize_XoaOption2TrenTungSku_VaThuGonAttrs()
    {
        var obj = BasePayload();
        var variations = new List<IReadOnlyDictionary<string, string>> { Attrs("Black") };
        var slots = ListingSalePropSync.ResolveAxisSlots(obj, variations);

        var row = (JsonObject)obj["variation"]![0]!;
        ListingSalePropSync.ApplyVariationOptions(row, Attrs("Black"), slots);

        Assert.Equal("Black", row["option1"]!.GetValue<string>());
        Assert.False(row.ContainsKey("option2"));
        Assert.Equal(["Black"], ((JsonArray)row["attrs"]!).Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void DoiNhanTrenSkuClone_DungLaiAttrsTheoOptionMoi()
    {
        var row = Row("SKU-BLACK-1XL", "Black", "1XL", 1);
        var slots = new Dictionary<string, int> { ["Color"] = 1, ["Size"] = 2 };

        ListingSalePropSync.ApplyVariationOptions(row, Attrs("Grey", "3XL"), slots);

        // Trước đây attrs chỉ được dựng khi chưa là array → row clone giữ nhãn cũ mãi mãi.
        Assert.Equal(["Grey", "3XL"], ((JsonArray)row["attrs"]!).Select(n => n!.GetValue<string>()));
    }

    [Fact]
    public void ThemMauMoi_KhongCoIdThiBaoThieu_RoiLayIdTuSchema()
    {
        var obj = BasePayload();
        var variations = new List<IReadOnlyDictionary<string, string>>
        {
            Attrs("Black", "1XL"), Attrs("Beige", "1XL")
        };

        var gaps = ListingSalePropSync.CollectGaps(obj, variations);
        Assert.Contains(("Color", "Beige"), gaps.Labels);
        Assert.Empty(gaps.Axes);

        var slots = ListingSalePropSync.ResolveAxisSlots(obj, variations);
        ListingSalePropSync.RebuildRootMaps(
            obj,
            variations,
            slots,
            axis => axis == "Color"
                ? new SalePropAxisSchema("100000", new Dictionary<string, string> { ["Beige"] = "1009" })
                : null);

        Assert.Equal("Beige", ReadMap(obj, "option1Value")["1009"]);
    }

    [Fact]
    public void NhanMoiKhongCoTrongSchema_LayChinhNhanLamValueId()
    {
        var obj = BasePayload();
        var variations = new List<IReadOnlyDictionary<string, string>>
        {
            Attrs("Black", "1XL"), Attrs("Beige", "1XL")
        };

        var slots = ListingSalePropSync.ResolveAxisSlots(obj, variations);
        ListingSalePropSync.RebuildRootMaps(obj, variations, slots, allowMintedValueIds: true);

        var colors = ReadMap(obj, "option1Value");
        // 4Seller đặt key = nhãn cho giá trị tùy biến; id số tự chế rơi vào không gian id TikTok
        // và trang edit không resolve được.
        Assert.Equal("Beige", colors["Beige"]);
        Assert.Equal("Black", colors["1001"]);
    }

    [Fact]
    public void KhongMint_ThiNhanMoiBiBoQuaChoDenLuotPush()
    {
        var obj = BasePayload();
        var variations = new List<IReadOnlyDictionary<string, string>>
        {
            Attrs("Black", "1XL"), Attrs("Beige", "1XL")
        };

        var slots = ListingSalePropSync.ResolveAxisSlots(obj, variations);
        ListingSalePropSync.RebuildRootMaps(obj, variations, slots);

        // PatchRawPayload (local-only) không được gọi 4Seller, nên nhãn mới để PushAsync xử lý.
        var colors = ReadMap(obj, "option1Value");
        Assert.DoesNotContain("Beige", colors.Values);
        // Và map cũ phải nguyên vẹn: prune ở đây sẽ xóa id mà chỉ 4Seller cấp lại được.
        Assert.Equal("Black", colors["1001"]);
    }

    [Fact]
    public void PayloadShapeMang_DocDuocValueIdNenKhongCanGoiSchema()
    {
        var obj = ArrayShapePayload();
        var variations = new List<IReadOnlyDictionary<string, string>>
        {
            Attrs("Black", "4XL"), Attrs("Black", "2XL"),
            Attrs("Green", "4XL"), Attrs("Green", "2XL")
        };

        // Đọc hụt shape mảng thì mọi nhãn thành gap → mỗi lần push lại gọi category-attribute-list.
        var gaps = ListingSalePropSync.CollectGaps(obj, variations);

        Assert.Empty(gaps.Labels);
        Assert.Empty(gaps.Axes);
    }

    [Fact]
    public void PayloadShapeMang_GhiLaiVanLaMangVaGiuNguyenValueId()
    {
        var obj = ArrayShapePayload();
        var variations = new List<IReadOnlyDictionary<string, string>>
        {
            Attrs("Black", "4XL"), Attrs("Black", "2XL"),
            Attrs("Green", "4XL"), Attrs("Green", "2XL")
        };

        var slots = ListingSalePropSync.ResolveAxisSlots(obj, variations);
        var unresolved = ListingSalePropSync.RebuildRootMaps(obj, variations, slots);

        Assert.Empty(unresolved);
        Assert.StartsWith("[", obj["option1Value"]!.GetValue<string>());
        Assert.StartsWith("[", obj["option2Value"]!.GetValue<string>());
        // option{N} vẫn là object — hai key này không cùng container.
        Assert.StartsWith("{", obj["option1"]!.GetValue<string>());

        // Orange không còn variant nào → prune; nhãn còn lại giữ đúng id, kể cả key dạng nhãn.
        Assert.Equal(
            new[]
            {
                new KeyValuePair<string, string>("1001063", "Black"),
                new KeyValuePair<string, string>("1001068", "Green")
            },
            ReadEntries(obj, "option1Value"));
        Assert.Equal(
            new[]
            {
                new KeyValuePair<string, string>("1003584", "4XL"),
                new KeyValuePair<string, string>("2XL", "2XL")
            },
            ReadEntries(obj, "option2Value"));
    }

    [Fact]
    public void KeyDangNhan_CoSchemaThiNangLenIdSoThat()
    {
        // 4Seller trả `{"Black":"Black"}` khi chưa resolve được value — có schema thì phải lên id thật,
        // còn size tùy biến không có trong schema vẫn giữ nhãn làm key (đúng như body browser).
        var obj = new JsonObject
        {
            ["option1"] = """{"Color":"Color"}""",
            ["option1Value"] = """[{"Black":"Black"},{"Green":"Green"}]""",
            ["option2"] = """{"100007":"Size"}""",
            ["option2Value"] = """[{"0XL":"0XL"},{"3XL":"3XL"}]""",
            ["variation"] = new JsonArray()
        };
        var variations = new List<IReadOnlyDictionary<string, string>>
        {
            Attrs("Black", "0XL"), Attrs("Green", "3XL")
        };

        var slots = ListingSalePropSync.ResolveAxisSlots(obj, variations);
        ListingSalePropSync.RebuildRootMaps(
            obj,
            variations,
            slots,
            axis => axis == "Color"
                ? new SalePropAxisSchema(
                    "100000",
                    new Dictionary<string, string> { ["Black"] = "1001063", ["Green"] = "1001068" })
                : new SalePropAxisSchema(
                    "100007",
                    new Dictionary<string, string> { ["3XL"] = "1003583" }),
            allowMintedValueIds: true);

        var colors = ReadMap(obj, "option1Value");
        Assert.Equal("Black", colors["1001063"]);
        Assert.Equal("Green", colors["1001068"]);
        Assert.StartsWith("[", obj["option1Value"]!.GetValue<string>());

        var sizes = ReadMap(obj, "option2Value");
        Assert.Equal("3XL", sizes["1003583"]);
        Assert.Equal("0XL", sizes["0XL"]);
        Assert.StartsWith("[", obj["option2Value"]!.GetValue<string>());
    }

    [Fact]
    public void PatchRoiSchema_GiuShapeMang_KhiNangLabelLenIdTikTok()
    {
        // E2E KBT 851868260824: DB gốc mảng, push-preview/save-draft trước fix ra object.
        var obj = new JsonObject
        {
            ["option1"] = """{"100000":"Color"}""",
            ["option1Value"] = """[{"White":"White"}]""",
            ["option2"] = """{"100007":"Size"}""",
            ["option2Value"] =
                """[{"1003578":"XS"},{"1003579":"S"},{"1003580":"M"},{"1003581":"L"},{"1002183":"XL"},{"1003582":"XXL"}]""",
            ["variation"] = new JsonArray()
        };
        var variations = new List<IReadOnlyDictionary<string, string>>
        {
            Attrs("White", "XS"), Attrs("White", "S"), Attrs("White", "M"),
            Attrs("White", "L"), Attrs("White", "XL"), Attrs("White", "XXL")
        };
        var slots = ListingSalePropSync.ResolveAxisSlots(obj, variations);
        var valueShapes = ListingSalePropSync.CaptureValueShapes(obj);

        // PatchRawPayload (local-only, không schema)
        ListingSalePropSync.RebuildRootMaps(
            obj, variations, slots, capturedValueShapes: valueShapes);
        Assert.StartsWith("[", obj["option1Value"]!.GetValue<string>());
        Assert.StartsWith("[", obj["option2Value"]!.GetValue<string>());

        // CompleteSalePropMapsAsync khi có gap label-as-key trên Color → schema nâng id
        var gaps = ListingSalePropSync.CollectGaps(obj, variations);
        Assert.Contains(("Color", "White"), gaps.Labels);

        ListingSalePropSync.RebuildRootMaps(
            obj,
            variations,
            slots,
            axis => axis == "Color"
                ? new SalePropAxisSchema("100000", new Dictionary<string, string> { ["White"] = "1001079" })
                : null,
            allowMintedValueIds: true,
            capturedValueShapes: valueShapes);

        Assert.StartsWith("[", obj["option1Value"]!.GetValue<string>());
        Assert.Equal("White", ReadMap(obj, "option1Value")["1001079"]);
        Assert.StartsWith("[", obj["option2Value"]!.GetValue<string>());
    }

    [Fact]
    public void CapturedShapeArray_SchemaPassKhongDoiThanhObject()
    {
        var obj = new JsonObject
        {
            ["option1"] = """{"100000":"Color"}""",
            ["option1Value"] = """[{"White":"White"}]""",
            ["variation"] = new JsonArray()
        };
        var variations = new List<IReadOnlyDictionary<string, string>> { Attrs("White") };
        var slots = ListingSalePropSync.ResolveAxisSlots(obj, variations);
        var valueShapes = ListingSalePropSync.CaptureValueShapes(obj);

        // Giả lập pass PatchRawPayload ghi nhầm object trước khi schema chạy.
        obj["option1Value"] = """{"White":"White"}""";

        ListingSalePropSync.RebuildRootMaps(
            obj,
            variations,
            slots,
            _ => new SalePropAxisSchema("100000", new Dictionary<string, string> { ["White"] = "1001079" }),
            allowMintedValueIds: true,
            capturedValueShapes: valueShapes);

        Assert.StartsWith("[", obj["option1Value"]!.GetValue<string>());
        Assert.Equal("White", ReadMap(obj, "option1Value")["1001079"]);
    }

    [Fact]
    public void CollectGaps_SizeTuyBienKhongTinhLaGap()
    {
        var obj = ArrayShapePayload();
        var variations = new List<IReadOnlyDictionary<string, string>>
        {
            Attrs("Black", "2XL")
        };

        var gaps = ListingSalePropSync.CollectGaps(obj, variations);

        Assert.DoesNotContain(gaps.Labels, g => g.Axis == "Size");
    }

    [Fact]
    public void PayloadShapeMang_BoTrucSize_XoaCaOption2VaOption2Value()
    {
        var obj = ArrayShapePayload();
        var variations = new List<IReadOnlyDictionary<string, string>>
        {
            Attrs("Black"), Attrs("Green")
        };

        var slots = ListingSalePropSync.ResolveAxisSlots(obj, variations);
        ListingSalePropSync.RebuildRootMaps(obj, variations, slots);

        Assert.False(obj.ContainsKey("option2"));
        Assert.False(obj.ContainsKey("option2Value"));
        Assert.StartsWith("[", obj["option1Value"]!.GetValue<string>());
    }

    [Fact]
    public void MapKhongDoi_ThiGiuNguyenChuoiGoc()
    {
        var obj = ArrayShapePayload();
        var variations = new List<IReadOnlyDictionary<string, string>>
        {
            Attrs("Orange", "4XL"), Attrs("Black", "2XL"), Attrs("Green", "3XL")
        };
        var before = obj["option1Value"]!.GetValue<string>();

        var slots = ListingSalePropSync.ResolveAxisSlots(obj, variations);
        ListingSalePropSync.RebuildRootMaps(obj, variations, slots);

        Assert.Equal(before, obj["option1Value"]!.GetValue<string>());
    }

    [Fact]
    public void TrucMoiHoanToan_KhongCoAttrIdThiBaoUnresolved()
    {
        var obj = new JsonObject { ["variation"] = new JsonArray() };
        var variations = new List<IReadOnlyDictionary<string, string>> { Attrs("Black") };

        var slots = ListingSalePropSync.ResolveAxisSlots(obj, variations);
        var unresolved = ListingSalePropSync.RebuildRootMaps(obj, variations, slots);

        Assert.Equal(["Color"], unresolved);
        Assert.False(obj.ContainsKey("option1"));
    }

    [Fact]
    public void AttrIdPlaceholder_ThiUuTienIdSoTuSchema()
    {
        // Detail 4Seller trả về `{"Color":"Color"}` — key đó không phải attribute id TikTok.
        var obj = new JsonObject
        {
            ["option1"] = """{"Color":"Color"}""",
            ["option1Value"] = """{"1001":"Black"}""",
            ["variation"] = new JsonArray()
        };
        var variations = new List<IReadOnlyDictionary<string, string>> { Attrs("Black") };
        var slots = ListingSalePropSync.ResolveAxisSlots(obj, variations);

        Assert.Contains("Color", ListingSalePropSync.CollectGaps(obj, variations).Axes);

        ListingSalePropSync.RebuildRootMaps(
            obj,
            variations,
            slots,
            _ => new SalePropAxisSchema("100000", new Dictionary<string, string>()));

        Assert.Equal("Color", ReadMap(obj, "option1")["100000"]);
    }

    [Fact]
    public void KhongCoSchema_ThiGiuAttrIdPlaceholderChoKhoiMatTruc()
    {
        var obj = new JsonObject
        {
            ["option1"] = """{"Color":"Color"}""",
            ["option1Value"] = """{"1001":"Black"}""",
            ["variation"] = new JsonArray()
        };
        var variations = new List<IReadOnlyDictionary<string, string>> { Attrs("Black") };
        var slots = ListingSalePropSync.ResolveAxisSlots(obj, variations);

        var unresolved = ListingSalePropSync.RebuildRootMaps(obj, variations, slots);

        Assert.Empty(unresolved);
        Assert.Equal("Color", ReadMap(obj, "option1")["Color"]);
    }

    [Fact]
    public void GiuSlotPayloadDaKhai_KhongDaoNhanColorSize()
    {
        // Payload khai option1 = Size (ngược mặc định) → phải giữ, nếu không nhãn sẽ đảo chỗ.
        var obj = new JsonObject
        {
            ["option1"] = """{"100007":"Size"}""",
            ["option2"] = """{"100000":"Color"}""",
            ["variation"] = new JsonArray()
        };
        var variations = new List<IReadOnlyDictionary<string, string>> { Attrs("Black", "1XL") };

        var slots = ListingSalePropSync.ResolveAxisSlots(obj, variations);

        Assert.Equal(1, slots["Size"]);
        Assert.Equal(2, slots["Color"]);
    }

    [Fact]
    public void KhongCoKhaiBao_ThiTheoThuTuGoldenShape()
    {
        var obj = new JsonObject();
        var variations = new List<IReadOnlyDictionary<string, string>>
        {
            Attrs("Black", "1XL", "2 pcs")
        };

        var slots = ListingSalePropSync.ResolveAxisSlots(obj, variations);

        Assert.Equal(1, slots["Color"]);
        Assert.Equal(2, slots["Size"]);
        Assert.Equal(3, slots["Number of Items"]);
    }

    [Fact]
    public void TrucKhongDungThiKhongChiemSlot()
    {
        var obj = new JsonObject();
        var variations = new List<IReadOnlyDictionary<string, string>> { Attrs(size: "1XL") };

        var slots = ListingSalePropSync.ResolveAxisSlots(obj, variations);

        Assert.Single(slots);
        Assert.Equal(1, slots["Size"]);
    }
}
