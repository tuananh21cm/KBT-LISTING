using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Application.DTOs.Listing;
using Application.Interfaces;
using Domain.Exceptions;
using Shared.Constants;

namespace Application.Services;

/// <summary>
/// Orchestrates OpenAI rewrite → plain markdown-lite → TTS-safe editor HTML for listing descriptions.
/// </summary>
public class ListingDescriptionAiService : IListingDescriptionAiService
{
    /// <summary>
    /// Plain-text budget for the rewritten copy (body + trailing hashtag line).
    /// ~400 chars are reserved for the hashtag line so a solid 1400-2000 body is not
    /// trimmed just for SEO tags. Still well under the editor's 10k HTML cap so
    /// gallery images inserted afterwards still fit.
    /// </summary>
    private const int MaxPlainTextLength = 2900;

    /// <summary>Cap on the trailing SEO hashtag line after sanitize.</summary>
    private const int MaxHashtags = 25;

    /// <summary>Keeps the facts block from crowding out the actual instructions.</summary>
    private const int MaxAttributeFacts = 40;

    /// <summary>TikTok Shop rejects titles shorter than this and cannot classify them.</summary>
    private const int MinTitleLength = 40;

    /// <summary>
    /// Below this a second call is worth its cost when leftover source tokens are still
    /// missing. Held at <see cref="TargetTitleMin"/> so a lower gate does not silently
    /// accept a title that dropped searchable source attributes.
    /// </summary>
    private const int MinAcceptableTitleLength = TargetTitleMin;

    /// <summary>
    /// Hard cap matching the Product name field / DTO <c>MaxLength(255)</c>.
    /// Longer titles are trimmed at a word boundary.
    /// </summary>
    private const int MaxTitleLength = 255;

    /// <summary>
    /// Band the prompt aims for in rewrite mode when the source title has enough real
    /// attributes. Never invent keywords to fill the band; a short grounded title wins.
    /// </summary>
    private const int TargetTitleMin = 150;

    /// <inheritdoc cref="TargetTitleMin" />
    private const int TargetTitleMax = 250;

    /// <summary>
    /// Marks where the description ends and the SEO title begins inside a single completion.
    /// Chosen so it cannot collide with the markdown-lite body, which only ever emits <c>##</c>
    /// headings, <c>-</c> bullets and prose.
    /// </summary>
    private const string TitleSentinel = "===SEO_TITLE===";

    /// <summary>Backend TikTok search terms; one phrase per line after this sentinel.</summary>
    private const string SearchTermsSentinel = "===SEARCH_TERMS===";

    /// <summary>PDP product highlights; one selling point per line after this sentinel.</summary>
    private const string ProductHighlightsSentinel = "===PRODUCT_HIGHLIGHTS===";

    private const int MaxSearchTerms = 6;
    private const int MaxProductHighlights = 5;
    private const int MaxMetaLineChars = 80;

    /// <summary>Longest bolded lead-in still treated as a feature label rather than a sentence.</summary>
    private const int MaxLeadInWords = 6;

    private const string LeadInSeparators = "—–-:,;.!?";

    /// <summary>Markdown-lite decoration that may sit before a line's first letter.</summary>
    private const string LineStartSkippable = " \t*#-•·–—_>";

    /// <summary>
    /// Strip-brands mode does not use the SEO title from the description completion,
    /// so skip that sentinel rather than paying for a title that will be discarded.
    /// Search terms and highlights still come from the same completion.
    /// </summary>
    internal const string SkipSeoTitleInstruction =
        "Do not output ===SEO_TITLE=== and do not write a search title. "
        + "After the rewritten description, still output ===SEARCH_TERMS=== with 4-6 "
        + "lowercase search terms (one per line), then ===PRODUCT_HIGHLIGHTS=== with "
        + "3-5 benefit-first highlights (one per line).";

    /// <summary>
    /// Fast path when strip-brands leaves a title too short for TikTok search.
    /// The new title is written against the same description completion so the
    /// two stay on the same primary and secondary queries.
    /// </summary>
    internal const string ShortTitleSeoInstruction =
        "The original title is too short for TikTok Shop search after brand tokens "
        + "are removed. Write a new 150-250 character machine-first search title. "
        + "Open a women's product with Women (not Women's) plus the distinctive "
        + "product type in the first 30 characters. Stuff the primary keyword and "
        + "2-4 secondary or synonym queries that already appear in the rewritten "
        + "description so the title and the description rank for the same queries. "
        + "Do not invent a material, fit, print, season or occasion that is in "
        + "neither the original title nor the description. Keep every distinctive "
        + "source token.";

    /// <summary>
    /// Title and Material attribute disagree on the fibre. Description / search terms /
    /// highlights must omit every fabric name — TikTok rejects listings that say linen
    /// in the title while the copy says polyester.
    /// </summary>
    internal const string MaterialConflictInstruction =
        "The product title and the Material attribute name different fabrics. "
        + "Do not name any fibre, fabric, weave, knit, blend or material feel anywhere "
        + "in the description, search terms or product highlights. Skip fabric bullets "
        + "entirely. Sell the cut, design details, coverage, occasions and care instead. "
        + "Omitting the material is required — naming one fabric would fail TikTok review.";

    private static readonly Regex TagRegex = new("<[^>]*>", RegexOptions.Compiled);
    private static readonly Regex MultiSpaceRegex = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex BlockSplitRegex = new(@"\n\s*\n", RegexOptions.Compiled);
    private static readonly Regex BoldRegex = new(@"\*\*(.+?)\*\*", RegexOptions.Compiled);
    private static readonly Regex HeadingRegex = new(@"^#{1,6}\s+(.+?)\s*:?\s*$", RegexOptions.Compiled);
    private static readonly Regex BoldOnlyLineRegex = new(@"^\*\*(.+?)\*\*\s*:?\s*$", RegexOptions.Compiled);

    private static readonly Regex BulletLeadInRegex = new(
        @"^(?<marker>\s*(?:[-*•·]|–|—)\s*)\*\*(?<label>[^*]+)\*\*\s+(?<rest>\S.*)$",
        RegexOptions.Compiled);

    private static readonly string[] NoBrandNames = ["no brand", "nobrand", "none", "n/a"];

    /// <summary>
    /// Values that carry no meaning for a US shopper and are never searched for, so they
    /// would only waste description budget if the model repeated them.
    /// </summary>
    private static readonly string[] VagueFactValues =
    [
        "multicolor", "multi-color", "multi color", "multicolour", "multi",
        "assorted", "random", "random color", "as picture", "as the picture",
        "as shown", "same as picture", "other", "others", "none", "n/a", "na", "-"
    ];

    /// <summary>Attributes that describe how the garment sits on the body.</summary>
    private static readonly string[] SilhouetteAttributeNames =
        ["fit", "fit type", "fittype", "fit style", "silhouette", "cut", "shape"];

    /// <summary>
    /// Silhouette vocabulary shared by titles and fit specs, used to spot the two disagreeing.
    /// </summary>
    private static readonly Regex SilhouetteRegex = new(
        @"\b(?:bodycon|bandage|skinny|slim|fitted|tight|snug|loose|oversized?|relaxed|baggy|"
        + @"flowy|billowy|boxy|tapered|bootcut|flared?|shift|tunic|peplum|regular|standard|classic|"
        + @"wide[- ]?leg|straight[- ]?leg|a[- ]?line|empire[- ]?waist)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Fibre and weave words that must leave the description when title and Material disagree.
    /// Broader than <see cref="ListingSpecificsMappingEngine.FabricKeysFromText"/> so a leftover
    /// "knit polyester" or "woven feel" bullet is dropped even without a named fibre.
    /// </summary>
    private static readonly Regex FabricRegex = new(
        @"\b(?:linen|cotton|denim|silk|satin|wool|leather|velvet|chiffon|corduroy|tweed|fleece|"
        + @"cashmere|modal|bamboo|mesh|lace|rayon|viscose|nylon|acrylic|spandex|elastane|"
        + @"polyester|knit(?:ted)?|woven)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Same fibres as <see cref="FabricRegex"/> but without word boundaries so
    /// <c>#linendress</c> / <c>#polyesterblend</c> are caught after a material conflict.
    /// </summary>
    private static readonly Regex FabricInHashtagRegex = new(
        @"#(?:[a-z0-9]*)(?:linen|cotton|denim|silk|satin|wool|leather|velvet|chiffon|corduroy|"
        + @"tweed|fleece|cashmere|modal|bamboo|mesh|lace|rayon|viscose|nylon|acrylic|spandex|"
        + @"elastane|polyester|knit(?:ted)?|woven)(?:[a-z0-9]*)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Clean trailing SEO hashtag line: at least two <c>#token</c> entries, letters/digits only.
    /// </summary>
    private static readonly Regex HashtagLineRegex = new(
        @"^(?:#[a-z0-9]+[ \t]*){2,}$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Pulls individual <c>#token</c> fragments out of a messy model hashtag line.</summary>
    private static readonly Regex HashtagTokenRegex = new(
        @"#([a-z0-9]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex NonLetterRegex = new("[^a-z]", RegexOptions.Compiled);

    private static readonly Regex MetaBulletPrefixRegex = new(
        @"^(?:[-*•·]|\d+[.)])\s+",
        RegexOptions.Compiled);

    private static readonly Regex MetaLabelPrefixRegex = new(
        @"^(?:primary|secondary|highlights?|search\s*terms?|keywords?)\s*[:\-]\s*",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>One OpenAI completion split into description + optional SEO meta blocks.</summary>
    internal readonly record struct RewriteCompletionBlocks(
        string Body,
        string Title,
        IReadOnlyList<string> SearchTerms,
        IReadOnlyList<string> ProductHighlights);

    private readonly record struct DescriptionRewritePass(
        string Rewritten,
        string TitleDraft,
        List<string> SearchTerms,
        List<string> ProductHighlights);

    /// <summary>
    /// SHEIN in-house lines that sit right after SHEIN in supplier titles
    /// (<c>SHEIN BAE</c>, <c>SHEIN ICON</c>). They are not TikTok search terms;
    /// stripping only SHEIN leaves a leftover like <c>BAE</c>. Petite / Curve / Swim
    /// stay — those are searchable size or category words.
    /// </summary>
    private const string SheinPrivateLabelPattern =
        @"bae|icon|essnce|motf|dazy|ezwear|sxy|lune|clasi|mod|frenchy|wywh|qutie|"
        + @"priv[eé]|unity|slayr|bizwear|coolane|muchica|vcay|glowmode|joah|insaf|sics|"
        + @"emery\s*rose";

    private static readonly HashSet<string> SheinPrivateLabelTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "bae", "icon", "essnce", "motf", "dazy", "ezwear", "sxy", "lune", "clasi",
        "mod", "frenchy", "wywh", "qutie", "prive", "privé", "unity", "slayr",
        "bizwear", "coolane", "muchica", "vcay", "glowmode", "joah", "insaf", "sics",
        "emery"
    };

    private static readonly Regex SheinTokenRegex = new(
        @"\bshein\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Sourcing marketplaces. Supplier titles lead with these, they are never what a US
    /// shopper searches for, and TikTok flags listings naming another marketplace.
    /// SHEIN consumes a following in-house line in the same match so <c>SHEIN BAE</c>
    /// does not collapse to leftover <c>BAE</c>.
    /// </summary>
    private static readonly Regex TitleMarketplaceRegex = new(
        @"\b(?:shein(?:\s+(?:" + SheinPrivateLabelPattern + @"))?|lazada|temu|amazon|"
        + @"aliexpress|ali\s*express|wish|romwe|zaful|cider|"
        + @"shopee|alibaba|etsy|ebay|walmart|tiktok\s*shop|tiktok)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Promotional and subjective wording TikTok's listing-quality rules ban in titles.</summary>
    private static readonly Regex TitlePromoRegex = new(
        @"\$\s*\d+(?:[.,]\d+)?|\b\d{1,3}\s*%\s*(?:off|discount)\b|"
        + @"\b(?:best\s*sellers?|bestsellers?|hot\s*sale|on\s*sale|flash\s*sale|big\s*sale|"
        + @"free\s*shipping|fast\s*shipping|new\s*arrivals?|limited\s*time|limited\s*edition|"
        + @"clearance|must\s*have|musthave|top\s*rated|number\s*one|cheap|wholesale|dropshipping|"
        + @"perfectsummer|perfect\s+summer|newin)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Catalog-year tokens SHEIN/ROMWE stamp on titles. Shoppers do not search 2026;
    /// 1970s / 90s / Y2K stay because those are style eras.
    /// </summary>
    private static readonly Regex TitleCatalogYearRegex = new(
        @"\b20(?:2[0-9]|3[0-9])\b",
        RegexOptions.Compiled);

    /// <summary>
    /// Calendar holidays, unsourced seasons/occasions and going-out filler. Allowed only
    /// when the source title already names the same token.
    /// </summary>
    private static readonly Regex TitleOccasionRegex = new(
        @"\b(?:halloween|christmas|xmas|thanksgiving|valentine'?s?(?:\s+day)?|"
        + @"easter|st\.?\s*patrick'?s?(?:\s+day)?|new\s*years?(?:\s+eve)?|"
        + @"black\s*friday|cyber\s*monday|back[- ]to[- ]school|"
        + @"beach|vacation|weddings?|concerts?|festivals?|holidays?|"
        + @"going[- ]?out|night[- ]?out|date[- ]?night|"
        + @"summer|spring|winter|autumn|fall|party)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Subjective claims and unsourced functional details the rewrite must not invent.</summary>
    private static readonly Regex TitleUnsourcedClaimRegex = new(
        @"\b(?:sexy|premium|luxury|comfortable|flattering|breathable|stretchy|sleeveless|sweet)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TitleShortDressRegex = new(
        @"\bshort\s+dress(?:es)?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex TrailingAudienceRegex = new(
        @"\s+\b(?:women|womens|ladies)\b\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WomensAudienceRegex = new(
        @"\b(?:women'?s?|woman|ladies|lady|womenswear)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex MensAudienceRegex = new(
        @"\b(?:men'?s|menswear|man)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex UnisexAudienceRegex = new(
        @"\bunisex\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex KidsAudienceRegex = new(
        @"\b(?:girls?|kids?|toddler)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WomensPossessiveRegex = new(
        @"\bWomen'?s\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex LadiesTokenRegex = new(
        @"\bLadies\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex DuplicateWomenRegex = new(
        @"\bWomen(\s+Women)+\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex WomenLeadRegex = new(
        @"^Women\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PlusSizeLeadRegex = new(
        @"^Plus\s+Size\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex PlusSizeWomenLeadRegex = new(
        @"^Plus\s+Size\s+Women\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Garments that are sleeveless by name. Adding Sleeveless Top is a search synonym,
    /// not an invented spec row.
    /// </summary>
    private static readonly Regex SourceSleevelessGarmentRegex = new(
        @"\b(?:tanks?|cami|camisoles?|halter|bandeaus?|tube)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Supplier slogans glued into one token: PerfectSummer, OverSize.</summary>
    private static readonly Regex CamelConcatRegex = new(
        @"\b(?:[A-Z][a-z]{2,}){2,}\b",
        RegexOptions.Compiled);

    private static readonly Regex CamelPartRegex = new(
        @"[A-Z][a-z]+",
        RegexOptions.Compiled);

    /// <summary>
    /// Source spelling of oversized, including the glued token <c>OverSize</c> and
    /// <c>Over Size</c> after a CamelCase split.
    /// </summary>
    private static readonly Regex OverSizePhraseRegex = new(
        @"\bover[- ]?sized?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Title-casing <c>70's</c> capitalizes the letter after the digits and the apostrophe,
    /// turning a decade into <c>70'S</c>. Fold it back after casing.
    /// </summary>
    private static readonly Regex DecadeApostropheRegex = new(
        @"\b(\d{2})'[Ss]\b",
        RegexOptions.Compiled);

    /// <summary>Everything outside this set is emoji, decoration or a banned symbol.</summary>
    private static readonly Regex TitleDisallowedCharRegex = new(
        @"[^A-Za-z0-9 '&,\-.]", RegexOptions.Compiled);

    /// <summary>Collapses the separator debris left behind after a word is stripped out.</summary>
    private static readonly Regex TitleSeparatorRunRegex = new(
        @"\s*([,\-])(?:\s*[,\-.])+\s*", RegexOptions.Compiled);

    private static readonly Regex TitleDuplicateWordRegex = new(
        @"\b([A-Za-z][A-Za-z']*)(\s+\1)+\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// In the US <c>boy shorts</c> is underwear, so the phrase drags an athletic-shorts
    /// listing in front of the wrong shoppers. Dropping the qualifier keeps the noun.
    /// </summary>
    private static readonly Regex BoyShortsRegex = new(
        @"\bboy\s+(shorts?)\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HighRiseRegex = new(
        @"\bhigh[- ]?rise\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex HighWaistRegex = new(
        @"\bhigh[- ]?waist(?:ed)?\b", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Title endings that burn characters on phrases almost nobody types. Drop them;
    /// do not replace them with an invented occasion.
    /// </summary>
    private static readonly Regex WeakLongTailRegex = new(
        @"\b(?:daily|everyday|casual|summer|vacation|party|going[- ]?out)\s+wear\b|"
        + @"\blayering\s+wear\b|\bathletic\s+style\b|\bretro\s+(?:fall\s+)?outfits?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Silhouette and neckline terms spelled letter-hyphen-word: A-Line, V-Neck, T-Shirt.</summary>
    private static readonly Regex LetterShapeTermRegex = new(
        @"\b([AaVvUuTtOoXx])[- ](line|neck|neckline|shirt|back|ring|shape)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private const RegexOptions TitleKeywordOptions = RegexOptions.Compiled | RegexOptions.IgnoreCase;

    /// <summary>
    /// Attribute words that are a US search query on their own — shoppers type <c>seamless
    /// set</c> and <c>ribbed dress</c>, not just <c>set</c> or <c>dress</c>. When the supplier
    /// title already spends characters on one, that is the product's headline feature and the
    /// rewrite has to keep it. Left alone the model trades these for smoother-reading filler
    /// and the query match goes with them. Order is priority: the first miss is reported.
    /// </summary>
    private static readonly (string Label, Regex Pattern)[] TitleSourceKeywords =
    [
        ("Seamless", new Regex(@"\bseamless\b", TitleKeywordOptions)),
        ("High Waist", new Regex(@"\bhigh[- ]?waist(?:ed)?\b", TitleKeywordOptions)),
        ("Ribbed", new Regex(@"\bribbed\b", TitleKeywordOptions)),
        ("Tummy Control", new Regex(@"\btummy[- ]?control\b", TitleKeywordOptions)),
        ("Compression", new Regex(@"\bcompression\b", TitleKeywordOptions)),
        ("Quick Dry", new Regex(@"\bquick[- ]?dry(?:ing)?\b", TitleKeywordOptions)),
        ("Moisture Wicking", new Regex(@"\bmoisture[- ]?wicking\b", TitleKeywordOptions)),
        ("Cropped", new Regex(@"\bcrop(?:ped|top)?\b", TitleKeywordOptions)),
        ("Oversized", new Regex(@"\boversi[sz]ed\b", TitleKeywordOptions)),
        ("Bodycon", new Regex(@"\bbodycon\b", TitleKeywordOptions)),
        ("Backless", new Regex(@"\bbackless\b", TitleKeywordOptions)),
        ("Strapless", new Regex(@"\bstrapless\b", TitleKeywordOptions)),
        ("Off Shoulder", new Regex(@"\boff[- ]?(?:the[- ]?)?shoulder\b", TitleKeywordOptions)),
        ("Plus Size", new Regex(@"\bplus[- ]?si[sz]e\b", TitleKeywordOptions)),
        ("Maternity", new Regex(@"\bmaternity\b", TitleKeywordOptions)),
        ("Fleece", new Regex(@"\bfleece\b", TitleKeywordOptions)),
        ("Linen", new Regex(@"\blinen\b", TitleKeywordOptions)),
        ("Denim", new Regex(@"\bdenim\b", TitleKeywordOptions)),
        ("Mesh", new Regex(@"\bmesh\b", TitleKeywordOptions)),
        ("Satin", new Regex(@"\bsatin\b", TitleKeywordOptions)),
        ("Velvet", new Regex(@"\bvelvet\b", TitleKeywordOptions)),
        ("Corduroy", new Regex(@"\bcorduroy\b", TitleKeywordOptions)),
        ("Waffle Knit", new Regex(@"\bwaffle\b", TitleKeywordOptions)),
        ("Sheer", new Regex(@"\bsheer\b", TitleKeywordOptions)),
        ("Padded", new Regex(@"\bpadded\b", TitleKeywordOptions)),
        ("Pockets", new Regex(@"\bpockets?\b", TitleKeywordOptions))
    ];

    /// <summary>Words TikTok's capitalization rule keeps lower case inside a title.</summary>
    private static readonly HashSet<string> TitleLowercaseWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "or", "but", "nor", "a", "an", "the", "for", "in", "on",
        "with", "to", "of", "at", "by", "from", "as", "per", "via"
    };

    /// <summary>
    /// Tokens that are not the listing's ranking identity. The rewrite may drop them;
    /// distinctive print/style words (paisley, grunge, y2k) must still be kept.
    /// </summary>
    private static readonly HashSet<string> TitleHeadlineSkipWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "women", "womens", "woman", "men", "mens", "man", "girls", "girl", "boys", "boy",
        "kids", "unisex", "ladies", "lady",
        "summer", "spring", "winter", "fall", "autumn",
        "top", "tops", "dress", "dresses", "shirt", "shirts", "pants", "trousers", "shorts",
        "skirt", "skirts", "set", "sets", "blouse", "tee", "tees", "cami", "camisole",
        "tank", "tanks", "hoodie", "jacket", "coat", "coats",
        "black", "white", "red", "blue", "green", "pink", "brown", "beige", "khaki",
        "navy", "grey", "gray", "orange", "yellow", "purple", "ivory", "cream", "nude",
        "burgundy", "coffee", "wine", "gold", "silver",
        "new", "hot", "sale"
    };

    /// <summary>First source token that is a real keyword, not a supplier brand.</summary>
    private static readonly HashSet<string> TitleHeadlineLeadWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "plus", "size", "women", "womens", "woman", "men", "mens", "man", "girls", "boys",
        "kids", "unisex", "maternity", "juniors", "ladies", "teen", "toddler", "baby"
    };

    /// <summary>
    /// Tokens that may sit right after SHEIN and are product words, not a private-label line.
    /// Used when a future SHEIN line is not yet in <see cref="SheinPrivateLabelTokens"/>.
    /// </summary>
    private static readonly HashSet<string> SheinLeadKeepExtras = new(StringComparer.OrdinalIgnoreCase)
    {
        "casual", "sexy", "elegant", "floral", "vintage", "solid", "stripe", "striped",
        "lace", "knit", "knitted", "cotton", "linen", "denim", "satin", "silk", "chiffon",
        "mesh", "graphic", "basic", "plain", "print", "printed", "boho", "y2k", "grunge",
        "french", "asymmetrical", "halter", "crop", "cropped", "mini", "midi", "maxi",
        "bodycon", "ruffle", "ruffled", "oversized", "oversize", "slim", "loose", "fitted",
        "off", "shoulder", "long", "short", "sleeve", "sleeveless", "backless", "strapless",
        "wrap", "cutout", "tiered", "pleated", "ribbed", "petite", "curve", "swim", "sport"
    };

    private static readonly HashSet<string> SheinLeadKeepCompact = BuildSheinLeadKeepCompact();

    /// <summary>
    /// Spec-table rows shoppers never type. Allowed only when the source title already
    /// uses the same phrase.
    /// </summary>
    private static readonly Regex TitleSpecSheetJunkRegex = new(
        @"\bnon[- ]?(?:sheer|stretch|lined|wired)\b|"
        + @"\b(?:regular|standard)[- ]?(?:length|fit)\b|"
        + @"\b(?:slight(?:ly)?|medium|high|low)[- ]?stretch\b|"
        + @"\bslim[- ]?fit(?:ted|ting)?\b|"
        + @"\b(?:polyester|spandex|elastane|viscose|acrylic)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Garment families. A rewrite may use tokens from a family the source already named
    /// (pants plus trousers) but must not introduce a new family (cami on a bandeau, set on a single top).
    /// Generic <c>top</c> is not a family so repeating Top never counts as invention.
    /// </summary>
    private static readonly (string Label, string[] Tokens)[] TitleGarmentFamilies =
    [
        ("matching set", ["set", "sets", "coord", "twopiece", "2piece"]),
        ("cami", ["cami", "camisole", "tank", "tanks", "tanktop"]),
        ("dress", ["dress", "dresses", "gown", "gowns"]),
        ("pants", ["pants", "trousers", "jeans", "slacks", "joggers", "sweatpants"]),
        ("leggings", ["leggings", "legging", "tights"]),
        ("shorts", ["shorts", "bermuda"]),
        ("skirt", ["skirt", "skirts", "skort"]),
        ("bandeau", ["bandeau", "tube", "bustier"]),
        ("tee", ["tee", "tees", "tshirt", "tshirts"]),
        ("blouse", ["blouse", "shirt", "shirts"]),
        ("sweater", ["sweater", "sweaters", "jumper", "cardigan", "hoodie", "pullover"]),
        ("jacket", ["jacket", "coat", "coats", "blazer", "parka"]),
        ("romper", ["romper", "jumpsuit", "playsuit"]),
        ("bodysuit", ["bodysuit", "leotard"]),
        ("bra", ["bra", "bralette"])
    ];

    /// <summary>
    /// Catches "2 Piece" / "two-piece" / "Matching Set" when those words are not a single token.
    /// Bare <c>short</c> is not a shorts token — it is sleeve length.
    /// </summary>
    private static readonly Regex MatchingSetPhraseRegex = new(
        @"\bmatching\s+sets?\b|\b(?:2|two)[- ]pieces?\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly char[] TitleEdgeChars = [' ', ',', '.', '-', '&'];

    private static readonly JsonSerializerOptions TitleBrandsJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IOpenAiService _openAiService;
    private readonly ISettingProvider _settings;

    /// <summary>
    /// Initializes the listing description AI service.
    /// </summary>
    public ListingDescriptionAiService(IOpenAiService openAiService, ISettingProvider settings)
    {
        _openAiService = openAiService;
        _settings = settings;
    }

    /// <inheritdoc />
    public async Task<RewriteListingDescriptionResultDto> RewriteAsync(
        RewriteListingDescriptionRequest request,
        CancellationToken cancellationToken = default)
    {
        var title = (request.Title ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new BusinessException("Nhập Product name trước khi AI viết lại mô tả.");
        }

        var plainDescription = StripHtml(request.Description ?? string.Empty);
        if (string.IsNullOrWhiteSpace(plainDescription))
        {
            throw new BusinessException("Nhập Description trước khi AI viết lại mô tả.");
        }

        var productFacts = BuildProductFacts(request);
        var materialConflict = HasMaterialConflict(request);
        var titleMode = ParseTitleMode(
            await _settings.GetValueAsync(ListingRewriteSettingKeys.TitleMode, cancellationToken));

        if (titleMode == ListingRewriteSettingKeys.TitleModeStripBrands)
        {
            return await RewriteStripBrandsAsync(
                title, plainDescription, productFacts, materialConflict, request.Seed, cancellationToken);
        }

        return await RewriteSeoTitleAsync(
            title, plainDescription, productFacts, materialConflict, request.Seed, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ListingRewriteConfigDto> GetRewriteConfigAsync(
        CancellationToken cancellationToken = default)
    {
        // One query — never Task.WhenAll multiple GetValueAsync calls: the scoped
        // DbContext is not thread-safe and that 500s the listing edit page,
        // which then keeps Rewrite locked as if allowRepeat were still false.
        var values = await _settings.GetValuesAsync(
            [
                ListingRewriteSettingKeys.TitleMode,
                ListingRewriteSettingKeys.AllowRepeat,
                ListingSpecificsSettingKeys.AllowRepeat,
                ListingSizeChartSettingKeys.AutoGenerateEnabled
            ],
            cancellationToken);

        values.TryGetValue(ListingRewriteSettingKeys.TitleMode, out var titleMode);
        values.TryGetValue(ListingRewriteSettingKeys.AllowRepeat, out var allowRepeat);
        values.TryGetValue(ListingSpecificsSettingKeys.AllowRepeat, out var allowSpecificsRepeat);
        values.TryGetValue(ListingSizeChartSettingKeys.AutoGenerateEnabled, out var autoSizeChart);

        return new ListingRewriteConfigDto
        {
            TitleMode = ParseTitleMode(titleMode),
            AllowRepeat = ParseAllowRepeat(allowRepeat),
            AllowSpecificsRepeat = ParseAllowRepeat(allowSpecificsRepeat),
            AutoGenerateSizeChart = ParseAllowRepeat(autoSizeChart)
        };
    }

    /// <summary>
    /// Rewrites the description and, in parallel, asks the model which brand tokens to
    /// drop from the source title. The remaining wording is kept — unless the leftover
    /// title is too short for TikTok search, in which case a new SEO title is written
    /// against the same description (women's products open with <c>Women</c>).
    /// </summary>
    private async Task<RewriteListingDescriptionResultDto> RewriteStripBrandsAsync(
        string title,
        string plainDescription,
        string productFacts,
        bool materialConflict,
        int? seed,
        CancellationToken cancellationToken)
    {
        // Regex strip is enough to know the leftover will miss the 40-character floor,
        // so skip the brand JSON call and take the description+title completion instead.
        if (NeedsSeoTitleFallback(SanitizeTitle(title, title)))
        {
            return await RewriteShortTitleSeoAsync(
                title, plainDescription, productFacts, materialConflict, seed, cancellationToken);
        }

        var extra = CombineTitleInstructions(
            SkipSeoTitleInstruction,
            materialConflict ? MaterialConflictInstruction : null);
        var input = new ProductDescriptionRewriteInput
        {
            Title = title,
            OriginalDescription = plainDescription,
            ProductFacts = productFacts,
            ExtraInstruction = extra,
            Seed = seed
        };

        var descriptionTask = RewriteDescriptionMarkdownAsync(
            input, extra, cancellationToken, captureTitle: false);
        var brandsTask = DetectTitleBrandsSafeAsync(title, cancellationToken);
        await Task.WhenAll(descriptionTask, brandsTask);

        var pass = await descriptionTask;
        var nextTitle = ApplyStrippedTitle(title, await brandsTask);
        if (NeedsSeoTitleFallback(nextTitle))
        {
            var seoTitle = await BuildSeoTitleAsync(
                title,
                productFacts,
                pass.Rewritten,
                titleDraft: string.Empty,
                cancellationToken,
                ShortTitleSeoInstruction,
                seed);
            nextTitle = EnsureWomenAudienceLead(
                seoTitle.Length > 0 ? seoTitle : nextTitle,
                title,
                productFacts);
        }

        return ToRewriteResult(
            pass.Rewritten,
            nextTitle.Length > 0 ? nextTitle : title,
            pass.SearchTerms,
            pass.ProductHighlights,
            materialConflict);
    }

    /// <summary>
    /// Strip-brands fast path when the source title cannot survive brand removal.
    /// One description completion also drafts the SEO title so the two share keywords.
    /// </summary>
    private async Task<RewriteListingDescriptionResultDto> RewriteShortTitleSeoAsync(
        string title,
        string plainDescription,
        string productFacts,
        bool materialConflict,
        int? seed,
        CancellationToken cancellationToken)
    {
        var extra = CombineTitleInstructions(
            ShortTitleSeoInstruction,
            materialConflict ? MaterialConflictInstruction : null);
        var input = new ProductDescriptionRewriteInput
        {
            Title = title,
            OriginalDescription = plainDescription,
            ProductFacts = productFacts,
            ExtraInstruction = extra,
            Seed = seed
        };

        var pass = await RewriteDescriptionMarkdownAsync(
            input, extra, cancellationToken, captureTitle: true);

        var seoTitle = await BuildSeoTitleAsync(
            title,
            productFacts,
            pass.Rewritten,
            pass.TitleDraft,
            cancellationToken,
            ShortTitleSeoInstruction,
            seed);

        return ToRewriteResult(
            pass.Rewritten,
            EnsureWomenAudienceLead(
                seoTitle.Length > 0 ? seoTitle : title,
                title,
                productFacts),
            pass.SearchTerms,
            pass.ProductHighlights,
            materialConflict);
    }

    /// <summary>Current SEO path: description completion may include a title draft.</summary>
    private async Task<RewriteListingDescriptionResultDto> RewriteSeoTitleAsync(
        string title,
        string plainDescription,
        string productFacts,
        bool materialConflict,
        int? seed,
        CancellationToken cancellationToken)
    {
        var extra = materialConflict ? MaterialConflictInstruction : string.Empty;
        var input = new ProductDescriptionRewriteInput
        {
            Title = title,
            OriginalDescription = plainDescription,
            ProductFacts = productFacts,
            ExtraInstruction = extra,
            Seed = seed
        };

        var pass = await RewriteDescriptionMarkdownAsync(
            input, extra, cancellationToken, captureTitle: true);

        var seoTitle = await BuildSeoTitleAsync(
            title, productFacts, pass.Rewritten, pass.TitleDraft, cancellationToken,
            seed: seed);

        return ToRewriteResult(
            pass.Rewritten,
            seoTitle.Length > 0 ? seoTitle : title,
            pass.SearchTerms,
            pass.ProductHighlights,
            materialConflict);
    }

    /// <summary>
    /// One description call plus an optional shorten retry. <paramref name="captureTitle"/>
    /// keeps the SEO sentinel when rewrite mode still wants it. Search terms and highlights
    /// are always captured when the model emits their sentinels.
    /// </summary>
    private async Task<DescriptionRewritePass> RewriteDescriptionMarkdownAsync(
        ProductDescriptionRewriteInput input,
        string extraBase,
        CancellationToken cancellationToken,
        bool captureTitle)
    {
        var blocks = SplitRewriteBlocks(
            await _openAiService.RewriteProductDescriptionAsync(input, cancellationToken));
        var rewritten = StripForbiddenDescriptionSections(Normalize(blocks.Body));
        var titleDraft = blocks.Title;
        var searchTerms = blocks.SearchTerms.ToList();
        var highlights = blocks.ProductHighlights.ToList();

        if (rewritten.Length > MaxPlainTextLength)
        {
            input.ExtraInstruction =
                extraBase
                + "\n\n"
                + $"The previous attempt was {rewritten.Length} characters, which is too long. "
                + $"Rewrite it under {MaxPlainTextLength} characters by cutting the least important "
                + "details. Keep the opening hook, the call to action, the closing thank-you line "
                + "and the final hashtag line.";

            var retryBlocks = SplitRewriteBlocks(
                await _openAiService.RewriteProductDescriptionAsync(input, cancellationToken));
            var retry = StripForbiddenDescriptionSections(Normalize(retryBlocks.Body));

            if (retry.Length > 0 && retry.Length < rewritten.Length)
            {
                rewritten = retry;
                if (captureTitle && retryBlocks.Title.Length > 0)
                {
                    titleDraft = retryBlocks.Title;
                }

                if (retryBlocks.SearchTerms.Count > 0)
                {
                    searchTerms = retryBlocks.SearchTerms.ToList();
                }

                if (retryBlocks.ProductHighlights.Count > 0)
                {
                    highlights = retryBlocks.ProductHighlights.ToList();
                }
            }
        }

        if (rewritten.Length > MaxPlainTextLength)
        {
            rewritten = StripForbiddenDescriptionSections(
                TrimPreservingClosing(rewritten, MaxPlainTextLength));
        }

        return new DescriptionRewritePass(
            rewritten,
            captureTitle ? titleDraft : string.Empty,
            searchTerms,
            highlights);
    }

    private static RewriteListingDescriptionResultDto ToRewriteResult(
        string rewritten,
        string title,
        IReadOnlyList<string> searchTerms,
        IReadOnlyList<string> highlights,
        bool stripMaterialMentions = false)
    {
        if (stripMaterialMentions)
        {
            rewritten = StripFabricFromHashtagLine(StripFabricMentionLines(rewritten));
            searchTerms = DropFabricMetaLines(searchTerms);
            highlights = DropFabricMetaLines(highlights);
        }

        return new RewriteListingDescriptionResultDto
        {
            DescriptionHtml = MarkdownLiteToHtml(rewritten),
            Title = title,
            SearchTerms = searchTerms.ToList(),
            ProductHighlights = highlights.ToList()
        };
    }

    /// <summary>
    /// Missing or unknown values fall back to strip-brands so a typo cannot silently
    /// switch every listing onto the SEO rewrite.
    /// </summary>
    internal static string ParseTitleMode(string? raw)
    {
        return string.Equals(
            raw?.Trim(),
            ListingRewriteSettingKeys.TitleModeRewrite,
            StringComparison.OrdinalIgnoreCase)
            ? ListingRewriteSettingKeys.TitleModeRewrite
            : ListingRewriteSettingKeys.TitleModeStripBrands;
    }

    /// <summary>
    /// Missing or unknown values lock Rewrite after the first run so a typo cannot
    /// silently spend a second OpenAI call.
    /// </summary>
    internal static bool ParseAllowRepeat(string? raw)
        => string.Equals(raw?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Removes AI-flagged brand tokens then sanitizes (promo, marketplace regex, title case).
    /// </summary>
    internal static string ApplyStrippedTitle(string originalTitle, IReadOnlyList<string> brands)
        => SanitizeTitle(StripBrandTokens(originalTitle, brands), originalTitle);

    /// <summary>
    /// TikTok cannot classify a title under <see cref="MinTitleLength"/> characters.
    /// Strip-brands keeps source wording when the leftover is long enough; below this
    /// floor a new SEO title is cheaper than publishing an unsearchable name.
    /// </summary>
    internal static bool NeedsSeoTitleFallback(string title)
        => string.IsNullOrWhiteSpace(title) || title.Trim().Length < MinTitleLength;

    /// <summary>
    /// Search queries use <c>Women</c>, not <c>Women's</c>. Men's and unisex titles
    /// are left alone. Plus-size listings keep Plus Size in front and insert Women after it.
    /// </summary>
    internal static string EnsureWomenAudienceLead(
        string title,
        string sourceTitle,
        string productFacts)
    {
        if (string.IsNullOrWhiteSpace(title) || !IsWomensProduct(sourceTitle, productFacts))
        {
            return title ?? string.Empty;
        }

        var text = LadiesTokenRegex.Replace(
            WomensPossessiveRegex.Replace(title, "Women"),
            "Women");
        text = DuplicateWomenRegex.Replace(text, "Women");
        text = MultiSpaceRegex.Replace(text, " ").Trim(TitleEdgeChars);

        if (WomenLeadRegex.IsMatch(text) || PlusSizeWomenLeadRegex.IsMatch(text))
        {
            return text.Length <= MaxTitleLength
                ? text
                : TrimTitleToWordBoundary(text, MaxTitleLength);
        }

        text = PlusSizeLeadRegex.IsMatch(text)
            ? PlusSizeLeadRegex.Replace(text, "Plus Size Women")
            : "Women " + text;
        text = DuplicateWomenRegex.Replace(text, "Women");
        text = MultiSpaceRegex.Replace(text, " ").Trim(TitleEdgeChars);

        return text.Length <= MaxTitleLength
            ? text
            : TrimTitleToWordBoundary(text, MaxTitleLength);
    }

    /// <summary>
    /// Women's fashion is the shop default. Only skip the Women lead when the listing
    /// is explicitly men's, unisex or kids and never names women.
    /// </summary>
    internal static bool IsWomensProduct(string sourceTitle, string productFacts)
    {
        var blob = $"{sourceTitle}\n{productFacts}";
        if (WomensAudienceRegex.IsMatch(blob))
        {
            return true;
        }

        return !MensAudienceRegex.IsMatch(blob)
               && !UnisexAudienceRegex.IsMatch(blob)
               && !KidsAudienceRegex.IsMatch(blob);
    }

    /// <summary>
    /// Drops brand tokens from the source title without rewriting the remaining words.
    /// Longer tokens first so "Ali Express" is not left as "Express" after "Ali".
    /// When the model only flags SHEIN, a following in-house line (BAE, ICON, …) is
    /// removed in the same pass.
    /// </summary>
    internal static string StripBrandTokens(string title, IEnumerable<string>? brands)
    {
        var text = title ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) || brands is null)
        {
            return text;
        }

        var tokens = brands
            .Select(b => (b ?? string.Empty).Trim())
            .Where(b => b.Length >= 2)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(b => b.Length)
            .ToList();

        foreach (var brand in tokens)
        {
            var pattern = brand.Equals("shein", StringComparison.OrdinalIgnoreCase)
                ? $@"\bshein(?:\s+(?:{SheinPrivateLabelPattern}))?\b"
                : $@"\b{Regex.Escape(brand)}\b";
            text = Regex.Replace(
                text,
                pattern,
                " ",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        return MultiSpaceRegex.Replace(text, " ").Trim(TitleEdgeChars);
    }

    /// <summary>Parses <c>{"brands":["SHEIN"]}</c>, including markdown fences.</summary>
    internal static IReadOnlyList<string> ParseBrandList(string? raw)
    {
        var json = UnwrapJsonObject(raw);
        if (json.Length == 0)
        {
            return [];
        }

        try
        {
            var parsed = JsonSerializer.Deserialize<TitleBrandsJson>(json, TitleBrandsJsonOptions);
            return (parsed?.Brands ?? [])
                .Select(b => (b ?? string.Empty).Trim())
                .Where(b => b.Length >= 2)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string UnwrapJsonObject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var text = raw.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNl = text.IndexOf('\n');
            if (firstNl >= 0)
            {
                text = text[(firstNl + 1)..];
            }

            var fence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0)
            {
                text = text[..fence];
            }

            text = text.Trim();
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : string.Empty;
    }

    private async Task<IReadOnlyList<string>> DetectTitleBrandsSafeAsync(
        string title,
        CancellationToken cancellationToken)
    {
        try
        {
            var raw = await _openAiService.StripTitleBrandsAsync(title, cancellationToken);
            return ParseBrandList(raw);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return [];
        }
    }

    private sealed class TitleBrandsJson
    {
        public List<string>? Brands { get; set; }
    }

    /// <summary>
    /// Sanitizes the title the description call already produced, and only pays for a dedicated
    /// title call when that draft fails a check.
    ///
    /// The draft comes from the same completion as the description, so it is written against the
    /// copy the shopper actually reads: a title that contradicts the description splits the
    /// listing. It also costs nothing extra, whereas the separate call resends the whole 2.000
    /// character description as context.
    ///
    /// A title failure never throws. The description is already paid for, so the worst case
    /// returns the cleaned original title rather than losing the whole rewrite.
    /// </summary>
    private async Task<string> BuildSeoTitleAsync(
        string originalTitle,
        string productFacts,
        string descriptionMarkdown,
        string titleDraft,
        CancellationToken cancellationToken,
        string? extraInstruction = null,
        int? seed = null)
    {
        if (titleDraft.Length > 0)
        {
            var draftClean = SanitizeTitle(titleDraft, originalTitle);
            if (DescribeTitleProblem(titleDraft, draftClean, originalTitle) is null)
            {
                return FinishRewriteTitle(draftClean, originalTitle);
            }
        }

        var input = new ProductTitleRewriteInput
        {
            OriginalTitle = originalTitle,
            RewrittenDescription = descriptionMarkdown,
            ProductFacts = productFacts,
            ExtraInstruction = extraInstruction,
            Seed = seed
        };

        try
        {
            var raw = (await _openAiService.RewriteProductTitleAsync(input, cancellationToken)).Trim();
            var sanitized = SanitizeTitle(raw, originalTitle);

            // The model cannot count characters and routinely repeats the head noun, so the
            // checks live here — one retry with the specific complaint, like the description.
            var problem = DescribeTitleProblem(raw, sanitized, originalTitle);
            if (problem is null)
            {
                return FinishRewriteTitle(sanitized, originalTitle);
            }

            var retryHint =
                problem
                + $" Add 2-4 same-product synonym queries US shoppers type and fill toward "
                + $"{TargetTitleMin}-{TargetTitleMax}. Examples: Halter Neck, Knit Tank, "
                + "Sleeveless Top, Sleep Dress, Nightdress, Tee, Outerwear, Camisole. "
                + "Do not pad with Club Top Party Top Summer Top. If you cannot reach "
                + $"{TargetTitleMin} without inventing a material, fit, print, season or occasion, "
                + "stop short. Do not invent a garment family such as Matching Set on a single "
                + "top, do not drop a source print or style word, and do not pack spec-sheet rows.";
            input.ExtraInstruction = CombineTitleInstructions(extraInstruction, retryHint);

            var retryRaw = (await _openAiService.RewriteProductTitleAsync(input, cancellationToken)).Trim();
            var retry = SanitizeTitle(retryRaw, originalTitle);
            if (DescribeTitleProblem(retryRaw, retry, originalTitle) is null)
            {
                return FinishRewriteTitle(retry, originalTitle);
            }

            // Every attempt fell short. Prefer a title that stayed on the source garment
            // over a longer one that invented attributes to hit the character band.
            var draft = titleDraft.Length > 0 ? SanitizeTitle(titleDraft, originalTitle) : string.Empty;
            var best = PickSaferTitle(originalTitle, retry, sanitized, draft);
            return FinishRewriteTitle(
                best.Length > 0 ? best : SanitizeTitle(originalTitle, originalTitle),
                originalTitle);
        }
        catch (BusinessException)
        {
            return FinishRewriteTitle(
                titleDraft.Length > 0
                    ? SanitizeTitle(titleDraft, originalTitle)
                    : SanitizeTitle(originalTitle, originalTitle),
                originalTitle);
        }
    }

    private static string CombineTitleInstructions(string? extra, string? retryHint)
    {
        var left = (extra ?? string.Empty).Trim();
        var right = (retryHint ?? string.Empty).Trim();
        if (left.Length == 0)
        {
            return right;
        }

        if (right.Length == 0)
        {
            return left;
        }

        return left + "\n\n" + right;
    }

    /// <summary>
    /// Returns why a title is worth one more call, or null when it is good enough. Every
    /// fault is reported at once — the budget is a single retry, so holding one back would
    /// just trade one defect for another. The text is fed to the model as ADDITIONAL REQUIREMENT.
    /// </summary>
    private static string? DescribeTitleProblem(string raw, string sanitized, string sourceTitle)
    {
        var faults = new List<string>();

        if (raw.Length > TargetTitleMax || sanitized.Length > TargetTitleMax)
        {
            var over = Math.Max(raw.Length, sanitized.Length);
            faults.Add($"it ran to {over} characters, past the {TargetTitleMax} limit");
        }
        else if (sanitized.Length < MinAcceptableTitleLength
                 && FillFromSourceTokens(sanitized, sourceTitle).Length < MinAcceptableTitleLength)
        {
            faults.Add(
                $"it was only {sanitized.Length} characters. Expand with 2-4 same-product "
                + "synonym queries US shoppers type, not a reshuffle of the original. If you "
                + $"cannot reach {TargetTitleMin} without inventing a material, fit, print, "
                + "season or occasion, stop short");
        }

        if (CountNovelTitleStems(sanitized, sourceTitle) < 2)
        {
            faults.Add(
                "it only reshuffled the original title and added almost no broader search "
                + "coverage. Add 2-4 same-product synonym queries such as Halter Neck, Knit "
                + "Tank, Sleeveless Top, Sleep Dress, Nightdress, Tee, Camisole or Outerwear. "
                + "Do not pad with Club Top Party Top Summer Top");
        }

        var missing = FindMissingSourceKeyword(sanitized, sourceTitle);
        if (missing is not null)
        {
            faults.Add($"it dropped the word {missing}, which the source title advertises and "
                       + "which US shoppers search for on its own");
        }

        var droppedHeadline = FindDroppedSourceHeadlineToken(sanitized, sourceTitle);
        if (droppedHeadline is not null)
        {
            faults.Add($"it dropped the source search word {droppedHeadline}, which is the "
                       + "listing's ranking identity and must stay in the title");
        }

        var specJunk = FindSpecSheetJunk(sanitized, sourceTitle);
        if (specJunk is not null)
        {
            faults.Add(
                $"it packed the spec-sheet phrase '{specJunk}', which US shoppers almost never "
                + "type; drop Non Sheer, Regular Length, Medium Stretch, Polyester and fill with "
                + "leftover source search terms instead");
        }

        var inventedGarment = FindInventedGarmentType(sanitized, sourceTitle);
        if (inventedGarment is not null)
        {
            faults.Add(
                $"it invented the garment type '{inventedGarment}', which the source title never "
                + "names; drop it and fill with primary and synonymous queries of the same product");
        }

        var weakLongTail = FindWeakLongTail(sanitized);
        if (weakLongTail is not null)
        {
            faults.Add(
                $"it ended on the filler long-tail '{weakLongTail}', which US shoppers almost "
                + "never type; drop that ending. Keep an occasion or season only when the "
                + "original title already named it");
        }

        return faults.Count == 0
            ? null
            : "The previous attempt failed because " + string.Join("; and ", faults) + ".";
    }

    /// <summary>
    /// Returns the filler long-tail phrase when the title burns its use-case slot on wording
    /// with almost no search demand (<c>Daily Wear</c>, <c>Layering Wear</c>, …).
    /// </summary>
    internal static string? FindWeakLongTail(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var match = WeakLongTailRegex.Match(title);
        return match.Success ? match.Value.Trim() : null;
    }

    /// <summary>
    /// Returns the highest-value attribute the supplier title advertises but the rewrite threw
    /// away. Only the source <em>title</em> counts: a supplier who spent title characters on a
    /// word was calling it the selling point, whereas the description mentions everything.
    /// </summary>
    internal static string? FindMissingSourceKeyword(string title, string sourceTitle)
    {
        if (string.IsNullOrWhiteSpace(sourceTitle))
        {
            return null;
        }

        foreach (var (label, pattern) in TitleSourceKeywords)
        {
            if (pattern.IsMatch(sourceTitle) && !pattern.IsMatch(title))
            {
                return label;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns a spec-table phrase the rewrite dumped into the title (Non Sheer, Regular
    /// Length, Medium Stretch, Polyester, …). Ignored when the source title already uses it.
    /// </summary>
    internal static string? FindSpecSheetJunk(string title, string sourceTitle)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        foreach (Match match in TitleSpecSheetJunkRegex.Matches(title))
        {
            var phrase = match.Value.Trim();
            if (phrase.Length == 0)
            {
                continue;
            }

            var inSource = !string.IsNullOrWhiteSpace(sourceTitle)
                           && Regex.IsMatch(
                               sourceTitle,
                               $@"\b{Regex.Escape(phrase)}\b",
                               RegexOptions.IgnoreCase);
            if (!inSource)
            {
                return phrase;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns a distinctive source-title token the rewrite dropped (paisley, grunge, y2k,
    /// print-through). Skips brand, audience, season, color and generic garment nouns so
    /// dropping EZwear or Summer is not a fault.
    /// </summary>
    internal static string? FindDroppedSourceHeadlineToken(string title, string sourceTitle)
    {
        if (string.IsNullOrWhiteSpace(sourceTitle) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var sourceWords = TokenizeTitleWords(sourceTitle);
        if (sourceWords.Count == 0)
        {
            return null;
        }

        var start = 0;
        if (!TitleHeadlineLeadWords.Contains(sourceWords[0])
            && !TitleHeadlineSkipWords.Contains(sourceWords[0])
            && sourceWords[0].Length >= 4)
        {
            start = 1;
        }

        var titleStems = new HashSet<string>(
            TokenizeTitleWords(title).Select(StemTitleWord),
            StringComparer.OrdinalIgnoreCase);

        for (var i = start; i < sourceWords.Count; i++)
        {
            var word = sourceWords[i];
            if (!IsSourceHeadlineToken(word))
            {
                continue;
            }

            if (!titleStems.Contains(StemTitleWord(word)))
            {
                return word;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns a garment family the rewrite introduced that the source title never named.
    /// Same-family synonyms are allowed (bandeau plus tube, pants plus trousers).
    /// </summary>
    internal static string? FindInventedGarmentType(string title, string sourceTitle)
    {
        if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(sourceTitle))
        {
            return null;
        }

        var sourceFamilies = DetectGarmentFamilies(sourceTitle);
        foreach (var family in DetectGarmentFamilies(title))
        {
            if (!sourceFamilies.Contains(family))
            {
                return family;
            }
        }

        return null;
    }

    /// <summary>
    /// Picks among failed rewrite attempts. An invented garment or spec-sheet dump
    /// loses to a shorter title that stayed on the source product.
    /// </summary>
    internal static string PickSaferTitle(string sourceTitle, params string[] candidates)
    {
        var list = candidates.Where(c => !string.IsNullOrWhiteSpace(c)).ToArray();
        if (list.Length == 0)
        {
            return string.Empty;
        }

        return list
            .OrderBy(t => FindInventedGarmentType(t, sourceTitle) is null ? 0 : 1)
            .ThenBy(t => FindSpecSheetJunk(t, sourceTitle) is null ? 0 : 1)
            .ThenBy(t => TitleLengthBandPenalty(t))
            .ThenByDescending(t => t.Length)
            .First();
    }

    private static int TitleLengthBandPenalty(string title)
    {
        if (title.Length is >= TargetTitleMin and <= TargetTitleMax)
        {
            return 0;
        }

        if (title.Length is >= MinTitleLength and <= MaxTitleLength)
        {
            return 1;
        }

        return 2;
    }

    private static HashSet<string> DetectGarmentFamilies(string text)
    {
        var compact = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var word in TokenizeGarmentWords(text))
        {
            compact.Add(word);
        }

        var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (label, tokens) in TitleGarmentFamilies)
        {
            foreach (var token in tokens)
            {
                if (compact.Contains(token))
                {
                    families.Add(label);
                    break;
                }
            }
        }

        if (MatchingSetPhraseRegex.IsMatch(text))
        {
            families.Add("matching set");
        }

        return families;
    }

    private static IEnumerable<string> TokenizeGarmentWords(string text)
    {
        foreach (var raw in text.Split([' ', ',', '/', '&'], StringSplitOptions.RemoveEmptyEntries))
        {
            var bare = raw.Trim(TitleEdgeChars);
            if (bare.Length == 0)
            {
                continue;
            }

            yield return bare;
            var compact = bare.Replace("-", "", StringComparison.Ordinal);
            if (!compact.Equals(bare, StringComparison.OrdinalIgnoreCase))
            {
                yield return compact;
            }
        }
    }

    private static List<string> TokenizeTitleWords(string text)
    {
        var words = new List<string>();
        foreach (var raw in text.Split([' ', ',', '-', '/', '&'], StringSplitOptions.RemoveEmptyEntries))
        {
            var bare = raw.Trim(TitleEdgeChars);
            if (bare.Length == 0
                || TitleMarketplaceRegex.IsMatch(bare)
                || SheinPrivateLabelTokens.Contains(bare))
            {
                continue;
            }

            words.Add(bare);
        }

        return words;
    }

    private static bool IsSourceHeadlineToken(string word)
    {
        if (TitleLowercaseWords.Contains(word) || TitleHeadlineSkipWords.Contains(word))
        {
            return false;
        }

        if (word.Length >= 4)
        {
            return true;
        }

        return word.Length >= 2 && word.Any(char.IsDigit);
    }

    private static string StemTitleWord(string word)
    {
        var bare = word.Trim(TitleEdgeChars);
        if (bare.StartsWith("oversize", StringComparison.OrdinalIgnoreCase))
        {
            return "oversize";
        }

        return bare.Length > 3 && bare.EndsWith('s') ? bare[..^1] : bare;
    }

    /// <summary>
    /// How many title stems are not already in the source. A rewrite that only reorders
    /// the original scores 0 and needs synonym expansion, not more of the same words.
    /// </summary>
    internal static int CountNovelTitleStems(string title, string sourceTitle)
    {
        var sourceStems = new HashSet<string>(
            TokenizeTitleWords(sourceTitle).Select(StemTitleWord),
            StringComparer.OrdinalIgnoreCase);
        var novel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var word in TokenizeTitleWords(title))
        {
            if (word.Length < 4
                || TitleLowercaseWords.Contains(word)
                || TitleHeadlineLeadWords.Contains(word)
                || TitleCatalogYearRegex.IsMatch(word)
                || TitleMarketplaceRegex.IsMatch(word)
                || TitlePromoRegex.IsMatch(word))
            {
                continue;
            }

            var stem = StemTitleWord(word);
            if (stem.Length < 4 || sourceStems.Contains(stem))
            {
                continue;
            }

            novel.Add(stem);
        }

        return novel.Count;
    }

    internal static bool SourceImpliesSleeveless(string sourceTitle) =>
        SourceSleevelessGarmentRegex.IsMatch(sourceTitle ?? string.Empty);

    /// <summary>
    /// Finds a keyword used twice anywhere in the title. Singular and plural count as the
    /// same word: <c>Yoga Shorts … Boy Shorts</c> and <c>Biker Shorts … Workout Short</c> both
    /// spend the head noun twice, and the regex pass only collapses adjacent repeats.
    /// </summary>
    internal static string? FindRepeatedWord(string title)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var word in title.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var bare = word.Trim(TitleEdgeChars);
            if (bare.Length < 3 || TitleLowercaseWords.Contains(bare))
            {
                continue;
            }

            var stem = bare.Length > 3 && bare.EndsWith('s') ? bare[..^1] : bare;
            if (!seen.Add(stem))
            {
                return bare;
            }
        }

        return null;
    }

    /// <summary>
    /// Cleans a title down to what the Product name field accepts: no marketplace names,
    /// no emoji, no promo wording, title case and within the 255 cap. Falls back to the
    /// cleaned original when the model returns something too short to classify.
    /// </summary>
    internal static string SanitizeTitle(string raw, string fallbackTitle)
    {
        var cleaned = PolishTitle(
            RestoreSourceWaistWording(CleanTitle(raw, fallbackTitle), fallbackTitle),
            fallbackTitle);
        if (cleaned.Length >= MinTitleLength)
        {
            return cleaned;
        }

        // A short model title still beats a supplier title stuffed with marketplace noise,
        // so the fallback only wins when it actually carries more usable text.
        var fallback = PolishTitle(CleanTitle(fallbackTitle, fallbackTitle), fallbackTitle);
        return fallback.Length > cleaned.Length ? fallback : cleaned;
    }

    /// <summary>
    /// Drops catalog years, concatenated slogans and invented occasions, then pads with
    /// leftover source search tokens when the rewrite still sits under
    /// <see cref="TargetTitleMin"/>. Called only on the SEO rewrite path — strip-brands
    /// keeps source wording. Never invents tokens to fill the band.
    /// </summary>
    internal static string FinishRewriteTitle(string title, string sourceTitle)
    {
        var cleaned = string.IsNullOrWhiteSpace(title)
            ? SanitizeTitle(sourceTitle, sourceTitle)
            : title;
        var filled = FillFromSourceTokens(cleaned, sourceTitle);
        // Fill can re-insert OverSize / Daily Wear from the source after CleanTitle already
        // normalized them. Re-polish so leftover source tokens do not undo the sanitizer.
        return PolishTitle(
            OverSizePhraseRegex.Replace(SplitCamelConcatenations(filled), "Oversized"),
            sourceTitle);
    }

    private static HashSet<string> BuildSheinLeadKeepCompact()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in TitleHeadlineLeadWords
                     .Concat(TitleHeadlineSkipWords)
                     .Concat(SheinLeadKeepExtras))
        {
            var compact = CompactTitleToken(word);
            if (compact.Length > 0)
            {
                set.Add(compact);
            }
        }

        return set;
    }

    private static string CompactTitleToken(string token) =>
        NonLetterRegex.Replace((token ?? string.Empty).ToLowerInvariant(), string.Empty);

    /// <summary>
    /// After SHEIN is gone, drop a leftover in-house line that sat right after it
    /// (<c>BAE Women's …</c>) and unknown future lines in the same <c>SHEIN X Women's</c> slot.
    /// Only runs when the source still contains SHEIN, so a real title starting with ICON is kept.
    /// </summary>
    private static string StripOrphanSheinLine(string text, string sourceTitle)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text ?? string.Empty;
        }

        var sourceHasShein = !string.IsNullOrWhiteSpace(sourceTitle)
                             && SheinTokenRegex.IsMatch(sourceTitle);
        if (!sourceHasShein && !SheinTokenRegex.IsMatch(text))
        {
            return text;
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2)
        {
            return text;
        }

        var first = words[0].Trim(TitleEdgeChars);
        var second = words[1].Trim(TitleEdgeChars);
        if (first.Length == 0)
        {
            return text;
        }

        if (first.Equals("emery", StringComparison.OrdinalIgnoreCase)
            && second.Equals("rose", StringComparison.OrdinalIgnoreCase))
        {
            return string.Join(' ', words.Skip(2));
        }

        if (SheinPrivateLabelTokens.Contains(first)
            || ShouldDropUnknownSheinLine(first, second))
        {
            return string.Join(' ', words.Skip(1));
        }

        return text;
    }

    private static bool ShouldDropUnknownSheinLine(string first, string second) =>
        IsAudienceOrSizeLead(second) && !IsSheinLeadKeep(first);

    private static bool IsAudienceOrSizeLead(string token)
    {
        var compact = CompactTitleToken(token);
        if (compact.Length == 0)
        {
            return false;
        }

        if (compact.StartsWith("plus", StringComparison.Ordinal))
        {
            return true;
        }

        var bare = token.Trim(TitleEdgeChars);
        return TitleHeadlineLeadWords.Contains(bare)
               || TitleHeadlineLeadWords.Contains(compact)
               || compact is "womens" or "womans" or "mens" or "mans" or "ladies";
    }

    private static bool IsSheinLeadKeep(string token)
    {
        var compact = CompactTitleToken(token);
        if (compact.Length == 0)
        {
            return true;
        }

        var bare = token.Trim(TitleEdgeChars);
        return SheinLeadKeepCompact.Contains(compact)
               || TitleHeadlineLeadWords.Contains(bare)
               || TitleHeadlineSkipWords.Contains(bare);
    }

    private static string PolishTitle(string title, string sourceTitle)
    {
        title = StripUnsourcedOccasions(title, sourceTitle);
        title = StripUnsourcedClaims(title, sourceTitle);
        title = StripUnsourcedShortDress(title, sourceTitle);
        title = WeakLongTailRegex.Replace(title ?? string.Empty, " ");
        title = StripTrailingAudienceRepeat(title);
        title = MultiSpaceRegex.Replace(title ?? string.Empty, " ").Trim(TitleEdgeChars);
        return TrimDanglingStopWords(title);
    }

    private static string CleanTitle(string raw, string sourceTitle)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        // Only the first line matters: models occasionally append an explanation.
        var text = raw.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')[0];

        text = text
            .Replace('\u2019', '\'')
            .Replace('\u2018', '\'')
            .Replace('\u2013', '-')
            .Replace('\u2014', '-');

        text = SplitCamelConcatenations(text);
        text = OverSizePhraseRegex.Replace(text, "Oversized");

        // Promo wording is stripped before the symbol pass so $ and % are still there to match on.
        text = TitlePromoRegex.Replace(text, " ");
        text = TitleMarketplaceRegex.Replace(text, " ");
        text = StripOrphanSheinLine(text, sourceTitle);
        text = TitleCatalogYearRegex.Replace(text, " ");
        text = BoyShortsRegex.Replace(text, "$1");
        text = TitleDisallowedCharRegex.Replace(text, " ");
        text = MultiSpaceRegex.Replace(text, " ");
        text = TitleSeparatorRunRegex.Replace(text, "$1 ");
        text = TitleDuplicateWordRegex.Replace(text, "$1");
        text = MultiSpaceRegex.Replace(text, " ").Trim(TitleEdgeChars);

        text = NormalizeLetterShapeTerms(ToSeoTitleCase(TrimDanglingStopWords(text)));
        text = DecadeApostropheRegex.Replace(text, "$1's");
        return text.Length <= MaxTitleLength ? text : TrimTitleToWordBoundary(text, MaxTitleLength);
    }

    private static string SplitCamelConcatenations(string text) =>
        CamelConcatRegex.Replace(
            text,
            m => string.Join(" ", CamelPartRegex.Matches(m.Value).Select(p => p.Value)));

    /// <summary>
    /// Removes a season, occasion or holiday the rewrite invented. Compact-letter matching
    /// is intentionally avoided: <c>PerfectSummer</c> must not count as source evidence for
    /// <c>Summer</c>. Christmas stays when the source already advertised it.
    /// </summary>
    internal static string StripUnsourcedOccasions(string title, string sourceTitle)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return title ?? string.Empty;
        }

        return TitleOccasionRegex.Replace(
            title,
            m => SourceHasOccasion(sourceTitle, m.Value) ? m.Value : " ");
    }

    /// <summary>
    /// Drops subjective claims the original title never used (sexy, premium, luxury, …).
    /// </summary>
    internal static string StripUnsourcedClaims(string title, string sourceTitle)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return title ?? string.Empty;
        }

        return TitleUnsourcedClaimRegex.Replace(
            title,
            m =>
            {
                var phrase = NormalizeOccasionPhrase(m.Value);
                if (SourceHasPhrase(sourceTitle, phrase))
                {
                    return m.Value;
                }

                // Tank / cami / halter / bandeau are sleeveless by name — that is a
                // same-product search synonym, not an invented spec.
                if (phrase == "sleeveless" && SourceImpliesSleeveless(sourceTitle))
                {
                    return m.Value;
                }

                return " ";
            });
    }

    /// <summary>
    /// Mini Dress must not become Short Dress. Keep Short Dress only when the source
    /// already uses the word Short.
    /// </summary>
    internal static string StripUnsourcedShortDress(string title, string sourceTitle)
    {
        if (string.IsNullOrWhiteSpace(title) || SourceHasPhrase(sourceTitle, "short"))
        {
            return title ?? string.Empty;
        }

        return TitleShortDressRegex.Replace(title, " ");
    }

    /// <summary>
    /// Drops a stray <c>Women</c> glued on the end after Women's already opened the title.
    /// </summary>
    private static string StripTrailingAudienceRepeat(string title)
    {
        if (string.IsNullOrWhiteSpace(title)
            || !Regex.IsMatch(title, @"\bwomen'?s\b", RegexOptions.IgnoreCase))
        {
            return title ?? string.Empty;
        }

        return TrailingAudienceRegex.Replace(title, string.Empty);
    }

    private static bool SourceHasOccasion(string sourceTitle, string matchedPhrase)
    {
        var phrase = NormalizeOccasionPhrase(matchedPhrase);
        if (phrase.Length == 0)
        {
            return false;
        }

        if (SourceHasPhrase(sourceTitle, phrase))
        {
            return true;
        }

        return phrase switch
        {
            "xmas" => SourceHasPhrase(sourceTitle, "christmas"),
            "christmas" => SourceHasPhrase(sourceTitle, "xmas"),
            "fall" => SourceHasPhrase(sourceTitle, "autumn"),
            "autumn" => SourceHasPhrase(sourceTitle, "fall"),
            _ => false
        };
    }

    private static string NormalizeOccasionPhrase(string phrase) =>
        MultiSpaceRegex.Replace(
            (phrase ?? string.Empty).Replace('-', ' ').ToLowerInvariant(),
            " ").Trim();

    private static bool SourceHasPhrase(string sourceTitle, string phrase)
    {
        if (string.IsNullOrWhiteSpace(sourceTitle) || string.IsNullOrWhiteSpace(phrase))
        {
            return false;
        }

        var pattern = @"\b" + Regex.Escape(phrase).Replace(@"\ ", @"[\s\-]+") + @"\b";
        return Regex.IsMatch(sourceTitle, pattern, RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// Packs unused source search tokens into a short rewrite so it can reach
    /// <see cref="TargetTitleMin"/> without another model call. Skips the leading
    /// supplier brand, marketplace names and catalog years. Inserts before the
    /// <c>for</c> long-tail when one already exists. Stops when leftover source
    /// tokens run out — never invents words to fill the band.
    /// </summary>
    internal static string FillFromSourceTokens(string title, string sourceTitle)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length >= TargetTitleMin)
        {
            return title ?? string.Empty;
        }

        var used = new HashSet<string>(
            TokenizeTitleWords(title).Select(StemTitleWord),
            StringComparer.OrdinalIgnoreCase);
        var sourceWords = TokenizeTitleWords(sourceTitle);
        var start = 0;
        if (sourceWords.Count > 0
            && !TitleHeadlineLeadWords.Contains(sourceWords[0])
            && !TitleHeadlineSkipWords.Contains(sourceWords[0])
            && sourceWords[0].Length >= 4)
        {
            start = 1;
        }

        var current = title;
        for (var i = start; i < sourceWords.Count && current.Length < TargetTitleMin; i++)
        {
            var word = sourceWords[i];
            if (word.Length < 3
                || TitleLowercaseWords.Contains(word)
                || TitleCatalogYearRegex.IsMatch(word)
                || TitleMarketplaceRegex.IsMatch(word)
                || TitlePromoRegex.IsMatch(word))
            {
                continue;
            }

            if (!used.Add(StemTitleWord(word)))
            {
                continue;
            }

            var token = CapitalizeTitleWord(word);
            var forIdx = current.LastIndexOf(" for ", StringComparison.OrdinalIgnoreCase);
            var next = forIdx > 0
                ? current[..forIdx] + " " + token + current[forIdx..]
                : current + " " + token;
            if (next.Length > TargetTitleMax)
            {
                continue;
            }

            current = next;
        }

        return current;
    }

    /// <summary>
    /// Restores the hyphenated spelling of silhouette and neckline terms that open with a single
    /// letter. Title casing treats a lone <c>A</c> as the article and lowercases it, turning the
    /// searched term <c>A-Line</c> into <c>a Line</c>; a shopper searching a-line dress then
    /// misses the listing entirely. Handled after casing so it also fixes the unhyphenated form.
    /// </summary>
    private static string NormalizeLetterShapeTerms(string text) =>
        LetterShapeTermRegex.Replace(
            text,
            m => $"{char.ToUpperInvariant(m.Groups[1].Value[0])}-{CapitalizeTitleWord(m.Groups[2].Value)}");

    /// <summary>
    /// Puts the source's own waist wording back. Models keep trading the shopper phrase
    /// <c>high waist</c> for the trade term <c>high rise</c>, which almost nobody searches —
    /// so when the supplier title said high waist, that spelling wins.
    /// </summary>
    private static string RestoreSourceWaistWording(string title, string sourceTitle)
        => HighRiseRegex.IsMatch(title) && HighWaistRegex.IsMatch(sourceTitle ?? string.Empty)
            ? HighRiseRegex.Replace(title, "High Waist")
            : title;

    /// <summary>
    /// Removes the preposition or conjunction left hanging at either end once a banned word
    /// was cut out, so <c>… Vacation on Lazada</c> does not end on a dangling <c>on</c>.
    /// </summary>
    private static string TrimDanglingStopWords(string text)
    {
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();

        while (words.Count > 0 && TitleLowercaseWords.Contains(words[0].Trim(TitleEdgeChars)))
        {
            words.RemoveAt(0);
        }

        while (words.Count > 0 && TitleLowercaseWords.Contains(words[^1].Trim(TitleEdgeChars)))
        {
            words.RemoveAt(words.Count - 1);
        }

        return string.Join(' ', words);
    }

    /// <summary>
    /// TikTok's listing-quality rule: capitalize every word except conjunctions, articles
    /// and prepositions. Shouted supplier titles are folded down first.
    /// </summary>
    private static string ToSeoTitleCase(string text)
    {
        // A title shouted end to end carries no capitalization worth preserving.
        if (!text.Any(char.IsLower))
        {
            text = text.ToLowerInvariant();
        }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];

            // Short all-caps tokens such as UPF, 3D or USA are real; longer ones are shouting.
            if (word.Length >= 4 && word.All(c => !char.IsLetter(c) || char.IsUpper(c)))
            {
                word = word.ToLowerInvariant();
            }

            var bare = word.Trim(TitleEdgeChars);
            words[i] = i > 0 && i < words.Length - 1 && TitleLowercaseWords.Contains(bare)
                ? word.ToLowerInvariant()
                : CapitalizeTitleWord(word);
        }

        return string.Join(' ', words);
    }

    /// <summary>Capitalizes the first letter and every letter after a hyphen (Wide-Leg, 2-Piece).</summary>
    private static string CapitalizeTitleWord(string word)
    {
        var chars = word.ToCharArray();
        var atStart = true;

        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] == '-')
            {
                atStart = true;
                continue;
            }

            if (!char.IsLetter(chars[i]))
            {
                continue;
            }

            if (atStart)
            {
                chars[i] = char.ToUpperInvariant(chars[i]);
            }

            atStart = false;
        }

        return new string(chars);
    }

    private static string TrimTitleToWordBoundary(string title, int max)
    {
        var cut = title[..max];
        var lastSpace = cut.LastIndexOf(' ');
        return (lastSpace > max / 2 ? cut[..lastSpace] : cut).TrimEnd(TitleEdgeChars);
    }

    /// <summary>
    /// Splits a completion into the description body and the SEO title that follows the
    /// sentinel. A completion without the sentinel yields an empty title, which sends the
    /// caller down the dedicated title call — so an older prompt or a model that ignores the
    /// instruction degrades to the previous two-call behaviour instead of losing the title.
    /// </summary>
    internal static (string Body, string Title) SplitTitleBlock(string completion)
    {
        var blocks = SplitRewriteBlocks(completion);
        return (blocks.Body, blocks.Title);
    }

    /// <summary>
    /// Splits one completion into description + optional <c>===SEO_TITLE===</c> /
    /// <c>===SEARCH_TERMS===</c> / <c>===PRODUCT_HIGHLIGHTS===</c> blocks. Body is everything
    /// before the first sentinel so <see cref="Normalize"/> never copies meta into HTML.
    /// Missing sentinels yield empty title / empty lists rather than failing the rewrite.
    /// </summary>
    internal static RewriteCompletionBlocks SplitRewriteBlocks(string completion)
    {
        var text = completion ?? string.Empty;
        var markers = new List<(int Index, string Sentinel)>(3);
        AddSentinelMarker(markers, text, TitleSentinel);
        AddSentinelMarker(markers, text, SearchTermsSentinel);
        AddSentinelMarker(markers, text, ProductHighlightsSentinel);
        markers.Sort(static (a, b) => a.Index.CompareTo(b.Index));

        if (markers.Count == 0)
        {
            return new RewriteCompletionBlocks(text, string.Empty, [], []);
        }

        var body = text[..markers[0].Index];
        var sections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < markers.Count; i++)
        {
            var start = markers[i].Index + markers[i].Sentinel.Length;
            var end = i + 1 < markers.Count ? markers[i + 1].Index : text.Length;
            if (start <= end)
            {
                sections[markers[i].Sentinel] = text[start..end];
            }
        }

        sections.TryGetValue(TitleSentinel, out var titleSection);
        sections.TryGetValue(SearchTermsSentinel, out var termsSection);
        sections.TryGetValue(ProductHighlightsSentinel, out var highlightsSection);

        return new RewriteCompletionBlocks(
            body,
            CleanTitleLine(FirstNonEmptyLine(titleSection)),
            SanitizeSearchTerms(ParseMetaLines(termsSection)),
            SanitizeProductHighlights(ParseMetaLines(highlightsSection)));
    }

    private static void AddSentinelMarker(
        List<(int Index, string Sentinel)> markers,
        string text,
        string sentinel)
    {
        var index = text.IndexOf(sentinel, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            markers.Add((index, sentinel));
        }
    }

    private static string FirstNonEmptyLine(string? section)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            return string.Empty;
        }

        return section
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(static line => line.Trim())
            .FirstOrDefault(static line => line.Length > 0)
            ?? string.Empty;
    }

    private static string CleanTitleLine(string title)
    {
        if (title.StartsWith("title:", StringComparison.OrdinalIgnoreCase))
        {
            title = title[6..].Trim();
        }

        return title.Trim('*', '#', '"', '\'', '.', ' ');
    }

    private static IEnumerable<string> ParseMetaLines(string? section)
    {
        if (string.IsNullOrWhiteSpace(section))
        {
            yield break;
        }

        foreach (var raw in section.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = raw.Trim().Trim('"', '\'', '*', '#');
            line = MetaBulletPrefixRegex.Replace(line, string.Empty).Trim();
            line = MetaLabelPrefixRegex.Replace(line, string.Empty).Trim().Trim('"', '\'');
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("===", StringComparison.Ordinal))
            {
                continue;
            }

            yield return line;
        }
    }

    internal static List<string> SanitizeSearchTerms(IEnumerable<string>? items)
        => SanitizeMetaLines(items, MaxSearchTerms, lowercase: true);

    internal static List<string> SanitizeProductHighlights(IEnumerable<string>? items)
        => SanitizeMetaLines(items, MaxProductHighlights, lowercase: false);

    private static List<string> SanitizeMetaLines(
        IEnumerable<string>? items,
        int maxCount,
        bool lowercase)
    {
        var result = new List<string>(maxCount);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (items is null)
        {
            return result;
        }

        foreach (var raw in items)
        {
            var line = (raw ?? string.Empty).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (TitleMarketplaceRegex.IsMatch(line) || TitlePromoRegex.IsMatch(line))
            {
                continue;
            }

            if (line.Length > MaxMetaLineChars)
            {
                line = TrimTitleToWordBoundary(line, MaxMetaLineChars);
            }

            if (line.Length == 0)
            {
                continue;
            }

            if (lowercase)
            {
                line = line.ToLowerInvariant();
            }

            if (!seen.Add(line))
            {
                continue;
            }

            result.Add(line);
            if (result.Count >= maxCount)
            {
                break;
            }
        }

        return result;
    }

    private static string Normalize(string text)
        => SanitizeHashtagBlock(
            CapitalizeLineStarts(
                NormalizeBulletLeadIns((text ?? string.Empty).Trim().Trim('"').Trim())));

    /// <summary>
    /// Capitalizes the first letter of every block, heading and bullet. The prompt tells the
    /// model to keep the word after a bullet's dash in lower case, and it over-applies that to
    /// sentence starts too — most visibly on the opening line, which begins with the primary
    /// keyword and so came out as <c>women's strapless ruffle pants set — …</c>.
    /// Hashtag lines are left alone: <c>#</c> is in <see cref="LineStartSkippable"/>, so
    /// without this skip <c>#womentanktop</c> becomes <c>#Womentanktop</c>.
    /// </summary>
    internal static string CapitalizeLineStarts(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var atBlockStart = true;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Trim().Length == 0)
            {
                atBlockStart = true;
                continue;
            }

            if (IsHashtagLine(line))
            {
                atBlockStart = true;
                continue;
            }

            var isHeading = TryGetHeading(line.Trim()) is not null;
            var isBullet = IsBulletLine(line);

            // Mid-paragraph lines are sentence continuations, so only the first line of a
            // block, a heading or a bullet may be capitalized.
            if (atBlockStart || isHeading || isBullet)
            {
                lines[i] = CapitalizeFirstLetter(line);
            }

            atBlockStart = isHeading || isBullet;
        }

        return string.Join("\n", lines);
    }

    private static string CapitalizeFirstLetter(string line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (char.IsLetter(c))
            {
                return char.IsLower(c)
                    ? string.Concat(line[..i], char.ToUpperInvariant(c).ToString(), line[(i + 1)..])
                    : line;
            }

            // Skip the markdown-lite decoration wrapping the first word; anything else
            // (a digit, a quote of an odd kind) means there is no letter to fix.
            if (!LineStartSkippable.Contains(c))
            {
                return line;
            }
        }

        return line;
    }

    /// <summary>
    /// Forces the <c>**Feature** — explanation</c> bullet shape. Models routinely drop the dash
    /// and start the explanation with a capital letter, which reads as two sentences jammed
    /// together once the bold becomes a <c>strong</c> element in the editor.
    /// </summary>
    internal static string NormalizeBulletLeadIns(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (IsBulletLine(lines[i]))
            {
                lines[i] = SeparateBulletLeadIn(lines[i]);
            }
        }

        return string.Join("\n", lines);
    }

    private static string SeparateBulletLeadIn(string line)
    {
        var match = BulletLeadInRegex.Match(line);
        if (!match.Success)
        {
            return line;
        }

        var label = match.Groups["label"].Value.Trim();
        var rest = match.Groups["rest"].Value;

        // A long or already punctuated lead-in is a sentence the writer bolded on purpose.
        if (label.Length == 0
            || label.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > MaxLeadInWords
            || LeadInSeparators.Contains(label[^1])
            || LeadInSeparators.Contains(rest[0]))
        {
            return line;
        }

        return $"{match.Groups["marker"].Value}**{label}** — {StartLowercase(rest)}";
    }

    /// <summary>
    /// The explanation continues the bullet after the dash, so it reads as one line rather
    /// than two. Acronyms and brand names such as UPF or TikTok keep their capitals.
    /// </summary>
    private static string StartLowercase(string text)
    {
        var firstWord = text.Split(' ', 2)[0];
        return firstWord.Length > 1
               && char.IsUpper(firstWord[0])
               && !firstWord.Skip(1).Any(char.IsUpper)
            ? char.ToLowerInvariant(text[0]) + text[1..]
            : text;
    }

    /// <summary>
    /// Renders the structured listing data the model may cite as verified facts.
    /// Everything here is data the operator already curated, so the copy can name
    /// material, fit or season without inventing specifications.
    /// </summary>
    internal static string BuildProductFacts(RewriteListingDescriptionRequest request)
    {
        var lines = new List<string>();

        var category = (request.CategoryName ?? string.Empty).Trim();
        if (category.Length > 0)
        {
            lines.Add($"Category: {category}");
        }

        var brand = (request.BrandName ?? string.Empty).Trim();
        if (brand.Length > 0 && !NoBrandNames.Contains(brand.ToLowerInvariant()))
        {
            lines.Add($"Brand: {brand}");
        }

        var colors = CleanList(request.Colors);
        if (colors.Count > 0)
        {
            lines.Add($"Available colors: {string.Join(", ", colors)}");
        }

        var sizes = CleanList(request.Sizes);
        if (sizes.Count > 0)
        {
            lines.Add($"Available sizes: {string.Join(", ", sizes)}");
        }

        var title = (request.Title ?? string.Empty).Trim();
        var materialConflict = HasMaterialConflict(request);
        var attributes = (request.Attributes ?? [])
            .Select(a => (Name: (a.Name ?? string.Empty).Trim(), Value: (a.Value ?? string.Empty).Trim()))
            .Where(a => a.Name.Length > 0 && a.Value.Length > 0 && !IsVagueValue(a.Value))
            .Where(a => !ContradictsTitleSilhouette(title, a.Name, a.Value))
            // Conflicting Material must leave the facts block: otherwise the model treats it as
            // true and writes "knit polyester" under a Linen title — TikTok's reject reason.
            .Where(a => !(materialConflict && ListingSpecificsMappingEngine.IsMaterialAttribute(a.Name)))
            .Take(MaxAttributeFacts)
            .ToList();

        foreach (var (name, value) in attributes)
        {
            lines.Add($"{name}: {value}");
        }

        return string.Join("\n", lines);
    }

    /// <summary>
    /// True when the title and a Material/Fabric attribute both name fibres and those sets
    /// do not overlap (e.g. title says Linen, attribute says Polyester). Title with no fibre
    /// claim is never a conflict — the attribute may still be cited.
    /// </summary>
    internal static bool HasMaterialConflict(RewriteListingDescriptionRequest request)
    {
        var titleFibres = ListingSpecificsMappingEngine.FabricKeysFromText(request.Title);
        if (titleFibres.Count == 0)
        {
            return false;
        }

        var titleSet = titleFibres.ToHashSet(StringComparer.Ordinal);
        var attrSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var attr in request.Attributes ?? [])
        {
            if (!ListingSpecificsMappingEngine.IsMaterialAttribute(attr.Name))
            {
                continue;
            }

            foreach (var fibre in ListingSpecificsMappingEngine.FabricKeysFromText(attr.Value))
            {
                attrSet.Add(fibre);
            }
        }

        return attrSet.Count > 0 && !titleSet.Overlaps(attrSet);
    }

    /// <summary>
    /// Drops whole bullet lines that still name a fabric after a material conflict, so a
    /// model that ignored <see cref="MaterialConflictInstruction"/> cannot leave
    /// "Knit polyester — …" on the PDP. Non-bullet prose is left alone.
    /// </summary>
    internal static string StripFabricMentionLines(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var kept = new List<string>();
        foreach (var rawLine in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            var line = rawLine;
            if (IsBulletLine(line) && FabricRegex.IsMatch(line))
            {
                continue;
            }

            kept.Add(line);
        }

        return string.Join("\n", kept).Trim();
    }

    /// <summary>
    /// Search terms / highlights that still name a fibre after a material conflict.
    /// </summary>
    internal static List<string> DropFabricMetaLines(IEnumerable<string>? items)
        => (items ?? [])
            .Select(t => (t ?? string.Empty).Trim())
            .Where(t => t.Length > 0 && !FabricRegex.IsMatch(t))
            .ToList();

    /// <summary>
    /// Drops <c>#linendress</c>-style tags from the trailing hashtag line when title and
    /// Material disagree — <see cref="FabricRegex"/> word boundaries miss concatenated tags.
    /// </summary>
    internal static string StripFabricFromHashtagLine(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var blocks = SplitBlocks(text);
        var changed = false;
        for (var i = 0; i < blocks.Count; i++)
        {
            if (!IsHashtagBlock(blocks[i]))
            {
                continue;
            }

            var kept = new List<string>();
            foreach (Match m in HashtagTokenRegex.Matches(blocks[i]))
            {
                var tag = "#" + m.Groups[1].Value.ToLowerInvariant();
                if (FabricInHashtagRegex.IsMatch(tag))
                {
                    changed = true;
                    continue;
                }

                kept.Add(tag);
            }

            if (kept.Count >= 2)
            {
                blocks[i] = string.Join(' ', kept);
            }
            else
            {
                blocks.RemoveAt(i);
                changed = true;
                break;
            }
        }

        return changed ? string.Join("\n\n", blocks) : text.Trim();
    }

    /// <summary>
    /// Finds the last hashtag-looking block, cleans it (lowercase, alnum-only tokens, dedupe,
    /// drop marketplace/promo/catalog-year tags, cap <see cref="MaxHashtags"/>) and pins it
    /// as a single trailing line so length checks measure the final copy.
    /// </summary>
    internal static string SanitizeHashtagBlock(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var blocks = SplitBlocks(text);
        var hashIndex = -1;
        for (var i = blocks.Count - 1; i >= 0; i--)
        {
            if (IsHashtagBlock(blocks[i]))
            {
                hashIndex = i;
                break;
            }
        }

        if (hashIndex < 0)
        {
            return text.Trim();
        }

        var cleaned = CleanHashtagTokens(blocks[hashIndex]);
        blocks.RemoveAt(hashIndex);
        // Drop any earlier hashtag-looking blocks so only one trailing line survives.
        for (var i = blocks.Count - 1; i >= 0; i--)
        {
            if (IsHashtagBlock(blocks[i]))
            {
                blocks.RemoveAt(i);
            }
        }

        if (cleaned.Length > 0)
        {
            blocks.Add(cleaned);
        }

        return string.Join("\n\n", blocks);
    }

    private static string CleanHashtagTokens(string block)
    {
        var result = new List<string>(MaxHashtags);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match m in HashtagTokenRegex.Matches(block))
        {
            var body = m.Groups[1].Value.ToLowerInvariant();
            if (body.Length == 0)
            {
                continue;
            }

            var tag = "#" + body;
            var bare = body;
            if (TitleMarketplaceRegex.IsMatch(bare)
                || TitlePromoRegex.IsMatch(bare)
                || TitleCatalogYearRegex.IsMatch(bare)
                || TitleMarketplaceRegex.IsMatch(tag)
                || TitlePromoRegex.IsMatch(tag))
            {
                continue;
            }

            // Promo / marketplace regexes expect word boundaries around phrases; also reject
            // concatenated forms that still start with a banned stem (sheinbae, freeshipping).
            if (IsBannedHashtagBody(bare))
            {
                continue;
            }

            if (!seen.Add(tag))
            {
                continue;
            }

            result.Add(tag);
            if (result.Count >= MaxHashtags)
            {
                break;
            }
        }

        return result.Count >= 2 ? string.Join(' ', result) : string.Empty;
    }

    /// <summary>
    /// Hashtag bodies are concatenated, so marketplace/promo word-boundary regexes miss
    /// <c>#sheinbae</c> / <c>#freeshipping</c>. Reject those stems as whole body or prefix
    /// (never <c>Contains</c> — <c>temu</c> would false-positive <c>#costume</c>).
    /// </summary>
    private static bool IsBannedHashtagBody(string body)
    {
        if (TitleCatalogYearRegex.IsMatch(body))
        {
            return true;
        }

        foreach (var stem in BannedHashtagStems)
        {
            if (body.Equals(stem, StringComparison.OrdinalIgnoreCase)
                || body.StartsWith(stem, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly string[] BannedHashtagStems =
    [
        "shein", "romwe", "temu", "lazada", "aliexpress", "amazon",
        "tiktok", "wish", "zaful", "cider", "shopee", "alibaba", "etsy", "ebay",
        "walmart", "freeshipping", "bestseller", "bestsellers", "musthave", "newin",
        "hotsale", "flashsale", "bigsale", "clearance", "dropshipping", "wholesale",
        "perfectsummer"
    ];

    /// <summary>True when a block is (or mostly is) a hashtag line.</summary>
    internal static bool IsHashtagBlock(string block)
    {
        if (string.IsNullOrWhiteSpace(block))
        {
            return false;
        }

        // Hashtag line is a single physical line; multi-line blocks are prose/sections.
        var firstLine = block.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n')[0];
        return IsHashtagLine(firstLine);
    }

    /// <summary>
    /// True for a clean <c>#a #b …</c> line, or a messy model line that is still mostly hashtags.
    /// </summary>
    internal static bool IsHashtagLine(string? line)
    {
        var t = (line ?? string.Empty).Trim();
        if (t.Length == 0 || !t.Contains('#', StringComparison.Ordinal))
        {
            return false;
        }

        if (HashtagLineRegex.IsMatch(t))
        {
            return true;
        }

        // Loose match: at least two #tokens and they dominate the line (avoids turning
        // "#1 pick for summer" prose into a hashtag block).
        var tokens = HashtagTokenRegex.Matches(t);
        if (tokens.Count < 2)
        {
            return false;
        }

        var tagChars = tokens.Sum(m => m.Length);
        return tagChars * 2 >= t.Replace(" ", "", StringComparison.Ordinal).Length;
    }

    private static List<string> CleanList(List<string>? values)
        => (values ?? [])
            .Select(v => (v ?? string.Empty).Trim())
            .Where(v => v.Length > 0 && !IsVagueValue(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static bool IsVagueValue(string value)
        => VagueFactValues.Contains(value.ToLowerInvariant());

    /// <summary>
    /// Drops a fit spec that disagrees with the silhouette already named in the title, so a
    /// bodycon dress filed by the supplier as <c>Fit: Regular</c> cannot end up described as
    /// both. The title wins: it is what the shopper clicked and what the listing ranks for.
    /// </summary>
    private static bool ContradictsTitleSilhouette(string title, string name, string value)
    {
        if (!SilhouetteAttributeNames.Contains(name.ToLowerInvariant()))
        {
            return false;
        }

        var titleWords = SilhouetteWordsIn(title);
        return titleWords.Count > 0 && !titleWords.Overlaps(SilhouetteWordsIn(value));
    }

    private static HashSet<string> SilhouetteWordsIn(string text)
        => SilhouetteRegex.Matches(text)
            .Select(m => CanonicalSilhouetteWord(m.Value))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Folds the spellings that mean the same shape — <c>wide-leg</c> with <c>wide leg</c>,
    /// <c>oversize</c> with <c>oversized</c> — into one comparison key.
    /// </summary>
    private static string CanonicalSilhouetteWord(string word)
    {
        var letters = NonLetterRegex.Replace(word.ToLowerInvariant(), string.Empty);
        return letters.EndsWith('d') ? letters[..^1] : letters;
    }

    internal static string StripHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = html
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", "\n\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</li>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase)
            .Replace("&amp;", "&", StringComparison.OrdinalIgnoreCase)
            .Replace("&lt;", "<", StringComparison.OrdinalIgnoreCase)
            .Replace("&gt;", ">", StringComparison.OrdinalIgnoreCase)
            .Replace("&quot;", "\"", StringComparison.OrdinalIgnoreCase);

        text = TagRegex.Replace(text, " ");
        text = WebUtility.HtmlDecode(text);
        return MultiSpaceRegex.Replace(text, " ").Trim();
    }

    /// <summary>
    /// Converts the model's markdown-lite output (## heading, - bullet) into plain-text-friendly
    /// HTML for the editor and TikTok Shop. TTS strips rich formatting, so bullets become
    /// left-aligned <c>&lt;p&gt;- …&lt;/p&gt;</c> lines (not nested lists) and emphasis is flattened.
    /// Headings stay semantic <c>&lt;h3&gt;</c> so Insert Images can still skip section titles.
    /// </summary>
    internal static string MarkdownLiteToHtml(string plain)
    {
        if (string.IsNullOrWhiteSpace(plain))
        {
            return string.Empty;
        }

        var lines = plain
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Trim()
            .Split('\n');

        var sb = new StringBuilder();
        var paragraph = new List<string>();
        var bullets = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0)
            {
                return;
            }

            sb.Append("<p>");
            sb.Append(string.Join("<br>", paragraph.Select(InlineToHtml)));
            sb.Append("</p>");
            paragraph.Clear();
        }

        void FlushBullets()
        {
            if (bullets.Count == 0)
            {
                return;
            }

            // One left-aligned "- …" paragraph per bullet so TTS strip keeps the dash + line break.
            foreach (var item in bullets)
            {
                sb.Append("<p>- ").Append(InlineToHtml(item)).Append("</p>");
            }

            bullets.Clear();
        }

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                FlushBullets();
                FlushParagraph();
                continue;
            }

            var heading = TryGetHeading(line);
            if (heading is not null)
            {
                FlushBullets();
                FlushParagraph();
                sb.Append("<h3>").Append(InlineToHtml(heading)).Append("</h3>");
                continue;
            }

            if (IsBulletLine(line))
            {
                FlushParagraph();
                bullets.Add(StripBulletPrefix(line));
                continue;
            }

            FlushBullets();
            paragraph.Add(line);
        }

        FlushBullets();
        FlushParagraph();
        return sb.ToString();
    }

    /// <summary>
    /// Recognises both `## Section` and a standalone `**Section**` line as a heading —
    /// models drift between the two no matter how the prompt is worded.
    /// Hashtag lines must never become headings: <c># tanktop #summer</c> matches
    /// <see cref="HeadingRegex"/> (<c>#</c> + space + text) and would otherwise turn into
    /// <c>&lt;h3&gt;</c>.
    /// </summary>
    private static string? TryGetHeading(string line)
    {
        if (IsHashtagLine(line))
        {
            return null;
        }

        var hash = HeadingRegex.Match(line);
        if (hash.Success)
        {
            return hash.Groups[1].Value.Trim();
        }

        var bold = BoldOnlyLineRegex.Match(line);
        if (!bold.Success)
        {
            return null;
        }

        // A bolded full sentence is emphasis (the thank-you line), not a section title.
        var text = bold.Groups[1].Value.Trim();
        return text.EndsWith('.') || text.EndsWith('!') || text.EndsWith('?')
            ? null
            : text;
    }

    private static string InlineToHtml(string text)
    {
        // TikTok Shop strips <strong>; flatten **markers** so live copy stays even plain text.
        var plain = BoldRegex.Replace(text, m => m.Groups[1].Value);
        return WebUtility.HtmlEncode(plain);
    }

    private static bool IsBulletLine(string line)
    {
        var t = line.TrimStart();
        return t.StartsWith("•", StringComparison.Ordinal)
               || t.StartsWith("·", StringComparison.Ordinal)
               || t.StartsWith("- ", StringComparison.Ordinal)
               || t.StartsWith("* ", StringComparison.Ordinal)
               || t.StartsWith("– ", StringComparison.Ordinal)
               || t.StartsWith("— ", StringComparison.Ordinal);
    }

    private static string StripBulletPrefix(string line)
    {
        var t = line.TrimStart();
        if (t.StartsWith("•", StringComparison.Ordinal)
            || t.StartsWith("·", StringComparison.Ordinal))
        {
            return t[1..].TrimStart();
        }

        if (t.StartsWith("- ", StringComparison.Ordinal)
            || t.StartsWith("* ", StringComparison.Ordinal)
            || t.StartsWith("– ", StringComparison.Ordinal)
            || t.StartsWith("— ", StringComparison.Ordinal))
        {
            return t[2..].TrimStart();
        }

        return t;
    }

    /// <summary>
    /// Removes leftover Shipping / Care Tips sections and Colors / Sizes spec lines the model
    /// still emits from older prompt habits.
    /// </summary>
    internal static string StripForbiddenDescriptionSections(string text)
        => StripSizesLines(StripColorsLines(StripCareTipsSection(StripShippingSection(text))));

    /// <summary>
    /// Removes a ## Shipping section when the model still emits one from an older prompt.
    /// </summary>
    internal static string StripShippingSection(string text)
    {
        var markdown = (text ?? string.Empty).Trim();
        if (markdown.Length == 0)
        {
            return string.Empty;
        }

        var blocks = SplitBlocks(markdown);
        var start = blocks.FindIndex(IsShippingHeading);
        if (start < 0)
        {
            return markdown;
        }

        var end = TakeNamedSectionEnd(blocks, start, MentionsDelivery);
        blocks.RemoveRange(start, end - start);
        return string.Join("\n\n", blocks);
    }

    /// <summary>
    /// Removes a ## Care Tips section — care copy is no longer part of the rewrite contract.
    /// </summary>
    internal static string StripCareTipsSection(string text)
    {
        var markdown = (text ?? string.Empty).Trim();
        if (markdown.Length == 0)
        {
            return string.Empty;
        }

        var blocks = SplitBlocks(markdown);
        var start = blocks.FindIndex(IsCareTipsHeading);
        if (start < 0)
        {
            return markdown;
        }

        var end = TakeNamedSectionEnd(blocks, start, MentionsCare);
        blocks.RemoveRange(start, end - start);
        return string.Join("\n\n", blocks);
    }

    /// <summary>
    /// Drops Colors / Available colors spec lines. Leaves ordinary prose that happens to say
    /// "color" alone — only labeled color-variant list rows are removed.
    /// </summary>
    internal static string StripColorsLines(string text)
        => StripLabeledSpecLines(text, IsColorsSpecLine);

    /// <summary>
    /// Drops Sizes / Available sizes list lines. Leaves fit advice prose ("true to size",
    /// "size up if between sizes") — only labeled size-option rows are removed.
    /// </summary>
    internal static string StripSizesLines(string text)
        => StripLabeledSpecLines(text, IsSizesSpecLine);

    private static string StripLabeledSpecLines(string text, Func<string?, bool> isForbidden)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var kept = new List<string>();
        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (isForbidden(raw))
            {
                continue;
            }

            kept.Add(raw);
        }

        return string.Join("\n", kept).Trim();
    }

    private static readonly Regex ColorsSpecLineRegex = new(
        @"^\s*(?:[-*•·]|–|—)?\s*(?:available\s+)?colou?rs?\s*[:\-–—]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex SizesSpecLineRegex = new(
        @"^\s*(?:[-*•·]|–|—)?\s*(?:available\s+)?sizes?\s*[:\-–—]",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    internal static bool IsColorsSpecLine(string? line)
        => !string.IsNullOrWhiteSpace(line) && ColorsSpecLineRegex.IsMatch(line.Trim());

    internal static bool IsSizesSpecLine(string? line)
        => !string.IsNullOrWhiteSpace(line) && SizesSpecLineRegex.IsMatch(line.Trim());

    private static List<string> SplitBlocks(string text)
        => BlockSplitRegex.Split(text)
            .Select(b => b.Trim())
            .Where(b => b.Length > 0)
            .ToList();

    /// <summary>
    /// Heading plus following content until the next heading / thank-you / hashtag, extended
    /// while <paramref name="belongs"/> still matches so a closing CTA is not dragged along.
    /// </summary>
    private static int TakeNamedSectionEnd(
        List<string> blocks,
        int start,
        Func<string, bool> belongs)
    {
        var end = start + 1;
        if (end >= blocks.Count
            || IsHeadingBlock(blocks[end])
            || IsThankYouBlock(blocks[end])
            || IsHashtagBlock(blocks[end]))
        {
            return end;
        }

        end++;
        while (end < blocks.Count
               && !IsHeadingBlock(blocks[end])
               && !IsThankYouBlock(blocks[end])
               && !IsHashtagBlock(blocks[end])
               && belongs(blocks[end]))
        {
            end++;
        }

        return end;
    }

    private static bool IsHeadingBlock(string block)
        => TryGetHeading(block.Split('\n')[0].Trim()) is not null;

    private static bool IsShippingHeading(string block)
        => string.Equals(
            TryGetHeading(block.Split('\n')[0].Trim()),
            "Shipping",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsCareTipsHeading(string block)
        => string.Equals(
            TryGetHeading(block.Split('\n')[0].Trim()),
            "Care Tips",
            StringComparison.OrdinalIgnoreCase);

    private static bool IsThankYouBlock(string block)
        => block.Contains("Thank you for shopping", StringComparison.OrdinalIgnoreCase);

    private static bool MentionsDelivery(string block)
        => block.Contains("deliver", StringComparison.OrdinalIgnoreCase)
           || block.Contains("shipping", StringComparison.OrdinalIgnoreCase)
           || block.Contains("arrive", StringComparison.OrdinalIgnoreCase);

    private static bool MentionsCare(string block)
        => block.Contains("wash", StringComparison.OrdinalIgnoreCase)
           || block.Contains("dry", StringComparison.OrdinalIgnoreCase)
           || block.Contains("iron", StringComparison.OrdinalIgnoreCase)
           || block.Contains("bleach", StringComparison.OrdinalIgnoreCase)
           || block.Contains("laundry", StringComparison.OrdinalIgnoreCase)
           || (block.Contains("care", StringComparison.OrdinalIgnoreCase)
               && !block.Contains("Thank you", StringComparison.OrdinalIgnoreCase));

    private static bool IsProtectedTrimBlock(string block)
        => IsThankYouBlock(block) || IsHashtagBlock(block);

    /// <summary>
    /// Last-resort trim. Drops whole blocks from the middle so the opening hook and the
    /// closing CTA / hashtag line always survive. When a hard cut is still needed, trailing
    /// protected blocks (thank-you + hashtags) are peeled off, the body is trimmed, then
    /// they are reattached — otherwise <see cref="TrimToSentence"/> would slice mid-hashtag.
    /// </summary>
    internal static string TrimPreservingClosing(string text, int max)
    {
        if (text.Length <= max)
        {
            return text;
        }

        var blocks = BlockSplitRegex.Split(text)
            .Select(b => b.Trim())
            .Where(b => b.Length > 0)
            .ToList();

        while (blocks.Count > 2 && string.Join("\n\n", blocks).Length > max)
        {
            var removeAt = -1;
            for (var i = blocks.Count - 2; i >= 1; i--)
            {
                if (!IsProtectedTrimBlock(blocks[i]))
                {
                    removeAt = i;
                    break;
                }
            }

            if (removeAt < 0)
            {
                break;
            }

            blocks.RemoveAt(removeAt);
        }

        var result = string.Join("\n\n", blocks);
        if (result.Length <= max)
        {
            return result;
        }

        var trailing = new List<string>();
        while (blocks.Count > 0 && IsProtectedTrimBlock(blocks[^1]))
        {
            trailing.Insert(0, blocks[^1]);
            blocks.RemoveAt(blocks.Count - 1);
        }

        if (trailing.Count == 0)
        {
            return TrimToSentence(result, max);
        }

        var trailer = string.Join("\n\n", trailing);
        var bodyBudget = max - trailer.Length - 2; // room for the blank line before trailer
        if (bodyBudget < 40 || blocks.Count == 0)
        {
            // Not enough room for a meaningful body — keep opening + trailer only.
            var opening = blocks.Count > 0 ? blocks[0] : string.Empty;
            if (opening.Length + 2 + trailer.Length <= max)
            {
                return string.IsNullOrEmpty(opening)
                    ? trailer
                    : opening + "\n\n" + trailer;
            }

            return trailer.Length <= max ? trailer : trailer[..max].TrimEnd();
        }

        var body = TrimToSentence(string.Join("\n\n", blocks), bodyBudget);
        return body + "\n\n" + trailer;
    }

    private static string TrimToSentence(string text, int max)
    {
        if (max <= 0)
        {
            return string.Empty;
        }

        if (text.Length <= max)
        {
            return text;
        }

        var cut = text[..max];
        var lastStop = cut.LastIndexOfAny(['.', '!', '?']);
        if (lastStop > max / 2)
        {
            return cut[..(lastStop + 1)].TrimEnd();
        }

        var lastSpace = cut.LastIndexOf(' ');
        return (lastSpace > max / 2 ? cut[..lastSpace] : cut).TrimEnd();
    }
}
