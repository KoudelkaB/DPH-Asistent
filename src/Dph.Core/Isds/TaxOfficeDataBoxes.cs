// ID datových schránek finančních úřadů a jejich územních pracovišť dle oficiálního seznamu
// Finanční správy „Identifikační kódy orgánů finanční správy ČR“
// (https://financnisprava.gov.cz/assets/cs/prilohy/de-datove-schranky/Identifikacni_kody_FR_a_FU.pdf).
// Adresátem se volí schránka územního pracoviště, které podání skutečně spravuje (c_pracufo);
// schránka finančního úřadu (kraje) je záloha pro pracoviště, která vlastní schránku nemají.
using Dph.Core.Epo;

namespace Dph.Core.Isds;

public static class TaxOfficeDataBoxes
{
    private static readonly Dictionary<string, string> ByOfficeCode = new()
    {
        ["451"] = "7nyn2d9", // Finanční úřad pro hlavní město Prahu
        ["452"] = "6sxny3p", // Finanční úřad pro Středočeský kraj
        ["453"] = "scdnz6b", // Finanční úřad pro Jihočeský kraj
        ["454"] = "gf9n2e3", // Finanční úřad pro Plzeňský kraj
        ["455"] = "q9in2ev", // Finanční úřad pro Karlovarský kraj
        ["456"] = "qjfn2bj", // Finanční úřad pro Ústecký kraj
        ["457"] = "zcqn2bf", // Finanční úřad pro Liberecký kraj
        ["458"] = "fj8ny4i", // Finanční úřad pro Královéhradecký kraj
        ["459"] = "95zn2bb", // Finanční úřad pro Pardubický kraj
        ["460"] = "tqjn2cy", // Finanční úřad pro Kraj Vysočina
        ["461"] = "qdhny4c", // Finanční úřad pro Jihomoravský kraj
        ["462"] = "25nnz67", // Finanční úřad pro Olomoucký kraj
        ["463"] = "4hun2cu", // Finanční úřad pro Moravskoslezský kraj
        ["464"] = "n69nz5y", // Finanční úřad pro Zlínský kraj
        ["13"] = "7cs8cge",  // Specializovaný finanční úřad
    };

    // Územní pracoviště podle kódu c_pracufo. Seznam obsahuje jen pracoviště s vlastní datovou
    // schránkou – „optimalizovaná“ pracoviště (úřední dny jen několikrát v týdnu) tu chybí
    // záměrně a podání za ně jde do schránky jejich finančního úřadu.
    private static readonly Dictionary<string, string> ByWorkplaceCode = new()
    {
        // Finanční úřad pro hlavní město Prahu
        ["2001"] = "2p2n5ad", // Územní pracoviště pro Prahu 1
        ["2002"] = "qijn44u", // Územní pracoviště pro Prahu 2
        ["2003"] = "945n44k", // Územní pracoviště pro Prahu 3
        ["2004"] = "ty2n5jf", // Územní pracoviště pro Prahu 4
        ["2005"] = "jkhn48p", // Územní pracoviště pro Prahu 5
        ["2006"] = "qk4n645", // Územní pracoviště pro Prahu 6
        ["2007"] = "hh8n4xs", // Územní pracoviště pro Prahu 7
        ["2008"] = "bhbn5b5", // Územní pracoviště pro Prahu 8
        ["2009"] = "z4gn679", // Územní pracoviště pro Prahu 9
        ["2010"] = "jvun5nv", // Územní pracoviště pro Prahu 10
        ["2011"] = "zbun44q", // Územní pracoviště pro Prahu - Jižní Město
        ["2012"] = "sbhn4xk", // Územní pracoviště v Praze - Modřanech
        // Finanční úřad pro Středočeský kraj
        ["2101"] = "4sbn5j9", // Územní pracoviště Praha – východ
        ["2102"] = "24sn4xg", // Územní pracoviště Praha – západ
        ["2103"] = "djkn5k4", // Územní pracoviště v Benešově
        ["2104"] = "bv3n4ya", // Územní pracoviště v Berouně
        ["2105"] = "4sbn5j9", // Územní pracoviště v Brandýse nad Labem - Staré Boleslavi
        ["2106"] = "3pgn5tt", // Územní pracoviště v Čáslavi
        ["2109"] = "tkan5w9", // Územní pracoviště v Hořovicích
        ["2110"] = "w56n5ku", // Územní pracoviště v Kladně
        ["2111"] = "un5n5ns", // Územní pracoviště v Kolíně
        ["2112"] = "5gen5nk", // Územní pracoviště v Kralupech nad Vltavou
        ["2113"] = "iwen45c", // Územní pracoviště v Kutné Hoře
        ["2114"] = "k92n5uh", // Územní pracoviště v Mělníce
        ["2115"] = "tppn459", // Územní pracoviště v Mladé Boleslavi
        ["2118"] = "c6un5xx", // Územní pracoviště v Nymburku
        ["2119"] = "kjxn5r8", // Územní pracoviště v Poděbradech
        ["2120"] = "v3bn5ub", // Územní pracoviště v Příbrami
        ["2121"] = "6ukn5u7", // Územní pracoviště v Rakovníku
        ["2122"] = "fmvn5v2", // Územní pracoviště v Říčanech
        ["2124"] = "gqqn5mh", // Územní pracoviště ve Slaném
        ["2125"] = "qe6n5vw", // Územní pracoviště ve Vlašimi
        // Finanční úřad pro Jihočeský kraj
        ["2201"] = "mx5n5xt", // Územní pracoviště v Českých Budějovicích
        ["2203"] = "n2yn5pa", // Územní pracoviště v Českém Krumlově
        ["2205"] = "vc8n5r4", // Územní pracoviště v Jindřichově Hradci
        ["2208"] = "wren5xn", // Územní pracoviště v Písku
        ["2209"] = "56hn5rw", // Územní pracoviště v Prachaticích
        ["2211"] = "8min5pz", // Územní pracoviště ve Strakonicích
        ["2212"] = "j67n543", // Územní pracoviště v Táboře
        // Finanční úřad pro Plzeňský kraj
        ["2301"] = "hetn5qt", // Územní pracoviště v Plzni
        ["2302"] = "r74n5qp", // Územní pracoviště Plzeň - sever
        ["2303"] = "yicn5se", // Územní pracoviště Plzeň - jih
        ["2305"] = "2zdn5qh", // Územní pracoviště v Domažlicích
        ["2308"] = "43nn5zf", // Územní pracoviště v Klatovech
        ["2312"] = "ei2n55k", // Územní pracoviště v Rokycanech
        ["2313"] = "3gnn7dw", // Územní pracoviště v Tachově
        // Finanční úřad pro Karlovarský kraj
        ["2401"] = "sv7n5ty", // Územní pracoviště v Karlových Varech
        ["2403"] = "b3zn57h", // Územní pracoviště v Chebu
        ["2407"] = "i4cn6cj", // Územní pracoviště v Sokolově
        // Finanční úřad pro Ústecký kraj
        ["2501"] = "gbyn5yc", // Územní pracoviště v Ústí nad Labem
        ["2503"] = "q49n5y8", // Územní pracoviště v Děčíně
        ["2504"] = "tvmn6cf", // Územní pracoviště v Chomutově
        ["2505"] = "duxn529", // Územní pracoviště v Kadani
        ["2507"] = "vnjn578", // Územní pracoviště v Litoměřicích
        ["2509"] = "6fun574", // Územní pracoviště v Lounech
        ["2510"] = "8wvn556", // Územní pracoviště v Mostě
        ["2512"] = "e85n58w", // Územní pracoviště v Roudnici nad Labem
        ["2513"] = "4nwn6cc", // Územní pracoviště v Rumburku
        ["2514"] = "df7n6d6", // Územní pracoviště v Teplicích
        ["2515"] = "hp6n56z", // Územní pracoviště v Žatci
        // Finanční úřad pro Liberecký kraj
        ["2601"] = "shfn56t", // Územní pracoviště v Liberci
        ["2602"] = "ay6n6f3", // Územní pracoviště v České Lípě
        ["2604"] = "mjdn6an", // Územní pracoviště v Jablonci nad Nisou
        ["2607"] = "nm8n526", // Územní pracoviště v Semilech
        ["2609"] = "jsfn6fv", // Územní pracoviště v Turnově
        // Finanční úřad pro Královéhradecký kraj
        ["2701"] = "wxcn66u", // Územní pracoviště v Hradci Králové
        ["2707"] = "fw8n6b8", // Územní pracoviště v Jičíně
        ["2709"] = "78sn52u", // Územní pracoviště v Náchodě
        ["2712"] = "5czn6fn", // Územní pracoviště v Rychnově nad Kněžnou
        ["2713"] = "d6an6ge", // Územní pracoviště v Trutnově
        // Finanční úřad pro Pardubický kraj
        ["2801"] = "gmbn6eh", // Územní pracoviště v Pardubicích
        ["2804"] = "9kyn58h", // Územní pracoviště v Chrudimi
        ["2808"] = "aptn5zv", // Územní pracoviště ve Svitavách
        ["2809"] = "2vyn6hj", // Územní pracoviště v Ústí nad Orlicí
        ["2810"] = "sssn6kz", // Územní pracoviště ve Vysokém Mýtě
        ["2811"] = "bn9n6ie", // Územní pracoviště v Žamberku
        // Finanční úřad pro Kraj Vysočina
        ["2901"] = "nxjn6ga", // Územní pracoviště v Jihlavě
        ["2903"] = "u9tn6i4", // Územní pracoviště v Havlíčkově Brodě
        ["2910"] = "396n6p8", // Územní pracoviště v Pelhřimově
        ["2912"] = "3j3n6kw", // Územní pracoviště v Třebíči
        ["2913"] = "yqan6zm", // Územní pracoviště ve Velkém Meziříčí
        ["2914"] = "zhsn6bx", // Územní pracoviště ve Žďáru nad Sázavou
        // Finanční úřad pro Jihomoravský kraj
        ["3001"] = "r4pn6hp", // Územní pracoviště Brno I.
        ["3002"] = "cdcn6mn", // Územní pracoviště Brno II.
        ["3003"] = "pmnn6jm", // Územní pracoviště Brno III.
        ["3004"] = "yexn6jh", // Územní pracoviště Brno IV.
        ["3005"] = "k6mn6mi", // Územní pracoviště Brno - venkov
        ["3006"] = "zedn64x", // Územní pracoviště v Blansku
        ["3007"] = "vxwn6me", // Územní pracoviště v Boskovicích
        ["3008"] = "7qmn66r", // Územní pracoviště v Břeclavi
        ["3010"] = "aa3n6cr", // Územní pracoviště v Hodoníně
        ["3011"] = "ra7n67f", // Územní pracoviště v Hustopečích
        ["3013"] = "jdpn6sr", // Územní pracoviště v Kyjově
        ["3018"] = "t6yn6sm", // Územní pracoviště ve Veselí nad Moravou
        ["3019"] = "ybin7ch", // Územní pracoviště ve Vyškově
        ["3020"] = "ij7n7gi", // Územní pracoviště ve Znojmě
        // Finanční úřad pro Olomoucký kraj
        ["3101"] = "2g8n6uf", // Územní pracoviště v Olomouci
        ["3102"] = "c3fn6qy", // Územní pracoviště v Hranicích
        ["3103"] = "hzhn6k5", // Územní pracoviště v Jeseníku
        ["3106"] = "wmzn6qr", // Územní pracoviště v Prostějově
        ["3107"] = "bahn6v7", // Územní pracoviště v Přerově
        ["3108"] = "7fan6qj", // Územní pracoviště ve Šternberku
        ["3109"] = "6q7n6mb", // Územní pracoviště v Šumperku
        ["3110"] = "fign6n3", // Územní pracoviště v Zábřehu
        // Finanční úřad pro Moravskoslezský kraj
        ["3201"] = "qbrn6nx", // Územní pracoviště Ostrava I.
        ["3202"] = "sd2n6xv", // Územní pracoviště Ostrava II.
        ["3203"] = "f8jn6rd", // Územní pracoviště Ostrava III.
        ["3205"] = "drin6ta", // Územní pracoviště v Bruntále
        ["3207"] = "qzun6r9", // Územní pracoviště ve Frýdku-Místku
        ["3210"] = "3ven62z", // Územní pracoviště v Havířově
        ["3212"] = "mfyn63q", // Územní pracoviště v Karviné
        ["3213"] = "nitn6t6", // Územní pracoviště v Kopřivnici
        ["3214"] = "v89n63k", // Územní pracoviště v Krnově
        ["3215"] = "zs5n6r5", // Územní pracoviště v Novém Jičíně
        ["3216"] = "uu3n6vx", // Územní pracoviště v Opavě
        ["3218"] = "bykn6yh", // Územní pracoviště v Třinci
        // Finanční úřad pro Zlínský kraj
        ["3301"] = "97nn64t", // Územní pracoviště ve Zlíně
        ["3304"] = "62in63e", // Územní pracoviště v Kroměříži
        ["3306"] = "xb4n6t2", // Územní pracoviště v Otrokovicích
        ["3307"] = "tr8n65i", // Územní pracoviště v Rožnově pod Radhoštěm
        ["3308"] = "efmn6wk", // Územní pracoviště v Uherském Brodě
        ["3309"] = "75dn6tu", // Územní pracoviště v Uherském Hradišti
        ["3310"] = "n8wn6wg", // Územní pracoviště ve Valašském Meziříčí
        ["3312"] = "4jhn65c", // Územní pracoviště ve Vsetíně
    };

    public static IReadOnlyDictionary<string, string> All => ByOfficeCode;

    public static IReadOnlyDictionary<string, string> AllWorkplaces => ByWorkplaceCode;

    // null = číselník kód nezná (např. živý číselník ADIS přidal nový úřad); volající pak požádá
    // o ruční zadání ID schránky místo toho, aby poslal podání někam jinam.
    public static string? For(string? taxOfficeCode)
        => string.IsNullOrWhiteSpace(taxOfficeCode)
            ? null
            : ByOfficeCode.GetValueOrDefault(taxOfficeCode.Trim());

    // Ke kterému finančnímu úřadu pracoviště patří. Slouží k odhalení kombinace, která spolu
    // nesouvisí – uložený kód pracoviště totiž přežije i změnu úřadu (např. doplnění z ARES).
    private static readonly Dictionary<string, string> OfficeByWorkplaceCode =
        TaxOfficeDirectory.Workplaces.ToDictionary(x => x.Code, x => x.OfficeCode);

    /// <summary>
    /// Schránka územního pracoviště. Je-li zadaný i kód finančního úřadu, vrátí schránku jen tehdy,
    /// když pracoviště pod ten úřad opravdu spadá – jinak null, ať podání nezamíří k cizímu úřadu.
    /// </summary>
    public static string? ForWorkplace(string? workplaceCode, string? taxOfficeCode = null)
    {
        if (string.IsNullOrWhiteSpace(workplaceCode))
        {
            return null;
        }

        var code = workplaceCode.Trim();
        return taxOfficeCode is not null && !BelongsToOffice(code, taxOfficeCode)
            ? null
            : ByWorkplaceCode.GetValueOrDefault(code);
    }

    /// <summary>Patří územní pracoviště pod daný finanční úřad? Neznámá dvojice = nepatří.</summary>
    public static bool BelongsToOffice(string? workplaceCode, string? taxOfficeCode)
        => !string.IsNullOrWhiteSpace(workplaceCode)
           && !string.IsNullOrWhiteSpace(taxOfficeCode)
           && OfficeByWorkplaceCode.GetValueOrDefault(workplaceCode.Trim()) == taxOfficeCode.Trim();

    public static bool IsValidId(string? dataBoxId)
        => dataBoxId is { Length: 7 } && dataBoxId.All(char.IsLetterOrDigit);
}
