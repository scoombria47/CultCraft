using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CultCraft
{
    internal static class LolcatText
    {
        private static readonly Regex Tokens = new Regex(
            MemeText.Protected + "|" + MemeText.Word,
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

        private static readonly Dictionary<string, string> Words = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["accessibility"]="akzessibilitee", ["accept"]="aksept", ["attack"]="attak",
            ["audio"]="awdio", ["cancel"]="cansul", ["confirm"]="konfurm", ["controller"]="kontrollur",
            ["damage"]="dammij", ["difficulty"]="diffikultie", ["devotion"]="devoshun", ["drop"]="dropp",
            ["faithful"]="faythful", ["fertilizer"]="fertilizurr", ["graphics"]="grafix", ["harvest"]="harvestz",
            ["inventory"]="inventoree", ["meat"]="meet", ["music"]="mewzik", ["pause"]="pawz",
            ["pilgrim"]="pilgrem", ["revelation"]="revelashun", ["sacrifice"]="sacrifize",
            ["shrine"]="shryne", ["skip"]="skipp", ["tarot"]="tarrot", ["tutorial"]="tutoryul",
            ["volume"]="volyoom", ["worship"]="wurship", ["easy"]="eazy", ["normal"]="normul", ["hard"]="hawrd",
            ["acquired"]="akwired", ["add"]="addz", ["after"]="aftur", ["altar"]="altur",
            ["ancient"]="aynshunt", ["animal"]="animmal", ["apply"]="applai", ["arena"]="areena", ["assign"]="assine",
            ["ate"]="nommed", ["auto"]="awto", ["axe"]="aks", ["bane"]="bayn", ["belief"]="beleef", ["bell"]="bel",
            ["bet"]="bett", ["blood"]="blud", ["blunderbuss"]="blundurbus", ["body"]="boddy", ["bone"]="boan",
            ["bloom"]="blooom", ["bread"]="bred", ["bush"]="bushh", ["candle"]="candul",
            ["child"]="childe", ["circle"]="surkul", ["clear"]="cleer", ["collect"]="collectz", ["collected"]="collectid",
            ["consume"]="consuum", ["control"]="kontrol", ["convert"]="convurt", ["cook"]="kook", ["cost"]="kost",
            ["credits"]="credditz", ["crystal"]="crystul", ["cult"]="kult", ["curse"]="kurrse", ["cursed"]="kurrsed",
            ["custom"]="customz", ["dagger"]="daggur", ["day"]="dai", ["decline"]="declienz", ["decoration"]="dekorashun",
            ["defeat"]="defeet", ["defeated"]="defeetid", ["delete"]="deleet", ["design"]="desine", ["disable"]="disabul",
            ["dissenter"]="dissenturr", ["dodge"]="doj", ["draw"]="drawz", ["drinkhouse"]="drinnkhaus",
            ["edit"]="edditt", ["egg"]="eg", ["enemy"]="enemi", ["enemies"]="enemiezz", ["enforcer"]="enforcurr",
            ["extra"]="ekstra", ["farmer"]="farmurr", ["feed"]="fead", ["fertiliser"]="fertilizurr", ["fervour"]="furvurr",
            ["flail"]="flayl", ["fleece"]="flees", ["flower"]="flowur", ["frame"]="fraym", ["fruit"]="froot",
            ["fullscreen"]="fullskreen", ["furnace"]="furniss", ["gain"]="gayn", ["gauntlet"]="gauntlett",
            ["godly"]="godleh", ["golden"]="goldun", ["got"]="gots", ["grass"]="gras", ["hammer"]="hammur",
            ["heretic"]="heretik", ["high"]="hai", ["holy"]="holee", ["indoctrinate"]="indoktrinate",
            ["intensity"]="intensitee", ["interact"]="interakt", ["keyboard"]="keybord", ["lantern"]="lanturn",
            ["leader"]="leedur", ["legendary"]="legendarree", ["less"]="les", ["line"]="lyne", ["locked"]="lokd",
            ["lose"]="looz", ["low"]="loh", ["loyalty"]="loyaltee", ["max"]="maxx", ["meal"]="meel",
            ["medium"]="meedjum", ["meditate"]="meditaet", ["member"]="membur", ["merciless"]="mercyless",
            ["missionary"]="mishunarry", ["mode"]="moed", ["move"]="moov", ["mushroom"]="mushrum", ["necklace"]="nekklace",
            ["okay"]="okai", ["opponent"]="opponunt", ["parent"]="parrent", ["perfect"]="purrfect", ["photo"]="foto",
            ["pick"]="pikk", ["poop"]="poopz", ["praise"]="praiz", ["pumpkin"]="punkin", ["quality"]="kwalitee",
            ["quit"]="kwit", ["randomise"]="randumize", ["ranch"]="ranchh", ["rate"]="rayt", ["received"]="receevd",
            ["refresh"]="refressh", ["release"]="releez", ["remove"]="remoov", ["required"]="rekwired", ["resolution"]="rezolushun",
            ["resume"]="resoom", ["resurrection"]="resurrektshun", ["reward"]="rewurd", ["robe"]="roab", ["roll"]="roal",
            ["rotten"]="rottun", ["saved"]="saevd", ["scale"]="skayl", ["scribe"]="skrybe", ["seed"]="sead",
            ["sensitivity"]="sensitivitee", ["shadow"]="shaddow", ["shelter"]="shelturr", ["shield"]="sheeld",
            ["silk"]="silkz", ["sin"]="sinn", ["skull"]="skul", ["slept"]="snoozd", ["small"]="smol",
            ["snow"]="sno", ["spider"]="spydurr", ["station"]="stashun", ["statue"]="statyoo", ["tablet"]="tablitt",
            ["target"]="targit", ["tax"]="taks", ["text"]="tekst", ["texture"]="tekstchur", ["totem"]="totumm",
            ["trait"]="trayt", ["trophy"]="trofee", ["unlock"]="unlok", ["unapplied"]="unapplaid", ["vampiric"]="vampyric",
            ["vibration"]="vibrashun", ["wall"]="wahl", ["welcome"]="welkum", ["win"]="winn", ["winter"]="winturr",
            ["wooden"]="wuddn", ["wool"]="woolz", ["zealous"]="zeelus",
            ["aberration"]="aberrashun", ["age"]="ayj", ["appearance"]="appeerance",
            ["borderless"]="borderles", ["brightness"]="bryteness", ["camera"]="camra", ["chromatic"]="kromatik",
            ["currency"]="currensee", ["destroy"]="destroi", ["detail"]="detael", ["dice"]="dyce",
            ["duration"]="durashun", ["economy"]="ekonommee", ["empty"]="emptee", ["enlightenment"]="enlitenmunt",
            ["environment"]="envyronment", ["escaped"]="escaepd", ["flip"]="flipp", ["form"]="foarm",
            ["gameplay"]="gaemplai", ["general"]="generul", ["hunger"]="hungurr", ["interface"]="interfaes",
            ["item"]="itemm", ["label"]="laybul", ["layout"]="layowt", ["left"]="lefft",
            ["martyred"]="martyrrd", ["match"]="matsh", ["motion"]="moshun", ["navigate"]="navigaet",
            ["notification"]="notifurcashun", ["path"]="paath", ["plant"]="plaant", ["queue"]="kyoo",
            ["reduce"]="reduse", ["relationship"]="relashunship", ["require"]="rekwiar", ["retry"]="retrai",
            ["role"]="roal", ["sacred"]="saekrid", ["speed"]="spead", ["stat"]="stahht",
            ["structure"]="strukchur", ["thought"]="thawt", ["tier"]="teer", ["tracked"]="trakd",
            ["unbind"]="unbynd", ["unlimited"]="unlimitid", ["user"]="yoozur", ["vignette"]="vinyet",
            ["windowed"]="windoad",
            ["a"]="a", ["about"]="abowt", ["all"]="awl", ["am"]="iz", ["an"]="an",
            ["and"]="an", ["are"]="iz", ["as"]="az", ["at"]="at", ["back"]="bak", ["be"]="bee",
            ["been"]="bin", ["before"]="b4", ["believe"]="beleev", ["better"]="bettur",
            ["build"]="bild", ["building"]="bildin", ["button"]="buttun", ["by"]="bi",
            ["can"]="can", ["cannot"]="cannawt", ["can't"]="cannawt", ["can’t"]="cannawt",
            ["catch"]="katch", ["change"]="chaenj", ["choose"]="chooz", ["close"]="cloz",
            ["come"]="com", ["coming"]="comin", ["continue"]="keap goin", ["controls"]="kontrolz",
            ["could"]="cud", ["couldn't"]="cud nawt", ["couldn’t"]="cud nawt",
            ["craft"]="kraft", ["crafting"]="kraftin", ["creature"]="crittr", ["crown"]="croun",
            ["danger"]="dangur", ["death"]="dedness", ["done"]="dun", ["don't"]="doan", ["don’t"]="doan",
            ["divine"]="divyne", ["doctrine"]="doktrin", ["doctrines"]="doktrinz",
            ["down"]="doun", ["dream"]="dreem", ["eat"]="nom", ["eating"]="nommin",
            ["enough"]="enuf", ["everyone"]="evrywun", ["everything"]="evryfing", ["exit"]="leev",
            ["faith"]="fayth", ["far"]="faar", ["favorite"]="favrit", ["fear"]="feer", ["fight"]="fite",
            ["find"]="fynd", ["first"]="furst", ["fish"]="fesh", ["fishing"]="feshin",
            ["follower"]="followurr", ["followers"]="followurrz",
            ["food"]="nomz", ["for"]="fur", ["forever"]="furevur", ["friend"]="fren", ["friends"]="frenz",
            ["from"]="frum", ["full"]="ful", ["game"]="gaem", ["games"]="gaemz", ["give"]="giv",
            ["going"]="goin", ["good"]="gud", ["goat"]="goet", ["great"]="grate", ["had"]="had",
            ["has"]="haz", ["have"]="haz", ["health"]="helth", ["hear"]="heer", ["heart"]="hart",
            ["hello"]="oh hai", ["help"]="halp", ["here"]="heer", ["hold"]="hoeld", ["home"]="hoam",
            ["how"]="howz", ["i'm"]="i iz", ["i’m"]="i iz", ["i've"]="i haz", ["i’ve"]="i haz",
            ["if"]="if", ["in"]="in", ["inside"]="insyde", ["inspiration"]="inspurrashun", ["into"]="intu", ["is"]="iz", ["it"]="it",
            ["it's"]="it iz", ["it’s"]="it iz", ["just"]="jus", ["keep"]="keap", ["kill"]="kild",
            ["kind"]="kynd", ["knucklebones"]="knuckulbonez", ["know"]="kno", ["lamb"]="lamm", ["language"]="langwidge",
            ["last"]="laast", ["learn"]="lurn", ["leave"]="leev", ["life"]="lyf", ["light"]="lite",
            ["like"]="liek", ["little"]="lil", ["load"]="loed", ["loading"]="loedin",
            ["look"]="luk", ["lord"]="lawd", ["lost"]="lawst", ["love"]="luv", ["make"]="maek",
            ["many"]="menny", ["me"]="meh", ["menu"]="menyoo", ["mine"]="myn", ["more"]="moar",
            ["most"]="moast", ["mouse"]="maus", ["much"]="mutch", ["my"]="mai", ["name"]="naem",
            ["near"]="neer", ["need"]="ned", ["never"]="nevar", ["new"]="nu", ["next"]="nekst",
            ["night"]="nite", ["no"]="noez", ["not"]="nawt", ["nothing"]="nuffin", ["now"]="nao",
            ["of"]="ov", ["off"]="awf", ["on"]="awn", ["once"]="wunce", ["one"]="wun", ["only"]="onleh",
            ["open"]="opun", ["options"]="opshuns", ["other"]="othur", ["our"]="owr", ["out"]="owt",
            ["play"]="plai", ["player"]="playur", ["please"]="plz", ["power"]="powur",
            ["press"]="presh", ["quest"]="kwest", ["quests"]="kwests", ["quick"]="kwik",
            ["ready"]="reddy", ["really"]="rly", ["remember"]="remembur", ["reset"]="reeset",
            ["rest"]="resst", ["return"]="returrrn", ["right"]="rite", ["ritual"]="rityool",
            ["safe"]="saef", ["save"]="saev", ["saving"]="saevin", ["say"]="sez", ["search"]="serch",
            ["see"]="seez", ["select"]="pick", ["sermon"]="sermun", ["settings"]="opshuns", ["shall"]="shal",
            ["show"]="sho", ["sick"]="sik", ["sleep"]="snooz", ["some"]="sum", ["someone"]="sumwun",
            ["something"]="sumfing", ["soon"]="sooon", ["speak"]="speek", ["start"]="staht",
            ["stone"]="stoan", ["stop"]="stahp", ["strong"]="stronk", ["sword"]="sord",
            ["take"]="taek", ["talk"]="tawk", ["temple"]="templ", ["thank"]="fank", ["thanks"]="fanks",
            ["that"]="dat", ["that's"]="dat iz", ["that’s"]="dat iz", ["the"]="teh", ["their"]="der",
            ["them"]="dem", ["then"]="den", ["there"]="dere", ["these"]="deez", ["they"]="dey",
            ["thing"]="fing", ["things"]="fingz", ["think"]="fink", ["this"]="dis", ["those"]="doze",
            ["time"]="tiem", ["to"]="tu", ["today"]="tuday", ["together"]="togethur", ["too"]="too",
            ["treasure"]="trezhur", ["tree"]="treh", ["true"]="troo", ["try"]="tri", ["two"]="too",
            ["under"]="undur", ["until"]="til", ["up"]="upp", ["upgrade"]="upgrayd", ["us"]="uz",
            ["use"]="yooz", ["used"]="yoozd", ["very"]="verry", ["wait"]="wate", ["waiting"]="waitin", ["waits"]="waitz",
            ["want"]="wantz", ["was"]="wuz", ["watch"]="wotch", ["water"]="watur", ["way"]="wai",
            ["we"]="wee", ["weapon"]="weppun", ["weapons"]="weppunz", ["well"]="wel", ["were"]="wur",
            ["what"]="wat", ["when"]="wen", ["where"]="wher", ["which"]="wich", ["while"]="wile",
            ["who"]="hoo", ["why"]="wai", ["will"]="wil", ["with"]="wif", ["without"]="wifowt",
            ["wood"]="wud", ["world"]="wurld", ["would"]="wud", ["yes"]="yus", ["you"]="u",
            ["your"]="ur", ["you're"]="u iz", ["you’re"]="u iz", ["you've"]="u haz", ["you’ve"]="u haz",
            ["yours"]="urz"
        };

        internal static string Translate(string text)
        {
            if (String.IsNullOrEmpty(text)) return text;
            return Tokens.Replace(text, match =>
            {
                string word = match.Value;
                if (!Words.TryGetValue(word, out string changed))
                {
                    if (!Char.IsLetter(word[0])) return word;
                    string suffix = word.EndsWith("'s", StringComparison.OrdinalIgnoreCase) ||
                        word.EndsWith("’s", StringComparison.OrdinalIgnoreCase) ? word.Substring(word.Length - 2) :
                        word.Length > 3 && word.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? "s" : "";
                    if (suffix.Length > 0 && Words.TryGetValue(word.Substring(0, word.Length - suffix.Length), out string stem))
                        changed = stem + suffix;
                    else if (word.Length >= 5 && word.EndsWith("ing", StringComparison.OrdinalIgnoreCase))
                        changed = word.Substring(0, word.Length - 1);
                    else
                        return word;
                }
                return MemeText.MatchCase(word, changed);
            });
        }
    }
}
