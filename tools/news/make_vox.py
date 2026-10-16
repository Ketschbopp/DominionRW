# Generates 1.7/Defs/RulePackDefs/Dominion_NewsVoxEvents.xml: per-event colonist quotes (vox_<Event>) and asides (aside_<Event>).
import sys
from xml.sax.saxutils import escape
V = {}
def v(ev, *lines, c=None):
    V.setdefault(ev, []).extend((c, l) for l in lines)

# ---- war
v("WarDeclared",
  "[FACTION_A] and [FACTION_B] at each other's throats. Again. I'm starting to think it's a hobby.",
  "War between [FACTION_A] and [FACTION_B]. Somebody remind me which one owes us silver.",
  "If [FACTION_A] wins, we're neighbours with a winner. That's never good.",
  "I give it a season. Two if [LEADER_A] is as stubborn as they say.",
  "Every war out there means more raiders down here. Check the sandbags.",
  "I hope they both lose. Is that allowed?",
  "Someone get the map out. I want to know how far away this is.",
  "War is just trade by other means. Worse means.",
  "[FACTION_B] never saw it coming. Neither did I, to be fair.",
  "First the drums, then the refugees, then the raiders. You watch.")
v("WarDeclared", "They declared war on US? On our little [COLONY]? Bold.",
  "Well. Now we're in the paper for the wrong reasons.",
  "Everyone to the walls. And someone tell the cook to stop crying.",
  "War with [FACTION_A]. I knew that trader was looking at our turrets too long.", c="US==yes")
v("WarDeclared", "Good. Let them bleed each other. Less for us to deal with.", "Finally. I was getting bored.", c="TONE==callous")
v("WarDeclared", "All those families. I can't stop thinking about the families.", c="TONE==kind")
v("WarDeclared", "Both sides will need steel and medicine. We have steel and medicine.", c="TONE==greedy")
v("WarDeclared", "This is how it starts. Then it's everyone. Then it's us.", c="TONE==grim")
v("WarDeclared", "Maybe it'll be short! Wars are sometimes short. Right?", c="TONE==sunny")

v("PeaceMade",
  "Peace between [FACTION_A] and [FACTION_B]. I'll believe it when the caravans start running.",
  "Good. Fewer bodies on the roads means fewer bodies on our doorstep.",
  "Peace! Someone open the good beer. Not the very good beer. The good beer.",
  "They'll be at it again within the year. But tonight, peace.",
  "I wonder who blinked first. My bet's on [FACTION_A].",
  "The traders will be back. And they'll want to talk about it for hours.",
  "Peace treaties are only as good as the people who sign them. Let's see.",
  "Maybe now the refugees can go home.")
v("PeaceMade", "We're at peace with [FACTION_A]? Then who do I glare at from the wall?", "Peace with [FACTION_A]. I'll keep my rifle oiled anyway.", c="US==yes")
v("PeaceMade", "Shame. I was enjoying the reports.", c="TONE==callous")
v("PeaceMade", "Finally some good news. I could cry.", c="TONE==kind")
v("PeaceMade", "Peace is bad for the arms trade. Good for everything else, I suppose.", c="TONE==greedy")

v("JoinedWar",
  "[FACTION_A] jumping into the fight. More's the merrier, if you're an undertaker.",
  "Allies dragging allies in. This is how small wars become big ones.",
  "Now it's a proper mess. Pick a side? No thanks.",
  "One more flag on the battlefield. One less faction to trade with.")
v("BattleFought",
  "A battle near [DETAIL]. I hope the medics are better than ours.",
  "They say [FACTION_A] carried the day. They always say that about whoever wrote the report.",
  "Hundreds fighting over one town. We fight over who does the dishes.",
  "Battles mean bodies, and bodies mean scavengers. Watch the roads.",
  "I'd like to know what they're using. Charge rifles? Spears? Both?",
  "Somewhere a doctor is very, very tired.")
v("BattleFought", "Wish I'd been there. Do they take volunteers?", c="TONE==callous")
v("BattleFought", "There are people dying in the mud over this. Real people.", c="TONE==kind")
v("SettlementOccupied",
  "[FACTION_A] holds [DETAIL] now. The people there didn't get a vote.",
  "New flag over [DETAIL]. Same mud underneath.",
  "If they can take a town like that, they can take a colony like ours. Just saying.",
  "Occupied. I hope the new masters at least fix the roads.",
  "[FACTION_B] won't forget [DETAIL]. Nobody forgets a lost town.")
v("SettlementReturned",
  "[DETAIL] is back where it belongs. Somebody's very relieved tonight.",
  "Taken, retaken. The people of [DETAIL] must be exhausted.",
  "Good for them. Nobody likes living under someone else's flag.")
v("CapitalLost",
  "[FACTION_B] lost their capital. That's not a defeat, that's a disaster.",
  "Imagine waking up and your capital belongs to someone else.",
  "Without a capital, who even runs [FACTION_B] now?",
  "That's the sort of news that changes maps. And not in pencil.",
  "If [CAPITAL_A] can fall, anything can.")
v("CapitalEstablished",
  "[FACTION_A] has a new capital at [CAPITAL_A]. Fancy.",
  "New capital, new taxes. That's how it always goes.",
  "A capital is just a town with a bigger opinion of itself.")
v("Capitulation",
  "[FACTION_B] gave up. Can't say I blame them.",
  "Surrender. The hardest word for any leader. [LEADER_A] must be pleased with themselves.",
  "The war's over, for them at least. Now comes the bill.",
  "Losers pay. They always pay.")
v("PeaceTerms",
  "Peace terms. A polite word for 'give us your stuff'.",
  "Reparations, land, apologies. I'd have asked for pie.",
  "The winners write the terms, the losers write the history. Wait, that's not right.")
v("Blockade",
  "A blockade. Nobody in, nobody out. I'd go mad.",
  "They're starving them out. Slow, ugly and it works.",
  "No caravans through. Prices will jump everywhere, you watch.")
v("SiegeBegan",
  "A siege at [DETAIL]. I hope they have a good well.",
  "Sieges are the worst. All waiting and no glory.",
  "The defenders of [DETAIL] are in for a long, hungry season.",
  "If anyone ever besieges us, I want it known that I called dibs on the chocolate.")
v("SiegeBegan", "Someone's besieging us? Lock the freezer!", c="US==yes")
v("WarbandMarching",
  "A warband on the march. Let's hope it's marching somewhere else.",
  "Fighters moving through the hills. Keep the animals in tonight.",
  "Where are they headed? I'd really like to know where they're headed.",
  "Warbands don't march for fun. Somebody's about to have a bad week.")
v("WarbandMarching", "That warband is heading our way. Everyone get your armour on. Yes, now.", c="US==yes")
v("WarbandIntercepted", "Caught them on the road. Ambush beats a fair fight every time.", "So much for the big march.")
v("Reinforcements", "Reinforcements. Because one army wasn't enough.", "More soldiers, more mouths to feed, more trouble.")
v("BorderSkirmish",
  "A skirmish on the border. That's how wars clear their throat.",
  "Shots across the border. Somebody will call it an accident. Nobody will believe them.",
  "Small fights become big fights. I've seen it before.")
v("MissileStrike",
  "Missiles. Actual missiles. We're not ready for missiles.",
  "Did anyone feel that? No? Good. Let's keep it that way.",
  "Whoever fires missiles first never fires them last.",
  "That's a crater where a town used to be. Let that sink in.",
  "We need a deeper basement. Much deeper.")
v("MissileStrike", "I've never seen a mushroom cloud. I would like to keep it that way.", c="TONE==grim")
v("ArmsRace", "Everyone's building more guns. Wonderful. Very reassuring.", "Arms race. Which means someone's going to want to try them out.", "More weapons in the world, and none of them are ours.")
v("ColdWarBegan", "Nobody's shooting yet. That's the scary part.", "A cold war. Lots of glaring, lots of spies, lots of nerves.", "They hate each other but won't say it with bullets. Yet.")
v("ColdWarEnded", "The cold war's over. Everyone can stop glaring now.", "Thaw. Took them long enough.")
v("ProxyWar", "They fund someone else to do the fighting. Cowards, the lot of them.", "Proxy war. Somebody else's sons dying for someone else's grudge.")

# ---- diplomacy
v("RelationsWarmed",
  "[FACTION_A] and [FACTION_B] are friends now. Friends who might gang up on someone.",
  "An alliance. Good for them. Hopefully not bad for us.",
  "Two factions holding hands. Watch where they point next.",
  "Allies today, rivals tomorrow. That's the rim.")
v("AllianceBroken",
  "Allies no more. Someone said something they shouldn't have.",
  "Broken alliance. That's a lot of awkward dinners cancelled.",
  "When friends fall out, the knives come out quicker.",
  "I always said [FACTION_A] couldn't be trusted. I say that about everyone, but still.")
v("TreatySigned",
  "A treaty. Lots of signatures, lots of seals, lots of promises.",
  "Signed and sealed. Let's see if it's kept.",
  "Paper's cheap. Keeping your word is expensive.",
  "Good. Rules are better than raids.")
v("TreatySigned", "We signed something? I hope someone read it first.", c="US==yes")
v("TreatyBroken", "A broken treaty. Nobody will trust [FACTION_A] for years.", "They broke it. Of course they broke it.", "A promise on paper is still just a promise.")
v("TreatyCancelled", "Cancelled. Sometimes the polite way out is still a slammed door.", "No more treaty. No more pretending.")
v("TreatyExpired", "Ran out quietly. Nobody bothered to renew it. That says something.", "Expired. Like the milk.")
v("EmbargoDeclared",
  "An embargo. So the price of everything goes up again.",
  "No trading with [FACTION_B]. Someone's going to smuggle, mark my words.",
  "Embargoes hurt the poor first. Always.",
  "Good luck enforcing that. Traders find a way.")
v("EmbargoDeclared", "They won't trade with us? Fine. We'll trade with their neighbours. At a markup.", c="US==yes")
v("EmbargoDeclared", "An embargo means a smuggling market. A very profitable smuggling market.", c="TONE==greedy")
v("EmbargoLifted", "Trade's open again. Finally.", "Embargo lifted. Prices should come back down. Eventually. Maybe.")
v("BordersChanged", "Borders opening, borders closing. Lines on a map, getting moved by people who never walk them.", "So we can't go there now? Or we can? I've lost track.")
v("Denounced", "Denounced. Fancy word for a public telling-off.", "[FACTION_A] said rude things about [FACTION_B]. In public. With a podium.", "Words now, swords later.")
v("TributePaid", "Tribute. Paying someone not to hurt you. Nice work if you can get it.", "Silver flowing uphill, as usual.")
v("DelegationDispatched", "Diplomats on the road. Let's hope they don't get eaten by a thrumbo.", "An envoy. Which means someone wants something.", "They're sending fancy people in fancy clothes. Must be serious.")
v("DelegationIntercepted",
  "Someone ambushed a delegation. That's not diplomacy, that's a declaration.",
  "Attacking envoys. Even raiders usually draw the line there.",
  "Whatever they were going to sign, it's not getting signed now.")
v("DelegationIntercepted", "That was us. I'm not sorry.", "We ambushed envoys? Well, they had it coming. Probably.", c="US==yes")
v("SummitHeld", "A summit. Lots of talking, lots of eating, maybe some agreeing.", "Leaders in one room. I'd pay to hear what was said.", "Good. Talking is cheaper than fighting.")
v("SummitFailed", "The summit fell apart. Shocking. Nobody's shocked.", "They couldn't even agree on the seating.", "Failed talks are worse than no talks. Now they've seen each other's faces.")
v("HostagesExchanged", "Hostages home. Somebody's mother is very happy tonight.", "Swapping people like trade goods. What a world.")
v("EnvoyMission", "Our envoy made it. Let's hope they behaved.", "Diplomacy by caravan. Very civilised.")
v("EnvoyCaptured", "They took our envoy. That's not how this works.", "Captured? Then we go and get them back.")
v("VisitIncident", "A visit gone wrong. Someone said the wrong thing to the wrong guard.", "Visitors and trouble go together like muffalo and mud.")
v("VassalOutpostGained", "Another outpost under someone's thumb.", "Vassals. A polite word for 'owned'.")
v("VassalOutpostLost", "Lost an outpost. Things fall apart at the edges first.", "One less flag on the frontier.")
v("FactionAbsorbed", "Swallowed whole. One faction fewer on the map.", "They didn't lose a war, they lost their name. That's worse.")
v("CallToArmsRefused", "Asked for help and got a shrug. Remember that next time they ask.", "Allies who don't show up aren't allies.")
v("FederationCharter", "A federation charter. Big words. Big hopes.", "If they can hold this together, the rim might actually change.")
v("FederationJoined", "Another member for the federation. It's growing.", "Strength in numbers. Or squabbles in numbers.")
v("FederationLeft", "Walking out of the federation. That'll sting.", "Unity lasted about as long as I expected.")
v("FederationProclaimed", "A planetary federation. I never thought I'd see it.", "History. Actual history. And I was cleaning the freezer when it happened.")

# ---- council
v("CouncilConvened", "Another council session. More speeches, fewer results.", "They're meeting again. Bring snacks.", "Everybody in one hall, all pretending to like each other.")
v("DecreePassed", "A new decree. I'll obey it if I understand it.", "Passed! Now we see who actually follows it.", "Rules for everyone, except whoever wrote them.")
v("MotionRejected", "Voted down. Democracy, or something like it.", "Rejected. Somebody's sulking tonight.")
v("DecreeViolated", "Broke the decree already? That didn't take long.", "Rules are only rules when someone enforces them.")
v("DecreeExpired", "Expired. Like most good intentions.", "And just like that, it's over.")

# ---- politics
v("LeaderDied",
  "[FACTION_A]'s leader is dead. Now comes the scramble.",
  "Dead leaders leave empty chairs, and empty chairs start fights.",
  "I wonder who's next in line. I wonder if they're worse.",
  "Rest in peace, I suppose. Even leaders deserve that much.",
  "Funerals for leaders are always the most crowded. Half are there to make sure.")
v("LeaderDied", "One less tyrant. Or one less hero. Depends who you ask.", c="TONE==callous")
v("Riots",
  "Riots in [FACTION_A]. Something's very wrong over there.",
  "When people riot, it's never just about bread. But it's usually about bread.",
  "Burning shops, broken windows, angry crowds. Glad I'm here.",
  "I'd riot too if they served what our cook makes.",
  "Hungry people, angry people. Same thing, really.")
v("Riots", "Riots! Our cook had better watch it.", c="TONE==sunny")
v("Riots", "Hope nobody got hurt. Well, too late for that.", c="TONE==kind")
v("Coup",
  "A coup in [FACTION_A]. Somebody woke up with a new boss.",
  "New leader, same guards. Funny how that works.",
  "Coups are quick. The trouble afterwards is slow.",
  "One day you're in charge, the next you're in a cell. Politics.",
  "I bet the new lot promise everything. They always do.")
v("Revolution",
  "A revolution! Statues coming down, flags going up.",
  "Everything's going to change over there. For better or worse? Both.",
  "Revolutions eat their own children, they say. I'd keep my distance.",
  "When ordinary people rise up, somebody really pushed them too far.")
v("Revolution", "Good for them. Down with the bosses. Not ours, obviously.", c="TONE==sunny")
v("PowerStruggle", "Too many people want the same chair.", "A power struggle. Lots of smiling and stabbing.", "Nobody's running [FACTION_A] properly right now. Good time to be a raider.")
v("SuccessionCrisis", "Nobody knows who's in charge. That's never a calm situation.", "Two heirs, one throne. It's always one throne.")
v("Scandal", "A scandal! Finally, something interesting in the paper.", "Caught with their hand in the silver box. Classic.", "Everyone's shocked. Nobody's surprised.")
v("OfficialDefects", "Defected. Must have been a good offer.", "If their own people are leaving, something's rotten.")
v("ReformMovement", "Reformers. Good luck, honestly.", "Change from the inside. Slowest kind of change there is.")
v("SecessionMovement", "They want out. Can't say I blame them.", "Breaking away. Maps are going to need redrawing.")
v("Emigration", "People leaving [FACTION_A] in droves. That's a vote with your feet.", "If they're fleeing, there must be something to flee from.", "Refugees on the road. Some of them might end up here.")
v("PuppetInstalled", "A puppet government. Strings and all.", "They didn't conquer it, they just bought it a new head.")
v("PuppetFreed", "Cut the strings. Good.", "Free again. Let's see what they do with it.")

# ---- espionage
v("AgentsExposed", "Spies, caught. Somebody's in trouble.", "Exposed! I'd hate to be in that interrogation room.", "If they caught those spies, they've got better eyes than us.")
v("ForeignAgentsCaught", "Caught spies in our midst. I knew that trader was too friendly.", "Who sends spies to a place like this? What are they after, our recipes?")
v("Assassination",
  "Assassinated. That's not politics, that's murder with paperwork.",
  "Somebody's very good at their job. Terrifying, but good.",
  "If they can get to [LEADER_A], they can get to anyone.",
  "I'm locking my door tonight. Both locks.",
  "Blades in the dark. Nobody's safe at the top.")
v("Assassination", "Clean. Professional. I'd tip my hat if I had one.", c="TONE==callous")
v("Sabotage",
  "Sabotage. Someone wanted that place to burn.",
  "Blown up in the night. Their factories will be quiet for a while.",
  "That's a week of nobody working over there. Maybe more.",
  "Sabotage is a coward's war. An effective coward's war.",
  "I'm checking our generators. Twice.")
v("FalseFlag", "Somebody framed somebody. The truth's the first casualty.", "Who really did it? I bet we'll never know.")
v("TechnologyStolen", "Stolen secrets. Someone's going to have a nasty surprise in their next war.", "Knowledge is the best loot. Can't be raided back.")
v("HostageRescued", "Rescued! Somebody's a hero tonight.", "In and out, without a scratch. I'd like to meet that team.")

# ---- culture
v("ReligiousSchism", "A schism. Two sides, both sure they're right.", "Arguing about the gods. The gods are probably laughing.", "Faith splits are the worst kind. Nobody backs down.")
v("CulturalFestival", "A festival! Music, dancing, too much beer. I'm jealous.", "Finally some cheerful news.", "They're celebrating. Let's celebrate too. Any excuse.")

# ---- disasters and economy
v("Famine",
  "Famine in [FACTION_A]. We should count our blessings. And our pemmican.",
  "Empty bellies make for desperate neighbours.",
  "When food runs out, raids start. Lock the freezer.",
  "We should send something. Or at least not send raiders.",
  "I've been hungry before. It's not something you forget.")
v("Famine", "Hungry people pay well for food. Very well.", c="TONE==greedy")
v("Famine", "We have spare rice. We should send it. We really should.", c="TONE==kind")
v("PlagueOutbreak",
  "Plague. Keep the visitors out of the dining room.",
  "Wash your hands. I'm serious. Everyone. Now.",
  "If any of their traders turn up coughing, I'm hiding in the freezer.",
  "Plague is the one enemy you can't shoot.",
  "Our doctor had better be ready. Is our doctor ready?")
v("PlagueOutbreak", "Is it catching? It's catching, isn't it.", c="TONE==grim")
v("NaturalDisaster",
  "Nature reminding everyone who's really in charge.",
  "Floods, quakes, fires. The planet doesn't care whose flag it is.",
  "Hope they have good walls. And good shovels.",
  "That could have been us. Next time it might be.")
v("Starvation",
  "They're starving in [FACTION_A]. Actually starving.",
  "When the stores run dry, everything else falls apart.",
  "We should send food. Relief caravans make friends.",
  "Starving neighbours become desperate neighbours. Then they become raiders.")
v("Starvation", "Desperate times, desperate prices. Ours.", c="TONE==greedy")
v("SettlementLooted", "Looted. Everything not nailed down, and some things that were.", "They sacked [DETAIL]. Nothing left but ashes and angry people.", "Plunder. The oldest trade on the rim.")
v("PirateTheft", "Pirates again. They raid everyone, even the ones who raid.", "Their stores got cleaned out. Lock ours.", "Someone's eating very well tonight, and it's not [FACTION_B].")
v("SettlementConstructionBegun", "A new town going up. Somebody's optimistic.", "Settlers. Brave or foolish. Usually both.")
v("SettlementFounded", "A new town on the map. More neighbours.", "They built a town. We built a slightly bigger freezer. We're even.")
v("ConstructionRaided", "Attacked settlers before the walls were up. Nasty.", "Hitting builders. Low, even for raiders.")
v("ConvoyLost", "A whole convoy, gone. Somebody's very poor this morning.", "Trade routes aren't safe. Nothing's safe.", "Lost goods mean higher prices. Thanks, bandits.")

# ---- news itself
v("NewsCorrection",
  "So it never happened? I spent a whole night worrying about that.",
  "Fake news. I knew it. I didn't know it, but I suspected it.",
  "A correction. The paper's admitting it was wrong? Mark the calendar.",
  "I'd like my worry back, please.")

asides = {
 "WarDeclared": ["Recruiters were seen nailing posters to every tree in [CAPITAL_A].", "Blacksmiths near the front report they have never been busier.", "One soldier was overheard asking which side he was on."],
 "PeaceMade": ["Soldiers on both sides were seen sharing cigarettes within the hour.", "The signing was delayed by an argument over the pen."],
 "Riots": ["The bakery was the first building looted. It always is.", "A goat was reportedly crowned leader of the rioters for about an hour."],
 "Coup": ["The old leader's portrait was replaced before the paint on the new one had dried.", "Palace guards were seen changing uniforms in a hurry."],
 "Famine": ["Rat prices in the markets have tripled.", "Some families have started eating insect jelly. Willingly."],
 "PlagueOutbreak": ["Healers are said to be running out of herbal medicine and patience.", "Masks made from old shirts are the season's fashion."],
 "Assassination": ["The guards insist they were on duty. Nobody believes them.", "A single bootprint is the only clue so far."],
 "Sabotage": ["The fire could be seen from three hills away.", "Investigators found a suspicious amount of chemfuel residue."],
 "MissileStrike": ["Survivors describe a sound like the sky tearing.", "The crater is reportedly still warm."],
 "SummitHeld": ["Both delegations brought their own food. Just in case.", "The seating chart took longer to agree on than the agenda."],
 "LeaderDied": ["Flags flew at half-mast, mostly because nobody could reach the top.", "Three people have already claimed to be the rightful heir."],
}

out = ['<?xml version="1.0" encoding="utf-8"?>', '<Defs>',
       '  <!-- Generated by tools/news/make_vox.py: colonist quotes (vox_<Event>) and asides (aside_<Event>) for each kind of story.',
       '       Constants: TONE (grim, sunny, callous, kind, greedy, plain), US (yes when the colony is involved), CAT, RUMOUR. -->',
       '  <RulePackDef>', '    <defName>Dominion_NewsVoxEvents</defName>', '    <rulePack>', '      <rulesStrings>']
n = 0
for ev, lines in V.items():
    for c, l in lines:
        key = f"vox_{ev}" + (f"({c})" if c else "")
        out.append(f"        <li>{escape(key)}-&gt;{escape(l)}</li>"); n += 1
for ev, lines in asides.items():
    for l in lines:
        out.append(f"        <li>aside_{ev}-&gt;{escape(l)}</li>"); n += 1
out += ['      </rulesStrings>', '    </rulePack>', '  </RulePackDef>', '</Defs>', '']
open(sys.argv[1] if len(sys.argv) > 1 else '1.7/Defs/RulePackDefs/Dominion_NewsVoxEvents.xml', 'w').write("\n".join(out))
print(n, "lines,", len(V), "events")
