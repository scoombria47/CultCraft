using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CultCraft
{
    internal static class ShakespeareText
    {
        private static readonly Dictionary<string, string> Scenes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Conversation_NPC/Ratau/Intro/0"] = "Fear not, good Lamb. Ratau am I. Once was I a chosen vessel, as thou art now, but those days are scattered to the winds.",
            ["Conversation_NPC/Ratau/Intro/1"] = "I am sent to guide thee. Deep stand we in the lands of the <wave><color=#FFD201>Old Faith</color></wave>, and danger hems us in.",
            ["Conversation_NPC/Ratau/Intro/2"] = "To safety must I lead thee. On through the woods. Thy way to freedom lies ahead, and I shall bide near.",
            ["Conversation_NPC/Ratau/Base/Devotion/0"] = "Praise be! Thy Followers may now <wave>worship thee!</wave>",
            ["Conversation_NPC/Ratau/Base/Dungeon/0"] = "Now must we raise a <color=#FFD201>Shrine</color> - yet first we stand in need of more Followers <sprite name=\"icon_Followers\"> and more gold <sprite name=\"icon_blackgold\">.",
            ["Conversation_NPC/Ratau/Base/GetBones/0"] = "A wonder wert thou to behold. Born to lead, indeed. Now see I why thou wert chosen.",
            ["Conversation_NPC/Ratau/Base/Food/0"] = "By thy hand our Flock shall grow <shake>mighty!</shake> Yet prayer alone will fill no belly, <wave>they must eat.</wave>",
            ["Conversation_NPC/Ratau/Base/ReturnWithFollower/0"] = "Glad am I to see thee safe, and gladder still thou hast not come alone. More hast thou won to join our faithful flock.",
            ["Conversation_NPC/Ratau/IntroRescue/0"] = "Safety is all but won. Yet look ahead! Another hapless soul is soon to be <shake>sacrificed.</shake>",
            ["Conversation_NPC/Ratau/IntroRescue/1"] = "Deliver that soul, and they shall have <wave>no choice</wave> but to join thy new Flock.",
            ["Conversation_NPC/Ratau/IntroTeleporter/0"] = "Out of peril are we. Well hast thou done.",
            ["Conversation_NPC/Ratau/IntroTeleporter/2"] = "It shall bear thee to a temple fallen into ruin. There mayst thou gather thy new Flock.",
            ["Conversation_NPC/Ratau/Spells/0/0"] = "Well met, Lamb! Good tidings do I bring thee!",
            ["Conversation_NPC/Ratau/Spells/1/0"] = "The foes thou slayest shall now yield <color=#FD1D03>Fervour</color> <sprite name=\"icon_blackSoul\"> Gather it to replenish thy Curses.",
            ["Conversation_NPC/DeathCatBossFight/Intro0"] = "Well hast thou done, Lamb.",
            ["Conversation_NPC/DeathCatBossFight/Intro1"] = "Thou hast raised a Flock whose Faith has fed my strength. The Bishops thou didst send to me, and I have broken them and their chains alike.",
            ["Conversation_NPC/DeathCatBossFight/Intro2"] = "Vessel, thy service to the Red Crown is at an end. Give it back to me and meet the end that waits on thee. With this last sacrifice of my most faithful Follower, I shall go free.",
            ["Conversation_NPC/DeathCatBossFight/Intro3"] = "At last... I shall be <shake>FREE!</shake>",
            ["Conversation_NPC/DeathCatBossFight/Intro4"] = "Let us keep the rite. My faithful Followers I call forth to bear witness to my rising.",
            ["Conversation_NPC/DeathCatBossFight/Intro5"] = "Draw near, vessel, and lay thy life at my feet.",
            ["Conversation_NPC/DeathCatBossFight/Intro6"] = "My liege! What do you!?",
            ["Conversation_NPC/DeathCatBossFight/Intro7"] = "<shake>SILENCE!</shake> What blasphemy is here!? I am the God of this Flock! To me shall ye bow, and me alone!",
            ["Conversation_NPC/DeathCatBossFight/Intro8"] = "The Lamb hath fed and sheltered us and shown the way. Our true leader is the Lamb. What are you to us?",
            ["Conversation_NPC/DeathCatBossFight/Intro9"] = "So, Lamb, thou hast betrayed me. My gospel hast thou spurned, and raised thyself a false idol.",
            ["Conversation_NPC/DeathCatBossFight/Intro10"] = "<shake>By divine right that crown is mine!</shake> Thinkest thou to stand in the very face of death?",
            ["Conversation_NPC/DeathCatBossFight/Phase1/0"] = "Thinkest thou thyself victorious? Thinkest thou thyself safe? Forget not that <shake>thou art mine.</shake>",
            ["Conversation_NPC/DeathCatBossFight/Phase1/2"] = "When I have slain thee, thy pain shall find no end. <shake>Even in death thou canst not flee me.</shake>",
            ["Conversation_NPC/DeathCatBossFight/Phase1Dead/0"] = "Didst thou think me vanquished? Was that the whole of what thou thought'st I was?",
            ["Conversation_NPC/DeathCatBossFight/Phase2/1"] = "Corrupt art thou, false idol. And doubtless hath thy rot spread far.",
            ["Conversation_NPC/DeathCatBossFight/Dead/1"] = "Thou hast supplanted me. A vessel no more, but a god that bears a Crown. Accursèd Lamb!",
            ["Conversation_NPC/DeathCatBossFight/Consumed"] = "So, after all, thou art as I. Thou hast become what I am.",
            ["Conversation_NPC/DeathCat/Relic/First/0"] = "Lamb. Wilt thou walk with me?",
            ["Conversation_NPC/DeathCat/Relic/Second/0"] = "Here did the Bishops forge the chains that bound me. Here they doomed me to be forgot. To rot.",
            ["Conversation_NPC/Leshy/Special/Encounter_1/0"] = "Little Lamb, I feel thee there… Darkwood hath not yet forgotten me…",
            ["Conversation_NPC/Heket/Special/Encounter_1/0"] = "<wiggle>Foul beast! Dar'st thou ensnare me thus?</wiggle>",
            ["Conversation_NPC/Shamura/Special/Encounter_1/0"] = "A bond wrought in the shedding of blood… I know thou hearest me, God of Death."
        };

        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Apply"] = "Make It So", ["Cancel"] = "Forbear", ["Close"] = "Shut",
            ["Continue"] = "Proceed", ["Decline"] = "Nay", ["Done"] = "So Done", ["Drop"] = "Lay Down",
            ["Easy"] = "Gentle", ["Empty"] = "Bare", ["Exit"] = "Depart", ["Extra Hard"] = "Most Grievous",
            ["Hard"] = "Grievous", ["Help"] = "Aid", ["Inventory"] = "Goods", ["Journal"] = "Chronicle",
            ["Language"] = "Tongue", ["Medium"] = "Moderate", ["No"] = "Nay", ["Options"] = "Settings",
            ["Quit"] = "Depart", ["Resume"] = "Proceed", ["Select"] = "Choose", ["Use"] = "Employ", ["Yes"] = "Ay"
        };

        private static readonly Dictionary<string, string> Phrases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Divine Inspiration"] = "Heavenly Boon", ["Sacred Enlightenments"] = "Sacred Revelations",
            ["Doctrine Stone"] = "Decree Stone", ["New Doctrine"] = "New Decree",
            ["Ritual of Resurrection"] = "Rite of Resurrection", ["Ritual of Enlightenment"] = "Rite of Revelation",
            ["Ritual of the Ocean's Bounty"] = "Rite of the Sea's Bounty", ["Brainwashing Ritual"] = "Rite of Bewitchment",
            ["Ascend Follower"] = "Raise Follower Heavenward", ["Sacrifice Follower"] = "Offer Up a Follower",
            ["Sacrifice Health"] = "Offer Up Health", ["Perform a Ritual"] = "Hold a Rite",
            ["Give Sermon"] = "Deliver a Sermon", ["Preach a Sermon"] = "Deliver a Sermon",
            ["Collect Devotion"] = "Gather Devotion", ["Collect Reward"] = "Claim Thy Reward",
            ["Collect Loyalty Reward"] = "Claim Fealty's Reward", ["Re-Educate"] = "Reform Follower",
            ["Read Mind"] = "Read Thoughts", ["Take Confession"] = "Hear Confession",
            ["Accept Quest"] = "Undertake Quest", ["Decline Quest"] = "Refuse Quest", ["Complete Quest"] = "Fulfil Quest",
            ["Quest Complete"] = "Quest Fulfilled", ["Quest Failed"] = "Quest Undone",
            ["Follower Forms"] = "Follower Likenesses", ["Follower Form"] = "Follower Likeness",
            ["Tarot Cards"] = "Cards of Fortune", ["Tarot Card"] = "Card of Fortune",
            ["Draw Tarot Card"] = "Draw a Card of Fortune", ["Take Item"] = "Take Goods",
            ["Crafting Table"] = "Crafts Table", ["Chop Wood"] = "Hew Wood", ["Clear Rubble"] = "Clear the Ruin", ["Pick Berries"] = "Gather Berries",
            ["Return to Cult"] = "Return to Thy Flock", ["Return to your flock"] = "Return to thy flock",
            ["Pick up"] = "Take up", ["Press {0} to pick up"] = "Press {0} to take up",
            ["Hold to interact"] = "Hold to act", ["Press any button"] = "Press a button", ["Go back"] = "Return",
            ["Main Menu"] = "Title Screen", ["New Game"] = "Begin Anew", ["Load Game"] = "Open Saved Game",
            ["Game Over"] = "The Play Is Done", ["Continue Game"] = "Play On", ["Save and Quit"] = "Save and Depart",
            ["Add Co-Op Player"] = "Admit a Second Player", ["Add Gamepad for Co-Op"] = "Add a Gamepad for Two",
            ["CultCraft Settings"] = "CultCraft's Settings", ["Camera Settings"] = "View Settings",
            ["Mouse sensitivity"] = "Mouse subtlety", ["Field of view"] = "Ocular field",
            ["First Person"] = "Through Thine Eyes", ["Top-down"] = "View from Above",
            ["Reset Defaults"] = "Restore First Settings", ["Apply Changes"] = "Keep These Changes",
            ["Revert Changes"] = "Undo These Changes", ["No Food"] = "No Fare",
            ["Health is full"] = "Thou art hale", ["Empty hand"] = "Bare hand", ["Choose food"] = "Choose fare",
            ["Outside combat"] = "Outside battle", ["Use selected item"] = "Use chosen item",
            ["Release Curse"] = "Unleash Curse", ["Dodge Roll"] = "Roll Aside", ["Disable Tutorials"] = "Hide Lessons",
            ["Master Volume"] = "Overall Loudness", ["Music Volume"] = "Music Loudness", ["Voice Over Volume"] = "Speech Loudness",
            ["Critical Hit"] = "Grievous Blow", ["Attack Damage"] = "Strength of Blow", ["Attack Speed"] = "Speed of Blows",
            ["Heretics Defeated"] = "Heretics Vanquished", ["A shepherd cannot survive without a flock"] = "A shepherd cannot live bereft of a flock",
            ["You shall rise again"] = "Rise again thou shalt", ["You have died"] = "Thou art slain",
            ["You lost"] = "Thou hast lost", ["You have done well"] = "Well hast thou done",
            ["You are welcome"] = "Thou art welcome", ["You are not alone"] = "Thou art not alone",
            ["You have returned"] = "Thou hast returned", ["You have come"] = "Thou hast come",
            ["You have found"] = "Thou hast found", ["You have chosen"] = "Thou hast chosen",
            ["You have failed"] = "Thou hast failed", ["You have saved"] = "Thou hast saved",
            ["You have earned"] = "Thou hast earned", ["You have brought"] = "Thou hast brought",
            ["You may now"] = "Thou mayst now", ["You can now"] = "Thou canst now",
            ["You cannot enter"] = "Thou canst not enter", ["You must go"] = "Thou must go",
            ["You must find"] = "Thou must find", ["You must first"] = "Thou must first",
            ["You will need"] = "Thou shalt need", ["You'll need"] = "Thou shalt need",
            ["Are you ready"] = "Art thou ready", ["Are you sure"] = "Art thou certain",
            ["Do you see"] = "Dost thou see", ["Do you know"] = "Dost thou know",
            ["Do you think"] = "Dost thou think", ["Do you wish"] = "Dost thou wish",
            ["Have you seen"] = "Hast thou seen", ["Have you found"] = "Hast thou found",
            ["Did you see"] = "Didst thou see", ["Did you know"] = "Didst thou know",
            ["Will you help me"] = "Wilt thou aid me", ["Can you help me"] = "Canst thou aid me",
            ["What are you doing"] = "What do you", ["What do you want"] = "What wouldst thou have",
            ["What do you think"] = "What thinkest thou", ["Where are you going"] = "Whither goest thou",
            ["Where did you come from"] = "Whence didst thou come", ["How did you know"] = "How didst thou know",
            ["There you are"] = "There art thou",
            ["Thank you"] = "I thank you", ["Thanks for"] = "My thanks for",
            ["I beg you"] = "I beseech you", ["I don't know"] = "I know not",
            ["I do not know"] = "I know not", ["I don't understand"] = "I understand not", ["I cannot believe"] = "I scarce can believe",
            ["I don't care"] = "I care not", ["I do not care"] = "I care not", ["I will not"] = "I shall not",
            ["I swear to you"] = "I swear it to thee", ["I promise you"] = "I give thee my word",
            ["Help me"] = "Aid me", ["Listen to me"] = "Hear me",
            ["Give me"] = "Grant me", ["Come here"] = "Come hither", ["Come closer"] = "Draw near",
            ["Go away"] = "Begone", ["Get away from me"] = "Hence, away from me", ["Do not fear"] = "Fear not",
            ["Don't be afraid"] = "Be not afeard", ["Don't forget"] = "Forget not", ["Do not forget"] = "Forget not",
            ["Please accept"] = "Pray accept", ["Please help"] = "Pray aid", ["Please bring"] = "Pray bring",
            ["Please find"] = "Pray find", ["Please tell"] = "Pray tell", ["Please do not"] = "Pray, do not",
            ["If you please"] = "An it please you", ["As you wish"] = "As you will", ["For your sake"] = "For thy sake",
            ["Your own"] = "Thine own", ["Your heart"] = "Thy heart", ["Your life"] = "Thy life",
            ["Your weapon"] = "Thy weapon", ["Your crown"] = "Thy crown", ["Your flock"] = "Thy flock",
            ["Your cult"] = "Thy flock", ["Your followers"] = "Thy followers", ["Your faith"] = "Thy faith",
            ["Your devotion"] = "Thy devotion", ["Your reward"] = "Thy reward", ["Your journey"] = "Thy journey",
            ["Good morning"] = "Good morrow", ["Good evening"] = "Good even",
            ["Good luck"] = "Fair fortune", ["Good news"] = "Glad tidings", ["Bad news"] = "Ill tidings",
            ["At once"] = "Forthwith", ["From now on"] = "Henceforth", ["Before long"] = "Ere long",
            ["Long ago"] = "In days of old", ["Until then"] = "Till then", ["Right away"] = "Straightway",
            ["All at once"] = "Of a sudden", ["At last"] = "At long last",
            ["Over there"] = "Yonder", ["In vain"] = "To no avail", ["Without a doubt"] = "Beyond all doubt",
            ["In any case"] = "Be that as it may", ["From here"] = "Hence"
        };

        private static readonly Dictionary<string, string> Words = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["afraid"] = "afeard", ["afterward"] = "thereafter", ["afterwards"] = "thereafter",
            ["among"] = "amongst", ["anger"] = "wrath", ["angry"] = "wroth",
            ["attain"] = "come by",
            ["banished"] = "exiled", ["beforehand"] = "beforetime",
            ["beg"] = "beseech", ["begging"] = "beseeching", ["belongings"] = "goods", ["beware"] = "take heed",
            ["blessing"] = "benison", ["blessings"] = "benisons", ["blessed"] = "blest",
            ["captured"] = "taken captive", ["careful"] = "heedful", ["certainly"] = "assuredly", ["cheap"] = "of small price",
            ["childhood"] = "tender years", ["chop"] = "hew", ["circumstances"] = "affairs",
            ["clothes"] = "garments", ["clothing"] = "raiment", ["collect"] = "gather",
            ["collected"] = "gathered", ["collecting"] = "gathering", ["combat"] = "battle", ["companion"] = "fellow",
            ["conclude"] = "make an end", ["confess"] = "avow", ["consecrate"] = "hallow", ["consecrated"] = "hallowed",
            ["consume"] = "devour", ["consumed"] = "devoured", ["corpse"] = "dead body", ["coward"] = "craven",
            ["cowards"] = "cravens", ["crafting"] = "craft", ["cruel"] = "fell", ["cults"] = "flocks", ["cult"] = "flock",
            ["currency"] = "coin", ["cursed"] = "accursed", ["damned"] = "accursed", ["danger"] = "peril",
            ["dangerous"] = "perilous", ["darkness"] = "dark", ["deceitful"] = "false",
            ["defeat"] = "vanquish", ["defeated"] = "vanquished", ["delay"] = "tarry",
            ["difficult"] = "arduous",
            ["dignity"] = "honour", ["discard"] = "cast off", ["discover"] = "find", ["discovered"] = "found",
            ["dissenter"] = "malcontent", ["dissenters"] = "malcontents", ["distress"] = "woe",
            ["divine"] = "heavenly", ["doctrine"] = "decree", ["doctrines"] = "decrees",
            ["easily"] = "with ease", ["elsewhere"] = "in another place", ["enemies"] = "foes", ["enemy"] = "foe",
            ["entirely"] = "wholly", ["entrance"] = "way in",
            ["escaped"] = "fled", ["eternal"] = "everlasting", ["eternally"] = "evermore", ["evil"] = "wicked",
            ["exactly"] = "just so", ["except"] = "save", ["exhausted"] = "spent", ["expenses"] = "charges",
            ["extinguish"] = "put out", ["favourable"] = "propitious",
            ["fertile"] = "fruitful",
            ["fierce"] = "fell", ["finally"] = "at last", ["finished"] = "done", ["fleeing"] = "in flight",
            ["food"] = "fare", ["forever"] = "for ever", ["fortunate"] = "blest",
            ["fury"] = "wrath", ["glorious"] = "resplendent",
            ["goodbye"] = "farewell", ["greetings"] = "well met",
            ["happiness"] = "mirth",
            ["happy"] = "glad", ["heal"] = "mend", ["healing"] = "mending",
            ["hello"] = "well met", ["help"] = "aid", ["helped"] = "aided",
            ["hide"] = "conceal", ["honestly"] = "in truth", ["honored"] = "honoured",
            ["however"] = "yet", ["immediately"] = "forthwith", ["incredible"] = "wondrous",
            ["injured"] = "wounded", ["inside"] = "within", ["instantly"] = "forthwith",
            ["intentions"] = "purpose",
            ["kill"] = "slay", ["killed"] = "slain", ["killing"] = "slaying",
            ["large"] = "great", ["lastly"] = "last of all",
            ["liberate"] = "set free", ["loyalty"] = "fealty",
            ["martyred"] = "slain for thy faith",
            ["messenger"] = "herald", ["mistake"] = "error", ["mistakes"] = "errors", ["money"] = "coin",
            ["mournful"] = "woeful", ["mystery"] = "riddle", ["nearly"] = "well-nigh", ["nearby"] = "near at hand",
            ["never"] = "ne'er", ["nevertheless"] = "yet", ["nobody"] = "no one", ["nothing"] = "naught",
            ["obey"] = "heed", ["obligation"] = "duty", ["often"] = "oft",
            ["opponent"] = "adversary", ["opponents"] = "adversaries",
            ["perhaps"] = "perchance",
            ["permission"] = "leave", ["persuade"] = "prevail upon", ["plan"] = "design",
            ["pleased"] = "well pleased", ["poisoned"] = "envenomed", ["poorly"] = "ill", ["possessions"] = "goods",
            ["presently"] = "forthwith", ["prison"] = "gaol", ["prisoner"] = "captive", ["prisoners"] = "captives",
            ["promise"] = "pledge", ["purchased"] = "bought",
            ["quickly"] = "with haste", ["quietly"] = "softly", ["realize"] = "perceive",
            ["recruit"] = "enlist", ["regret"] = "rue",
            ["remember"] = "bear in mind", ["remove"] = "take away", ["repair"] = "mend", ["reply"] = "answer",
            ["rescue"] = "deliver", ["rescued"] = "delivered", ["resist"] = "withstand", ["rest"] = "repose",
            ["ritual"] = "rite", ["rituals"] = "rites",
            ["ruined"] = "undone", ["sacred"] = "hallowed", ["sadness"] = "sorrow",
            ["sanctuary"] = "refuge", ["scared"] = "afeard", ["search"] = "seek",
            ["select"] = "choose", ["shameful"] = "base",
            ["sick"] = "ill", ["sickness"] = "malady", ["silently"] = "in silence", ["sleep"] = "slumber",
            ["sleeping"] = "slumbering", ["slightly"] = "a little",
            ["spoke"] = "spake",
            ["steal"] = "purloin", ["stole"] = "purloined",
            ["stolen"] = "purloined", ["strong"] = "mighty",
            ["suddenly"] = "of a sudden", ["summon"] = "call forth", ["supplies"] = "provisions", ["surrender"] = "yield",
            ["teaching"] = "instruction",
            ["thief"] = "cutpurse", ["thieves"] = "cutpurses", ["threat"] = "menace", ["tired"] = "weary",
            ["tomorrow"] = "to-morrow", ["tonight"] = "to-night", ["trader"] = "merchant",

            ["truly"] = "in truth", ["trustworthy"] = "true", ["unfortunate"] = "hapless",
            ["until"] = "till",
            ["urgent"] = "pressing", ["valuable"] = "precious",
            ["victorious"] = "triumphant", ["villainous"] = "vile",
            ["weapons"] = "arms", ["while"] = "whilst",
            ["wickedness"] = "villainy", ["wonderful"] = "wondrous",
            ["worry"] = "fret", ["worried"] = "troubled", ["worthless"] = "worth naught",
            
        };

        private static readonly Regex Tokens = new Regex(
            MemeText.Protected + @"|\b(?:Cult of the Lamb|The One Who Waits)\b|\b(?:" +
            String.Join("|", Phrases.Keys.OrderByDescending(key => key.Length)
                .Select(key => Regex.Escape(key).Replace("'", "['’]"))) + @")\b|" + MemeText.Word,
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);
        private static readonly Regex Markup = new Regex(MemeText.Protected,
            RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.CultureInvariant);

        internal static string TranslateTerm(string term, string text) =>
            Scenes.TryGetValue(term, out string line) &&
            Markup.Matches(text).Cast<Match>().Select(match => match.Value)
                .SequenceEqual(Markup.Matches(line).Cast<Match>().Select(match => match.Value)) ? line : Translate(text);

        internal static string Translate(string text)
        {
            if (String.IsNullOrEmpty(text)) return text;
            if (Labels.TryGetValue(text, out string label)) return MemeText.MatchCase(text, label);
            return Tokens.Replace(text, token =>
            {
                string original = token.Value;
                if (!Phrases.TryGetValue(original.Replace('’', '\''), out string replacement) &&
                    !Words.TryGetValue(original, out replacement)) return original;
                if (Char.IsLower(original[0])) replacement = replacement.ToLowerInvariant();
                return MemeText.MatchCase(original, replacement);
            });
        }
    }
}
