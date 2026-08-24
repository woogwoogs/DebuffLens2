const exactCombatTextByInternalId = new Map([
    ["archnemesis_time_bubble", "Severely Reduced Action Speed + Slower Cooldowns"],
    ["archnemesis_voodoo_doll_debuff", "Effigy Reflects Hits to You + Move Away"],
    ["atlas_orion_meteor_ground", "Multi-Element Damage + No Life/ES Recovery"],
    ["attached_retract_arrow", "Bleed + Maim When Arrows Retract"],
    ["azmeri_darkness_debuff", "Banished from the Wildwood at 10 Stacks"],
    ["bestiary_spider_web", "Chaos DoT + Reduced Move Speed"],
    ["blackstar_moonlight", "More Cold Hit Damage + Higher Freeze Chance"],
    ["blackstar_sunlight", "More Fire Hit Damage + Higher Ignite Chance"],
    ["broken_armour_debuff", "No Armour + More Physical Hit Damage"],
    ["caustic_cloud_snake_boss_vomit", "Chaos DoT + Reduced Move Speed"],
    ["covered_in_oil", "Reduced Move Speed + Fire Exposure + Guaranteed Ignite"],
    ["covered_in_spiders", "Reduced Attack/Cast Speed + Less Damage Dealt"],
    ["curse_pain_synchronisation", "Stored Damage Repeats on Expiry"],
    ["despair_bear_buff", "Reduced Move Speed + Increased Damage Taken"],
    ["doedre_pillar_green_aura", "5% Less Damage Dealt per Stack"],
    ["expedition2_protective_rune", "Severely Reduced Action Speed + Slower Cooldowns"],
    ["ground_fungal_artillery", "Chaos DoT + Reduced Move Speed"],
    ["ground_oil", "Reduced Move Speed + Fire Exposure + Guaranteed Ignite"],
    ["ground_profane_enemy", "Reduced Resists + Easier to Crit"],
    ["gruelling_madness", "Reduced Move Speed + Stronger Slows per Stack"],
    ["harvest_boss_raven_screech", "Stronger + Longer Shocks"],
    ["hellscape_debuff", "+1% Scourge Damage Taken per Stack"],
    ["hunters_mark", "Heavy Stun Buildup + Blood Explosion"],
    ["hounded_by_wisps", "Hits Against You Gain Random Element Damage"],
    ["infernal_blow_new_debuff", "Explodes on Death, Expiry, or at 6 Charges"],
    ["magma_sigil", "Brand Builds Energy + Explodes on Removal"],
    ["mamba_strike_initial_debuff", "Death Releases Poison-Based Chaos DoT"],
    ["perennial_king_tornado_degen", "Physical DoT + Reduced Move Speed + Stacks in Center"],
    ["pirate_boss_ghostfire_degen", "Fire DoT"],
    ["player_physical_damage_aura", "Increasing Physical Damage Taken"],
    ["queen_of_life_filth_reservation", "Skills Reserve Life"],
    ["queen_of_mana_filth_reservation", "Skills Reserve Mana"],
    ["rain_of_spores_vines_aura", "Chaos DoT + Reduced Move Speed"],
    ["ritual_golden_coin_buff", "Increased Damage Taken"],
    ["royale_pain", "Lethal Zone Damage + Follow the Pointer"],
    ["screech_snipe_debuff", "Incoming Extra Projectiles + Keep Moving"],
    ["flask_draining_lasso", "Reduced Move Speed + Flask Drain + Move Away"],
    ["legionnaire_arrow_tether", "Reduced Move Speed + Move Away to Break Snare"],
    ["suppressing_fire", "Slower Spells + Ranged Attacks"],
    ["synthesis_decay_effect", "Reduced Move Speed + Leave the Decayed Area"],
    ["synthesis_turret_charge_projectile_target", "Marked + Dodge Incoming Arena Projectiles"],
    ["sundered_armour", "No Armour + More Physical Hit Damage"],
    ["tornado_degen", "Physical DoT + Reduced Move Speed + Stacks in Center"],
    ["tumour_spider_goo_tether", "Reduced Move Speed + Move Away to Break Tether"],
    ["trialmaster_tether_debuff_display", "Chain: Distance Slows + Breaking Damages and Stuns"],
    ["twilight_order_wall_debuff", "Reduced Move Speed + Easier to Crit"],
]);

const phraseRules = [
    [/\bphysical damage over time\b/gi, "Physical DoT"],
    [/\bfire damage over time\b/gi, "Fire DoT"],
    [/\bcold damage over time\b/gi, "Cold DoT"],
    [/\blightning damage over time\b/gi, "Lightning DoT"],
    [/\bchaos damage over time\b/gi, "Chaos DoT"],
    [/\bburning damage\b/gi, "Fire DoT"],
    [/\bdamage over time\b/gi, "DoT"],
    [/\blife and energy shield\b/gi, "Life/ES"],
    [/\bflask and charm\b/gi, "Flask/Charm"],
    [/\bflask or charm\b/gi, "Flask/Charm"],
    [/\battack speed, cast speed and movement speed\b/gi, "Attack/Cast/Move Speed"],
    [/\battack speed, cast speed, and movement speed\b/gi, "Attack/Cast/Move Speed"],
    [/\battack and cast speeds?\b/gi, "Attack/Cast Speed"],
    [/\bskill and movement speed\b/gi, "Skill/Move Speed"],
    [/\bmovement speed\b/gi, "Move Speed"],
    [/\bcritical strike chance\b/gi, "Crit Chance"],
    [/\bcritical strike\b/gi, "Crit"],
    [/\belemental resistances\b/gi, "Elemental Resists"],
    [/\bresistances\b/gi, "Resists"],
    [/\bresistance\b/gi, "Res"],
    [/\bcooldown recovery speed\b/gi, "Cooldown Recovery"],
    [/\benergy shield\b/gi, "ES"],
];

function stripEnvironmentalCause(text) {
    return text
        .replace(/\s+due to (?:being|standing|remaining|having)\b.*$/i, "")
        .replace(/\s+from being (?:near|in|inside|within)\b.*$/i, "")
        .replace(/\s+while standing (?:in|on|inside|within)\b.*$/i, "")
        .replace(/\s+while near\b.*$/i, "")
        .replace(/\s+while in (?:the|an|a)\b.*$/i, "");
}

function compactSentence(sentence) {
    let text = sentence.trim();
    if (!text)
        return "";

    if (/^this debuff is removed when you stop being delirious$/i.test(text))
        return "";

    for (const [pattern, replacement] of phraseRules)
        text = text.replace(pattern, replacement);

    text = stripEnvironmentalCause(text)
        .replace(/^you are taking\s+/i, "")
        .replace(/^you're taking\s+/i, "")
        .replace(/^you take\s+/i, "")
        .replace(/^you are\s+/i, "")
        .replace(/^you're\s+/i, "")
        .replace(/^you have\s+/i, "")
        .replace(/^your\s+/i, "")
        .replace(/^debuff (?:inflicts|causes)\s+/i, "")
        .replace(/\bseverely lowered action speed\b/gi, "Severely Reduced Action Speed")
        .replace(/\byour cooldowns take longer to recover\b/gi, "Slower Cooldown Recovery")
        .replace(/\bnegative effects on you take longer to expire\b/gi, "Debuffs Last Longer")
        .replace(/\band (?:you )?have reduced Move Speed\b/gi, "+ Reduced Move Speed")
        .replace(/\band your Move Speed is being reduced\b/gi, "+ Reduced Move Speed")
        .replace(/\band (?:you )?are taking\b/gi, "+")
        .replace(/\band taking\b/gi, "+")
        .replace(/\bMove Speed is being reduced\b/gi, "Reduced Move Speed")
        .replace(/\bMove Speed (?:is|are) (?:reduced|lowered|slowed)\b/gi, "Reduced Move Speed")
        .replace(/\bhave reduced Move Speed\b/gi, "Reduced Move Speed")
        .replace(/\bMove Speed Slowed\b/gi, "Reduced Move Speed")
        .replace(/^slowed\b/i, "Reduced Move Speed")
        .replace(/\b(?:you )?(?:cannot|can't) recover Life(?:\/| or )ES\b/gi, "No Life/ES Recovery")
        .replace(/\b(?:you )?(?:cannot|can't) recover Mana\b/gi, "No Mana Recovery")
        .replace(/\b(?:you )?(?:cannot|can't) gain Flask\/Charm charges\b/gi, "No Flask/Charm Charge Gain")
        .replace(/\b(?:you )?(?:cannot|can't) gain Flask charges\b/gi, "No Flask Charge Gain")
        .replace(/\bFlask\/Charm charges are Drained\b/gi, "Flask/Charm Charge Drain")
        .replace(/\bbeing drained over time\b/gi, "Drained")
        .replace(/\bhas reduced effect\b/gi, "Reduced Effect")
        .replace(/\bare lowered\b/gi, "Reduced")
        .replace(/\bis lowered\b/gi, "Reduced")
        .replace(/\bare reduced\b/gi, "Reduced")
        .replace(/\bis reduced\b/gi, "Reduced")
        .replace(/\bdealing\b/gi, "Taking")
        .replace(/\binflicted with\b/gi, "+")
        .replace(/\s*,\s*and\s+/gi, " + ")
        .replace(/\s+and\s+/gi, " + ")
        .replace(/\s*;\s*/g, " + ")
        .replace(/\s*\+\s*\+\s*/g, " + ")
        .replace(/\s+/g, " ")
        .replace(/^[,;+\s]+|[,;+\s]+$/g, "")
        .trim();

    if (!text)
        return "";

    return text[0].toUpperCase() + text.slice(1);
}

export function createHcCombatDescription(internalId, sourceDescription) {
    const exact = exactCombatTextByInternalId.get(internalId);
    if (exact)
        return exact;

    const source = (sourceDescription ?? "").replace(/\s+/g, " ").trim();
    if (!source)
        return "";

    const compacted = source
        .split(/(?<=[.!?])\s+/)
        .map(sentence => compactSentence(sentence.replace(/[.!?]+$/g, "")))
        .filter(Boolean)
        .join(". ");

    return compacted || source;
}
