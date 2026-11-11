using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CultCraft
{
    internal static class PirateText
    {
        private static readonly Dictionary<string, string> Phrases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["The One Who Waits"] = "The One What Waits",
            ["Divine Inspiration"] = "Divine Guidance",
            ["Sacred Enlightenments"] = "Wisdom o' the Deep",
            ["Re-Educate"] = "Set 'em straight",
            ["CultCraft Settings"] = "CultCraft Ship's Orders",
            ["new game"] = "set sail",
            ["continue game"] = "back to the voyage",
            ["load game"] = "open the ship's log",
            ["save game"] = "write in the ship's log",
            ["return to main menu"] = "back to port",
            ["quit game"] = "abandon ship",
            ["pick up"] = "haul aboard",
            ["chop wood"] = "hew timber",
            ["clear rubble"] = "clear the wreckage",
            ["dodge roll"] = "roll clear",
            ["press any button"] = "press any button, matey",
            ["field of view"] = "spyglass view",
            ["mouse sensitivity"] = "mouse sensitivity",
            ["reset defaults"] = "restore ship's orders",
            ["apply changes"] = "make it so",
            ["hold to interact"] = "hold to act",
            ["main menu"] = "port",
            ["show hints"] = "show sailing tips",
            ["no food"] = "no grub aboard",
            ["empty hand"] = "bare hand",
            ["wooden sword"] = "cutlass o' timber",
            ["health is full"] = "fit as a fiddle",
            ["you are"] = "ye be",
            ["you have"] = "ye've",
            ["is not"] = "ain't",
            ["are not"] = "ain't",
            ["there is"] = "there be",
            ["there are"] = "there be",
            ["thank you"] = "much obliged",
            ["my friend"] = "me hearty",
            ["I have been waiting for you"] = "I've been waitin' fer ye",
            ["you have died"] = "ye've gone to Davy Jones",
            ["game over"] = "voyage's end"
        };

        private static readonly Dictionary<string, string> Words = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["accept"]="aye", ["achievement"]="feat", ["add"]="bring aboard", ["after"]="afterward",
            ["allow"]="let", ["altar"]="shrine", ["ancient"]="old as the tides", ["apprentice"]="deckhand",
            ["assign"]="put on duty", ["attack"]="strike", ["available"]="at hand", ["axe"]="hatchet",
            ["banish"]="cast out", ["bed"]="bunk", ["belief"]="faith", ["blessing"]="good fortune",
            ["break"]="smash", ["careful"]="mind yerself", ["certain"]="sure", ["child"]="young'un",
            ["clean"]="swab", ["colony"]="settlement", ["command"]="order", ["completed"]="finished",
            ["conversation"]="parley", ["cost"]="price", ["credits"]="ship's roll", ["danger"]="peril",
            ["decline"]="nay", ["destroy"]="scuttle", ["dock"]="berth", ["doctrine"]="article",
            ["doctrines"]="articles", ["dungeon"]="dungeon", ["empty"]="bare", ["escape"]="make off",
            ["explore"]="chart", ["farmer"]="farmhand", ["feast"]="spread", ["find"]="spy",
            ["first"]="first", ["follower's"]="crewmate's", ["free"]="loose", ["freshwater"]="fresh water",
            ["furniture"]="furnishin's", ["gift"]="token", ["gifts"]="tokens", ["go"]="make way",
            ["guard"]="sentry", ["happened"]="came to pass", ["heretic"]="heretic", ["hope"]="reckon",
            ["icon"]="mark", ["inside"]="below decks", ["interact"]="act", ["language"]="lingo",
            ["locked"]="battened shut", ["map"]="chart", ["message"]="word", ["mining"]="diggin'",
            ["mission"]="errand", ["money"]="coin", ["named"]="called", ["navigation"]="steering",
            ["notes"]="log entries", ["opponent"]="rival", ["payment"]="pay", ["permission"]="leave",
            ["plant"]="sow", ["promise"]="word", ["raise"]="hoist", ["rebuild"]="rig anew",
            ["reduce"]="cut down", ["relic"]="relic", ["required"]="needed", ["rescue"]="save",
            ["rest"]="turn in", ["ritual"]="rite", ["rituals"]="rites", ["route"]="course",
            ["secret"]="secret", ["settlement"]="outpost", ["slippery"]="slick", ["sort"]="stow",
            ["sound"]="sound", ["speak"]="parley", ["speed"]="pace", ["stolen"]="pilfered",
            ["stop"]="avast", ["storm"]="squall", ["supplied"]="stocked", ["supply"]="stock",
            ["task"]="duty", ["teach"]="show the ropes", ["thief"]="pilferer", ["tired"]="weary",
            ["tool"]="implement", ["town"]="port", ["trade"]="barter", ["trader"]="merchant",
            ["trophy"]="prize", ["unlock"]="unbar", ["visit"]="call on", ["volume"]="loudness",
            ["ah"]="arr", ["all"]="all", ["already"]="already", ["am"]="be", ["and"]="an'",
            ["back"]="astern", ["belongings"]="gear", ["beware"]="ware", ["boat"]="vessel",
            ["boss"]="captain", ["build"]="rig", ["building"]="structure", ["bury"]="lay to rest",
            ["buy"]="barter fer", ["cancel"]="belay", ["carry"]="haul", ["catch"]="snag", ["chest"]="coffer",
            ["choose"]="pick", ["chop"]="hew", ["close"]="shut", ["coin"]="doubloon", ["coins"]="doubloons",
            ["collect"]="gather", ["collected"]="hauled in", ["combat"]="scrap", ["complete"]="finished",
            ["continue"]="carry on", ["controls"]="helm controls", ["corpse"]="body", ["craft"]="make",
            ["crew"]="crew", ["damage"]="harm", ["death"]="doom", ["defeat"]="best", ["defeated"]="best'd",
            ["devotion"]="devotion", ["difficulty"]="roughness o' the seas", ["dissenter"]="mutineer",
            ["dissenters"]="mutineers", ["doctor"]="ship's surgeon", ["done"]="aye, done", ["drop"]="set down",
            ["eat"]="eat", ["enemies"]="foes", ["enemy"]="foe", ["enough"]="'nough", ["everyone"]="all hands",
            ["exit"]="leave port", ["fast"]="smartly", ["fervour"]="zeal", ["fight"]="scrap", ["flee"]="make off",
            ["follower"]="crewmate", ["followers"]="crew", ["food"]="grub", ["for"]="fer", ["found"]="spied",
            ["friend"]="matey", ["friends"]="mateys", ["from"]="from", ["game"]="voyage", ["get"]="fetch",
            ["give"]="hand over", ["gold"]="gold", ["greetings"]="ahoy", ["harvest"]="reap", ["hello"]="ahoy",
            ["help"]="lend a hand", ["here"]="hereabouts", ["hide"]="stow", ["hold"]="hold fast",
            ["home"]="home port", ["hunger"]="empty bellies", ["hungry"]="in need o' grub", ["inventory"]="hold",
            ["island"]="isle", ["item"]="piece o' gear", ["items"]="gear", ["journey"]="voyage", ["keep"]="hold on to",
            ["knucklebones"]="Bones o' Chance", ["land"]="shore", ["leader"]="captain", ["leave"]="shove off",
            ["left"]="left", ["listen"]="listen up", ["load"]="open", ["loot"]="plunder", ["lost"]="adrift",
            ["loyalty"]="loyalty", ["martyred"]="fell fer the faith", ["meal"]="mess",
            ["merchant"]="trader", ["menu"]="chart", ["more"]="more", ["move"]="make way", ["my"]="me",
            ["near"]="close aboard", ["need"]="be needin'", ["never"]="ne'er", ["new"]="fresh", ["no"]="nay",
            ["nothing"]="nothin'", ["of"]="o'", ["off"]="off", ["often"]="oft", ["old"]="ol'", ["on"]="on",
            ["open"]="unlatch", ["options"]="ship's orders", ["our"]="our", ["outside"]="out on deck",
            ["pause"]="heave to", ["people"]="folk", ["perfect"]="shipshape", ["place"]="set",
            ["play"]="set sail", ["player"]="sailor", ["please"]="if ye please", ["prison"]="brig",
            ["prisoner"]="prisoner", ["quest"]="errand", ["quests"]="errands", ["quick"]="smart",
            ["quit"]="abandon ship", ["ready"]="ready", ["received"]="took aboard", ["recruit"]="sign on",
            ["release"]="let go", ["repair"]="patch up", ["resume"]="carry on", ["return"]="head back",
            ["reward"]="bounty", ["rewards"]="bounties", ["right"]="right", ["rubble"]="wreckage",
            ["sacrifice"]="offering", ["save"]="log", ["saved"]="logged", ["scared"]="afeared",
            ["search"]="scour", ["select"]="choose", ["sell"]="trade away", ["settings"]="ship's orders",
            ["ship"]="ship", ["shout"]="holler", ["show"]="show", ["small"]="wee", ["something"]="somethin'",
            ["start"]="begin", ["steal"]="pilfer", ["stone"]="stone", ["store"]="stow", ["stranger"]="stranger",
            ["strong"]="stout", ["supplies"]="provisions", ["sword"]="cutlass", ["take"]="take", ["talk"]="parley",
            ["tavern"]="tavern", ["thanks"]="much obliged", ["them"]="'em", ["they"]="they",
            ["throw"]="toss", ["today"]="this day", ["together"]="as one crew", ["tomorrow"]="on the morrow",
            ["treasure"]="booty", ["trouble"]="rough seas", ["tutorial"]="sailing lesson", ["understand"]="savvy",
            ["unpack"]="unload", ["upgrade"]="improve", ["upgrades"]="improvements", ["use"]="make use of",
            ["very"]="mighty", ["wait"]="hold fast", ["want"]="fancy", ["was"]="were", ["watch"]="keep watch",
            ["welcome"]="welcome aboard", ["well"]="well now", ["where"]="whereabouts", ["while"]="whilst",
            ["who"]="who", ["win"]="prevail", ["with"]="wi'", ["without"]="sans", ["wood"]="timber",
            ["world"]="world", ["would"]="would", ["yes"]="aye", ["you"]="ye", ["your"]="yer", ["yours"]="yers",
            ["you're"]="ye be", ["you've"]="ye've", ["you'll"]="ye'll"
        };

        private static readonly Regex Tokens = new Regex(
            MemeText.Protected + @"|\b(?:" + String.Join("|", Phrases.Keys.OrderByDescending(key => key.Length).Select(Regex.Escape)) +
            @")\b|" + MemeText.Word,
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

        internal static string Translate(string text)
        {
            if (String.IsNullOrEmpty(text)) return text;
            return Tokens.Replace(text, match =>
            {
                string original = match.Value;
                string word = original.Replace('’', '\'');
                if (Phrases.TryGetValue(word, out string changed) || Words.TryGetValue(word, out changed))
                    return MemeText.MatchCase(original, changed);
                if (word.Length >= 5 && word.EndsWith("ing", StringComparison.OrdinalIgnoreCase) &&
                    word.All(Char.IsLetter) && !word.Equals("thing", StringComparison.OrdinalIgnoreCase))
                    return original.Substring(0, original.Length - 1) + "'";
                return original;
            });
        }
    }
}
